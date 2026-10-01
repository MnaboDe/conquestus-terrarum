using ConquestusTerrarum.Domain.World;
using ConquestusTerrarum.Testing;
using Npgsql;
using Xunit;
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
        DataSource = NpgsqlDataSource.Create(builder.ConnectionString);
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

public sealed class PostgresIntegrationTests(PostgresFixture database) : IClassFixture<PostgresFixture>
{
    private readonly WorldSnapshotSerializer _serializer = new();
    private PostgresMatchRepository Repository => new(database.DataSource);
    private static WorldState EmptyWorld() => new(new GameMap([new(Id(1), "Home"), new(Id(2), "Next")],
        [new(Id(1), Id(2), TimeSpan.FromSeconds(30))]), [Army(11, 21, 1, Unit(101))], new DateTimeOffset(2000, 1, 1, 0, 0, 0, TimeSpan.Zero));

    private async Task<(string Json, long Version, DateTimeOffset Created, DateTimeOffset Updated)> Row(Guid id)
    {
        await using var command = database.DataSource.CreateCommand("SELECT state::text, version, created_at, updated_at FROM matches WHERE id = $1");
        command.Parameters.AddWithValue(id);
        await using var reader = await command.ExecuteReaderAsync();
        Assert.True(await reader.ReadAsync());
        return (reader.GetString(0), reader.GetInt64(1), new DateTimeOffset(reader.GetDateTime(2)), new DateTimeOffset(reader.GetDateTime(3)));
    }

    [PostgresFact]
    [Trait("Category", "Postgres")]
    public async Task CreateStoresExactInitialState_AndCurrentReadAdvancesAndPersists()
    {
        var id = Guid.NewGuid(); var world = Combat();
        await Repository.CreateAsync(id, world);
        var initial = await Row(id);
        Assert.Equal(_serializer.Serialize(world.CreateSnapshot()), _serializer.Serialize(_serializer.Deserialize(initial.Json)));
        Assert.Equal(0, initial.Version); Assert.Equal(initial.Created, initial.Updated);
        var current = await Repository.LoadCurrentAsync(id);
        WorldSimulator.AdvanceTo(world, current.DatabaseTime);
        Assert.Equal(_serializer.Serialize(world.CreateSnapshot()), _serializer.Serialize(current.Value));
        Assert.Equal(current.DatabaseTime, current.Value.CheckpointTime);
        var persisted = await Row(id);
        Assert.Equal(1, current.Version); Assert.Equal(1, persisted.Version);
        Assert.Equal(current.DatabaseTime, persisted.Updated);
        Assert.Equal(_serializer.Serialize(current.Value), _serializer.Serialize(_serializer.Deserialize(persisted.Json)));
    }

    [PostgresFact]
    [Trait("Category", "Postgres")]
    public async Task CapturedDatabaseTimeIsUsedForCheckpointCommandAndUpdate()
    {
        var id = Guid.NewGuid(); await Repository.CreateAsync(id, EmptyWorld());
        var result = await Repository.ExecuteAsync(id, async (world, time) =>
        {
            Assert.Equal(time, world.CheckpointTime);
            world.StartMovement(Id(11), Id(2), time);
            // Deliberately spend time in the callback; save must not sample the clock again.
            await using var delay = database.DataSource.CreateCommand("SELECT pg_sleep(0.05)");
            await delay.ExecuteNonQueryAsync();
            return world.GetArmy(Id(11)).Movement!.StartedAt;
        });
        var row = await Row(id); var snapshot = _serializer.Deserialize(row.Json);
        Assert.Equal(result.DatabaseTime, result.Value);
        Assert.Equal(result.DatabaseTime, row.Updated);
        Assert.Equal(result.DatabaseTime, snapshot.CheckpointTime);
        Assert.Equal(result.DatabaseTime.AddSeconds(30), snapshot.Armies[0].Movement!.ArriveAt);
    }

    [PostgresFact]
    [Trait("Category", "Postgres")]
    public async Task ThrownOperationRollsBackBothLazyAdvanceAndCommand()
    {
        var id = Guid.NewGuid(); await Repository.CreateAsync(id, EmptyWorld());
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
        var id = Guid.NewGuid(); await Repository.CreateAsync(id, EmptyWorld());
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
        finally { release.TrySetResult(); }
        var firstResult = await first;
        var secondResult = await second;
        Assert.True(secondResult.DatabaseTime >= releaseLowerBound);
        Assert.True(secondResult.DatabaseTime >= firstResult.DatabaseTime);
        Assert.Equal(1, firstResult.Version); Assert.Equal(2, secondResult.Version);
        Assert.Equal(2, (await Row(id)).Version);
    }

    private async Task WaitForBlockedTransaction(CancellationToken cancellationToken)
    {
        while (true)
        {
            await using var command = database.DataSource.CreateCommand("""
                SELECT EXISTS (SELECT 1 FROM pg_stat_activity
                WHERE datname = current_database() AND wait_event_type = 'Lock'
                  AND query LIKE 'SELECT state::text, version FROM matches%')
                """);
            if ((bool)(await command.ExecuteScalarAsync(cancellationToken))!) return;
            await Task.Delay(10, cancellationToken); // Test synchronization only; never simulation.
        }
    }

