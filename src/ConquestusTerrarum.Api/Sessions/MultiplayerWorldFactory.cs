using ConquestusTerrarum.Domain.World;

namespace ConquestusTerrarum.Api.Sessions;

public static class MultiplayerWorldFactory
{
    public static WorldState Create(DateTimeOffset databaseTime, Guid firstPlayer, Guid secondPlayer)
    {
        var west = new Location(Guid.NewGuid(), "West camp", firstPlayer, 2);
        var center = new Location(Guid.NewGuid(), "Crossroads", combatSlotCount: 2);
        var east = new Location(Guid.NewGuid(), "East camp", secondPlayer, 2);
        var north = new Location(Guid.NewGuid(), "North pass");
        var map = new GameMap([west, center, east, north],
        [
            new MapConnection(west.Id, center.Id, TimeSpan.FromSeconds(5)),
            new MapConnection(center.Id, east.Id, TimeSpan.FromSeconds(5)),
            new MapConnection(center.Id, north.Id, TimeSpan.FromSeconds(8)),
            new MapConnection(west.Id, north.Id, TimeSpan.FromSeconds(12))
        ]);
        return new WorldState(map,
            [Army(firstPlayer, west.Id), Army(firstPlayer, west.Id),
             Army(secondPlayer, east.Id), Army(secondPlayer, east.Id)], databaseTime, randomSeed: 12345);
    }

    private static Army Army(Guid owner, Guid location) => new(Guid.NewGuid(), owner, location,
        [new Unit(Guid.NewGuid(), 100m, 5m), new Unit(Guid.NewGuid(), 100m, 5m), new Unit(Guid.NewGuid(), 100m, 5m)]);
}
