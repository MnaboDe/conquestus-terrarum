using ConquestusTerrarum.Domain.World;

namespace ConquestusTerrarum.Domain.Tests;

public class ArmyTests
{
    [Fact]
    public void StartMovement_RemovesArmyFromLocation()
    {
        var startLocation = Guid.NewGuid();
        var destination = Guid.NewGuid();

        var army = TestArmies.Create(
            Guid.NewGuid(),
            Guid.NewGuid(),
            startLocation);

        var connection = new MapConnection(
            startLocation,
            destination,
            TimeSpan.FromMinutes(10));

        var startedAt = new DateTimeOffset(2026, 1, 1, 10, 0, 0, TimeSpan.Zero);

        army.StartMovement(
            connection,
            startedAt);

        Assert.True(army.IsMoving);
        Assert.Null(army.LocationId);

        Assert.NotNull(army.Movement);
        Assert.Equal(
            startLocation,
            army.Movement.FromLocationId);

        Assert.Equal(
            destination,
            army.Movement.ToLocationId);

        Assert.Equal(
            startedAt.AddMinutes(10),
            army.Movement.ArriveAt);
    }

    [Fact]
    public void CompleteMovement_PlacesArmyAtDestination()
    {
        var startLocation = Guid.NewGuid();
        var destination = Guid.NewGuid();

        var army = TestArmies.Create(
            Guid.NewGuid(),
            Guid.NewGuid(),
            startLocation);

        var connection = new MapConnection(
            startLocation,
            destination,
            TimeSpan.FromMinutes(10));

        army.StartMovement(
            connection,
            new DateTimeOffset(2026, 1, 1, 10, 0, 0, TimeSpan.Zero));

        army.CompleteMovement();

        Assert.False(army.IsMoving);
        Assert.Equal(
            destination,
            army.LocationId);

        Assert.Null(army.Movement);
    }

    [Fact]
    public void StartMovement_WhenAlreadyMoving_IsRejected()
    {
        var a = Guid.NewGuid();
        var b = Guid.NewGuid();
        var c = Guid.NewGuid();

        var army = TestArmies.Create(
            Guid.NewGuid(),
            Guid.NewGuid(),
            a);

        var firstConnection = new MapConnection(
            a,
            b,
            TimeSpan.FromMinutes(10));

        var secondConnection = new MapConnection(
            b,
            c,
            TimeSpan.FromMinutes(10));

        army.StartMovement(
            firstConnection,
            new DateTimeOffset(2026, 1, 1, 10, 0, 0, TimeSpan.Zero));

        Assert.Throws<InvalidOperationException>(() =>
            army.StartMovement(
                secondConnection,
                new DateTimeOffset(2026, 1, 1, 10, 0, 0, TimeSpan.Zero)));
    }

    [Fact]
    public void StartMovement_RejectsUnrelatedConnection()
    {
        var armyLocation = Guid.NewGuid();

        var army = TestArmies.Create(
            Guid.NewGuid(),
            Guid.NewGuid(),
            armyLocation);

        var connection = new MapConnection(
            Guid.NewGuid(),
            Guid.NewGuid(),
            TimeSpan.FromMinutes(10));

        Assert.Throws<InvalidOperationException>(() =>
            army.StartMovement(
                connection,
                new DateTimeOffset(2026, 1, 1, 10, 0, 0, TimeSpan.Zero)));
    }

    [Fact]
    public void CompleteMovement_WhenArmyIsNotMoving_IsRejected()
    {
        var army = TestArmies.Create(
            Guid.NewGuid(),
            Guid.NewGuid(),
            Guid.NewGuid());

        Assert.Throws<InvalidOperationException>(() =>
            army.CompleteMovement());
    }
}