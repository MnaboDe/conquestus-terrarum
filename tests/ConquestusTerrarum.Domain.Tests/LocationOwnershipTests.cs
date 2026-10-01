using ConquestusTerrarum.Domain.World;

namespace ConquestusTerrarum.Domain.Tests;

public class LocationOwnershipTests
{
    [Fact]
    public void AfterBattle_EarliestAlliedSurvivorGetsLocation()
    {
        var start =
            new DateTimeOffset(
                2026, 1, 1,
                10, 0, 0,
                TimeSpan.Zero);

        var playerA = Guid.NewGuid();
        var playerB = Guid.NewGuid();
        var playerC = Guid.NewGuid();

        var battlefield =
            new Location(
                Guid.NewGuid(),
                "Rome");

        var cStart =
            new Location(
                Guid.NewGuid(),
                "C Start");

        var bStart =
            new Location(
                Guid.NewGuid(),
                "B Start");

        var cConnection =
            new MapConnection(
                cStart.Id,
                battlefield.Id,
                TimeSpan.FromMinutes(5));

        var bConnection =
            new MapConnection(
                bStart.Id,
                battlefield.Id,
                TimeSpan.FromMinutes(10));

        var map =
            new GameMap(
                new[]
                {
                    battlefield,
                    cStart,
                    bStart
                },
                new[]
                {
                    cConnection,
                    bConnection
                });

        // A уже находится в Rome с 10:00.
        var armyA =
            new Army(
                Guid.NewGuid(),
                playerA,
                battlefield.Id);

        var armyC =
            new Army(
                Guid.NewGuid(),
                playerC,
                cStart.Id);

        var armyB =
            new Army(
                Guid.NewGuid(),
                playerB,
                bStart.Id);

        armyC.StartMovement(
            cConnection,
            start);

        armyB.StartMovement(
            bConnection,
            start);

        var alliance =
            new PlayerAlliance(
                playerA,
                playerC);

        var world =
            new WorldState(
                map,
                new[]
                {
                    armyA,
                    armyC,
                    armyB
                },
                start,
                new[] { alliance });

        WorldSimulator.AdvanceTo(
            world,
            start.AddMinutes(10));

        Assert.NotNull(
            world.GetBattleAt(
                battlefield.Id));

        Assert.True(
            armyB.CanRetreat);

        world.Retreat(
            armyB.Id,
            start.AddMinutes(10));

        Assert.Null(
            world.GetBattleAt(
                battlefield.Id));

        Assert.Equal(
            playerA,
            battlefield.OwnerId);
    }

    [Fact]
    public void AfterBattle_SimultaneousAlliedArrivals_DoNotPickOwnerArbitrarily()
    {
        var start =
            new DateTimeOffset(
                2026, 1, 1,
                10, 0, 0,
                TimeSpan.Zero);

        var playerA = Guid.NewGuid();
        var playerB = Guid.NewGuid();
        var playerC = Guid.NewGuid();

        var battlefield =
            new Location(
                Guid.NewGuid(),
                "Rome");

        var aStart =
            new Location(
                Guid.NewGuid(),
                "A Start");

        var bStart =
            new Location(
                Guid.NewGuid(),
                "B Start");

        var cStart =
            new Location(
                Guid.NewGuid(),
                "C Start");

        var aConnection =
            new MapConnection(
                aStart.Id,
                battlefield.Id,
                TimeSpan.FromMinutes(10));

        var bConnection =
            new MapConnection(
                bStart.Id,
                battlefield.Id,
                TimeSpan.FromMinutes(5));

        var cConnection =
            new MapConnection(
                cStart.Id,
                battlefield.Id,
                TimeSpan.FromMinutes(10));

        var map =
            new GameMap(
                new[]
                {
                    battlefield,
                    aStart,
                    bStart,
                    cStart
                },
                new[]
                {
                    aConnection,
                    bConnection,
                    cConnection
                });

        var armyA =
            new Army(
                Guid.NewGuid(),
                playerA,
                aStart.Id);

        var armyB =
            new Army(
                Guid.NewGuid(),
                playerB,
                bStart.Id);

        var armyC =
            new Army(
                Guid.NewGuid(),
                playerC,
                cStart.Id);

        armyA.StartMovement(
            aConnection,
            start);

        armyB.StartMovement(
            bConnection,
            start);

        armyC.StartMovement(
            cConnection,
            start);

        var alliance =
            new PlayerAlliance(
                playerA,
                playerC);

        var world =
            new WorldState(
                map,
                new[]
                {
                    armyA,
                    armyB,
                    armyC
                },
                start,
                new[] { alliance });

        // B приходит первым, в 10:05.
        WorldSimulator.AdvanceTo(
            world,
            start.AddMinutes(5));

        Assert.Equal(
            battlefield.Id,
            armyB.LocationId);

        Assert.Null(
            world.GetBattleAt(
                battlefield.Id));

        // A и C одновременно приходят в 10:10.
        // Оба враждебны B, но друг другу являются союзниками.
        WorldSimulator.AdvanceTo(
            world,
            start.AddMinutes(10));

        Assert.NotNull(
            world.GetBattleAt(
                battlefield.Id));

        Assert.Equal(
            start.AddMinutes(10),
            armyA.EnteredLocationAt);

        Assert.Equal(
            start.AddMinutes(10),
            armyC.EnteredLocationAt);

        Assert.True(
            armyB.CanRetreat);

        // B уходит, остаются только союзники A и C.
        world.Retreat(
            armyB.Id,
            start.AddMinutes(10));

        Assert.Null(
            world.GetBattleAt(
                battlefield.Id));

        // A и C прибыли абсолютно одновременно.
        // Не выбираем владельца произвольно.
        Assert.Null(
            battlefield.OwnerId);
    }
}