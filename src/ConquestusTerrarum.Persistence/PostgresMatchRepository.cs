using System.Data;
using ConquestusTerrarum.Domain.World;
using Npgsql;
using NpgsqlTypes;

namespace ConquestusTerrarum.Persistence;

public sealed record MatchExecutionResult<T>(T Value, long Version, DateTimeOffset DatabaseTime);

/// <summary>One row and one transaction per match. The caller owns the reusable data source.</summary>
public sealed class PostgresMatchRepository(NpgsqlDataSource dataSource)
{
    private readonly WorldSnapshotSerializer _serializer = new();

    public async Task CreateAsync(Guid matchId, WorldState initialWorld, CancellationToken cancellationToken = default)
    {
        if (matchId == Guid.Empty) throw new ArgumentException("Match id cannot be empty.", nameof(matchId));
        ArgumentNullException.ThrowIfNull(initialWorld);
        var snapshot = initialWorld.CreateSnapshot();
        var json = _serializer.Serialize(snapshot);
        await using var connection = await dataSource.OpenConnectionAsync(cancellationToken);
        await using var transaction = await connection.BeginTransactionAsync(IsolationLevel.ReadCommitted, cancellationToken);
        var now = await ReadDatabaseTimeAsync(connection, transaction, cancellationToken);
        EnsureTimeIsNotBehind(now, snapshot.CheckpointTime);
        // Creation stores the supplied initial checkpoint exactly; the first current read advances it.
        await using var command = new NpgsqlCommand("""
            INSERT INTO matches (id, state, version, created_at, updated_at)
            VALUES ($1, $2, 0, $3, $3)
            """, connection, transaction);
        command.Parameters.AddWithValue(matchId);
        command.Parameters.AddWithValue(NpgsqlDbType.Jsonb, json);
        command.Parameters.AddWithValue(now);
        await command.ExecuteNonQueryAsync(cancellationToken);
        await transaction.CommitAsync(cancellationToken);
    }

    /// <summary>Current reads also lock, advance, save, and increment the version.</summary>
    public Task<MatchExecutionResult<WorldSnapshot>> LoadCurrentAsync(Guid matchId, CancellationToken cancellationToken = default) =>
        ExecuteAsync(matchId, (world, _) => Task.FromResult(world.CreateSnapshot()), cancellationToken);

    /// <summary>
    /// The callback runs while holding the match lock. Use only its captured timestamp for commands.
    /// Keep it short; do not perform external side effects or recursively execute on the same match.
    /// Exceptions/cancellation dispose the uncommitted transaction, rolling it back.
    /// </summary>
    public async Task<MatchExecutionResult<T>> ExecuteAsync<T>(Guid matchId,
        Func<WorldState, DateTimeOffset, Task<T>> operation, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(operation);
        await using var connection = await dataSource.OpenConnectionAsync(cancellationToken);
        await using var transaction = await connection.BeginTransactionAsync(IsolationLevel.ReadCommitted, cancellationToken);
        string json;
        long version;
        await using (var command = new NpgsqlCommand("SELECT state::text, version FROM matches WHERE id = $1 FOR UPDATE", connection, transaction))
        {
            command.Parameters.AddWithValue(matchId);
            await using var reader = await command.ExecuteReaderAsync(cancellationToken);
            if (!await reader.ReadAsync(cancellationToken)) throw new MatchNotFoundException(matchId);
            json = reader.GetString(0);
            version = reader.GetInt64(1);
        }
        // Capture once AFTER the lock wait, never at transaction start: an earlier waiter timestamp
        // could otherwise precede the checkpoint committed by the preceding lock holder.
        var now = await ReadDatabaseTimeAsync(connection, transaction, cancellationToken);
        var world = WorldState.Restore(_serializer.Deserialize(json));
        EnsureTimeIsNotBehind(now, world.CheckpointTime);
        WorldSimulator.AdvanceTo(world, now);
        var result = await operation(world, now);
        cancellationToken.ThrowIfCancellationRequested();
        if (world.CheckpointTime != now)
            throw new InvalidOperationException("The operation must use the captured database timestamp and must not advance beyond it.");
        var state = _serializer.Serialize(world.CreateSnapshot());
        var nextVersion = checked(version + 1);
        await using (var command = new NpgsqlCommand("UPDATE matches SET state = $1, version = $2, updated_at = $3 WHERE id = $4", connection, transaction))
        {
            command.Parameters.AddWithValue(NpgsqlDbType.Jsonb, state);
            command.Parameters.AddWithValue(nextVersion);
            command.Parameters.AddWithValue(now);
            command.Parameters.AddWithValue(matchId);
            await command.ExecuteNonQueryAsync(cancellationToken);
        }
        await transaction.CommitAsync(cancellationToken);
        return new(result, nextVersion, now);
    }

    private static void EnsureTimeIsNotBehind(DateTimeOffset now, DateTimeOffset checkpoint)
    {
        if (now < checkpoint) throw new DatabaseTimeBehindWorldException(now, checkpoint);
    }

    private static async Task<DateTimeOffset> ReadDatabaseTimeAsync(NpgsqlConnection connection,
        NpgsqlTransaction transaction, CancellationToken cancellationToken)
    {
        await using var command = new NpgsqlCommand("SELECT clock_timestamp()", connection, transaction);
        var timestamp = (DateTime)(await command.ExecuteScalarAsync(cancellationToken))!;
        return new DateTimeOffset(timestamp);
    }
}
