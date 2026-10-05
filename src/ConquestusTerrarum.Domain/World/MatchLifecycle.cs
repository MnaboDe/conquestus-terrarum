namespace ConquestusTerrarum.Domain.World;

public enum MatchPhase { WaitingForPlayers, Active, Finished }

public sealed record MatchLifecycle(MatchPhase Phase, Guid? WinnerPlayerId = null)
{
    public bool IsDraw => Phase == MatchPhase.Finished && WinnerPlayerId is null;

    // Session participation is an input, never simulation state or an authentication secret.
    public static MatchLifecycle Evaluate(WorldState world, Guid firstPlayer, Guid secondPlayer, bool started)
    {
        if (!started) return new(MatchPhase.WaitingForPlayers);
        var firstAlive = world.Armies.Any(a => a.OwnerId == firstPlayer && !a.IsDestroyed);
        var secondAlive = world.Armies.Any(a => a.OwnerId == secondPlayer && !a.IsDestroyed);
        if (firstAlive && secondAlive) return new(MatchPhase.Active);
        return new(MatchPhase.Finished, firstAlive ? firstPlayer : secondAlive ? secondPlayer : null);
    }
}
