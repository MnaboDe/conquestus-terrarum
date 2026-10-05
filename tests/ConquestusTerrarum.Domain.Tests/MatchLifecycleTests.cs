using ConquestusTerrarum.Domain.World;
using static ConquestusTerrarum.Testing.SnapshotWorlds;

namespace ConquestusTerrarum.Domain.Tests;

public sealed class MatchLifecycleTests
{
    [Theory]
    [InlineData(false, 0, 0, MatchPhase.WaitingForPlayers, 0)]
    [InlineData(true, 1, 1, MatchPhase.Active, 0)]
    [InlineData(true, 1, 0, MatchPhase.Finished, 21)]
    [InlineData(true, 0, 1, MatchPhase.Finished, 22)]
    [InlineData(true, 0, 0, MatchPhase.Finished, 0)]
    public void ParticipationAndArmySurvivalDetermineOutcome(bool started, int firstHp, int secondHp, MatchPhase phase, int winner)
    {
        var world = new WorldState(new GameMap([new Location(Id(1), "Home")], []),
            [Army(11, 21, 1, Unit(101, current: firstHp)), Army(12, 22, 1, Unit(102, current: secondHp)),
             Army(13, 23, 1, Unit(103))], Start);
        var lifecycle = MatchLifecycle.Evaluate(world, Id(21), Id(22), started);
        Assert.Equal(phase, lifecycle.Phase);
        Assert.Equal(winner == 0 ? null : (Guid?)Id(winner), lifecycle.WinnerPlayerId);
        Assert.Equal(phase == MatchPhase.Finished && winner == 0, lifecycle.IsDraw);
    }

    [Fact]
    public void EmptyDestroyedArmiesDoNotCount_ButOneWoundedLivingUnitDoes()
    {
        var world = new WorldState(new GameMap([new Location(Id(1), "Home")], []),
            [Army(11, 21, 1), Army(12, 21, 1, Unit(101, current: 0)),
             Army(13, 22, 1, Unit(102, current: 0.001m))], Start);
        Assert.Equal(new MatchLifecycle(MatchPhase.Finished, Id(22)), MatchLifecycle.Evaluate(world, Id(21), Id(22), true));
        Assert.Equal(MatchLifecycle.Evaluate(world, Id(21), Id(22), true),
            MatchLifecycle.Evaluate(WorldState.Restore(world.CreateSnapshot()), Id(21), Id(22), true));
    }
}
