using ConquestusTerrarum.Domain.World;

namespace ConquestusTerrarum.Domain.Tests;

public class WorldStateTests
{
    [Fact]
    public void StartMovement_UsesMapTravelTime()
    {
        var checkpoint =
            new DateTimeOffset(
                2026, 1, 1,
                10, 0, 0,
                TimeSpan.Zero);

        var a = new Location(Guid.NewGuid(), "A");
        var b = new Location(Guid.NewGuid(), "B");

        var connection = new MapConnection(
            a.Id,
            b.Id,
            TimeSpan.FromMinutes(15));

        var map = new GameMap(
            new[] { a, b },
            new[] { connection });

        var army = TestArmies.Create(
            Guid.NewGuid(),
            Guid.NewGuid(),
            a.Id);

        var world = new WorldState(
            map,
            new[] { army },
            checkpoint);

        world.StartMovement(
            army.Id,
            b.Id,
            checkpoint);

        Assert.True(army.IsMoving);
        Assert.NotNull(army.Movement);

        Assert.Equal(
            checkpoint.AddMinutes(15),
            army.Movement.ArriveAt);
    }

    [Fact]
    public void StartMovement_RejectsUnconnectedDestination()
    {
        var checkpoint =
            new DateTimeOffset(
                2026, 1, 1,
                10, 0, 0,
                TimeSpan.Zero);

        var a = new Location(Guid.NewGuid(), "A");
        var b = new Location(Guid.NewGuid(), "B");

        var map = new GameMap(
            new[] { a, b },
            Array.Empty<MapConnection>());

        var army = TestArmies.Create(
            Guid.NewGuid(),
            Guid.NewGuid(),
            a.Id);

        var world = new WorldState(
            map,
            new[] { army },
            checkpoint);

        Assert.Throws<InvalidOperationException>(() =>
            world.StartMovement(
                army.Id,
                b.Id,
                checkpoint));
    }

    [Fact]
    public void StartMovement_AdvancesWorldBeforeApplyingCommand()
    {
        var checkpoint =
            new DateTimeOffset(
                2026, 1, 1,
                10, 0, 0,
                TimeSpan.Zero);

        var a = new Location(Guid.NewGuid(), "A");
        var b = new Location(Guid.NewGuid(), "B");
        var c = new Location(Guid.NewGuid(), "C");

        var ab = new MapConnection(
            a.Id,
            b.Id,
            TimeSpan.FromMinutes(10));

        var bc = new MapConnection(
            b.Id,
            c.Id,
            TimeSpan.FromMinutes(10));

        var map = new GameMap(
            new[] { a, b, c },
            new[] { ab, bc });

        var army = TestArmies.Create(
            Guid.NewGuid(),
            Guid.NewGuid(),
            a.Id);

        army.StartMovement(
            ab,
            checkpoint);

        var world = new WorldState(
            map,
            new[] { army },
            checkpoint);

        world.StartMovement(
            army.Id,
            c.Id,
            checkpoint.AddMinutes(10));

        Assert.True(army.IsMoving);
        Assert.NotNull(army.Movement);

        Assert.Equal(
            b.Id,
            army.Movement.FromLocationId);

        Assert.Equal(
            c.Id,
            army.Movement.ToLocationId);
    }

    [Fact]
    public void StartMovement_RejectsCommandBeforeCheckpoint()
    {
        var checkpoint =
            new DateTimeOffset(
                2026, 1, 1,
                10, 0, 0,
                TimeSpan.Zero);

        var location = new Location(
            Guid.NewGuid(),
            "A");

        var map = new GameMap(
            new[] { location },
            Array.Empty<MapConnection>());

        var army = TestArmies.Create(
            Guid.NewGuid(),
            Guid.NewGuid(),
            location.Id);

        var world = new WorldState(
            map,
            new[] { army },
            checkpoint);

        Assert.Throws<InvalidOperationException>(() =>
            world.StartMovement(
                army.Id,
                Guid.NewGuid(),
                checkpoint.AddMinutes(-1)));
    }
}