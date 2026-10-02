using ConquestusTerrarum.Domain.World;

namespace ConquestusTerrarum.Api.Development;

/// <summary>Local bootstrap data only; these illustrative stats are not production unit types or balance.</summary>
public static class DemoWorldFactory
{
    public static WorldState Create(DateTimeOffset databaseTime)
    {
        var west = new Location(Guid.NewGuid(), "West camp", combatSlotCount: 2);
        var center = new Location(Guid.NewGuid(), "Crossroads", combatSlotCount: 2);
        var east = new Location(Guid.NewGuid(), "East camp", combatSlotCount: 2);
        var north = new Location(Guid.NewGuid(), "North pass");
        var firstPlayer = Guid.NewGuid();
        var secondPlayer = Guid.NewGuid();
        var ally = Guid.NewGuid();
        var map = new GameMap([west, center, east, north],
        [
            new MapConnection(west.Id, center.Id, TimeSpan.FromSeconds(5)),
            new MapConnection(center.Id, east.Id, TimeSpan.FromSeconds(5)),
            new MapConnection(center.Id, north.Id, TimeSpan.FromSeconds(8)),
            new MapConnection(west.Id, north.Id, TimeSpan.FromSeconds(12))
        ]);
        return new WorldState(map,
            [
                Army(firstPlayer, west.Id), Army(firstPlayer, west.Id), Army(secondPlayer, east.Id),
                Army(secondPlayer, east.Id), Army(ally, north.Id)
            ], databaseTime,
            [new PlayerAlliance(firstPlayer, ally)], 12345);
    }

    private static Army Army(Guid owner, Guid location)
    {
        return new Army(Guid.NewGuid(), owner, location,
        [
            new Unit(Guid.NewGuid(), 100m, 5m), new Unit(Guid.NewGuid(), 100m, 5m), new Unit(Guid.NewGuid(), 100m, 5m)
        ]);
    }
}