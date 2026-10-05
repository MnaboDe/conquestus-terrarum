using ConquestusTerrarum.Domain.World;
using Npgsql;

namespace ConquestusTerrarum.Persistence;

// Read only after obtaining the match lock. Join uses that same lock, so participation,
// lazy advancement, command eligibility and durable lifecycle events cannot race.
internal sealed record MatchLifecycleContext(bool Started)
{
    internal MatchLifecycle Evaluate() => MatchLifecycle.Evaluate(Started);

    internal static async Task<MatchLifecycleContext?> LoadAsync(NpgsqlConnection connection,
        NpgsqlTransaction transaction, Guid matchId, CancellationToken cancellationToken)
    {
        await using var command = new NpgsqlCommand("""
            SELECT second.token_hash IS NOT NULL
            FROM match_players first JOIN match_players second ON first.match_id = second.match_id
            WHERE first.match_id = $1 AND first.slot = 1 AND second.slot = 2
            """, connection, transaction);
        command.Parameters.AddWithValue(matchId);
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        return await reader.ReadAsync(cancellationToken)
            ? new(reader.GetBoolean(0)) : null;
    }
}

public sealed class MatchNotPlayableException(MatchPhase phase)
    : Exception("Waiting for the second player.")
{
    public MatchPhase Phase { get; } = phase;
}
