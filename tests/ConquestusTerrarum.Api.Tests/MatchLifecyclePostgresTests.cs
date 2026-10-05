using System.Net.Http.Json;
using ConquestusTerrarum.Api.Contracts;
using ConquestusTerrarum.Api.Sessions;
using ConquestusTerrarum.Domain.World;
using ConquestusTerrarum.Persistence;
using Npgsql;
using NpgsqlTypes;

namespace ConquestusTerrarum.Api.Tests;

[Trait("Category", "Postgres")]
public sealed class MatchLifecyclePostgresTests(ApiPostgresFixture database) : IClassFixture<ApiPostgresFixture>
{
    private static async Task<MatchStateResponse> State(HttpClient client, Guid id) =>
        (await client.GetFromJsonAsync<MatchStateResponse>($"/api/v1/matches/{id}", MatchEndpointTests.JsonOptions))!;

    private async Task<(string State, long Version, long Sequence, string Feed, DateTime Updated)> Row(Guid id)
    {
        await using var command = database.DataSource.CreateCommand("""
            SELECT state::text, version, next_update_sequence,
              (SELECT coalesce(jsonb_agg(jsonb_build_object('sequence', sequence, 'type', type, 'payload', payload)
                  ORDER BY sequence)::text, '[]') FROM match_updates WHERE match_id = $1), updated_at
            FROM matches WHERE id = $1
            """);
        command.Parameters.AddWithValue(id);
        await using var reader = await command.ExecuteReaderAsync();
        Assert.True(await reader.ReadAsync());
        return (reader.GetString(0), reader.GetInt64(1), reader.GetInt64(2), reader.GetString(3), reader.GetDateTime(4));
    }

    [ApiPostgresFact]
    public async Task WaitingCommandsAreImmutable_JoinStartsOnceAndBothPlayersCanCommand()
    {
        await using var factory = new PostgresApiFactory(database.ConnectionString);
        using var creator = factory.CreateClient();
        using var joiner = factory.CreateClient();
        var issued = await MatchSessionEndpointTests.Create(creator);
        MatchSessionEndpointTests.Authorize(creator, issued.PlayerToken);
        var waiting = await State(creator, issued.MatchId);
        Assert.Equal(MatchPhase.WaitingForPlayers, waiting.Phase);
        using (var contract = System.Text.Json.JsonDocument.Parse(await creator.GetStringAsync($"/api/v1/matches/{issued.MatchId}")))
            Assert.Equal("waitingForPlayers", contract.RootElement.GetProperty("phase").GetString());
        var own = waiting.Armies.First(a => a.OwnerId == issued.PlayerId);
        var center = waiting.Locations.Single(l => l.Name == "Crossroads").Id;
        var path = $"/api/v1/matches/{issued.MatchId}";
        var before = await Row(issued.MatchId);
        foreach (var command in new[] { "move", "retreat" })
        {
            var response = command == "move"
                ? await creator.PostAsJsonAsync($"{path}/armies/{own.Id}/move", new { destinationLocationId = center })
                : await creator.PostAsync($"{path}/armies/{own.Id}/retreat", null);
            var problem = await MatchEndpointTests.AssertProblem(response, 409);
            Assert.Equal("match_waiting_for_players", problem.GetProperty("code").GetString());
            Assert.Equal(before, await Row(issued.MatchId));
        }
        var claim = await joiner.PostAsJsonAsync("/api/v1/matches/join", new { issued.JoinCode });
        claim.EnsureSuccessStatusCode();
        var second = (await claim.Content.ReadFromJsonAsync<JoinMatchResponse>())!;
        MatchSessionEndpointTests.Authorize(joiner, second.PlayerToken);
        var activatedRow = await Row(issued.MatchId);
        Assert.Equal(before.Version + 1, activatedRow.Version);
        Assert.Equal(before.Sequence + 1, activatedRow.Sequence);
        Assert.Equal(before.State, activatedRow.State); // Session join does not rewrite the simulation snapshot.
        var page = (await creator.GetFromJsonAsync<MatchUpdatesResponse>($"{path}/updates?after={waiting.UpdateCursor}"))!;
        Assert.Equal("MatchStarted", Assert.Single(page.Updates).Type);
        Assert.Equal("active", page.Updates[0].Payload.GetProperty("phase").GetString());
        foreach (var client in new[] { creator, joiner }) Assert.Equal(MatchPhase.Active, (await State(client, issued.MatchId)).Phase);
        var all = (await creator.GetFromJsonAsync<MatchUpdatesResponse>($"{path}/updates?after=0"))!;
        Assert.Single(all.Updates, u => u.Type == "MatchStarted");
        foreach (var pair in new[] { (Client: creator, Player: issued.PlayerId), (Client: joiner, Player: second.PlayerId) })
        {
            var active = await State(pair.Client, issued.MatchId);
            var army = active.Armies.First(a => a.OwnerId == pair.Player);
            var response = await pair.Client.PostAsJsonAsync($"{path}/armies/{army.Id}/move", new { destinationLocationId = center });
            response.EnsureSuccessStatusCode();
        }
    }

