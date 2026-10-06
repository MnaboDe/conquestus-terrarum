using System.Collections.Immutable;
using System.Net.Http.Json;
using ConquestusTerrarum.Api.Contracts;
using ConquestusTerrarum.Api.Sessions;
using ConquestusTerrarum.Domain.World;
using ConquestusTerrarum.Persistence;
using NpgsqlTypes;
using static ConquestusTerrarum.Testing.SnapshotWorlds;

namespace ConquestusTerrarum.Api.Tests;

[Trait("Category", "Postgres")]
public sealed class PostBattleWoundPostgresTests(ApiPostgresFixture database) : IClassFixture<ApiPostgresFixture>
{
    private readonly WorldSnapshotSerializer _serializer = new();

    private static WorldState World(MatchSessionCreation session, DateTimeOffset time, bool retreat, bool noDestination = false)
    {
        var road = new MapConnection(Id(1), Id(2), TimeSpan.FromSeconds(retreat ? 1 : 30));
        var leaving = new Army(Id(11), session.FirstPlayerId, retreat && !noDestination ? Id(2) : Id(1),
            retreat
                ? [Unit(101, dps: 0, current: 40), Unit(102, dps: 0), Unit(103, dps: 0, current: 20), Unit(104, current: 0)]
                : [Unit(101, hp: 10, dps: 1)]);
        if (retreat && !noDestination) leaving.StartMovement(road, time.AddSeconds(-1));
        var remaining = new Army(Id(12), session.SecondPlayerId, Id(1),
            [Unit(105, dps: retreat ? 0 : 10, current: 30), Unit(106, dps: retreat ? 0 : 10), Unit(107, current: 0)]);
        var world = new WorldState(new GameMap([new Location(Id(1), "Battle"), new Location(Id(2), "Origin")], [road]),
            [leaving, remaining], retreat && !noDestination ? time.AddSeconds(-1) : time, randomSeed: 12);
        if (retreat && !noDestination) WorldSimulator.AdvanceTo(world, time);
        return world;
    }

    private async Task<(MatchSessionCreation Session, string FirstToken, string SecondToken)> Prepared(bool retreat, bool noDestination = false)
    {
        var firstToken = PlayerCredentials.GenerateToken();
        var secondToken = PlayerCredentials.GenerateToken();
        var session = new MatchSessionCreation(Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(),
            PlayerCredentials.HashToken(firstToken), PlayerCredentials.GenerateJoinCode());
        var sessions = new PostgresMatchSessionRepository(database.DataSource);
        await sessions.CreateAsync(session, now => World(session, now, retreat, noDestination));
        await sessions.JoinAsync(session.JoinCode, PlayerCredentials.HashToken(secondToken));
        if (!retreat)
        {
            await using var clock = database.DataSource.CreateCommand("SELECT clock_timestamp()");
            var now = new DateTimeOffset((DateTime)(await clock.ExecuteScalarAsync())!);
            await Seed(session.MatchId, World(session, now.AddSeconds(-5), retreat: false).CreateSnapshot());
        }
        return (session, firstToken, secondToken);
    }

    private async Task Seed(Guid matchId, WorldSnapshot snapshot)
    {
        // Test-only snapshot fixture; never exposed by a production endpoint.
        await using var seed = database.DataSource.CreateCommand("UPDATE matches SET state = $1 WHERE id = $2");
        seed.Parameters.AddWithValue(NpgsqlDbType.Jsonb, _serializer.Serialize(snapshot));
        seed.Parameters.AddWithValue(matchId);
        await seed.ExecuteNonQueryAsync();
    }

    private async Task<(string Json, long Version, long Sequence)> Row(Guid matchId)
    {
        await using var command = database.DataSource.CreateCommand("SELECT state::text, version, next_update_sequence FROM matches WHERE id = $1");
        command.Parameters.AddWithValue(matchId);
        await using var reader = await command.ExecuteReaderAsync();
        Assert.True(await reader.ReadAsync());
        return (reader.GetString(0), reader.GetInt64(1), reader.GetInt64(2));
    }

    private static async Task<MatchStateResponse> State(HttpClient client, Guid matchId) =>
        (await client.GetFromJsonAsync<MatchStateResponse>($"/api/v1/matches/{matchId}", MatchEndpointTests.JsonOptions))!;

    private static void Resolved(WorldSnapshot snapshot, bool retreat)
    {
        Assert.Empty(snapshot.Battles);
        Assert.Equal(retreat ? new decimal[] { 0, 100, 0, 0 } : new decimal[] { 0 },
            snapshot.Armies.Single(a => a.Id == Id(11)).Units.Select(u => u.CurrentHitPoints));
        Assert.Equal(new decimal[] { 100, 100, 0 }, snapshot.Armies.Single(a => a.Id == Id(12)).Units.Select(u => u.CurrentHitPoints));
        Assert.All(snapshot.Armies, a => Assert.False(a.IsInBattle));
    }

