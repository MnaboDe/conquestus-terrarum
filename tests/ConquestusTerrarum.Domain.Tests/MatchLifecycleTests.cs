using ConquestusTerrarum.Domain.World;

namespace ConquestusTerrarum.Domain.Tests;

public sealed class MatchLifecycleTests
{
    [Theory]
    [InlineData(false, MatchPhase.WaitingForPlayers)]
    [InlineData(true, MatchPhase.Active)]
    public void ParticipationAloneDeterminesPhase(bool started, MatchPhase phase)
    {
        Assert.Equal(new MatchLifecycle(phase), MatchLifecycle.Evaluate(started));
        Assert.Equal(new[] { "WaitingForPlayers", "Active" }, Enum.GetNames<MatchPhase>());
    }
}