    private static WorldState Combat(MatchSessionCreation session, DateTimeOffset time, int survivor)
    {
        var home = Guid.NewGuid(); var next = Guid.NewGuid();
        return new WorldState(new GameMap([new Location(home, "Combat", combatSlotCount: 1), new Location(next, "Next")],
            [new MapConnection(home, next, TimeSpan.FromSeconds(5))]),
            [new Army(Guid.NewGuid(), session.FirstPlayerId, home, [new Unit(Guid.NewGuid(), 100, survivor == 2 ? 5 : 20)]),
             new Army(Guid.NewGuid(), session.SecondPlayerId, home, [new Unit(Guid.NewGuid(), 100, survivor == 1 ? 5 : 20)])], time);
    }

    private async Task<(MatchSessionCreation Session, string Token, string SecondToken)> Prepared(int survivor)
    {
        var token = PlayerCredentials.GenerateToken();
        var session = new MatchSessionCreation(Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(),
            PlayerCredentials.HashToken(token), PlayerCredentials.GenerateJoinCode());
        var sessions = new PostgresMatchSessionRepository(database.DataSource);
        await sessions.CreateAsync(session, now => Combat(session, now, survivor));
        var secondToken = PlayerCredentials.GenerateToken();
        await sessions.JoinAsync(session.JoinCode, PlayerCredentials.HashToken(secondToken));
        // Test-only fixture: place the combat checkpoint in the past using DATABASE time,
        // so any endpoint must cross the same boundary without waiting or using an application clock.
        await using var clock = database.DataSource.CreateCommand("SELECT clock_timestamp()");
        var now = new DateTimeOffset((DateTime)(await clock.ExecuteScalarAsync())!);
        var snapshot = Combat(session, now.AddSeconds(-20), survivor).CreateSnapshot();
        await using var seed = database.DataSource.CreateCommand("UPDATE matches SET state = $1 WHERE id = $2");
        seed.Parameters.AddWithValue(NpgsqlDbType.Jsonb, new WorldSnapshotSerializer().Serialize(snapshot));
        seed.Parameters.AddWithValue(session.MatchId);
        await seed.ExecuteNonQueryAsync();
        return (session, token, secondToken);
    }

