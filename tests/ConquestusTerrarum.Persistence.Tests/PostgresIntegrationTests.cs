using System.Text.Json;
using ConquestusTerrarum.Domain.World;
using Npgsql;
using static ConquestusTerrarum.Testing.SnapshotWorlds;

namespace ConquestusTerrarum.Persistence.Tests;

public sealed class PostgresFactAttribute : FactAttribute
{
    public PostgresFactAttribute()
    {
        if (string.IsNullOrWhiteSpace(Environment.GetEnvironmentVariable("CT_TEST_CONNECTION_STRING")))
            Skip = "Set CT_TEST_CONNECTION_STRING to run PostgreSQL integration tests (see db/README.md).";
    }
}

public sealed class PostgresFixture : IAsyncLifetime
{
    private readonly string _schema = "ct_test_" + Guid.NewGuid().ToString("N");
    private string? _connectionString;
    public string ConnectionString { get; private set; } = null!;
    public NpgsqlDataSource DataSource { get; private set; } = null!;

    public async Task InitializeAsync()
    {
        _connectionString = Environment.GetEnvironmentVariable("CT_TEST_CONNECTION_STRING");
        if (string.IsNullOrWhiteSpace(_connectionString)) return;
        await using var connection = new NpgsqlConnection(_connectionString);
        await connection.OpenAsync();
        await using var command = new NpgsqlCommand($"CREATE SCHEMA \"{_schema}\"", connection);
        await command.ExecuteNonQueryAsync();
        var builder = new NpgsqlConnectionStringBuilder(_connectionString) { SearchPath = _schema };
        ConnectionString = builder.ConnectionString;
        DataSource = NpgsqlDataSource.Create(ConnectionString);
        await new PostgresMigrator(DataSource).MigrateAsync();
    }

    public async Task DisposeAsync()
    {
        if (DataSource is null) return;
        await DataSource.DisposeAsync();
        await using var connection = new NpgsqlConnection(_connectionString);
        await connection.OpenAsync();
        // Only the unique schema created by this fixture is removed.
        await using var command = new NpgsqlCommand($"DROP SCHEMA \"{_schema}\" CASCADE", connection);
        await command.ExecuteNonQueryAsync();
    }
}

public sealed class PostgresTheoryAttribute : TheoryAttribute
{
    public PostgresTheoryAttribute()
    {
        if (string.IsNullOrWhiteSpace(Environment.GetEnvironmentVariable("CT_TEST_CONNECTION_STRING")))
            Skip = "Set CT_TEST_CONNECTION_STRING to run PostgreSQL integration tests (see db/README.md).";
    }
}

public sealed class PostgresIntegrationTests(PostgresFixture database) : IClassFixture<PostgresFixture>
{
    private readonly WorldSnapshotSerializer _serializer = new();
    private PostgresMatchRepository Repository => new(database.DataSource);

    private static WorldState EmptyWorld()
    {
        return new WorldState(new GameMap([new Location(Id(1), "Home"), new Location(Id(2), "Next")],
                [new MapConnection(Id(1), Id(2), TimeSpan.FromSeconds(30))]), [Army(11, 21, 1, Unit(101))],
            new DateTimeOffset(2000, 1, 1, 0, 0, 0, TimeSpan.Zero));
    }

    private async Task<(string Json, long Version, DateTimeOffset Created, DateTimeOffset Updated)> Row(Guid id)
    {
        await using var command =
            database.DataSource.CreateCommand(
                "SELECT state::text, version, created_at, updated_at FROM matches WHERE id = $1");
        command.Parameters.AddWithValue(id);
        await using var reader = await command.ExecuteReaderAsync();
        Assert.True(await reader.ReadAsync());
        return (reader.GetString(0), reader.GetInt64(1), new DateTimeOffset(reader.GetDateTime(2)),
            new DateTimeOffset(reader.GetDateTime(3)));
    }

