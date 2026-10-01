using ConquestusTerrarum.Domain.World;

namespace ConquestusTerrarum.Domain.Tests;

public class BattleParticipationTests
{
    [Fact]
    public void ArmyWithNoHostileArmy_IsNotBattleParticipant()
    {
        var checkpoint =
            new DateTimeOffset(
                2026, 1, 1,
                10, 0, 0,
                TimeSpan.Zero);

        var location =
            new Location(
                Guid.NewGuid(),
                "Rome");

        var map =
            new GameMap(
                new[] { location },
                Array.Empty<MapConnection>());

        var playerA = Guid.NewGuid();
        var playerB = Guid.NewGuid();
        var playerC = Guid.NewGuid();

        var armyA =
            new Army(
                Guid.NewGuid(),
                playerA,
                location.Id);

        var armyB =
            new Army(
                Guid.NewGuid(),
                playerB,
                location.Id);

        var armyC =
            new Army(
                Guid.NewGuid(),
                playerC,
                location.Id);

        var alliances =
            new[]
            {
                new PlayerAlliance(
                    playerC,
                    playerA),

                new PlayerAlliance(
                    playerC,
                    playerB)
            };

        var world =
            new WorldState(
                map,
                new[]
                {
                    armyA,
                    armyB,
                    armyC
                },
                checkpoint,
                alliances);

        Assert.NotNull(
            world.GetBattleAt(location.Id));

        Assert.True(
            armyA.IsInBattle);

        Assert.True(
            armyB.IsInBattle);

        Assert.False(
            armyC.IsInBattle);

        Assert.False(
            armyC.CanRetreat);
    }

    [Fact]
    public void NonParticipantCanLeaveLocationWhereBattleExists()
    {
        var checkpoint =
            new DateTimeOffset(
                2026, 1, 1,
                10, 0, 0,
                TimeSpan.Zero);

        var battlefield =
            new Location(
                Guid.NewGuid(),
                "Rome");

        var destination =
            new Location(
                Guid.NewGuid(),
                "Exit");

        var connection =
            new MapConnection(
                battlefield.Id,
                destination.Id,
                TimeSpan.FromMinutes(10));

        var map =
            new GameMap(
                new[]
                {
                    battlefield,
                    destination
                },
                new[] { connection });

        var playerA = Guid.NewGuid();
        var playerB = Guid.NewGuid();
        var playerC = Guid.NewGuid();

        var armyA =
            new Army(
                Guid.NewGuid(),
                playerA,
                battlefield.Id);

        var armyB =
            new Army(
                Guid.NewGuid(),
                playerB,
                battlefield.Id);

        var armyC =
            new Army(
                Guid.NewGuid(),
                playerC,
                battlefield.Id);

        var alliances =
            new[]
            {
                new PlayerAlliance(
                    playerC,
                    playerA),

                new PlayerAlliance(
                    playerC,
                    playerB)
            };

        var world =
            new WorldState(
                map,
                new[]
                {
                    armyA,
                    armyB,
                    armyC
                },
                checkpoint,
                alliances);

        Assert.NotNull(
            world.GetBattleAt(
                battlefield.Id));

        Assert.False(
            armyC.IsInBattle);

        world.StartMovement(
            armyC.Id,
            destination.Id,
            checkpoint);

        Assert.True(
            armyC.IsMoving);

        Assert.Null(
            armyC.LocationId);

        // A и B всё ещё дерутся.
        Assert.NotNull(
            world.GetBattleAt(
                battlefield.Id));
    }
}