    [PostgresFact]
    [Trait("Category", "Postgres")]
    public async Task DifferentMatchesDoNotShareTheRowLock()
    {
        var firstId = Guid.NewGuid(); var secondId = Guid.NewGuid();
        await Repository.CreateAsync(firstId, EmptyWorld()); await Repository.CreateAsync(secondId, EmptyWorld());
        var entered = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var release = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(10));
        var first = Repository.ExecuteAsync(firstId, async (_, _) =>
        {
            entered.SetResult(); await release.Task.WaitAsync(timeout.Token); return 0;
        }, timeout.Token);
        await entered.Task.WaitAsync(timeout.Token);
        try { Assert.Equal(1, (await Repository.LoadCurrentAsync(secondId, timeout.Token)).Version); }
        finally { release.TrySetResult(); await first; }
    }

    [PostgresFact]
    [Trait("Category", "Postgres")]
    public async Task MigrationsCanRunRepeatedlyAndConcurrently()
    {
        await Task.WhenAll(new PostgresMigrator(database.DataSource).MigrateAsync(), new PostgresMigrator(database.DataSource).MigrateAsync());
        await using var command = database.DataSource.CreateCommand("SELECT count(*) FROM schema_migrations");
        Assert.Equal(1L, await command.ExecuteScalarAsync());
    }

    [PostgresFact]
    [Trait("Category", "Postgres")]
    public async Task MissingAndCorruptMatchesProduceClearErrors()
    {
        await Assert.ThrowsAsync<MatchNotFoundException>(() => Repository.LoadCurrentAsync(Guid.NewGuid()));
        var id = Guid.NewGuid(); await Repository.CreateAsync(id, EmptyWorld());
        await using var corrupt = database.DataSource.CreateCommand("UPDATE matches SET state = '{}'::jsonb WHERE id = $1");
        corrupt.Parameters.AddWithValue(id); await corrupt.ExecuteNonQueryAsync();
        await Assert.ThrowsAsync<InvalidWorldStateException>(() => Repository.LoadCurrentAsync(id));
        Assert.Equal(0, (await Row(id)).Version);
    }

    [PostgresFact]
    [Trait("Category", "Postgres")]
    public async Task CancellationRollsBack()
    {
        var id = Guid.NewGuid(); await Repository.CreateAsync(id, EmptyWorld()); var before = await Row(id);
        using var cancellation = new CancellationTokenSource();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => Repository.ExecuteAsync(id, (world, time) =>
        {
            world.StartMovement(Id(11), Id(2), time); cancellation.Cancel(); return Task.FromResult(1);
        }, cancellation.Token));
        Assert.Equal(before, await Row(id));
    }

    [PostgresFact]
    [Trait("Category", "Postgres")]
    public async Task CallbackCannotAdvanceToAnApplicationClockTimestamp()
    {
        var id = Guid.NewGuid(); await Repository.CreateAsync(id, EmptyWorld()); var before = await Row(id);
        await Assert.ThrowsAsync<InvalidOperationException>(() => Repository.ExecuteAsync(id, (world, time) =>
        {
            WorldSimulator.AdvanceTo(world, time.AddSeconds(1)); return Task.FromResult(0);
        }));
        Assert.Equal(before, await Row(id));
    }

    [PostgresFact]
    [Trait("Category", "Postgres")]
    public async Task PersistedCheckpointAheadOfDatabaseTimeIsRejectedWithoutMutation()
    {
        var id = Guid.NewGuid(); await Repository.CreateAsync(id, EmptyWorld());
        var future = EmptyWorld(); WorldSimulator.AdvanceTo(future, DateTimeOffset.MaxValue);
        await using var command = database.DataSource.CreateCommand("UPDATE matches SET state = $1::jsonb WHERE id = $2");
        command.Parameters.AddWithValue(_serializer.Serialize(future.CreateSnapshot()));
        command.Parameters.AddWithValue(id); await command.ExecuteNonQueryAsync();
        var before = await Row(id);
        await Assert.ThrowsAsync<DatabaseTimeBehindWorldException>(() => Repository.LoadCurrentAsync(id));
        Assert.Equal(before, await Row(id));
    }

    [PostgresFact]
    [Trait("Category", "Postgres")]
    public async Task CreationPreservesSubMicrosecondTimesAndDecimalExtremesInJsonb()
    {
        var id = Guid.NewGuid(); var start = Start.AddTicks(1);
        var connection = new MapConnection(Id(1), Id(2), TimeSpan.FromTicks(17));
        var army = Army(11, 21, 1, Unit(101, decimal.MaxValue, 0.0000000000000000000000000001m));
        army.StartMovement(connection, start);
        var world = new WorldState(new GameMap([new(Id(1), "One"), new(Id(2), "Two")], [connection]), [army], start, randomSeed: ulong.MaxValue);
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
            var world = new WorldState(new GameMap([new(Id(1), "Battlefield")], []),
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
                new WorldState(new GameMap([new(Id(1), "Home")], []), [], time.AddTicks(offset))));
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
}