    [ApiPostgresTheory]
    [InlineData("updates", 1)] [InlineData("updates", 2)] [InlineData("updates", 0)]
    [InlineData("details", 1)] [InlineData("details", 2)] [InlineData("details", 0)]
    [InlineData("snapshot", 1)] [InlineData("snapshot", 2)] [InlineData("snapshot", 0)]
    public async Task EveryLazyReadBoundaryEliminatesArmiesButWorldRemainsActive(string route, int survivor)
    {
        var (session, token, secondToken) = await Prepared(survivor);
        await using var factory = new PostgresApiFactory(database.ConnectionString);
        using var client = factory.CreateClient();
        MatchSessionEndpointTests.Authorize(client, token);
        var initial = await Row(session.MatchId);
        var snapshot = new WorldSnapshotSerializer().Deserialize(initial.State);
        var army = snapshot.Armies.First(a => a.OwnerId == session.FirstPlayerId);
        var path = $"/api/v1/matches/{session.MatchId}";
        (await client.GetAsync(route switch
        {
            "updates" => path + "/updates?after=1",
            "details" => path + $"/armies/{army.Id}/details",
            _ => path
        })).EnsureSuccessStatusCode();
        var committed = await Row(session.MatchId);
        Assert.Equal(initial.Version + 1, committed.Version);
        Assert.True(committed.Sequence > initial.Sequence);
        var state = await State(client, session.MatchId);
        Assert.Equal(MatchPhase.Active, state.Phase);
        Assert.Empty(state.ActiveBattles);
        foreach (var a in state.Armies)
            Assert.Equal(survivor == 0 || a.OwnerId != (survivor == 1 ? session.FirstPlayerId : session.SecondPlayerId), a.IsDestroyed);
        using (var contract = System.Text.Json.JsonDocument.Parse(await client.GetStringAsync(path)))
        {
            Assert.Equal("active", contract.RootElement.GetProperty("phase").GetString());
            Assert.False(contract.RootElement.TryGetProperty("winnerPlayerId", out _));
            Assert.False(contract.RootElement.TryGetProperty("isDraw", out _));
        }
        var feed = (await client.GetFromJsonAsync<MatchUpdatesResponse>(path + "/updates?after=0"))!;
        Assert.Single(feed.Updates, u => u.Type == "MatchStarted");
        Assert.Contains(feed.Updates, u => u.Type == "ArmyDestroyed");
        Assert.Contains(feed.Updates, u => u.Type == "BattleEnded");
        Assert.DoesNotContain(feed.Updates, u => u.Type == "MatchFinished");
        Assert.Equal((long)feed.Updates.Count + 1, committed.Sequence);
        var stable = await Row(session.MatchId);
        // Poll/detail reads advance lazily without writing idle checkpoints or duplicate events.
        var empty = (await client.GetFromJsonAsync<MatchUpdatesResponse>(path + $"/updates?after={feed.NextCursor}"))!;
        Assert.Empty(empty.Updates);
        Assert.Equal(feed.NextCursor, empty.NextCursor);
        foreach (var a in state.Armies)
            (await client.GetAsync(path + $"/armies/{a.Id}/details")).EnsureSuccessStatusCode();
        Assert.Equal(stable, await Row(session.MatchId));
        var query = await new PostgresMatchRepository(database.DataSource).QueryCurrentAsync(session.MatchId, (_, _) => 0);
        Assert.Equal(MatchPhase.Active, query.Lifecycle!.Phase);
        Assert.Equal(stable, await Row(session.MatchId));

        // Fresh API instance and both original credentials restore the open-ended Active world.
        await using var restarted = new PostgresApiFactory(database.ConnectionString);
        foreach (var credential in new[] { token, secondToken })
        {
            using var reconnect = restarted.CreateClient();
            MatchSessionEndpointTests.Authorize(reconnect, credential);
            var restored = await State(reconnect, session.MatchId);
            Assert.Equal(MatchPhase.Active, restored.Phase);
            Assert.Equal(feed.NextCursor, restored.UpdateCursor);
            Assert.Equal(state.Armies.Select(a => (a.Id, a.IsDestroyed)), restored.Armies.Select(a => (a.Id, a.IsDestroyed)));
        }
        using var second = factory.CreateClient();
        MatchSessionEndpointTests.Authorize(second, secondToken);
        var destination = snapshot.Locations.Single(l => l.Name == "Next").Id;
        if (survivor != 0)
        {
            var remaining = state.Armies.Single(a => !a.IsDestroyed);
            var owner = survivor == 1 ? client : second;
            var foreign = survivor == 1 ? second : client;
            var beforeForeign = await Row(session.MatchId);
            await MatchEndpointTests.AssertProblem(await foreign.PostAsJsonAsync($"{path}/armies/{remaining.Id}/move",
                new { destinationLocationId = destination }), 403);
            Assert.Equal(beforeForeign, await Row(session.MatchId));
            var moved = await owner.PostAsJsonAsync($"{path}/armies/{remaining.Id}/move", new { destinationLocationId = destination });
            moved.EnsureSuccessStatusCode();
            var movement = (await moved.Content.ReadFromJsonAsync<MatchStateResponse>(MatchEndpointTests.JsonOptions))!;
            Assert.Equal(MatchPhase.Active, movement.Phase);
            Assert.True(movement.Armies.Single(a => a.Id == remaining.Id).IsMoving);
            Assert.Equal(beforeForeign.Version + 1, movement.Version);
            Assert.Equal(feed.NextCursor + 1, movement.UpdateCursor);
        }
        // Dead armies still obey ordinary domain rules; this is not a global lifecycle rejection.
        var destroyed = state.Armies.First(a => a.IsDestroyed);
        var deadOwner = destroyed.OwnerId == session.FirstPlayerId ? client : second;
        var beforeDead = await Row(session.MatchId);
        foreach (var command in new[] { "move", "retreat" })
        {
            var rejected = command == "move"
                ? await deadOwner.PostAsJsonAsync($"{path}/armies/{destroyed.Id}/move", new { destinationLocationId = destination })
                : await deadOwner.PostAsync($"{path}/armies/{destroyed.Id}/retreat", null);
            Assert.Equal("gameplay_conflict", (await MatchEndpointTests.AssertProblem(rejected, 409)).GetProperty("code").GetString());
            Assert.Equal(beforeDead, await Row(session.MatchId));
        }
        Assert.DoesNotContain((await new PostgresMatchRepository(database.DataSource).ReadUpdatesAsync(session.MatchId, 0)).Updates,
            u => u.Type == "MatchFinished");
    }

