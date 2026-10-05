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
        Assert.Null(waiting.WinnerPlayerId); Assert.False(waiting.IsDraw);
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

    private static WorldState Combat(MatchSessionCreation session, DateTimeOffset time, int winner)
    {
        var home = Guid.NewGuid(); var next = Guid.NewGuid();
        return new WorldState(new GameMap([new Location(home, "Combat", combatSlotCount: 1), new Location(next, "Next")],
            [new MapConnection(home, next, TimeSpan.FromSeconds(5))]),
            [new Army(Guid.NewGuid(), session.FirstPlayerId, home, [new Unit(Guid.NewGuid(), 100, winner == 2 ? 5 : 20)]),
             new Army(Guid.NewGuid(), session.SecondPlayerId, home, [new Unit(Guid.NewGuid(), 100, winner == 1 ? 5 : 20)])], time);
    }

    private async Task<(MatchSessionCreation Session, string Token)> Prepared(int winner)
    {
        var token = PlayerCredentials.GenerateToken();
        var session = new MatchSessionCreation(Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(),
            PlayerCredentials.HashToken(token), PlayerCredentials.GenerateJoinCode());
        var sessions = new PostgresMatchSessionRepository(database.DataSource);
        await sessions.CreateAsync(session, now => Combat(session, now, winner));
        await sessions.JoinAsync(session.JoinCode, PlayerCredentials.HashToken(PlayerCredentials.GenerateToken()));
        // Test-only fixture: place the combat checkpoint in the past using DATABASE time,
        // so any endpoint must cross the same boundary without waiting or using an application clock.
        await using var clock = database.DataSource.CreateCommand("SELECT clock_timestamp()");
        var now = new DateTimeOffset((DateTime)(await clock.ExecuteScalarAsync())!);
        var snapshot = Combat(session, now.AddSeconds(-20), winner).CreateSnapshot();
        await using var seed = database.DataSource.CreateCommand("UPDATE matches SET state = $1 WHERE id = $2");
        seed.Parameters.AddWithValue(NpgsqlDbType.Jsonb, new WorldSnapshotSerializer().Serialize(snapshot));
        seed.Parameters.AddWithValue(session.MatchId);
        await seed.ExecuteNonQueryAsync();
        return (session, token);
    }

    [ApiPostgresTheory]
    [InlineData("updates", 1)] [InlineData("updates", 2)] [InlineData("updates", 0)]
    [InlineData("details", 1)] [InlineData("details", 2)] [InlineData("details", 0)]
    [InlineData("snapshot", 1)] [InlineData("snapshot", 2)] [InlineData("snapshot", 0)]
    public async Task EveryLazyReadBoundaryFinishesOnce_WithAuthoritativeWinnerAndImmutableCommandRejection(string route, int winner)
    {
        var (session, token) = await Prepared(winner);
        await using var factory = new PostgresApiFactory(database.ConnectionString);
        using var client = factory.CreateClient();
        MatchSessionEndpointTests.Authorize(client, token);
        var initial = await Row(session.MatchId);
        var snapshot = new WorldSnapshotSerializer().Deserialize(initial.State);
        var army = snapshot.Armies.First(a => a.OwnerId == session.FirstPlayerId);
        var path = $"/api/v1/matches/{session.MatchId}";
        var wake = await client.GetAsync(route switch
        {
            "updates" => path + "/updates?after=1",
            "details" => path + $"/armies/{army.Id}/details",
            _ => path
        });
        wake.EnsureSuccessStatusCode();
        var committed = await Row(session.MatchId);
        Assert.True(committed.Version > initial.Version);
        Assert.True(committed.Sequence > initial.Sequence);
        var expected = winner == 0 ? null : (Guid?)(winner == 1 ? session.FirstPlayerId : session.SecondPlayerId);
        var state = await State(client, session.MatchId);
        Assert.Equal(MatchPhase.Finished, state.Phase);
        Assert.Equal(expected, state.WinnerPlayerId);
        Assert.Equal(winner == 0, state.IsDraw);
        var feed = (await client.GetFromJsonAsync<MatchUpdatesResponse>(path + "/updates?after=0"))!;
        var finished = Assert.Single(feed.Updates, u => u.Type == "MatchFinished");
        Assert.Equal(winner == 0, finished.Payload.GetProperty("isDraw").GetBoolean());
        Assert.Equal(expected?.ToString(), finished.Payload.GetProperty("winnerPlayerId").ValueKind == System.Text.Json.JsonValueKind.Null
            ? null : finished.Payload.GetProperty("winnerPlayerId").GetString());
        var before = await Row(session.MatchId);
        foreach (var command in new[] { "move", "retreat" })
        {
            var destination = snapshot.Locations.Single(l => l.Name == "Next").Id;
            var rejection = command == "move" ? await client.PostAsJsonAsync($"{path}/armies/{army.Id}/move", new { destinationLocationId = destination })
                : await client.PostAsync($"{path}/armies/{army.Id}/retreat", null);
            Assert.Equal("match_finished", (await MatchEndpointTests.AssertProblem(rejection, 409)).GetProperty("code").GetString());
            Assert.Equal(before, await Row(session.MatchId));
        }
        foreach (var read in new[] { path, path + "/updates?after=0", path + $"/armies/{army.Id}/details" })
            (await client.GetAsync(read)).EnsureSuccessStatusCode();
        var repeated = (await client.GetFromJsonAsync<MatchUpdatesResponse>(path + "/updates?after=0"))!;
        Assert.Single(repeated.Updates, u => u.Type == "MatchFinished");
        // New API instance represents reconnect/restart; result is derived only from durable PostgreSQL data.
        await using var restarted = new PostgresApiFactory(database.ConnectionString);
        using var reconnect = restarted.CreateClient();
        MatchSessionEndpointTests.Authorize(reconnect, token);
        Assert.Equal(expected, (await State(reconnect, session.MatchId)).WinnerPlayerId);
    }

    [ApiPostgresFact]
    public async Task JoiningAnAlreadyEliminatedImportedWorldAnnouncesStartAndFinishOnce()
    {
        var token = PlayerCredentials.GenerateToken();
        var session = new MatchSessionCreation(Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(), PlayerCredentials.HashToken(token), PlayerCredentials.GenerateJoinCode());
        var sessions = new PostgresMatchSessionRepository(database.DataSource);
        await sessions.CreateAsync(session, now =>
        {
            var home = Guid.NewGuid();
            return new WorldState(new GameMap([new Location(home, "Home")], []),
                [new Army(Guid.NewGuid(), session.FirstPlayerId, home, [new Unit(Guid.NewGuid(), 100, 0)]),
                 new Army(Guid.NewGuid(), session.SecondPlayerId, home, [])], now);
        });
        await sessions.JoinAsync(session.JoinCode, PlayerCredentials.HashToken(PlayerCredentials.GenerateToken()));
        var feed = await new PostgresMatchRepository(database.DataSource).ReadUpdatesAsync(session.MatchId, 0);
        Assert.Equal(new[] { "MatchStarted", "MatchFinished" }, feed.Updates.Select(u => u.Type));
        await new PostgresMatchRepository(database.DataSource).LoadCurrentAsync(session.MatchId);
        var again = await new PostgresMatchRepository(database.DataSource).ReadUpdatesAsync(session.MatchId, 0);
        Assert.Single(again.Updates, u => u.Type == "MatchFinished");
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
    public async Task CommandCrossingFinishRollsBack_ThenSerializedCommandObservesCommittedFinish()
    {
        var (session, token) = await Prepared(1);
        await using var factory = new PostgresApiFactory(database.ConnectionString);
        using var client = factory.CreateClient();
        MatchSessionEndpointTests.Authorize(client, token);
        var before = await Row(session.MatchId);
        var army = new WorldSnapshotSerializer().Deserialize(before.State).Armies[0].Id;
        await MatchEndpointTests.AssertProblem(await client.PostAsync($"/api/v1/matches/{session.MatchId}/armies/{army}/retreat", null), 409);
        Assert.Equal(before, await Row(session.MatchId));

        // A lazy read holds the match lock through its asynchronous projection and commit.
        // Submit a command while that read is finishing; it must see the committed terminal world.
        var reached = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var release = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var finish = new PostgresMatchRepository(database.DataSource).ExecuteAsync(session.MatchId,
            async (_, _) => { reached.SetResult(); await release.Task; return 0; });
        await reached.Task.WaitAsync(TimeSpan.FromSeconds(10));
        var command = client.PostAsync($"/api/v1/matches/{session.MatchId}/armies/{army}/retreat", null);
        Assert.False(command.IsCompleted);
        release.SetResult();
        var committed = await finish;
        await MatchEndpointTests.AssertProblem(await command, 409);
        var serialized = await Row(session.MatchId);
        Assert.Equal(committed.Version, serialized.Version);
        Assert.Equal(committed.UpdateCursor + 1, serialized.Sequence);
        Assert.Equal(MatchPhase.Finished, (await State(client, session.MatchId)).Phase);
        var finishedRow = await Row(session.MatchId);
        await MatchEndpointTests.AssertProblem(await client.PostAsync($"/api/v1/matches/{session.MatchId}/armies/{army}/retreat", null), 409);
        Assert.Equal(finishedRow, await Row(session.MatchId));
    }
}
