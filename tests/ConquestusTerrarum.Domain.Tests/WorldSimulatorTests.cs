using ConquestusTerrarum.Domain.World;

namespace ConquestusTerrarum.Domain.Tests;

public class WorldSimulatorTests
{
    [Fact]
    public void AdvanceTo_BeforeArrival_KeepsArmyMoving()
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

        var connection = new MapConnection(
            a.Id,
            b.Id,
            TimeSpan.FromMinutes(10));

        var map = new GameMap(
            new[] { a, b },
            new[] { connection });

        var army = TestArmies.Create(
            Guid.NewGuid(),
            Guid.NewGuid(),
            a.Id);

        army.StartMovement(
            connection,
            startedAt);

        var world = new WorldState(
            map,
            new[] { army },
            startedAt);

        WorldSimulator.AdvanceTo(
            world,
            startedAt.AddMinutes(5));

        Assert.True(army.IsMoving);
        Assert.Null(army.LocationId);

        Assert.Equal(
            startedAt.AddMinutes(5),
            world.CheckpointTime);
    }

    [Fact]
    public void AdvanceTo_AtArrival_CompletesMovement()
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

        var connection = new MapConnection(
            a.Id,
            b.Id,
            TimeSpan.FromMinutes(10));

        var map = new GameMap(
            new[] { a, b },
            new[] { connection });

        var army = TestArmies.Create(
            Guid.NewGuid(),
            Guid.NewGuid(),
            a.Id);

        army.StartMovement(
            connection,
            startedAt);

        var world = new WorldState(
            map,
            new[] { army },
            startedAt);

        WorldSimulator.AdvanceTo(
            world,
            startedAt.AddMinutes(10));

        Assert.False(army.IsMoving);
        Assert.Equal(b.Id, army.LocationId);

        Assert.Equal(
            startedAt.AddMinutes(10),
            world.CheckpointTime);
    }

    [Fact]
    public void AdvanceTo_PastArrival_CompletesMovementWithoutTicks()
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

        var connection = new MapConnection(
            a.Id,
            b.Id,
            TimeSpan.FromMinutes(10));

        var map = new GameMap(
            new[] { a, b },
            new[] { connection });

        var army = TestArmies.Create(
            Guid.NewGuid(),
            Guid.NewGuid(),
            a.Id);

        army.StartMovement(
            connection,
            startedAt);

        var world = new WorldState(
            map,
            new[] { army },
            startedAt);

        WorldSimulator.AdvanceTo(
            world,
            startedAt.AddHours(3));

        Assert.False(army.IsMoving);
        Assert.Equal(b.Id, army.LocationId);

        Assert.Equal(
            startedAt.AddHours(3),
            world.CheckpointTime);
    }

    [Fact]
    public void AdvanceTo_CompletesSimultaneousArrivals()
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

        var destination = new Location(
            Guid.NewGuid(),
            "Destination");

        var connectionA = new MapConnection(
            a.Id,
            destination.Id,
            TimeSpan.FromMinutes(10));

        var connectionB = new MapConnection(
            b.Id,
            destination.Id,
            TimeSpan.FromMinutes(10));

        var map = new GameMap(
            new[] { a, b, destination },
            new[] { connectionA, connectionB });

        var army1 = TestArmies.Create(
            Guid.NewGuid(),
            Guid.NewGuid(),
            a.Id);

        var army2 = TestArmies.Create(
            Guid.NewGuid(),
            Guid.NewGuid(),
            b.Id);

        army1.StartMovement(
            connectionA,
            startedAt);

        army2.StartMovement(
            connectionB,
            startedAt);

        var world = new WorldState(
            map,
            new[] { army1, army2 },
            startedAt);

        WorldSimulator.AdvanceTo(
            world,
            startedAt.AddMinutes(20));

        Assert.False(army1.IsMoving);
        Assert.False(army2.IsMoving);

        Assert.Equal(
            destination.Id,
            army1.LocationId);

        Assert.Equal(
            destination.Id,
            army2.LocationId);
    }

    [Fact]
    public void AdvanceTo_Backwards_IsRejected()
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

        var world = new WorldState(
            map,
            Array.Empty<Army>(),
            checkpoint);

        Assert.Throws<InvalidOperationException>(() =>
            WorldSimulator.AdvanceTo(
                world,
                checkpoint.AddMinutes(-1)));
    }
}