    [ApiPostgresTheory]
    [InlineData(false)] [InlineData(true)]
    public async Task JoiningAnAlreadyEliminatedOrEmptyWorldOnlyAnnouncesStart(bool empty)
    {
        var token = PlayerCredentials.GenerateToken();
        var session = new MatchSessionCreation(Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(), PlayerCredentials.HashToken(token), PlayerCredentials.GenerateJoinCode());
        var sessions = new PostgresMatchSessionRepository(database.DataSource);
        await sessions.CreateAsync(session, now =>
        {
            var home = Guid.NewGuid();
            return new WorldState(new GameMap([new Location(home, "Home")], []), empty ? [] :
                [new Army(Guid.NewGuid(), session.FirstPlayerId, home, []),
                 new Army(Guid.NewGuid(), session.SecondPlayerId, home, [])], now);
        });
        var repository = new PostgresMatchRepository(database.DataSource);
        Assert.Equal(MatchPhase.WaitingForPlayers, (await repository.LoadCurrentAsync(session.MatchId)).Lifecycle!.Phase);
        var before = await Row(session.MatchId);
        await sessions.JoinAsync(session.JoinCode, PlayerCredentials.HashToken(PlayerCredentials.GenerateToken()));
        var after = await Row(session.MatchId);
        Assert.Equal(before.State, after.State);
        Assert.Equal(before.Version + 1, after.Version);
        Assert.Equal(before.Sequence + 1, after.Sequence);
        var feed = await repository.ReadUpdatesAsync(session.MatchId, 0);
        Assert.Equal("MatchStarted", Assert.Single(feed.Updates).Type);
        Assert.Equal(MatchPhase.Active, (await repository.LoadCurrentAsync(session.MatchId)).Lifecycle!.Phase);
        await using var restarted = new PostgresApiFactory(database.ConnectionString);
        using var reconnect = restarted.CreateClient();
        MatchSessionEndpointTests.Authorize(reconnect, token);
        Assert.Equal(MatchPhase.Active, (await State(reconnect, session.MatchId)).Phase);
    }

    [ApiPostgresFact]
    public async Task FailedStartEventInsertRollsBackSlotClaimVersionAndCursor()
    {
        await using var factory = new PostgresApiFactory(database.ConnectionString);
        using var client = factory.CreateClient();
        var created = await MatchSessionEndpointTests.Create(client);
        var before = await Row(created.MatchId);
        // Force an insert failure without touching production schema or migrations.
        await using var blocker = database.DataSource.CreateCommand("""
            INSERT INTO match_updates (match_id, sequence, occurred_at, type, payload)
            VALUES ($1, $2, clock_timestamp(), 'VerificationBlocker', '{}')
            """);
        blocker.Parameters.AddWithValue(created.MatchId);
        blocker.Parameters.AddWithValue(before.Sequence);
        await blocker.ExecuteNonQueryAsync();
        var blocked = await Row(created.MatchId);
        await Assert.ThrowsAsync<PostgresException>(() => new PostgresMatchSessionRepository(database.DataSource)
            .JoinAsync(created.JoinCode, PlayerCredentials.HashToken(PlayerCredentials.GenerateToken())));
        Assert.Equal(blocked, await Row(created.MatchId));
        await using var slot = database.DataSource.CreateCommand("SELECT token_hash IS NULL FROM match_players WHERE match_id = $1 AND slot = 2");
        slot.Parameters.AddWithValue(created.MatchId);
        Assert.Equal(true, await slot.ExecuteScalarAsync());
    }

