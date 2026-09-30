using ConquestusTerrarum.Domain.World;

namespace ConquestusTerrarum.Domain.Tests;

public class BattleTests
{
    [Fact]
    public void SimultaneousEnemyArrivals_StartBattle()
    {
        var startedAt =
            new DateTimeOffset(
                2026, 1, 1,
                10, 0, 0,
                TimeSpan.Zero);

        var a = new Location(
            Guid.NewGuid(),
            "A");

        var b = new Location(
            Guid.NewGuid(),
            "B");

        var c = new Location(
            Guid.NewGuid(),
            "C");

        var ac = new MapConnection(
            a.Id,
            c.Id,
            TimeSpan.FromMinutes(10));

        var bc = new MapConnection(
            b.Id,
            c.Id,
            TimeSpan.FromMinutes(10));

        var map = new GameMap(
            new[] { a, b, c },
            new[] { ac, bc });

        var player1 = Guid.NewGuid();
        var player2 = Guid.NewGuid();

        var army1 = new Army(
            Guid.NewGuid(),
            player1,
            a.Id);

        var army2 = new Army(
            Guid.NewGuid(),
            player2,
            b.Id);

        army1.StartMovement(
            ac,
            startedAt);

        army2.StartMovement(
            bc,
            startedAt);

        var world = new WorldState(
            map,
            new[] { army1, army2 },
            startedAt);

        WorldSimulator.AdvanceTo(
            world,
            startedAt.AddMinutes(30));

        var battle = world.GetBattleAt(c.Id);

        Assert.NotNull(battle);
        Assert.Equal(c.Id, battle.LocationId);

        Assert.Equal(
            startedAt.AddMinutes(10),
            battle.StartedAt);

        Assert.Single(world.Battles);

        Assert.Equal(c.Id, army1.LocationId);
        Assert.Equal(c.Id, army2.LocationId);
    }

    [Fact]
    public void SimultaneousFriendlyArrivals_DoNotStartBattle()
    {
        var startedAt =
            new DateTimeOffset(
                2026, 1, 1,
                10, 0, 0,
                TimeSpan.Zero);

        var a = new Location(
            Guid.NewGuid(),
            "A");

        var b = new Location(
            Guid.NewGuid(),
            "B");

        var c = new Location(
            Guid.NewGuid(),
            "C");

        var ac = new MapConnection(
            a.Id,
            c.Id,
            TimeSpan.FromMinutes(10));

        var bc = new MapConnection(
            b.Id,
            c.Id,
            TimeSpan.FromMinutes(10));

        var map = new GameMap(
            new[] { a, b, c },
            new[] { ac, bc });

        var player = Guid.NewGuid();

        var army1 = new Army(
            Guid.NewGuid(),
            player,
            a.Id);

        var army2 = new Army(
            Guid.NewGuid(),
            player,
            b.Id);

        army1.StartMovement(
            ac,
            startedAt);

        army2.StartMovement(
            bc,
            startedAt);

        var world = new WorldState(
            map,
            new[] { army1, army2 },
            startedAt);

        WorldSimulator.AdvanceTo(
            world,
            startedAt.AddMinutes(20));

        Assert.Null(
            world.GetBattleAt(c.Id));

        Assert.Empty(world.Battles);
    }

    [Fact]
    public void EnemyArrivalToOccupiedLocation_StartsBattle()
    {
        var startedAt =
            new DateTimeOffset(
                2026, 1, 1,
                10, 0, 0,
                TimeSpan.Zero);

        var a = new Location(
            Guid.NewGuid(),
            "A");

        var c = new Location(
            Guid.NewGuid(),
            "C");

        var connection = new MapConnection(
            a.Id,
            c.Id,
            TimeSpan.FromMinutes(10));

        var map = new GameMap(
            new[] { a, c },
            new[] { connection });

        var defender = new Army(
            Guid.NewGuid(),
            Guid.NewGuid(),
            c.Id);

        var attacker = new Army(
            Guid.NewGuid(),
            Guid.NewGuid(),
            a.Id);

        attacker.StartMovement(
            connection,
            startedAt);

        var world = new WorldState(
            map,
            new[] { defender, attacker },
            startedAt);

        WorldSimulator.AdvanceTo(
            world,
            startedAt.AddMinutes(10));

        var battle =
            world.GetBattleAt(c.Id);

        Assert.NotNull(battle);

        Assert.Equal(
            startedAt.AddMinutes(10),
            battle.StartedAt);
    }

    [Fact]
    public void NormalMovement_FromBattle_IsRejected()
    {
        var checkpoint =
            new DateTimeOffset(
                2026, 1, 1,
                10, 0, 0,
                TimeSpan.Zero);

        var a = new Location(
            Guid.NewGuid(),
            "A");

        var b = new Location(
            Guid.NewGuid(),
            "B");

        var connection = new MapConnection(
            a.Id,
            b.Id,
            TimeSpan.FromMinutes(10));

        var map = new GameMap(
            new[] { a, b },
            new[] { connection });

        var army1 = new Army(
            Guid.NewGuid(),
            Guid.NewGuid(),
            a.Id);

        var army2 = new Army(
            Guid.NewGuid(),
            Guid.NewGuid(),
            a.Id);

        var world = new WorldState(
            map,
            new[] { army1, army2 },
            checkpoint);

        Assert.NotNull(
            world.GetBattleAt(a.Id));

        Assert.Throws<InvalidOperationException>(() =>
            world.StartMovement(
                army1.Id,
                b.Id,
                checkpoint));
    }
}