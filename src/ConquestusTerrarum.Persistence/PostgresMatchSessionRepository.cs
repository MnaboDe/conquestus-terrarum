using ConquestusTerrarum.Domain.World;
using Npgsql;

namespace ConquestusTerrarum.Persistence;

public sealed class PostgresMatchSessionRepository(NpgsqlDataSource dataSource) : IMatchSessionRepository
{
    public async Task CreateAsync(MatchSessionCreation session, Func<DateTimeOffset, WorldState> worldFactory,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(session);
        ArgumentNullException.ThrowIfNull(worldFactory);
        if (session.FirstPlayerId == Guid.Empty || session.SecondPlayerId == Guid.Empty ||
            session.FirstPlayerId == session.SecondPlayerId)
            throw new ArgumentException("Two distinct player identities are required.", nameof(session));
        ValidateHash(session.FirstTokenHash);

        await using var connection = await dataSource.OpenConnectionAsync(cancellationToken);
        await using var transaction = await connection.BeginTransactionAsync(cancellationToken);
        var now = await new PostgresMatchRepository(dataSource).CreateInTransactionAsync(
            connection, transaction, session.MatchId, worldFactory, cancellationToken);
        try
        {
            await using var command = new NpgsqlCommand("""
                WITH session AS (
                    INSERT INTO match_sessions (match_id, join_code, created_at) VALUES ($1, $2, $3)
                    RETURNING match_id
                )
                INSERT INTO match_players (match_id, slot, player_id, token_hash, joined_at)
                SELECT session.match_id, player.slot, player.id, player.hash, player.joined_at
                FROM session CROSS JOIN (VALUES (1, $4, $5, $3), (2, $6, NULL, NULL))
                    AS player(slot, id, hash, joined_at)
                """, connection, transaction);
            command.Parameters.AddWithValue(session.MatchId);
            command.Parameters.AddWithValue(session.JoinCode);
            command.Parameters.AddWithValue(now);
            command.Parameters.AddWithValue(session.FirstPlayerId);
            command.Parameters.AddWithValue(session.FirstTokenHash);
            command.Parameters.AddWithValue(session.SecondPlayerId);
            await command.ExecuteNonQueryAsync(cancellationToken);
            await transaction.CommitAsync(cancellationToken);
        }
        catch (PostgresException ex) when (ex.SqlState == PostgresErrorCodes.UniqueViolation &&
                                          ex.ConstraintName == "uq_match_sessions_join_code")
        {
            throw new JoinCodeCollisionException();
        }
    }

    public async Task<MatchPlayerIdentity> JoinAsync(string joinCode, byte[] tokenHash,
        CancellationToken cancellationToken = default)
    {
        ValidateHash(tokenHash);
        await using var connection = await dataSource.OpenConnectionAsync(cancellationToken);
        await using var transaction = await connection.BeginTransactionAsync(cancellationToken);
        Guid matchId;
        await using (var lookup = new NpgsqlCommand(
                         "SELECT match_id FROM match_sessions WHERE join_code = $1",
                         connection, transaction))
        {
            lookup.Parameters.AddWithValue(joinCode);
            matchId = await lookup.ExecuteScalarAsync(cancellationToken) is Guid id
                ? id : throw new JoinCodeNotFoundException();
        }

        // Match lock first, matching commands and lazy reads. Invitation codes are immutable.
        long version, sequence;
        string json;
        await using (var locked = new NpgsqlCommand(
                         "SELECT version, next_update_sequence, state::text FROM matches WHERE id = $1 FOR UPDATE", connection, transaction))
        {
            locked.Parameters.AddWithValue(matchId);
            await using var reader = await locked.ExecuteReaderAsync(cancellationToken);
            if (!await reader.ReadAsync(cancellationToken)) throw new MatchNotFoundException(matchId);
            version = reader.GetInt64(0);
            sequence = reader.GetInt64(1);
            json = reader.GetString(2);
        }
        DateTimeOffset now;
        await using (var clock = new NpgsqlCommand("SELECT clock_timestamp()", connection, transaction))
            now = new DateTimeOffset((DateTime)(await clock.ExecuteScalarAsync(cancellationToken))!);

        // Claim, start event, version and cursor commit together. Contenders cannot start twice.
        await using var claim = new NpgsqlCommand("""
            UPDATE match_players SET token_hash = $1, joined_at = $3
            WHERE match_id = $2 AND slot = 2 AND token_hash IS NULL
            RETURNING player_id
            """, connection, transaction);
        claim.Parameters.AddWithValue(tokenHash);
        claim.Parameters.AddWithValue(matchId);
        claim.Parameters.AddWithValue(now);
        var playerId = await claim.ExecuteScalarAsync(cancellationToken) is Guid player
            ? player : throw new MatchFullException();
        await PostgresMatchRepository.InsertUpdateAsync(connection, transaction, matchId, sequence,
            new PendingMatchUpdate(now, "MatchStarted", "{\"phase\":\"active\"}"), cancellationToken);
        sequence = checked(sequence + 1);
        var participants = await MatchLifecycleContext.LoadAsync(connection, transaction, matchId, cancellationToken);
        var world = WorldState.Restore(new WorldSnapshotSerializer().Deserialize(json));
        var snapshot = world.CreateSnapshot();
        foreach (var finished in await MatchLifecycleContext.DetectAsync(connection, transaction, matchId,
                     snapshot, snapshot, now, participants?.Evaluate(world), cancellationToken))
        {
            await PostgresMatchRepository.InsertUpdateAsync(connection, transaction, matchId, sequence, finished, cancellationToken);
            sequence = checked(sequence + 1);
        }
        await using var update = new NpgsqlCommand("""
            UPDATE matches SET version = $1, next_update_sequence = $2, updated_at = $3 WHERE id = $4
            """, connection, transaction);
        update.Parameters.AddWithValue(checked(version + 1));
        update.Parameters.AddWithValue(sequence);
        update.Parameters.AddWithValue(now);
        update.Parameters.AddWithValue(matchId);
        await update.ExecuteNonQueryAsync(cancellationToken);
        await transaction.CommitAsync(cancellationToken);
        return new MatchPlayerIdentity(matchId, playerId);
    }

    public async Task<Guid?> ResolvePlayerAsync(Guid matchId, byte[] tokenHash,
        CancellationToken cancellationToken = default)
    {
        ValidateHash(tokenHash);
        await using var command = dataSource.CreateCommand("""
            SELECT p.player_id FROM matches m
            LEFT JOIN match_players p ON p.match_id = m.id AND p.token_hash = $2
            WHERE m.id = $1
            """);
        command.Parameters.AddWithValue(matchId);
        command.Parameters.AddWithValue(tokenHash);
        var value = await command.ExecuteScalarAsync(cancellationToken);
        if (value is null) throw new MatchNotFoundException(matchId);
        return value is Guid player ? player : null;
    }

    private static void ValidateHash(byte[] hash)
    {
        ArgumentNullException.ThrowIfNull(hash);
        if (hash.Length != 32) throw new ArgumentException("Token hash must be 256 bits.", nameof(hash));
    }
}
