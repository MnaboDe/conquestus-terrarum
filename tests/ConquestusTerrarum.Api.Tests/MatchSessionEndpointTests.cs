using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using ConquestusTerrarum.Api.Contracts;
using ConquestusTerrarum.Api.Sessions;
using ConquestusTerrarum.Domain.World;
using ConquestusTerrarum.Persistence;
using static ConquestusTerrarum.Testing.SnapshotWorlds;

namespace ConquestusTerrarum.Api.Tests;

public sealed class MatchSessionEndpointTests
{
    private static RecordingMatchRepository Repository() => new(Retreat(), Start.AddSeconds(2));

    internal static void Authorize(HttpClient client, string token) =>
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);

    internal static async Task<CreateMatchResponse> Create(HttpClient client)
    {
        var response = await client.PostAsync("/api/v1/matches", null);
        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        var session = (await response.Content.ReadFromJsonAsync<CreateMatchResponse>())!;
        Assert.Equal($"/api/v1/matches/{session.MatchId}", response.Headers.Location!.ToString());
        Assert.Equal("no-store", response.Headers.CacheControl!.ToString());
        return session;
    }

    [Fact]
    public async Task ProductionCreateAndNormalizedJoinReturnIndependentCredentialsAndTwoOwners()
    {
        var repository = Repository();
        await using var factory = new ApiFactory(repository, "Production");
        using var client = factory.CreateClient();
        client.DefaultRequestHeaders.Authorization = null;
        var first = await Create(client);
        Assert.NotEqual(Guid.Empty, first.MatchId);
        Assert.NotEqual(Guid.Empty, first.PlayerId);
        Assert.True(PlayerCredentials.IsValidTokenFormat(first.PlayerToken));
        Assert.Equal(32, Convert.FromBase64String(first.PlayerToken.Replace('-', '+').Replace('_', '/') + "=").Length);
        Assert.Equal(6, first.JoinCode.Length);
        Assert.Equal(first.JoinCode, first.JoinCode.ToUpperInvariant());
        Assert.Contains(repository.Stored.Armies, a => a.OwnerId == first.PlayerId);
        var joined = await client.PostAsJsonAsync("/api/v1/matches/join", new { joinCode = "  " + first.JoinCode.ToLowerInvariant() + "  " });
        joined.EnsureSuccessStatusCode();
        var second = (await joined.Content.ReadFromJsonAsync<JoinMatchResponse>())!;
        Assert.Equal(first.MatchId, second.MatchId);
        Assert.NotEqual(first.PlayerId, second.PlayerId);
        Assert.NotEqual(first.PlayerToken, second.PlayerToken);
        Assert.True(PlayerCredentials.IsValidTokenFormat(second.PlayerToken));
        Assert.Equal(new[] { first.PlayerId, second.PlayerId }.Order(), repository.Stored.Armies.Select(a => a.OwnerId).Distinct().Order());
        await MatchEndpointTests.AssertProblem(await client.PostAsJsonAsync("/api/v1/matches/join", new { first.JoinCode }), 409);
    }

    [Theory]
    [InlineData("", 400)]
    [InlineData("ABC", 400)]
    [InlineData("!!!!!!", 400)]
    [InlineData("ABC234", 404)]
    [InlineData(null, 400)]
    public async Task InvalidJoinCodeFailsCleanly(string? code, int status)
    {
        await using var factory = new ApiFactory(Repository());
        using var client = factory.CreateClient();
        await MatchEndpointTests.AssertProblem(await client.PostAsJsonAsync("/api/v1/matches/join", new { joinCode = code }), status);
    }

    [Theory]
    [InlineData("", 400)]
    [InlineData("{\"playerId\":\"arbitrary\"}", 400)]
    [InlineData("{\"joinCode\":\"ABC234\",\"ownerId\":\"arbitrary\"}", 400)]
    public async Task JoinRequiresOnlyItsExplicitContract(string json, int status)
    {
        await using var factory = new ApiFactory(Repository());
        using var client = factory.CreateClient();
        await MatchEndpointTests.AssertProblem(await client.PostAsync("/api/v1/matches/join",
            new StringContent(json, System.Text.Encoding.UTF8, "application/json")), status);
    }

    [Theory]
    [InlineData("snapshot")]
    [InlineData("updates")]
    [InlineData("details")]
    [InlineData("move")]
    [InlineData("retreat")]
    public async Task AllProtectedRoutesRejectMissingMalformedAndInvalidCredentials(string route)
    {
        var repository = Repository();
        await using var factory = new ApiFactory(repository);
        using var client = factory.CreateClient();
        foreach (var value in new string?[] { null, "Basic abc", "Bearer", "Bearer short", "Bearer " + new string('B', 43), "Bearer " + new string('!', 43) })
        {
            client.DefaultRequestHeaders.Remove("Authorization");
            if (value is not null) client.DefaultRequestHeaders.TryAddWithoutValidation("Authorization", value);
            var path = $"/api/v1/matches/{repository.MatchId}";
            var response = route switch
            {
                "move" => await client.PostAsJsonAsync($"{path}/armies/{Id(11)}/move", new { destinationLocationId = Id(2) }),
                "retreat" => await client.PostAsync($"{path}/armies/{Id(11)}/retreat", null),
                "details" => await client.GetAsync($"{path}/armies/{Id(11)}/details"),
                "updates" => await client.GetAsync($"{path}/updates?after=0"),
                _ => await client.GetAsync(path)
            };
            var problem = await MatchEndpointTests.AssertProblem(response, 401);
            Assert.Equal("invalid_player_token", problem.GetProperty("code").GetString());
            Assert.Equal("Bearer", response.Headers.WwwAuthenticate.Single().Scheme);
        }
        Assert.Equal(0, repository.Executions);
        Assert.Equal(0, repository.Queries);
        Assert.Equal(0, repository.UpdateReads);
    }

    [Fact]
    public async Task ForeignArmyMoveAndRetreatRollBackWorld_AndOwnedCommandsStillWork()
    {
        var repository = Repository();
        await using var factory = new ApiFactory(repository);
        using var client = factory.CreateClient();
        var before = new WorldSnapshotSerializer().Serialize(repository.Stored);
        foreach (var command in new[] { "move", "retreat" })
        {
            var path = $"/api/v1/matches/{repository.MatchId}/armies/{Id(12)}/{command}";
            var response = command == "move"
                ? await client.PostAsJsonAsync(path, new { destinationLocationId = Id(2) })
                : await client.PostAsync(path, null);
            var problem = await MatchEndpointTests.AssertProblem(response, 403);
            Assert.Equal("army_not_owned", problem.GetProperty("code").GetString());
            Assert.Equal(before, new WorldSnapshotSerializer().Serialize(repository.Stored));
            Assert.Equal(40, repository.Version);
        }
        (await client.PostAsync($"/api/v1/matches/{repository.MatchId}/armies/{Id(11)}/retreat", null)).EnsureSuccessStatusCode();
        Assert.Equal(MovementKind.Retreat, repository.Stored.Armies.Single(a => a.Id == Id(11)).Movement!.Kind);
    }

    [Fact]
    public async Task JoinCodeCollisionIsRetriedBeforeReturningCreatedSession()
    {
        var repository = Repository();
        await using var factory = new ApiFactory(repository);
        factory.Sessions.CollisionsRemaining = 1;
        using var client = factory.CreateClient();
        await Create(client);
        Assert.Equal(2, factory.Sessions.CreationAttempts);
        Assert.Equal(1, repository.Creations);
    }
}
