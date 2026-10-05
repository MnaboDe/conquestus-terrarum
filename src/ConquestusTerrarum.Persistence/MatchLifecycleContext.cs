using System.Text.Json;
using ConquestusTerrarum.Domain.World;
using Npgsql;

namespace ConquestusTerrarum.Persistence;

// Read only after obtaining the match lock. Join uses that same lock, so participation,
// lazy advancement, command eligibility and durable lifecycle events cannot race.
internal sealed record MatchLifecycleContext(Guid FirstPlayer, Guid SecondPlayer, bool Started)
{
    internal MatchLifecycle Evaluate(WorldState world) =>
        MatchLifecycle.Evaluate(world, FirstPlayer, SecondPlayer, Started);

    internal static async Task<MatchLifecycleContext?> LoadAsync(NpgsqlConnection connection,
        NpgsqlTransaction transaction, Guid matchId, CancellationToken cancellationToken)
    {
        await using var command = new NpgsqlCommand("""
            SELECT first.player_id, second.player_id, second.token_hash IS NOT NULL
            FROM match_players first JOIN match_players second ON first.match_id = second.match_id
            WHERE first.match_id = $1 AND first.slot = 1 AND second.slot = 2
            """, connection, transaction);
        command.Parameters.AddWithValue(matchId);
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        return await reader.ReadAsync(cancellationToken)
            ? new(reader.GetGuid(0), reader.GetGuid(1), reader.GetBoolean(2)) : null;
    }

    internal static async Task<IReadOnlyList<PendingMatchUpdate>> DetectAsync(NpgsqlConnection connection,
        NpgsqlTransaction transaction, Guid matchId, WorldSnapshot before, WorldSnapshot after,
        DateTimeOffset now, MatchLifecycle? current, CancellationToken cancellationToken)
    {
        var updates = WorldUpdateDetector.Detect(before, after, now).ToList();
        if (current?.Phase == MatchPhase.Finished)
        {
            // Outcome authority comes only from armies/participants. This durable delivery marker
            // prevents repeat announcements, and also covers already-eliminated legacy snapshots.
            await using var announced = new NpgsqlCommand(
                "SELECT 1 FROM match_updates WHERE match_id = $1 AND type = 'MatchFinished' LIMIT 1", connection, transaction);
            announced.Parameters.AddWithValue(matchId);
            if (await announced.ExecuteScalarAsync(cancellationToken) is null)
                updates.Add(Finished(current, now));
        }
        return updates;
    }

    internal static PendingMatchUpdate Finished(MatchLifecycle result, DateTimeOffset now) =>
        new(now, "MatchFinished", JsonSerializer.Serialize(new
        { phase = "finished", winnerPlayerId = result.WinnerPlayerId, isDraw = result.IsDraw }));
}

public sealed class MatchNotPlayableException(MatchPhase phase)
    : Exception(phase == MatchPhase.WaitingForPlayers ? "Waiting for the second player." : "This match has finished.")
{
    public MatchPhase Phase { get; } = phase;
}
