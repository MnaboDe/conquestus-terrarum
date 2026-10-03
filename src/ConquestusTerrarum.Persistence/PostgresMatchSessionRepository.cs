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
                         "SELECT match_id FROM match_sessions WHERE join_code = $1 FOR UPDATE",
                         connection, transaction))
        {
            lookup.Parameters.AddWithValue(joinCode);
            matchId = await lookup.ExecuteScalarAsync(cancellationToken) is Guid id
                ? id : throw new JoinCodeNotFoundException();
        }

        // The conditional update is the claim. The row lock serializes contenders across API instances.
        await using var claim = new NpgsqlCommand("""
            UPDATE match_players SET token_hash = $1, joined_at = clock_timestamp()
            WHERE match_id = $2 AND slot = 2 AND token_hash IS NULL
            RETURNING player_id
            """, connection, transaction);
        claim.Parameters.AddWithValue(tokenHash);
        claim.Parameters.AddWithValue(matchId);
        var playerId = await claim.ExecuteScalarAsync(cancellationToken) is Guid player
            ? player : throw new MatchFullException();
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
