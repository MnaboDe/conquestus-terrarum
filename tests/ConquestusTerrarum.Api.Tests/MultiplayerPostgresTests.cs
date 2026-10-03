using System.Net;
using System.Net.Http.Json;
using ConquestusTerrarum.Api.Contracts;
using ConquestusTerrarum.Api.Sessions;
using ConquestusTerrarum.Persistence;
using Npgsql;

namespace ConquestusTerrarum.Api.Tests;

[Trait("Category", "Postgres")]
public sealed class MultiplayerPostgresTests(ApiPostgresFixture database) : IClassFixture<ApiPostgresFixture>
{
    [ApiPostgresFact]
    public async Task TwoPlayersReadTheSameMatch_CommandOnlyTheirArmies_AndObserveTheSameFeedAfterApiRestart()
    {
        CreateMatchResponse first;
        JoinMatchResponse second;
        CreateMatchResponse otherMatch;
        await using (var factory = new PostgresApiFactory(database.ConnectionString))
        {
            using var anonymous = factory.CreateClient();
            first = await MatchSessionEndpointTests.Create(anonymous);
            otherMatch = await MatchSessionEndpointTests.Create(anonymous);
            var join = await anonymous.PostAsJsonAsync("/api/v1/matches/join", new { joinCode = first.JoinCode.ToLowerInvariant() });
            join.EnsureSuccessStatusCode();
            second = (await join.Content.ReadFromJsonAsync<JoinMatchResponse>())!;
        }

        Assert.Equal(first.MatchId, second.MatchId);
        Assert.NotEqual(first.PlayerId, second.PlayerId);
        Assert.NotEqual(first.PlayerToken, second.PlayerToken);
        await using var restarted = new PostgresApiFactory(database.ConnectionString);
        using var playerOne = restarted.CreateClient();
        using var playerTwo = restarted.CreateClient();
        MatchSessionEndpointTests.Authorize(playerOne, first.PlayerToken);
        MatchSessionEndpointTests.Authorize(playerTwo, second.PlayerToken);
        var path = $"/api/v1/matches/{first.MatchId}";
        var stateOne = await State(await playerOne.GetAsync(path));
        var stateTwo = await State(await playerTwo.GetAsync(path));
        Assert.Equal(stateOne.MatchId, stateTwo.MatchId);
        Assert.Equal(stateOne.Armies.Select(a => (a.Id, a.OwnerId)), stateTwo.Armies.Select(a => (a.Id, a.OwnerId)));
        Assert.Equal(2, stateOne.Armies.Select(a => a.OwnerId).Distinct().Count());
        var armyOne = stateOne.Armies.First(a => a.OwnerId == first.PlayerId);
        var armyTwo = stateOne.Armies.First(a => a.OwnerId == second.PlayerId);
        var center = stateOne.Locations.Single(l => l.Name == "Crossroads").Id;

        var before = await Storage(first.MatchId);
        foreach (var command in new[] { "move", "retreat" })
        {
            await MatchEndpointTests.AssertProblem(command == "move"
                ? await playerOne.PostAsJsonAsync($"{path}/armies/{armyTwo.Id}/move", new { destinationLocationId = center })
                : await playerOne.PostAsync($"{path}/armies/{armyTwo.Id}/retreat", null), 403);
            Assert.Equal(before, await Storage(first.MatchId));
        }
        await MatchEndpointTests.AssertProblem(await playerTwo.PostAsJsonAsync($"{path}/armies/{armyOne.Id}/move",
            new { destinationLocationId = center }), 403);
        await MatchEndpointTests.AssertProblem(await playerTwo.PostAsync($"{path}/armies/{armyOne.Id}/retreat", null), 403);
        Assert.Equal(before, await Storage(first.MatchId));

        MatchSessionEndpointTests.Authorize(playerOne, otherMatch.PlayerToken);
        await MatchEndpointTests.AssertProblem(await playerOne.GetAsync(path), 401);
        await MatchEndpointTests.AssertProblem(await playerOne.GetAsync($"{path}/updates?after=0"), 401);
        await MatchEndpointTests.AssertProblem(await playerOne.PostAsJsonAsync($"{path}/armies/{armyOne.Id}/move",
            new { destinationLocationId = center }), 401);
        await MatchEndpointTests.AssertProblem(await playerOne.PostAsync($"{path}/armies/{armyOne.Id}/retreat", null), 401);
        await MatchEndpointTests.AssertProblem(await playerOne.GetAsync($"{path}/armies/{armyOne.Id}/details"), 401);
        MatchSessionEndpointTests.Authorize(playerOne, first.PlayerToken);
        await MatchEndpointTests.AssertProblem(await playerOne.GetAsync($"/api/v1/matches/{Guid.NewGuid()}"), 404);

        var movedOne = await State(await playerOne.PostAsJsonAsync($"{path}/armies/{armyOne.Id}/move", new { destinationLocationId = center }));
        var movedTwo = await State(await playerTwo.PostAsJsonAsync($"{path}/armies/{armyTwo.Id}/move", new { destinationLocationId = center }));
        Assert.True(movedOne.Armies.Single(a => a.Id == armyOne.Id).IsMoving);
        Assert.True(movedTwo.Armies.Single(a => a.Id == armyTwo.Id).IsMoving);
        Assert.True(movedTwo.UpdateCursor > movedOne.UpdateCursor);
        var feedOne = (await playerOne.GetFromJsonAsync<MatchUpdatesResponse>($"{path}/updates?after=0"))!;
        var feedTwo = (await playerTwo.GetFromJsonAsync<MatchUpdatesResponse>($"{path}/updates?after=0"))!;
        Assert.Equal(feedOne.NextCursor, feedTwo.NextCursor);
        Assert.Equal(feedOne.Updates.Select(u => (u.Sequence, u.Type, u.Payload.GetRawText())),
            feedTwo.Updates.Select(u => (u.Sequence, u.Type, u.Payload.GetRawText())));
        Assert.Equal(2, feedOne.Updates.Count(u => u.Type == "ArmyMovementStarted"));
        foreach (var client in new[] { playerOne, playerTwo })
            (await client.GetAsync($"{path}/armies/{armyOne.Id}/details")).EnsureSuccessStatusCode();
        await MatchEndpointTests.AssertProblem(await playerOne.PostAsJsonAsync("/api/v1/matches/join", new { first.JoinCode }), 409);

        foreach (var credential in new[] { first.PlayerToken, second.PlayerToken })
        {
            var row = await Storage(first.MatchId);
            Assert.DoesNotContain(credential, row.Json);
            Assert.DoesNotContain(credential, row.Feed);
            await using var hashes = database.DataSource.CreateCommand("SELECT token_hash FROM match_players WHERE match_id = $1");
            hashes.Parameters.AddWithValue(first.MatchId);
            await using var reader = await hashes.ExecuteReaderAsync();
            var values = new List<byte[]>();
            while (await reader.ReadAsync()) values.Add(reader.GetFieldValue<byte[]>(0));
            Assert.All(values, hash => Assert.Equal(32, hash.Length));
            Assert.Contains(values, hash => hash.SequenceEqual(PlayerCredentials.HashToken(credential)));
            Assert.DoesNotContain(values, hash => hash.SequenceEqual(System.Text.Encoding.ASCII.GetBytes(credential)));
        }
    }

