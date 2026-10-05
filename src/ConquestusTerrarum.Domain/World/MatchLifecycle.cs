namespace ConquestusTerrarum.Domain.World;

public enum MatchPhase { WaitingForPlayers, Active }

public sealed record MatchLifecycle(MatchPhase Phase)
{
    // Participation alone determines phase. Army elimination never ends the persistent world.
    public static MatchLifecycle Evaluate(bool started) =>
        new(started ? MatchPhase.Active : MatchPhase.WaitingForPlayers);
}
