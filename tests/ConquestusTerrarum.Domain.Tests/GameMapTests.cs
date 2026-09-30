using ConquestusTerrarum.Domain.World;

namespace ConquestusTerrarum.Domain.Tests;

public class GameMapTests
{
    [Fact]
    public void GetConnection_ReturnsConnectionInBothDirections()
    {
        var a = new Location(Guid.NewGuid(), "A");
        var b = new Location(Guid.NewGuid(), "B");

        var connection = new MapConnection(
            a.Id,
            b.Id,
            TimeSpan.FromMinutes(10));

        var map = new GameMap(
            new[] { a, b },
            new[] { connection });

        Assert.Same(
            connection,
            map.GetConnection(a.Id, b.Id));

        Assert.Same(
            connection,
            map.GetConnection(b.Id, a.Id));
    }

    [Fact]
    public void GetConnection_RejectsLocationsWithoutConnection()
    {
        var a = new Location(Guid.NewGuid(), "A");
        var b = new Location(Guid.NewGuid(), "B");

        var map = new GameMap(
            new[] { a, b },
            Array.Empty<MapConnection>());

        Assert.Throws<InvalidOperationException>(() =>
            map.GetConnection(a.Id, b.Id));
    }

    [Fact]
    public void Constructor_RejectsConnectionToUnknownLocation()
    {
        var a = new Location(Guid.NewGuid(), "A");

        var connection = new MapConnection(
            a.Id,
            Guid.NewGuid(),
            TimeSpan.FromMinutes(10));

        Assert.Throws<ArgumentException>(() =>
            new GameMap(
                new[] { a },
                new[] { connection }));
    }
}