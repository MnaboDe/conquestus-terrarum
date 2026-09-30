using ConquestusTerrarum.Domain.World;

namespace ConquestusTerrarum.Domain.Tests;

public class MapConnectionTests
{
    [Fact]
    public void Connects_WorksInBothDirections()
    {
        var a = Guid.NewGuid();
        var b = Guid.NewGuid();

        var connection = new MapConnection(
            a,
            b,
            TimeSpan.FromMinutes(10));

        Assert.True(connection.Connects(a, b));
        Assert.True(connection.Connects(b, a));
    }

    [Fact]
    public void Constructor_RejectsSelfConnection()
    {
        var locationId = Guid.NewGuid();

        Assert.Throws<ArgumentException>(() =>
            new MapConnection(
                locationId,
                locationId,
                TimeSpan.FromMinutes(10)));
    }

    [Fact]
    public void Constructor_RejectsNonPositiveTravelTime()
    {
        Assert.Throws<ArgumentOutOfRangeException>(() =>
            new MapConnection(
                Guid.NewGuid(),
                Guid.NewGuid(),
                TimeSpan.Zero));
    }
}