    private static void Resolved(MatchStateResponse state, bool retreat)
    {
        Assert.Equal(MatchPhase.Active, state.Phase);
        Assert.Empty(state.ActiveBattles);
        Assert.Equal(retreat ? new decimal[] { 0, 100, 0, 0 } : new decimal[] { 0 },
            state.Armies.Single(a => a.Id == Id(11)).Units.Select(u => u.CurrentHitPoints));
        Assert.Equal(new decimal[] { 100, 100, 0 }, state.Armies.Single(a => a.Id == Id(12)).Units.Select(u => u.CurrentHitPoints));
        Assert.All(state.Armies.SelectMany(a => a.Units), u => Assert.Equal(u.CurrentHitPoints > 0, u.IsAlive));
        Assert.All(state.Armies.SelectMany(a => a.Units), u => Assert.False(u.IsWounded));
    }

    [ApiPostgresFact]
    public async Task SuccessfulRetreatPersistsMixedCasualtiesHealingAndTravelAcrossApiRestart()
    {
        var (session, token, secondToken) = await Prepared(retreat: true);
        await using (var factory = new PostgresApiFactory(database.ConnectionString))
        {
            using var first = factory.CreateClient();
            MatchSessionEndpointTests.Authorize(first, token);
            var before = await Row(session.MatchId);
            var response = await first.PostAsync($"/api/v1/matches/{session.MatchId}/armies/{Id(11)}/retreat", null);
            response.EnsureSuccessStatusCode();
            var state = (await response.Content.ReadFromJsonAsync<MatchStateResponse>(MatchEndpointTests.JsonOptions))!;
            Resolved(state, retreat: true);
            var movement = state.Armies.Single(a => a.Id == Id(11)).Movement!;
            Assert.Equal(MovementKindResponse.Retreat, movement.Kind);
            Assert.Equal(Id(2), movement.ToLocationId);
            Assert.Equal(TimeSpan.FromSeconds(1), movement.ArriveAt - movement.StartedAt);
            var committed = await Row(session.MatchId);
            Assert.Equal(before.Version + 1, committed.Version);
            Resolved(_serializer.Deserialize(committed.Json), retreat: true);
        }
        await using var restarted = new PostgresApiFactory(database.ConnectionString);
        using var second = restarted.CreateClient();
        MatchSessionEndpointTests.Authorize(second, secondToken);
        Resolved(await State(second, session.MatchId), retreat: true);
        var freshRepository = new PostgresMatchRepository(database.DataSource);
        Resolved((await freshRepository.LoadCurrentAsync(session.MatchId)).Value, retreat: true);
        var feed = (await second.GetFromJsonAsync<MatchUpdatesResponse>($"/api/v1/matches/{session.MatchId}/updates?after=0"))!;
        Assert.Single(feed.Updates, u => u.Type == "BattleEnded");
        Assert.Single(feed.Updates, u => u.Type == "ArmyRetreatStarted");
        Assert.DoesNotContain(feed.Updates, u => u.Type.Contains("Heal") || u.Type.Contains("HitPoints"));
    }

    [ApiPostgresTheory]
    [InlineData("snapshot")]
    [InlineData("updates")]
    [InlineData("details")]
    [InlineData("move")]
    public async Task EveryLazyWakeResolvesTheSameEliminationAndHealthExactlyOnce(string route)
    {
        var (session, _, token) = await Prepared(retreat: false);
        await using var factory = new PostgresApiFactory(database.ConnectionString);
        using var client = factory.CreateClient();
        MatchSessionEndpointTests.Authorize(client, token);
        var before = await Row(session.MatchId);
        var path = $"/api/v1/matches/{session.MatchId}";
        var response = route == "move"
            ? await client.PostAsJsonAsync(path + $"/armies/{Id(12)}/move", new { destinationLocationId = Id(2) })
            : await client.GetAsync(route switch
            {
                "updates" => path + $"/updates?after={before.Sequence - 1}",
                "details" => path + $"/armies/{Id(12)}/details",
                _ => path
            });
        response.EnsureSuccessStatusCode();
        var committed = await Row(session.MatchId);
        Assert.Equal(before.Version + 1, committed.Version);
        Resolved(_serializer.Deserialize(committed.Json), retreat: false);
        var feed = (await client.GetFromJsonAsync<MatchUpdatesResponse>(path + $"/updates?after={before.Sequence - 1}"))!;
        Assert.Single(feed.Updates, u => u.Type == "BattleEnded");
        Assert.Single(feed.Updates, u => u.Type == "ArmyDestroyed");
        Assert.Equal(committed.Sequence - before.Sequence, feed.Updates.Count);
        Assert.Equal(committed.Sequence - 1, feed.NextCursor);
        Assert.DoesNotContain(feed.Updates, u => u.Type.Contains("Heal") || u.Type.Contains("HitPoints") || u.Type == "MatchFinished");
        if (route == "move") Assert.Single(feed.Updates, u => u.Type == "ArmyMovementStarted");
        var stable = await Row(session.MatchId);
        for (var i = 0; i < 3; i++)
        {
            var empty = (await client.GetFromJsonAsync<MatchUpdatesResponse>(path + $"/updates?after={feed.NextCursor}"))!;
            Assert.Empty(empty.Updates);
            Assert.Equal(feed.NextCursor, empty.NextCursor);
            var detail = (await client.GetFromJsonAsync<ArmyDetailsResponse>(path + $"/armies/{Id(12)}/details"))!;
            Assert.Equal(new decimal[] { 100, 100, 0 }, detail.Units.Select(u => u.CurrentHitPoints));
        }
        Assert.Equal(stable, await Row(session.MatchId)); // Idle polls/details do not persist checkpoints or duplicate consequences.
        Resolved(await State(client, session.MatchId), retreat: false);
        Assert.Equal(stable.Version + 1, (await Row(session.MatchId)).Version); // Full GET keeps its existing row-version policy.
        Assert.Equal(stable.Sequence, (await Row(session.MatchId)).Sequence);
        await using var fresh = new PostgresApiFactory(database.ConnectionString);
        using var reconnected = fresh.CreateClient();
        MatchSessionEndpointTests.Authorize(reconnected, token);
        Resolved(await State(reconnected, session.MatchId), retreat: false);
    }

