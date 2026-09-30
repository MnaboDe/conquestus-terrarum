using ConquestusTerrarum.Domain.World;

namespace ConquestusTerrarum.Domain.Tests;

public class LocationTests
{
    [Fact]
    public void Constructor_CreatesLocation()
    {
        var id = Guid.NewGuid();

        var location = new Location(id, "Rome");

        Assert.Equal(id, location.Id);
        Assert.Equal("Rome", location.Name);
    }

    [Fact]
    public void Constructor_RejectsEmptyName()
    {
        Assert.Throws<ArgumentException>(() =>
            new Location(Guid.NewGuid(), " "));
    }
}