    [PostgresFact]
    [Trait("Category", "Postgres")]
    public async Task CreateStoresExactInitialState_AndCurrentReadAdvancesAndPersists()
    {
        var id = Guid.NewGuid();
        var world = Combat();
        await Repository.CreateAsync(id, world);
        var initial = await Row(id);
        Assert.Equal(_serializer.Serialize(world.CreateSnapshot()),
            _serializer.Serialize(_serializer.Deserialize(initial.Json)));
        Assert.Equal(0, initial.Version);
        Assert.Equal(initial.Created, initial.Updated);
        var current = await Repository.LoadCurrentAsync(id);
        WorldSimulator.AdvanceTo(world, current.DatabaseTime);
        Assert.Equal(_serializer.Serialize(world.CreateSnapshot()), _serializer.Serialize(current.Value));
        Assert.Equal(current.DatabaseTime, current.Value.CheckpointTime);
        var persisted = await Row(id);
        Assert.Equal(1, current.Version);
        Assert.Equal(1, persisted.Version);
        Assert.Equal(current.DatabaseTime, persisted.Updated);
        Assert.Equal(_serializer.Serialize(current.Value),
            _serializer.Serialize(_serializer.Deserialize(persisted.Json)));
    }

    [PostgresFact]
    [Trait("Category", "Postgres")]
    public async Task CapturedDatabaseTimeIsUsedForCheckpointCommandAndUpdate()
    {
        var id = Guid.NewGuid();
        await Repository.CreateAsync(id, EmptyWorld());
        var result = await Repository.ExecuteAsync(id, async (world, time) =>
        {
            Assert.Equal(time, world.CheckpointTime);
            world.StartMovement(Id(11), Id(2), time);
            // Deliberately spend time in the callback; save must not sample the clock again.
            await using var delay = database.DataSource.CreateCommand("SELECT pg_sleep(0.05)");
            await delay.ExecuteNonQueryAsync();
            return world.GetArmy(Id(11)).Movement!.StartedAt;
        });
        var row = await Row(id);
        var snapshot = _serializer.Deserialize(row.Json);
        Assert.Equal(result.DatabaseTime, result.Value);
        Assert.Equal(result.DatabaseTime, row.Updated);
        Assert.Equal(result.DatabaseTime, snapshot.CheckpointTime);
        Assert.Equal(result.DatabaseTime.AddSeconds(30), snapshot.Armies[0].Movement!.ArriveAt);
    }

    [PostgresFact]
    [Trait("Category", "Postgres")]
    public async Task ThrownOperationRollsBackBothLazyAdvanceAndCommand()
    {
        var id = Guid.NewGuid();
        await Repository.CreateAsync(id, EmptyWorld());
        var before = await Row(id);
        await Assert.ThrowsAsync<InvalidOperationException>(() => Repository.ExecuteAsync<int>(id, (world, time) =>
        {
            world.StartMovement(Id(11), Id(2), time);
            throw new InvalidOperationException("Command failed.");
        }));
        Assert.Equal(before, await Row(id));
    }

    [PostgresFact]
    [Trait("Category", "Postgres")]
    public async Task SameMatchOperationsWaitForRowLock_AndCaptureTimeAfterWaiting()
    {
        var id = Guid.NewGuid();
        await Repository.CreateAsync(id, EmptyWorld());
        var entered = new TaskCompletionSource<DateTimeOffset>(TaskCreationOptions.RunContinuationsAsynchronously);
        var release = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(15));
        var first = Repository.ExecuteAsync(id, async (world, time) =>
        {
            world.StartMovement(Id(11), Id(2), time);
            entered.SetResult(time);
            await release.Task.WaitAsync(timeout.Token);
            return 1;
        }, timeout.Token);
        await entered.Task.WaitAsync(timeout.Token);
        var secondEntered = false;
        var second = Repository.ExecuteAsync(id, (world, time) =>
        {
            secondEntered = true;
            Assert.True(world.GetArmy(Id(11)).IsMoving);
            return Task.FromResult(time);
        }, timeout.Token);
        DateTimeOffset releaseLowerBound = default;
        try
        {
            // Observe a real PostgreSQL lock wait, not an assumed scheduling delay.
            await WaitForBlockedTransaction(timeout.Token);
            Assert.False(secondEntered);
            Assert.Equal(0, (await Row(id)).Version); // First transaction is still uncommitted.
            await using var clock = database.DataSource.CreateCommand("SELECT clock_timestamp()");
            releaseLowerBound = new DateTimeOffset((DateTime)(await clock.ExecuteScalarAsync(timeout.Token))!);
        }
        finally
        {
            release.TrySetResult();
        }

