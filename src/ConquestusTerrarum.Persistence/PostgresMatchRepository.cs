using System.Data;
using ConquestusTerrarum.Domain.World;
using Npgsql;
using NpgsqlTypes;

namespace ConquestusTerrarum.Persistence;

public sealed record MatchExecutionResult<T>(
    T Value,
    long Version,
    DateTimeOffset DatabaseTime);

/// <summary>
/// One row and one transaction per match.
/// The caller owns the reusable data source.
/// </summary>
public sealed class PostgresMatchRepository(
    NpgsqlDataSource dataSource) : IMatchRepository
{
    private readonly WorldSnapshotSerializer _serializer = new();

    /// <summary>
    /// Normal production creation: the factory constructs the world
    /// at the captured database time.
    /// </summary>
    public async Task CreateAsync(
        Guid matchId,
        Func<DateTimeOffset, WorldState> worldFactory,
        CancellationToken cancellationToken = default)
    {
        ValidateMatchId(matchId);
        ArgumentNullException.ThrowIfNull(worldFactory);

        await using var connection =
            await dataSource.OpenConnectionAsync(
                cancellationToken);

        await using var transaction =
            await connection.BeginTransactionAsync(
                IsolationLevel.ReadCommitted,
                cancellationToken);

        var now =
            await ReadDatabaseTimeAsync(
                connection,
                transaction,
                cancellationToken);

        var world =
            worldFactory(now)
            ?? throw new InvalidOperationException(
                "The world factory returned null.");

        if (world.CheckpointTime != now)
        {
            throw new InvalidOperationException(
                "The world factory must use the captured database timestamp as its initial checkpoint.");
        }

        var json =
            _serializer.Serialize(
                world.CreateSnapshot());

        await InsertAsync(
            connection,
            transaction,
            matchId,
            json,
            now,
            cancellationToken);

        await transaction.CommitAsync(
            cancellationToken);
    }

    /// <summary>
    /// Exact snapshot insertion for imports/tests.
    /// </summary>
    public async Task CreateAsync(
        Guid matchId,
        WorldState initialWorld,
        CancellationToken cancellationToken = default)
    {
        ValidateMatchId(matchId);
        ArgumentNullException.ThrowIfNull(initialWorld);

        var snapshot =
            initialWorld.CreateSnapshot();

        var json =
            _serializer.Serialize(snapshot);

        await using var connection =
            await dataSource.OpenConnectionAsync(
                cancellationToken);

        await using var transaction =
            await connection.BeginTransactionAsync(
                IsolationLevel.ReadCommitted,
                cancellationToken);

        var now =
            await ReadDatabaseTimeAsync(
                connection,
                transaction,
                cancellationToken);

        EnsureTimeIsNotBehind(
            now,
            snapshot.CheckpointTime);

        await InsertAsync(
            connection,
            transaction,
            matchId,
            json,
            now,
            cancellationToken);

        await transaction.CommitAsync(
            cancellationToken);
    }

    private static async Task InsertAsync(
        NpgsqlConnection connection,
        NpgsqlTransaction transaction,
        Guid matchId,
        string json,
        DateTimeOffset databaseTime,
        CancellationToken cancellationToken)
    {
        await using var command =
            new NpgsqlCommand(
                """
                INSERT INTO matches
                    (id, state, version, created_at, updated_at)
                VALUES
                    ($1, $2, 0, $3, $3)
                """,
                connection,
                transaction);

        command.Parameters.AddWithValue(
            matchId);

        command.Parameters.AddWithValue(
            NpgsqlDbType.Jsonb,
            json);

        command.Parameters.AddWithValue(
            databaseTime);

        await command.ExecuteNonQueryAsync(
            cancellationToken);
    }

    /// <summary>
    /// Current reads also lock, advance, save,
    /// generate coarse client updates and increment the version.
    /// </summary>
    public Task<MatchExecutionResult<WorldSnapshot>>
        LoadCurrentAsync(
            Guid matchId,
            CancellationToken cancellationToken = default)
    {
        return ExecuteAsync(
            matchId,
            (world, _) =>
                Task.FromResult(
                    world.CreateSnapshot()),
            cancellationToken);
    }

    /// <summary>
    /// Executes lazy simulation and an optional command under
    /// one match-row lock.
    ///
    /// Snapshot state and client change-feed entries are committed
    /// atomically in the same PostgreSQL transaction.
    /// </summary>
    public async Task<MatchExecutionResult<T>> ExecuteAsync<T>(
        Guid matchId,
        Func<WorldState, DateTimeOffset, Task<T>> operation,
        CancellationToken cancellationToken = default)
    {
        ValidateMatchId(matchId);
        ArgumentNullException.ThrowIfNull(operation);

        await using var connection =
            await dataSource.OpenConnectionAsync(
                cancellationToken);

        await using var transaction =
            await connection.BeginTransactionAsync(
                IsolationLevel.ReadCommitted,
                cancellationToken);

        string json;
        long version;
        long nextUpdateSequence;

        await using (var command =
                     new NpgsqlCommand(
                         """
                         SELECT
                             state::text,
                             version,
                             next_update_sequence
                         FROM matches
                         WHERE id = $1
                         FOR UPDATE
                         """,
                         connection,
                         transaction))
        {
            command.Parameters.AddWithValue(
                matchId);

            await using var reader =
                await command.ExecuteReaderAsync(
                    cancellationToken);

            if (!await reader.ReadAsync(
                    cancellationToken))
            {
                throw new MatchNotFoundException(
                    matchId);
            }

            json =
                reader.GetString(0);

            version =
                reader.GetInt64(1);

            nextUpdateSequence =
                reader.GetInt64(2);
        }

        // Capture authoritative database time only AFTER
        // obtaining the match-row lock.
        var now =
            await ReadDatabaseTimeAsync(
                connection,
                transaction,
                cancellationToken);

        var world =
            WorldState.Restore(
                _serializer.Deserialize(json));

        EnsureTimeIsNotBehind(
            now,
            world.CheckpointTime);

        // Snapshot before lazy simulation and before the command.
        var before =
            world.CreateSnapshot();

        WorldSimulator.AdvanceTo(
            world,
            now);

        var result =
            await operation(
                world,
                now);

        cancellationToken.ThrowIfCancellationRequested();

        if (world.CheckpointTime != now)
        {
            throw new InvalidOperationException(
                "The operation must use the captured database timestamp and must not advance beyond it.");
        }

        var after =
            world.CreateSnapshot();

        var updates =
            WorldUpdateDetector.Detect(
                before,
                after,
                now);

        var state =
            _serializer.Serialize(after);

        var nextVersion =
            checked(version + 1);

        var sequence =
            nextUpdateSequence;

        foreach (var update in updates)
        {
            await InsertUpdateAsync(
                connection,
                transaction,
                matchId,
                sequence,
                update,
                cancellationToken);

            sequence =
                checked(sequence + 1);
        }

        await using (var command =
                     new NpgsqlCommand(
                         """
                         UPDATE matches
                         SET
                             state = $1,
                             version = $2,
                             updated_at = $3,
                             next_update_sequence = $4
                         WHERE id = $5
                         """,
                         connection,
                         transaction))
        {
            command.Parameters.AddWithValue(
                NpgsqlDbType.Jsonb,
                state);

            command.Parameters.AddWithValue(
                nextVersion);

            command.Parameters.AddWithValue(
                now);

            command.Parameters.AddWithValue(
                sequence);

            command.Parameters.AddWithValue(
                matchId);

            await command.ExecuteNonQueryAsync(
                cancellationToken);
        }

        await transaction.CommitAsync(
            cancellationToken);

        return new MatchExecutionResult<T>(
            result,
            nextVersion,
            now);
    }

    /// <summary>
    /// Polls the durable change feed.
    ///
    /// Polling also wakes lazy simulation. If crossing the current
    /// database time produces a meaningful client-visible transition,
    /// the advanced snapshot and generated updates are persisted
    /// atomically before the feed is returned.
    ///
    /// If simulation only changes continuous state such as battle HP
    /// and checkpoint time, no snapshot write is performed. That interval
    /// can be recomputed deterministically by the next authoritative
    /// operation.
    /// </summary>
    public async Task<MatchUpdatesPage> ReadUpdatesAsync(
        Guid matchId,
        long after,
        int limit = 100,
        CancellationToken cancellationToken = default)
    {
        ValidateMatchId(matchId);

        if (after < 0)
        {
            throw new ArgumentOutOfRangeException(
                nameof(after),
                "Cursor cannot be negative.");
        }

        if (limit is < 1 or > 500)
        {
            throw new ArgumentOutOfRangeException(
                nameof(limit),
                "Limit must be between 1 and 500.");
        }

        await using var connection =
            await dataSource.OpenConnectionAsync(
                cancellationToken);

        await using var transaction =
            await connection.BeginTransactionAsync(
                IsolationLevel.ReadCommitted,
                cancellationToken);

        string json;
        long version;
        long nextUpdateSequence;

        // Polling participates in the same per-match serialization
        // as commands. This prevents two requests from advancing
        // the same world independently.
        await using (var command =
                     new NpgsqlCommand(
                         """
                         SELECT
                             state::text,
                             version,
                             next_update_sequence
                         FROM matches
                         WHERE id = $1
                         FOR UPDATE
                         """,
                         connection,
                         transaction))
        {
            command.Parameters.AddWithValue(
                matchId);

            await using var reader =
                await command.ExecuteReaderAsync(
                    cancellationToken);

            if (!await reader.ReadAsync(
                    cancellationToken))
            {
                throw new MatchNotFoundException(
                    matchId);
            }

            json =
                reader.GetString(0);

            version =
                reader.GetInt64(1);

            nextUpdateSequence =
                reader.GetInt64(2);
        }

        // Capture PostgreSQL time only after acquiring the match lock,
        // exactly like ExecuteAsync.
        var now =
            await ReadDatabaseTimeAsync(
                connection,
                transaction,
                cancellationToken);

        var world =
            WorldState.Restore(
                _serializer.Deserialize(json));

        EnsureTimeIsNotBehind(
            now,
            world.CheckpointTime);

        var before =
            world.CreateSnapshot();

        // Merely polling the change feed is enough to wake
        // deterministic lazy simulation.
        WorldSimulator.AdvanceTo(
            world,
            now);

        var afterSnapshot =
            world.CreateSnapshot();

        var generatedUpdates =
            WorldUpdateDetector.Detect(
                before,
                afterSnapshot,
                now);

        var sequence =
            nextUpdateSequence;

        foreach (var update in generatedUpdates)
        {
            await InsertUpdateAsync(
                connection,
                transaction,
                matchId,
                sequence,
                update,
                cancellationToken);

            sequence =
                checked(sequence + 1);
        }

        if (generatedUpdates.Count != 0)
        {
            // Persist only when polling crossed a meaningful
            // client-visible boundary.
            //
            // Empty polls should not continuously UPDATE matches.
            var state =
                _serializer.Serialize(
                    afterSnapshot);

            var nextVersion =
                checked(version + 1);

            await using var updateCommand =
                new NpgsqlCommand(
                    """
                    UPDATE matches
                    SET
                        state = $1,
                        version = $2,
                        updated_at = $3,
                        next_update_sequence = $4
                    WHERE id = $5
                    """,
                    connection,
                    transaction);

            updateCommand.Parameters.AddWithValue(
                NpgsqlDbType.Jsonb,
                state);

            updateCommand.Parameters.AddWithValue(
                nextVersion);

            updateCommand.Parameters.AddWithValue(
                now);

            updateCommand.Parameters.AddWithValue(
                sequence);

            updateCommand.Parameters.AddWithValue(
                matchId);

            await updateCommand.ExecuteNonQueryAsync(
                cancellationToken);
        }

        var requestedRowCount =
            checked(limit + 1);

        await using var readCommand =
            new NpgsqlCommand(
                """
                SELECT
                    sequence,
                    occurred_at,
                    type,
                    payload::text
                FROM match_updates
                WHERE match_id = $1
                  AND sequence > $2
                ORDER BY sequence
                LIMIT $3
                """,
                connection,
                transaction);

        readCommand.Parameters.AddWithValue(
            matchId);

        readCommand.Parameters.AddWithValue(
            after);

        readCommand.Parameters.AddWithValue(
            requestedRowCount);

        var entries =
            new List<MatchUpdateEntry>(
                requestedRowCount);

        await using (var reader =
                     await readCommand.ExecuteReaderAsync(
                         cancellationToken))
        {
            while (await reader.ReadAsync(
                       cancellationToken))
            {
                entries.Add(
                    new MatchUpdateEntry(
                        reader.GetInt64(0),
                        new DateTimeOffset(
                            reader.GetDateTime(1)),
                        reader.GetString(2),
                        reader.GetString(3)));
            }
        }

        var hasMore =
            entries.Count > limit;

        if (hasMore)
        {
            entries.RemoveAt(
                entries.Count - 1);
        }

        var nextCursor =
            entries.Count == 0
                ? after
                : entries[^1].Sequence;

        await transaction.CommitAsync(
            cancellationToken);

        return new MatchUpdatesPage(
            nextCursor,
            hasMore,
            entries);
    }

    private static async Task InsertUpdateAsync(
        NpgsqlConnection connection,
        NpgsqlTransaction transaction,
        Guid matchId,
        long sequence,
        PendingMatchUpdate update,
        CancellationToken cancellationToken)
    {
        await using var command =
            new NpgsqlCommand(
                """
                INSERT INTO match_updates
                    (
                        match_id,
                        sequence,
                        occurred_at,
                        type,
                        payload
                    )
                VALUES
                    ($1, $2, $3, $4, $5)
                """,
                connection,
                transaction);

        command.Parameters.AddWithValue(
            matchId);

        command.Parameters.AddWithValue(
            sequence);

        // PostgreSQL timestamptz through Npgsql requires UTC.
        command.Parameters.AddWithValue(
            update.OccurredAt.ToUniversalTime());

        command.Parameters.AddWithValue(
            update.Type);

        command.Parameters.AddWithValue(
            NpgsqlDbType.Jsonb,
            update.PayloadJson);

        await command.ExecuteNonQueryAsync(
            cancellationToken);
    }

    private static void ValidateMatchId(
        Guid matchId)
    {
        if (matchId == Guid.Empty)
        {
            throw new ArgumentException(
                "Match id cannot be empty.",
                nameof(matchId));
        }
    }

    private static void EnsureTimeIsNotBehind(
        DateTimeOffset now,
        DateTimeOffset checkpoint)
    {
        if (now < checkpoint)
        {
            throw new DatabaseTimeBehindWorldException(
                now,
                checkpoint);
        }
    }

    private static async Task<DateTimeOffset>
        ReadDatabaseTimeAsync(
            NpgsqlConnection connection,
            NpgsqlTransaction transaction,
            CancellationToken cancellationToken)
    {
        await using var command =
            new NpgsqlCommand(
                "SELECT clock_timestamp()",
                connection,
                transaction);

        var timestamp =
            (DateTime)(await command.ExecuteScalarAsync(
                cancellationToken))!;

        return new DateTimeOffset(
            timestamp);
    }
}