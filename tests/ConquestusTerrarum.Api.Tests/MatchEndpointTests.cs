using System.Net;
using System.Net.Http.Json;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using ConquestusTerrarum.Api.Contracts;
using ConquestusTerrarum.Domain.World;
using ConquestusTerrarum.Persistence;
using Npgsql;
using static ConquestusTerrarum.Testing.SnapshotWorlds;

namespace ConquestusTerrarum.Api.Tests;

public class MatchEndpointTests
{
    internal static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web)
    {
        Converters = { new JsonStringEnumConverter<MovementKindResponse>(JsonNamingPolicy.CamelCase) }
    };

    private static WorldState Peaceful() => new(new GameMap([new(Id(2), "Next"), new(Id(1), "Home", Id(21), 2)],
        [new(Id(1), Id(2), TimeSpan.FromTicks(300000001))]), [Army(11, 21, 1, Unit(101))], Start);

    private static RecordingMatchRepository Repository() => new(Peaceful(), Start.AddSeconds(2));
    private static string Path(RecordingMatchRepository repository) => $"/api/v1/matches/{repository.MatchId}";

    private static async Task<MatchStateResponse> State(HttpResponseMessage response)
    {
        response.EnsureSuccessStatusCode();
        return (await response.Content.ReadFromJsonAsync<MatchStateResponse>(JsonOptions))!;
    }

    [Fact]
    public async Task GetMapsCurrentWorldIntoClientContract_AndOmitsInternalState()
    {
        var repository = new RecordingMatchRepository(Combat(), Start.AddSeconds(0.5));
        await using var factory = new ApiFactory(repository);
        using var client = factory.CreateClient();
        var response = await client.GetAsync(Path(repository));
        var state = await State(response);
        Assert.Equal(repository.MatchId, state.MatchId);
        Assert.Equal(41, state.Version);
        Assert.Equal(repository.DatabaseTime, state.ServerTime);
        Assert.Equal(state.ServerTime, state.CheckpointTime);
        Assert.Equal(1, repository.Executions);
        Assert.Equal(0, repository.Loads);
        Assert.Equal("no-store", response.Headers.CacheControl!.ToString());
        var world = WorldState.Restore(repository.Stored);
        foreach (var location in state.Locations)
        {
            var expected = world.Map.GetLocation(location.Id);
            Assert.Equal(expected.Name, location.Name);
            Assert.Equal(expected.OwnerId, location.OwnerId);
            Assert.Equal(expected.CombatSlotCount, location.CombatSlotCount);
        }

        Assert.Equal(world.Map.Connections.Select(c => c.TravelTime.Ticks),
            state.Connections.Select(c => c.TravelTimeTicks));
        foreach (var army in state.Armies)
        {
            var expected = world.GetArmy(army.Id);
            Assert.Equal(expected.OwnerId, army.OwnerId);
            Assert.Equal(expected.LocationId, army.LocationId);
            Assert.Equal(expected.EnteredLocationAt, army.EnteredLocationAt);
            Assert.Equal(expected.PreviousLocationId, army.PreviousLocationId);
            Assert.Equal(expected.RetreatLocationId, army.RetreatLocationId);
            Assert.Equal(expected.CanRetreat, army.CanRetreat);
            Assert.Equal(expected.IsMoving, army.IsMoving);
            Assert.Equal(expected.IsDestroyed, army.IsDestroyed);
            Assert.Equal(expected.IsInBattle, army.IsInBattle);
            foreach (var unit in army.Units)
            {
                var expectedUnit = expected.Units.Single(u => u.Id == unit.Id);
                Assert.Equal(expectedUnit.MaxHitPoints, unit.MaxHitPoints);
                Assert.Equal(expectedUnit.CurrentHitPoints, unit.CurrentHitPoints);
                Assert.Equal(expectedUnit.DamagePerSecond, unit.DamagePerSecond);
                Assert.Equal(expectedUnit.IsAlive, unit.IsAlive);
                Assert.Equal(expectedUnit.IsWounded, unit.IsWounded);
            }
        }

        Assert.Equal(world.Alliances.Select(a => (a.FirstPlayerId, a.SecondPlayerId)),
            state.Alliances.Select(a => (a.FirstPlayerId, a.SecondPlayerId)));
        foreach (var battle in state.ActiveBattles)
        {
            var expected = world.GetBattleAt(battle.LocationId)!;
            Assert.Equal(expected.StartedAt, battle.StartedAt);
            Assert.Equal(
                expected.Slots.Select(s => (s.Index, s.FirstArmyId, s.FirstUnitId, s.SecondArmyId, s.SecondUnitId,
                    s.PairedAt, s.FirstDeathAt, s.SecondDeathAt)),
                battle.CombatSlots.Select(s => (s.Index, s.FirstArmyId, s.FirstUnitId, s.SecondArmyId, s.SecondUnitId,
                    s.PairedAt, s.FirstDeathAt, s.SecondDeathAt)));
        }

        var json = await response.Content.ReadAsStringAsync();
        foreach (var internalField in new[]
                 {
                     "randomSeed", "randomState", "retreatLocked", "formatVersion", "firstStartingHitPoints",
                     "secondStartingHitPoints"
                 })
            Assert.DoesNotContain(internalField, json, StringComparison.OrdinalIgnoreCase);
        Assert.Equal(state.Locations.Select(l => l.Id).Order(), state.Locations.Select(l => l.Id));
        Assert.Equal(state.Armies.Select(a => a.Id).Order(), state.Armies.Select(a => a.Id));
        Assert.All(state.Armies, a => Assert.Equal(a.Units.Select(u => u.Id).Order(), a.Units.Select(u => u.Id)));
        Assert.Equal(state.ActiveBattles.Select(b => b.LocationId).Order(),
            state.ActiveBattles.Select(b => b.LocationId));
        Assert.All(state.ActiveBattles,
            b => Assert.Equal(b.CombatSlots.Select(s => s.Index).Order(), b.CombatSlots.Select(s => s.Index)));
    }

    [Fact]
    public async Task MoveUsesDatabaseTime_AndReturnsSavedStateInOneTransaction()
    {
        var repository = Repository();
        await using var factory = new ApiFactory(repository);
        using var client = factory.CreateClient();
        var response = await client.PostAsJsonAsync($"{Path(repository)}/armies/{Id(11)}/move",
            new { destinationLocationId = Id(2) });
        var state = await State(response);
        var army = state.Armies.Single();
        Assert.Equal(41, state.Version);
        Assert.Equal(1, repository.Executions);
        Assert.Equal(0, repository.Loads);
        Assert.True(army.IsMoving);
        Assert.Null(army.LocationId);
        Assert.Equal(state.ServerTime, army.Movement!.StartedAt);
        Assert.Equal(repository.DatabaseTime, army.Movement.StartedAt);
        Assert.Equal(state.ServerTime.AddTicks(state.Connections.Single().TravelTimeTicks), army.Movement.ArriveAt);
        Assert.Equal(MovementKindResponse.Normal, army.Movement.Kind);
        Assert.Equal(repository.Stored.Armies[0].Movement!.ArriveAt, army.Movement.ArriveAt);
        Assert.Contains("\"kind\":\"normal\"", await response.Content.ReadAsStringAsync());
    }

    [Fact]
    public async Task RetreatUsesDatabaseTime_AndReturnsCasualtiesAndMovementInOneTransaction()
    {
        var repository = new RecordingMatchRepository(Retreat(), Start.AddSeconds(3));
        await using var factory = new ApiFactory(repository);
        using var client = factory.CreateClient();
        var state = await State(await client.PostAsync($"{Path(repository)}/armies/{Id(11)}/retreat", null));
        var army = state.Armies.Single(a => a.Id == Id(11));
        Assert.Equal(repository.DatabaseTime, army.Movement!.StartedAt);
        Assert.Equal(state.ServerTime, army.Movement.StartedAt);
        Assert.Equal(MovementKindResponse.Retreat, army.Movement.Kind);
        Assert.Equal(Id(2), army.Movement.ToLocationId);
        Assert.False(army.Units.Single(u => u.Id == Id(101)).IsAlive);
        Assert.True(army.Units.Single(u => u.Id == Id(102)).IsAlive);
        Assert.Equal(41, state.Version);
        Assert.Equal(1, repository.Executions);
        Assert.Equal(0, repository.Loads);
        Assert.Empty(state.ActiveBattles);
    }

    [Theory]
    [InlineData("not-json")]
    [InlineData("null")]
    [InlineData("{}")]
    [InlineData("[]")]
    [InlineData("{\"destinationLocationId\":null}")]
    [InlineData("{\"destinationLocationId\":\"invalid\"}")]
    [InlineData("{\"destinationLocationId\":\"00000000-0000-0000-0000-000000000000\"}")]
    [InlineData(
        "{\"destinationLocationId\":\"00000002-0000-0000-0000-000000000000\",\"commandTime\":\"2026-01-01T00:00:00Z\"}")]
    public async Task InvalidMoveBodyIncludingClientTimestampIsRejected(string body)
    {
        var repository = Repository();
        await using var factory = new ApiFactory(repository);
        using var client = factory.CreateClient();
        var response = await client.PostAsync($"{Path(repository)}/armies/{Id(11)}/move",
            new StringContent(body, Encoding.UTF8, "application/json"));
        await AssertProblem(response, 400);
        Assert.Equal(0, repository.Executions);
    }

    [Theory]
    [InlineData("/api/v1/matches/not-a-guid", "GET")]
    [InlineData("/api/v1/matches/00000000-0000-0000-0000-000000000000", "GET")]
    [InlineData("/api/v1/matches/not-a-guid/armies/0000000b-0000-0000-0000-000000000000/retreat", "POST")]
    [InlineData("/api/v1/matches/00002328-0000-0000-0000-000000000000/armies/not-a-guid/retreat", "POST")]
    [InlineData(
        "/api/v1/matches/00002328-0000-0000-0000-000000000000/armies/00000000-0000-0000-0000-000000000000/retreat",
        "POST")]
    public async Task EmptyOrMalformedRouteIdsAreBadRequests(string path, string method)
    {
        var repository = Repository();
        await using var factory = new ApiFactory(repository);
        using var client = factory.CreateClient();
        await AssertProblem(await client.SendAsync(new HttpRequestMessage(new HttpMethod(method), path)), 400);
        Assert.Equal(0, repository.Executions);
    }

    [Fact]
    public async Task RetreatBodyAndCommandQueryTimestampAreRejected()
    {
        var repository = Repository();
        await using var factory = new ApiFactory(repository);
        using var client = factory.CreateClient();
        await AssertProblem(
            await client.PostAsJsonAsync($"{Path(repository)}/armies/{Id(11)}/retreat", new { commandTime = Start }),
            400);
        await AssertProblem(
            await client.PostAsJsonAsync($"{Path(repository)}/armies/{Id(11)}/move?commandTime=2026-01-01",
                new { destinationLocationId = Id(2) }), 400);
        Assert.Equal(0, repository.Executions);
    }

    [Fact]
    public async Task MissingMatchReturns404()
    {
        var repository = Repository();
        await using var factory = new ApiFactory(repository);
        using var client = factory.CreateClient();
        var problem = await AssertProblem(await client.GetAsync($"/api/v1/matches/{Id(9999)}"), 404);
        Assert.Equal("match_not_found", problem.GetProperty("code").GetString());
    }

    [Theory]
    [InlineData("in-battle")]
    [InlineData("moving")]
    [InlineData("destroyed")]
    [InlineData("unconnected")]
    [InlineData("invalid-retreat")]
    public async Task IllegalGameplayCommandsReturn409(string scenario)
    {
        var world = Peaceful();
        if (scenario == "in-battle") world = Combat();
        if (scenario == "moving") world.StartMovement(Id(11), Id(2), Start);
        if (scenario == "destroyed") world = new WorldState(Peaceful().Map, [Army(11, 21, 1)], Start);
        if (scenario == "unconnected")
            world = new WorldState(new GameMap(Peaceful().Map.Locations, []), [Army(11, 21, 1, Unit(101))], Start);
        var repository = new RecordingMatchRepository(world, Start.AddSeconds(1));
        await using var factory = new ApiFactory(repository);
        using var client = factory.CreateClient();
        var response = scenario == "invalid-retreat"
            ? await client.PostAsync($"{Path(repository)}/armies/{Id(11)}/retreat", null)
            : await client.PostAsJsonAsync($"{Path(repository)}/armies/{Id(11)}/move",
                new { destinationLocationId = Id(2) });
        var problem = await AssertProblem(response, 409);
        Assert.Equal("gameplay_conflict", problem.GetProperty("code").GetString());
        Assert.Equal(40, repository.Version);
    }

    [Theory]
    [InlineData("clock", 503, "database_time_behind_world")]
    [InlineData("corrupt", 500, "invalid_world_state")]
    [InlineData("unexpected", 500, "internal_error")]
    [InlineData("database", 503, "database_unavailable")]
    public async Task PersistenceAndUnexpectedErrorsHaveSafeProblemDetails(string failure, int status, string code)
    {
        var repository = Repository();
        repository.Failure = failure switch
        {
            "clock" => new DatabaseTimeBehindWorldException(Start, Start.AddSeconds(1)),
            "corrupt" => new InvalidWorldStateException("SECRET_PERSISTED_JSON", new Exception("SECRET_STACK")),
            "database" => new NpgsqlException("SECRET_CONNECTION", new TimeoutException()),
            _ => new InvalidOperationException("SECRET_INTERNAL_DETAILS")
        };
        await using var factory = new ApiFactory(repository);
        using var client = factory.CreateClient();
        var response = await client.GetAsync(Path(repository));
        var problem = await AssertProblem(response, status);
        Assert.Equal(code, problem.GetProperty("code").GetString());
        var body = await response.Content.ReadAsStringAsync();
        Assert.DoesNotContain("SECRET", body);
        Assert.DoesNotContain("stackTrace", body, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task ClientCancellationReachesRepositoryAndIsNotConvertedToProblemDetails()
    {
        var repository = Repository();
        var entered = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var cancelled = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        repository.BeforeExecution = async token =>
        {
            entered.SetResult();
            try
            {
                await Task.Delay(Timeout.Infinite, token);
            }
            catch (OperationCanceledException)
            {
                cancelled.SetResult();
                throw;
            }
        };
        await using var factory = new ApiFactory(repository);
        using var client = factory.CreateClient();
        using var cancellation = new CancellationTokenSource();
        var pending = client.GetAsync(Path(repository), cancellation.Token);
        await entered.Task.WaitAsync(TimeSpan.FromSeconds(10));
        Assert.True(repository.LastCancellationToken.CanBeCanceled);
        cancellation.Cancel();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => pending);
        await cancelled.Task.WaitAsync(TimeSpan.FromSeconds(10));
        Assert.Equal(40, repository.Version);
    }

    [Fact]
    public async Task DevelopmentCreationUsesRepositoryFactoryTimeAndReturnsInitialState()
    {
        var repository = Repository();
        await using var factory = new ApiFactory(repository);
        using var client = factory.CreateClient();
        var response = await client.PostAsync("/api/v1/dev/matches", null);
        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        var state = await State(response);
        Assert.Equal(repository.MatchId, state.MatchId);
        Assert.Equal(0, state.Version);
        Assert.Equal(repository.DatabaseTime, state.CheckpointTime);
        Assert.Equal(state.CheckpointTime, state.ServerTime);
        Assert.Equal($"/api/v1/matches/{state.MatchId}", response.Headers.Location!.OriginalString);
        Assert.Equal(1, repository.Creations);
        Assert.Equal(0, repository.Executions);
        Assert.True(state.Locations.Count >= 3);
        Assert.True(state.Armies.Count >= 3);
        Assert.True(state.Armies.Select(a => a.OwnerId).Distinct().Count() >= 2);
        Assert.All(state.Armies, a =>
        {
            Assert.Equal(state.ServerTime, a.EnteredLocationAt);
            Assert.True(a.Units.Count >= 2);
        });
    }

    [Theory]
    [InlineData("Production")]
    [InlineData("Staging")]
    public async Task DevelopmentRouteAndOpenApiAreNotMappedOutsideDevelopment(string environment)
    {
        var repository = Repository();
        await using var factory = new ApiFactory(repository, environment);
        using var client = factory.CreateClient();
        Assert.Equal(HttpStatusCode.NotFound, (await client.PostAsync("/api/v1/dev/matches", null)).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await client.GetAsync("/openapi/v1.json")).StatusCode);
        Assert.Equal(0, repository.Creations);
    }

    [Fact]
    public async Task DevelopmentOpenApiDocumentsClientContractAndTemplateRouteIsRemoved()
    {
        var repository = Repository();
        await using var factory = new ApiFactory(repository);
        using var client = factory.CreateClient();
        var json = await client.GetStringAsync("/openapi/v1.json");
        using var document = JsonDocument.Parse(json);
        var paths = document.RootElement.GetProperty("paths");
        Assert.True(paths.TryGetProperty("/api/v1/matches/{matchId}", out _));
        Assert.True(paths.TryGetProperty("/api/v1/matches/{matchId}/armies/{armyId}/move", out _));
        Assert.True(paths.TryGetProperty("/api/v1/matches/{matchId}/armies/{armyId}/retreat", out _));
        Assert.True(paths.TryGetProperty("/api/v1/dev/matches", out _));
        Assert.True(paths.TryGetProperty("/api/v1/matches/{matchId}/updates", out _));
        Assert.Contains("MatchStateResponse", json);
        Assert.Contains("destinationLocationId", json);
        Assert.DoesNotContain("randomState", json);
        Assert.DoesNotContain("randomSeed", json);
        Assert.DoesNotContain("WorldSnapshot", json);
        Assert.Equal(HttpStatusCode.NotFound, (await client.GetAsync("/weatherforecast")).StatusCode);
    }

    [Fact]
    public async Task UpdatesEndpointReturnsEntriesAfterCursor()
    {
        var repository =
            Repository();

        repository.Updates.Add(
            new MatchUpdateEntry(
                1,
                Start.ToUniversalTime(),
                "ArmyMovementStarted",
                $$"""
                  {
                    "armyId": "{{Id(11)}}",
                    "fromLocationId": "{{Id(1)}}",
                    "toLocationId": "{{Id(2)}}"
                  }
                  """));

        repository.Updates.Add(
            new MatchUpdateEntry(
                2,
                Start.AddSeconds(1)
                    .ToUniversalTime(),
                "ArmyArrived",
                $$"""
                  {
                    "armyId": "{{Id(11)}}",
                    "locationId": "{{Id(2)}}"
                  }
                  """));

        await using var factory =
            new ApiFactory(repository);

        using var client =
            factory.CreateClient();

        var response =
            await client.GetAsync(
                $"{Path(repository)}/updates?after=1&limit=100");

        response.EnsureSuccessStatusCode();

        var result =
            await response.Content
                .ReadFromJsonAsync<MatchUpdatesResponse>(
                    JsonOptions);

        Assert.NotNull(result);

        Assert.Equal(
            2,
            result.NextCursor);

        Assert.False(
            result.HasMore);

        var update =
            Assert.Single(
                result.Updates);

        Assert.Equal(
            2,
            update.Sequence);

        Assert.Equal(
            "ArmyArrived",
            update.Type);

        Assert.Equal(
            Id(11),
            update.Payload
                .GetProperty("armyId")
                .GetGuid());

        Assert.Equal(
            Id(2),
            update.Payload
                .GetProperty("locationId")
                .GetGuid());

        Assert.Equal(
            1,
            repository.UpdateReads);

        Assert.Equal(
            0,
            repository.Executions);

        Assert.Equal(
            "no-store",
            response.Headers.CacheControl!
                .ToString());
    }

    [Theory]
    [InlineData("?after=-1")]
    [InlineData("?after=abc")]
    [InlineData("?after=1&after=2")]
    [InlineData("?limit=0")]
    [InlineData("?limit=501")]
    [InlineData("?limit=abc")]
    [InlineData("?commandTime=2026-01-01")]
    public async Task UpdatesEndpointRejectsInvalidQuery(
        string query)
    {
        var repository =
            Repository();

        await using var factory =
            new ApiFactory(repository);

        using var client =
            factory.CreateClient();

        await AssertProblem(
            await client.GetAsync(
                $"{Path(repository)}/updates{query}"),
            400);

        Assert.Equal(
            0,
            repository.UpdateReads);
    }

    internal static async Task<JsonElement> AssertProblem(HttpResponseMessage response, int status)
    {
        Assert.Equal(status, (int)response.StatusCode);
        Assert.Equal("application/problem+json", response.Content.Headers.ContentType!.MediaType);
        var problem = await response.Content.ReadFromJsonAsync<JsonElement>();
        Assert.Equal(status, problem.GetProperty("status").GetInt32());
        Assert.False(string.IsNullOrWhiteSpace(problem.GetProperty("title").GetString()));
        Assert.False(string.IsNullOrWhiteSpace(problem.GetProperty("type").GetString()));
        return problem;
    }
}