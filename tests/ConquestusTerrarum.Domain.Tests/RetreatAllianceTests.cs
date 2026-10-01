using ConquestusTerrarum.Domain.World;

namespace ConquestusTerrarum.Domain.Tests;

public class RetreatAllianceTests
{
    [Fact]
    public void EnemyRetreat_EndsBattleForRemainingAllies()
    {
        var start =
            new DateTimeOffset(
                2026, 1, 1,
                10, 0, 0,
                TimeSpan.Zero);

        var battlefield =
            new Location(
                Guid.NewGuid(),
                "Rome");

        var enemyStart =
            new Location(
                Guid.NewGuid(),
                "Enemy Start");

        var allyAExit =
            new Location(
                Guid.NewGuid(),
                "A Exit");

        var allyCExit =
            new Location(
                Guid.NewGuid(),
                "C Exit");

        var enemyConnection =
            new MapConnection(
                enemyStart.Id,
                battlefield.Id,
                TimeSpan.FromMinutes(10));

        var aExitConnection =
            new MapConnection(
                battlefield.Id,
                allyAExit.Id,
                TimeSpan.FromMinutes(5));

        var cExitConnection =
            new MapConnection(
                battlefield.Id,
                allyCExit.Id,
                TimeSpan.FromMinutes(5));

        var map =
            new GameMap(
                new[]
                {
                    battlefield,
                    enemyStart,
                    allyAExit,
                    allyCExit
                },
                new[]
                {
                    enemyConnection,
                    aExitConnection,
                    cExitConnection
                });

        var playerA = Guid.NewGuid();
        var playerB = Guid.NewGuid();
        var playerC = Guid.NewGuid();

        var armyA =
            TestArmies.Create(
                Guid.NewGuid(),
                playerA,
                battlefield.Id);

        var armyC =
            TestArmies.Create(
                Guid.NewGuid(),
                playerC,
                battlefield.Id);

        var armyB =
            TestArmies.Create(
                Guid.NewGuid(),
                playerB,
                enemyStart.Id);

        var alliance =
            new PlayerAlliance(
                playerA,
                playerC);

        armyB.StartMovement(
            enemyConnection,
            start);

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

        var battleTime =
            start.AddMinutes(10);

        WorldSimulator.AdvanceTo(
            world,
            battleTime);

        Assert.NotNull(
            world.GetBattleAt(
                battlefield.Id));

        Assert.True(armyA.IsInBattle);
        Assert.True(armyB.IsInBattle);
        Assert.True(armyC.IsInBattle);

        Assert.True(armyB.CanRetreat);

        world.Retreat(
            armyB.Id,
            battleTime);

        // Враг ушёл, в клетке остались только союзники.
        Assert.Null(
            world.GetBattleAt(
                battlefield.Id));

        Assert.False(armyA.IsInBattle);
        Assert.False(armyC.IsInBattle);

        Assert.True(armyB.IsMoving);
        Assert.Equal(
            MovementKind.Retreat,
            armyB.Movement!.Kind);

        // После окончания боя союзники снова
        // могут двигаться обычным способом.
        world.StartMovement(
            armyA.Id,
            allyAExit.Id,
            battleTime);

        world.StartMovement(
            armyC.Id,
            allyCExit.Id,
            battleTime);

        Assert.True(armyA.IsMoving);
        Assert.True(armyC.IsMoving);
    }
}