        var firstResult = await first;
        var secondResult = await second;
        Assert.True(secondResult.DatabaseTime >= releaseLowerBound);
        Assert.True(secondResult.DatabaseTime >= firstResult.DatabaseTime);
        Assert.Equal(1, firstResult.Version);
        Assert.Equal(2, secondResult.Version);
        Assert.Equal(2, (await Row(id)).Version);
    }

    private async Task WaitForBlockedTransaction(
        CancellationToken cancellationToken)
    {
        while (true)
        {
            await using var command =
                database.DataSource.CreateCommand(
                    """
                    SELECT EXISTS (
                        SELECT 1
                        FROM pg_stat_activity
                        WHERE datname = current_database()
                          AND wait_event_type = 'Lock'
                          AND query ILIKE '%FROM matches%'
                          AND query ILIKE '%FOR UPDATE%'
                    )
                    """);

            if ((bool)(await command.ExecuteScalarAsync(
                    cancellationToken))!)
                return;

            await Task.Delay(
                10,
                cancellationToken);
        }
    }

    [PostgresFact]
    [Trait("Category", "Postgres")]
    public async Task DifferentMatchesDoNotShareTheRowLock()
    {
        var firstId = Guid.NewGuid();
        var secondId = Guid.NewGuid();
        await Repository.CreateAsync(firstId, EmptyWorld());
        await Repository.CreateAsync(secondId, EmptyWorld());
        var entered = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var release = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(10));
        var first = Repository.ExecuteAsync(firstId, async (_, _) =>
        {
            entered.SetResult();
            await release.Task.WaitAsync(timeout.Token);
            return 0;
        }, timeout.Token);
        await entered.Task.WaitAsync(timeout.Token);
        try
        {
            Assert.Equal(1, (await Repository.LoadCurrentAsync(secondId, timeout.Token)).Version);
        }
        finally
        {
            release.TrySetResult();
            await first;
        }
    }

    [PostgresFact]
    [Trait("Category", "Postgres")]
    public async Task MigrationsCanRunRepeatedlyAndConcurrently()
    {
        await Task.WhenAll(new PostgresMigrator(database.DataSource).MigrateAsync(),
            new PostgresMigrator(database.DataSource).MigrateAsync());
        await using var command = database.DataSource.CreateCommand("SELECT count(*) FROM schema_migrations");
        Assert.Equal(3L, await command.ExecuteScalarAsync());
    }

    [PostgresFact]
    [Trait("Category", "Postgres")]
    public async Task MissingAndCorruptMatchesProduceClearErrors()
    {
        await Assert.ThrowsAsync<MatchNotFoundException>(() => Repository.LoadCurrentAsync(Guid.NewGuid()));
        var id = Guid.NewGuid();
        await Repository.CreateAsync(id, EmptyWorld());
        await using var corrupt =
            database.DataSource.CreateCommand("UPDATE matches SET state = '{}'::jsonb WHERE id = $1");
        corrupt.Parameters.AddWithValue(id);
        await corrupt.ExecuteNonQueryAsync();
        await Assert.ThrowsAsync<InvalidWorldStateException>(() => Repository.LoadCurrentAsync(id));
        Assert.Equal(0, (await Row(id)).Version);
    }

    [PostgresFact]
    [Trait("Category", "Postgres")]
    public async Task CancellationRollsBack()
    {
        var id = Guid.NewGuid();
        await Repository.CreateAsync(id, EmptyWorld());
        var before = await Row(id);
        using var cancellation = new CancellationTokenSource();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => Repository.ExecuteAsync(id, (world, time) =>
        {
            world.StartMovement(Id(11), Id(2), time);
            cancellation.Cancel();
            return Task.FromResult(1);
        }, cancellation.Token));
        Assert.Equal(before, await Row(id));
    }

    [PostgresFact]
    [Trait("Category", "Postgres")]
    public async Task CallbackCannotAdvanceToAnApplicationClockTimestamp()
    {
        var id = Guid.NewGuid();
        await Repository.CreateAsync(id, EmptyWorld());
        var before = await Row(id);
        await Assert.ThrowsAsync<InvalidOperationException>(() => Repository.ExecuteAsync(id, (world, time) =>
        {
            WorldSimulator.AdvanceTo(world, time.AddSeconds(1));
            return Task.FromResult(0);
        }));
        Assert.Equal(before, await Row(id));
    }

    [PostgresFact]
    [Trait("Category", "Postgres")]
    public async Task PersistedCheckpointAheadOfDatabaseTimeIsRejectedWithoutMutation()
    {
        var id = Guid.NewGuid();
        await Repository.CreateAsync(id, EmptyWorld());
        var future = EmptyWorld();
        WorldSimulator.AdvanceTo(future, DateTimeOffset.MaxValue);
        await using var command =
            database.DataSource.CreateCommand("UPDATE matches SET state = $1::jsonb WHERE id = $2");
        command.Parameters.AddWithValue(_serializer.Serialize(future.CreateSnapshot()));
        command.Parameters.AddWithValue(id);
        await command.ExecuteNonQueryAsync();
        var before = await Row(id);
        await Assert.ThrowsAsync<DatabaseTimeBehindWorldException>(() => Repository.LoadCurrentAsync(id));
        Assert.Equal(before, await Row(id));
    }

    [PostgresFact]
    [Trait("Category", "Postgres")]
    public async Task CreationPreservesSubMicrosecondTimesAndDecimalExtremesInJsonb()
    {
        var id = Guid.NewGuid();
        var start = Start.AddTicks(1);
        var connection = new MapConnection(Id(1), Id(2), TimeSpan.FromTicks(17));
        var army = Army(11, 21, 1, Unit(101, decimal.MaxValue, 0.0000000000000000000000000001m));
        army.StartMovement(connection, start);
        var world = new WorldState(new GameMap([new Location(Id(1), "One"), new Location(Id(2), "Two")], [connection]),
            [army], start,
            randomSeed: ulong.MaxValue);
        await Repository.CreateAsync(id, world);
        var saved = _serializer.Deserialize((await Row(id)).Json);
        Assert.Equal(_serializer.Serialize(world.CreateSnapshot()), _serializer.Serialize(saved));
        Assert.Equal(start.Ticks, saved.CheckpointTime.Ticks);
        Assert.Equal(start.AddTicks(17).Ticks, saved.Armies[0].Movement!.ArriveAt.Ticks);
    }

    [PostgresFact]
    [Trait("Category", "Postgres")]
    public async Task FactoryCreationUsesOneDatabaseTimestampForInitialWorldAndMetadata()
    {
        var id = Guid.NewGuid();
        DateTimeOffset captured = default;
        WorldSnapshot initial = null!;
        var calls = 0;
        await using var clock = database.DataSource.CreateCommand("SELECT clock_timestamp()");
        var before = new DateTimeOffset((DateTime)(await clock.ExecuteScalarAsync())!);
        await Repository.CreateAsync(id, databaseTime =>
        {
            captured = databaseTime;
            calls++;
            var world = new WorldState(new GameMap([new Location(Id(1), "Battlefield")], []),
                [Army(11, 21, 1, Unit(101)), Army(12, 22, 1, Unit(102))], databaseTime);
            initial = world.CreateSnapshot();
            return world;
        });
        var after = new DateTimeOffset((DateTime)(await clock.ExecuteScalarAsync())!);
        Assert.Equal(1, calls);
        Assert.InRange(captured, before, after);
        var row = await Row(id);
        var saved = _serializer.Deserialize(row.Json);
        Assert.Equal(captured, saved.CheckpointTime);
        Assert.Equal(captured, row.Created);
        Assert.Equal(captured, row.Updated);
        Assert.Equal(0, row.Version);
        Assert.Equal(_serializer.Serialize(initial), _serializer.Serialize(saved));
        Assert.All(saved.Armies, army => Assert.Equal(captured, army.EnteredLocationAt));
        Assert.Equal(captured, saved.Battles.Single().StartedAt);

        // The first read advances only elapsed database time from the factory checkpoint.
        // There is no application-clock checkpoint to correct or reject.
        var current = await Repository.LoadCurrentAsync(id);
        Assert.True(current.DatabaseTime >= captured);
        var expected = WorldState.Restore(initial);
        WorldSimulator.AdvanceTo(expected, current.DatabaseTime);
        Assert.Equal(_serializer.Serialize(expected.CreateSnapshot()), _serializer.Serialize(current.Value));
        Assert.Equal(current.DatabaseTime, current.Value.CheckpointTime);
        Assert.Equal(1, current.Version);
        Assert.Equal(captured, (await Row(id)).Created);
    }

    [PostgresFact]
    [Trait("Category", "Postgres")]
    public async Task FactoryCheckpointMustEqualDatabaseTime_EvenWhenEarlier()
    {
        foreach (var offset in new[] { -1, 1 })
        {
            var id = Guid.NewGuid();
            await Assert.ThrowsAsync<InvalidOperationException>(() => Repository.CreateAsync(id, time =>
                new WorldState(new GameMap([new Location(Id(1), "Home")], []), [], time.AddTicks(offset))));
            await Assert.ThrowsAsync<MatchNotFoundException>(() => Repository.LoadCurrentAsync(id));
        }
    }

    [PostgresFact]
    [Trait("Category", "Postgres")]
    public async Task FailedOrNullFactoryDoesNotCreateMatch()
    {
        var id = Guid.NewGuid();
        var failure = new InvalidOperationException("Factory failed.");
        Assert.Same(failure, await Assert.ThrowsAsync<InvalidOperationException>(() =>
            Repository.CreateAsync(id, _ => throw failure)));
        await Assert.ThrowsAsync<InvalidOperationException>(() => Repository.CreateAsync(id, _ => null!));
        await Assert.ThrowsAsync<MatchNotFoundException>(() => Repository.LoadCurrentAsync(id));
        await Repository.CreateAsync(id, time => new WorldState(new GameMap([], []), [], time));
        Assert.Equal(0, (await Row(id)).Version);
    }

    [PostgresFact]
    [Trait("Category", "Postgres")]
    public async Task ReadUpdatesUsesExclusiveCursorAndSupportsPaging()
    {
        var id = Guid.NewGuid();

        await Repository.CreateAsync(
            id,
            EmptyWorld());

        var occurredAt =
            new DateTimeOffset(
                2026,
                1,
                1,
                12,
                0,
                0,
                TimeSpan.Zero);

        await using (var command =
                     database.DataSource.CreateCommand(
                         """
                         INSERT INTO match_updates
                             (match_id, sequence, occurred_at, type, payload)
                         VALUES
                             ($1, 1, $2, 'ArmyMovementStarted',
                              '{"armyId":"00000000-0000-0000-0000-000000000011"}'::jsonb),
                             ($1, 2, $2, 'ArmyArrived',
                              '{"armyId":"00000000-0000-0000-0000-000000000011"}'::jsonb),
                             ($1, 3, $2, 'BattleStarted',
                              '{"locationId":"00000000-0000-0000-0000-000000000001"}'::jsonb)
                         """))
        {
            command.Parameters.AddWithValue(id);
            command.Parameters.AddWithValue(occurredAt);

            await command.ExecuteNonQueryAsync();
        }

        await using (var command =
                     database.DataSource.CreateCommand(
                         """
                         UPDATE matches
                         SET next_update_sequence = 4
                         WHERE id = $1
                         """))
        {
            command.Parameters.AddWithValue(id);

            await command.ExecuteNonQueryAsync();
        }

        var all =
            await Repository.ReadUpdatesAsync(
                id,
                0);

        Assert.False(all.HasMore);
        Assert.Equal(3, all.NextCursor);
        Assert.Equal(
            [1L, 2L, 3L],
            all.Updates
                .Select(update => update.Sequence)
                .ToArray());

        Assert.Equal(
            [
                "ArmyMovementStarted",
                "ArmyArrived",
                "BattleStarted"
            ],
            all.Updates
                .Select(update => update.Type)
                .ToArray());

        var afterFirst =
            await Repository.ReadUpdatesAsync(
                id,
                1);

        Assert.False(afterFirst.HasMore);
        Assert.Equal(3, afterFirst.NextCursor);
        Assert.Equal(
            [2L, 3L],
            afterFirst.Updates
                .Select(update => update.Sequence)
                .ToArray());

        var noChanges =
            await Repository.ReadUpdatesAsync(
                id,
                3);

        Assert.False(noChanges.HasMore);
        Assert.Equal(3, noChanges.NextCursor);
        Assert.Empty(noChanges.Updates);

        var firstPage =
            await Repository.ReadUpdatesAsync(
                id,
                0,
                1);

        Assert.True(firstPage.HasMore);
        Assert.Equal(1, firstPage.NextCursor);
        Assert.Single(firstPage.Updates);
        Assert.Equal(
            1,
            firstPage.Updates[0].Sequence);

        var secondPage =
            await Repository.ReadUpdatesAsync(
                id,
                firstPage.NextCursor,
                1);

        Assert.True(secondPage.HasMore);
        Assert.Equal(2, secondPage.NextCursor);
        Assert.Single(secondPage.Updates);
        Assert.Equal(
            2,
            secondPage.Updates[0].Sequence);
    }

    [PostgresFact]
    [Trait("Category", "Postgres")]
    public async Task ReadUpdatesDistinguishesMissingMatchFromEmptyFeed()
    {
        var existingId =
            Guid.NewGuid();

        await Repository.CreateAsync(
            existingId,
            EmptyWorld());

        var empty =
            await Repository.ReadUpdatesAsync(
                existingId,
                0);

        Assert.Empty(empty.Updates);
        Assert.Equal(0, empty.NextCursor);
        Assert.False(empty.HasMore);

        await Assert.ThrowsAsync<MatchNotFoundException>(() =>
            Repository.ReadUpdatesAsync(
                Guid.NewGuid(),
                0));
    }

    [PostgresFact]
    [Trait("Category", "Postgres")]
    public async Task MoveCommandCreatesArmyMovementStartedUpdate()
    {
        var id = Guid.NewGuid();

        await Repository.CreateAsync(
            id,
            EmptyWorld());

        var result =
            await Repository.ExecuteAsync(
                id,
                (world, time) =>
                {
                    world.StartMovement(
                        Id(11),
                        Id(2),
                        time);

                    return Task.FromResult(0);
                });

        var page =
            await Repository.ReadUpdatesAsync(
                id,
                0);

        Assert.False(page.HasMore);
        Assert.Equal(1, page.NextCursor);

        var update =
            Assert.Single(page.Updates);

        Assert.Equal(
            1,
            update.Sequence);

        Assert.Equal(
            "ArmyMovementStarted",
            update.Type);

        Assert.Equal(
            result.DatabaseTime.ToUniversalTime(),
            update.OccurredAt.ToUniversalTime());

        using var payload =
            JsonDocument.Parse(
                update.PayloadJson);

        var root =
            payload.RootElement;

        Assert.Equal(
            Id(11),
            root.GetProperty("armyId").GetGuid());

        Assert.Equal(
            Id(21),
            root.GetProperty("ownerId").GetGuid());

        Assert.Equal(
            Id(1),
            root.GetProperty("fromLocationId").GetGuid());

        Assert.Equal(
            Id(2),
            root.GetProperty("toLocationId").GetGuid());
    }

    [PostgresFact]
    [Trait("Category", "Postgres")]
    public async Task LazyArrivalCreatesArrivalAndBattleStartedUpdates()
    {
        var id =
            Guid.NewGuid();

        await Repository.CreateAsync(
            id,
            databaseTime =>
            {
                var connection =
                    new MapConnection(
                        Id(1),
                        Id(2),
                        TimeSpan.FromMilliseconds(5));

                var movingArmy =
                    Army(
                        11,
                        21,
                        1,
                        Unit(
                            101,
                            dps: 0));

                movingArmy.StartMovement(
                    connection,
                    databaseTime);

                var enemyArmy =
                    Army(
                        12,
                        22,
                        2,
                        Unit(
                            102,
                            dps: 0));

                return new WorldState(
                    new GameMap(
                        [
                            new Location(
                                Id(1),
                                "Origin"),
                            new Location(
                                Id(2),
                                "Battlefield")
                        ],
                        [
                            connection
                        ]),
                    [
                        movingArmy,
                        enemyArmy
                    ],
                    databaseTime,
                    randomSeed: 123);
            });

        // Wait using PostgreSQL itself so the test does not
        // depend on the application machine's clock.
        await using (var delay =
                     database.DataSource.CreateCommand(
                         "SELECT pg_sleep(0.02)"))
        {
            await delay.ExecuteNonQueryAsync();
        }

        var current =
            await Repository.LoadCurrentAsync(id);

        var movingArmyAfter =
            current.Value.Armies.Single(army =>
                army.Id == Id(11));

        Assert.Null(
            movingArmyAfter.Movement);

        Assert.Equal(
            Id(2),
            movingArmyAfter.LocationId);

        Assert.Single(
            current.Value.Battles);

        var page =
            await Repository.ReadUpdatesAsync(
                id,
                0);

        Assert.False(page.HasMore);
        Assert.Equal(2, page.NextCursor);
        Assert.Equal(2, page.Updates.Count);

        Assert.Equal(
            "ArmyArrived",
            page.Updates[0].Type);

        Assert.Equal(
            "BattleStarted",
            page.Updates[1].Type);

        Assert.Equal(
            1,
            page.Updates[0].Sequence);

        Assert.Equal(
            2,
            page.Updates[1].Sequence);

        using var arrivalPayload =
            JsonDocument.Parse(
                page.Updates[0].PayloadJson);

        Assert.Equal(
            Id(11),
            arrivalPayload.RootElement
                .GetProperty("armyId")
                .GetGuid());

        Assert.Equal(
            Id(2),
            arrivalPayload.RootElement
                .GetProperty("locationId")
                .GetGuid());

        using var battlePayload =
            JsonDocument.Parse(
                page.Updates[1].PayloadJson);

        Assert.Equal(
            Id(2),
            battlePayload.RootElement
                .GetProperty("locationId")
                .GetGuid());
    }
    
    [PostgresFact]
    [Trait("Category", "Postgres")]
    public async Task QueryCurrentDoesNotPersistContinuousCombatDamage()
    {
        var id =
            Guid.NewGuid();

        await Repository.CreateAsync(
            id,
            databaseTime =>
                new WorldState(
                    new GameMap(
                        [
                            new(
                                Id(1),
                                "Battlefield")
                        ],
                        []),
                    [
                        Army(
                            11,
                            21,
                            1,
                            Unit(
                                101,
                                hp: 100,
                                dps: 10)),
                        Army(
                            12,
                            22,
                            1,
                            Unit(
                                102,
                                hp: 100,
                                dps: 10))
                    ],
                    databaseTime,
                    randomSeed: 123));

        var before =
            await Row(id);

        await using (var delay =
                     database.DataSource.CreateCommand(
                         "SELECT pg_sleep(0.05)"))
        {
            await delay.ExecuteNonQueryAsync();
        }

        var result =
            await Repository.QueryCurrentAsync(
                id,
                (snapshot, _) =>
                    snapshot.Armies
                        .Single(
                            army =>
                                army.Id == Id(11))
                        .Units
                        .Single()
                        .CurrentHitPoints);

        Assert.True(
            result.Value < 100m);

        // Live query saw current HP...
        Assert.Equal(
            0,
            result.Version);

        // ...but continuous damage alone did not rewrite
        // the authoritative PostgreSQL snapshot.
        Assert.Equal(
            before,
            await Row(id));

        for (var attempt = 0; attempt < 3; attempt++)
        {
            var live = await Repository.QueryCurrentAsync(id, (snapshot, _) =>
                snapshot.Armies.Single(army => army.Id == Id(11)).Units.Single().CurrentHitPoints);
            Assert.True(live.Value <= result.Value);
            Assert.Equal(0, live.Version);
            Assert.Equal(0, live.UpdateCursor);
            Assert.Equal(before, await Row(id));
        }
        Assert.Empty((await Repository.ReadUpdatesAsync(id, 0)).Updates);
        Assert.Equal(before, await Row(id));
    }

    [PostgresTheory]
    [InlineData(false)]
    [InlineData(true)]
    [Trait("Category", "Postgres")]
    public async Task QueryCurrentCommitsMeaningfulTransitionAndFeedAtomicallyOrRollsBack(bool failQuery)
    {
        var id = Guid.NewGuid();
        var world = new WorldState(new GameMap([new Location(Id(1), "Battlefield")], []),
            [Army(11, 21, 1, Unit(101, hp: 1, dps: 1)), Army(12, 22, 1, Unit(102, hp: 1, dps: 1))],
            new DateTimeOffset(2000, 1, 1, 0, 0, 0, TimeSpan.Zero));
        await Repository.CreateAsync(id, world);
        var before = await Row(id);
        if (failQuery)
        {
            await Assert.ThrowsAsync<InvalidOperationException>(() => Repository.QueryCurrentAsync<int>(id,
                (_, _) => throw new InvalidOperationException("query failed")));
            Assert.Equal(before, await Row(id));
            await using var count = database.DataSource.CreateCommand("SELECT count(*) FROM match_updates WHERE match_id = $1");
            count.Parameters.AddWithValue(id);
            Assert.Equal(0L, await count.ExecuteScalarAsync());
            return;
        }
        var result = await Repository.QueryCurrentAsync(id, (snapshot, _) => snapshot);
        Assert.Equal(1, result.Version);
        Assert.Equal(3, result.UpdateCursor);
        Assert.Empty(result.Value.Battles);
        Assert.All(result.Value.Armies, army => Assert.All(army.Units, unit => Assert.Equal(0m, unit.CurrentHitPoints)));
        var saved = await Row(id);
        Assert.Equal(result.DatabaseTime, saved.Updated);
        var updates = await Repository.ReadUpdatesAsync(id, 0);
        Assert.Equal(3, updates.Updates.Count);
        Assert.Equal(2, updates.Updates.Count(update => update.Type == "ArmyDestroyed"));
        Assert.Single(updates.Updates, update => update.Type == "BattleEnded");
        var again = await Repository.QueryCurrentAsync(id, (snapshot, _) => snapshot);
        Assert.Equal(1, again.Version);
        Assert.Equal(3, again.UpdateCursor);
        Assert.Equal(saved, await Row(id));
    }
}