    [ApiPostgresFact]
    public async Task RejectedAndForeignRetreatLeaveWoundedUnitsAndStorageUnchanged()
    {
        var (session, token, otherToken) = await Prepared(retreat: true, noDestination: true);
        await using var factory = new PostgresApiFactory(database.ConnectionString);
        using var client = factory.CreateClient();
        var before = await Row(session.MatchId);
        MatchSessionEndpointTests.Authorize(client, otherToken);
        await MatchEndpointTests.AssertProblem(await client.PostAsync($"/api/v1/matches/{session.MatchId}/armies/{Id(11)}/retreat", null), 403);
        Assert.Equal(before, await Row(session.MatchId));
        MatchSessionEndpointTests.Authorize(client, token);
        await MatchEndpointTests.AssertProblem(await client.PostAsync($"/api/v1/matches/{session.MatchId}/armies/{Id(11)}/retreat", null), 409);
        Assert.Equal(before, await Row(session.MatchId));
        Assert.Equal(new decimal[] { 40, 100, 20, 0 }, _serializer.Deserialize(before.Json).Armies.Single(a => a.Id == Id(11)).Units.Select(u => u.CurrentHitPoints));
    }

    [ApiPostgresFact]
    public async Task QueuedStaleRetreatSeesCommittedResolutionAndCannotApplyItAgain()
    {
        var (session, _, _) = await Prepared(retreat: false);
        var locked = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var release = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var first = new PostgresMatchRepository(database.DataSource).ExecuteAsync(session.MatchId, async (world, _) =>
        {
            Resolved(world.CreateSnapshot(), retreat: false);
            locked.SetResult();
            await release.Task;
            return 0;
        });
        await locked.Task.WaitAsync(TimeSpan.FromSeconds(10));
        Task<MatchExecutionResult<int>> second;
        try
        {
            second = new PostgresMatchRepository(database.DataSource).ExecuteAsync(session.MatchId, (world, time) =>
            {
                Resolved(world.CreateSnapshot(), retreat: false);
                world.Retreat(Id(12), time);
                return Task.FromResult(0);
            });
        }
        finally { release.TrySetResult(); }
        await first;
        await Assert.ThrowsAsync<InvalidOperationException>(() => second);
        var row = await Row(session.MatchId);
        Resolved(_serializer.Deserialize(row.Json), retreat: false);
        Assert.Equal(2, row.Version); // Join plus the one successful resolving transaction.
        var feed = await new PostgresMatchRepository(database.DataSource).ReadUpdatesAsync(session.MatchId, 0, 100);
        Assert.Single(feed.Updates, u => u.Type == "BattleEnded");
        Assert.Single(feed.Updates, u => u.Type == "ArmyDestroyed");
    }

    [ApiPostgresFact]
    public async Task LaterIdleQueriesDoNotHealAgainWithoutAnotherBattleTransition()
    {
        var (session, _, token) = await Prepared(retreat: false);
        var snapshot = (await new PostgresMatchRepository(database.DataSource).LoadCurrentAsync(session.MatchId)).Value;
        snapshot = snapshot with { Armies = snapshot.Armies.Select(a => a.Id == Id(12)
            ? a with { Units = a.Units.Select(u => u.Id == Id(105) ? u with { CurrentHitPoints = 37 } : u).ToImmutableArray() }
            : a).ToImmutableArray() };
        await Seed(session.MatchId, snapshot);
        await using var factory = new PostgresApiFactory(database.ConnectionString);
        using var client = factory.CreateClient();
        MatchSessionEndpointTests.Authorize(client, token);
        for (var i = 0; i < 2; i++)
            Assert.Equal(37, (await State(client, session.MatchId)).Armies.Single(a => a.Id == Id(12)).Units.Single(u => u.Id == Id(105)).CurrentHitPoints);
    }
}