    [ApiPostgresFact]
    public async Task ConcurrentHttpJoinsYieldOneSecondPlayerAndOneConflict()
    {
        await using var factoryOne = new PostgresApiFactory(database.ConnectionString);
        await using var factoryTwo = new PostgresApiFactory(database.ConnectionString);
        using var first = factoryOne.CreateClient();
        using var second = factoryTwo.CreateClient();
        var session = await MatchSessionEndpointTests.Create(first);
        var results = await Task.WhenAll(
            first.PostAsJsonAsync("/api/v1/matches/join", new { session.JoinCode }),
            second.PostAsJsonAsync("/api/v1/matches/join", new { session.JoinCode }));
        var success = Assert.Single(results, r => r.StatusCode == HttpStatusCode.OK);
        await MatchEndpointTests.AssertProblem(Assert.Single(results, r => r.StatusCode == HttpStatusCode.Conflict), 409);
        var player = (await success.Content.ReadFromJsonAsync<JoinMatchResponse>())!;
        Assert.NotEqual(session.PlayerId, player.PlayerId);
        MatchSessionEndpointTests.Authorize(second, player.PlayerToken);
        (await second.GetAsync($"/api/v1/matches/{session.MatchId}")).EnsureSuccessStatusCode();
    }

    private static async Task<MatchStateResponse> State(HttpResponseMessage response)
    {
        response.EnsureSuccessStatusCode();
        return (await response.Content.ReadFromJsonAsync<MatchStateResponse>(MatchEndpointTests.JsonOptions))!;
    }

    private async Task<(string Json, long Version, DateTime Updated, long Sequence, string Feed)> Storage(Guid matchId)
    {
        await using var command = database.DataSource.CreateCommand("""
            SELECT state::text, version, updated_at, next_update_sequence,
                (SELECT coalesce(jsonb_agg(payload)::text, '[]') FROM match_updates WHERE match_id = $1)
            FROM matches WHERE id = $1
            """);
        command.Parameters.AddWithValue(matchId);
        await using var reader = await command.ExecuteReaderAsync();
        Assert.True(await reader.ReadAsync());
        return (reader.GetString(0), reader.GetInt64(1), reader.GetDateTime(2), reader.GetInt64(3), reader.GetString(4));
    }
}