    [ApiPostgresFact]
    public async Task CommandCrossingEliminationContinuesAndSerializesWithLazyRead()
    {
        var (session, token, _) = await Prepared(1);
        await using var factory = new PostgresApiFactory(database.ConnectionString);
        using var client = factory.CreateClient();
        MatchSessionEndpointTests.Authorize(client, token);
        var before = await Row(session.MatchId);
        var stored = new WorldSnapshotSerializer().Deserialize(before.State);
        var army = stored.Armies.First(a => a.OwnerId == session.FirstPlayerId).Id;
        var next = stored.Locations.Single(l => l.Name == "Next").Id;
        var path = $"/api/v1/matches/{session.MatchId}/armies/{army}/move";
        // The command itself crosses the elimination boundary and still succeeds for the survivor.
        var move = await client.PostAsJsonAsync(path, new { destinationLocationId = next });
        move.EnsureSuccessStatusCode();
        var moved = (await move.Content.ReadFromJsonAsync<MatchStateResponse>(MatchEndpointTests.JsonOptions))!;
        Assert.Equal(MatchPhase.Active, moved.Phase);
        Assert.True(moved.Armies.Single(a => a.Id == army).IsMoving);
        Assert.Equal(before.Version + 1, moved.Version);
        Assert.DoesNotContain((await new PostgresMatchRepository(database.DataSource).ReadUpdatesAsync(session.MatchId, 0)).Updates,
            u => u.Type == "MatchFinished");

        // A second world isolates the row-lock race from the movement already started above.
        var (queuedSession, queuedToken, _) = await Prepared(1);
        MatchSessionEndpointTests.Authorize(client, queuedToken);
        stored = new WorldSnapshotSerializer().Deserialize((await Row(queuedSession.MatchId)).State);
        army = stored.Armies.First(a => a.OwnerId == queuedSession.FirstPlayerId).Id;
        next = stored.Locations.Single(l => l.Name == "Next").Id;
        var reached = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var release = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var read = new PostgresMatchRepository(database.DataSource).ExecuteAsync(queuedSession.MatchId,
            async (_, _) => { reached.SetResult(); await release.Task.WaitAsync(TimeSpan.FromSeconds(10)); return 0; });
        await reached.Task.WaitAsync(TimeSpan.FromSeconds(10));
        var command = client.PostAsJsonAsync($"/api/v1/matches/{queuedSession.MatchId}/armies/{army}/move", new { destinationLocationId = next });
        Assert.False(command.IsCompleted);
        release.SetResult();
        var committed = await read;
        var response = await command;
        response.EnsureSuccessStatusCode();
        moved = (await response.Content.ReadFromJsonAsync<MatchStateResponse>(MatchEndpointTests.JsonOptions))!;
        Assert.Equal(MatchPhase.Active, moved.Phase);
        Assert.Equal(committed.Version + 1, moved.Version);
        Assert.Equal(committed.UpdateCursor + 1, moved.UpdateCursor);
    }

    [ApiPostgresFact]
    public async Task ObsoleteFinishFeedDataIsReadableAndDoesNotControlParticipation()
    {
        var (session, token, _) = await Prepared(0);
        var repository = new PostgresMatchRepository(database.DataSource);
        var state = await repository.LoadCurrentAsync(session.MatchId);
        await using var obsolete = database.DataSource.CreateCommand("""
            WITH inserted AS (
                INSERT INTO match_updates (match_id, sequence, occurred_at, type, payload)
                SELECT id, next_update_sequence, clock_timestamp(), 'MatchFinished', '{"phase":"finished","isDraw":true}'::jsonb
                FROM matches WHERE id = $1 RETURNING match_id
            )
            UPDATE matches SET next_update_sequence = next_update_sequence + 1
            WHERE id = (SELECT match_id FROM inserted)
            """);
        obsolete.Parameters.AddWithValue(session.MatchId);
        await obsolete.ExecuteNonQueryAsync();
        await using var factory = new PostgresApiFactory(database.ConnectionString);
        using var client = factory.CreateClient();
        MatchSessionEndpointTests.Authorize(client, token);
        var page = (await client.GetFromJsonAsync<MatchUpdatesResponse>($"/api/v1/matches/{session.MatchId}/updates?after={state.UpdateCursor}"))!;
        Assert.Equal("MatchFinished", Assert.Single(page.Updates).Type);
        Assert.Equal(state.UpdateCursor + 1, page.NextCursor);
        Assert.Equal(MatchPhase.Active, (await State(client, session.MatchId)).Phase);
        var repeated = await repository.ReadUpdatesAsync(session.MatchId, page.NextCursor);
        Assert.Empty(repeated.Updates);
        Assert.Equal(page.NextCursor, repeated.NextCursor);
    }
}
