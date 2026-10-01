using ConquestusTerrarum.Domain.World;

namespace ConquestusTerrarum.Testing;

internal static class SnapshotWorlds
{
    internal static readonly DateTimeOffset Start = new(2026, 1, 1, 10, 0, 0, TimeSpan.FromHours(2));
    internal static Guid Id(int value) => new(value, 0, 0, new byte[8]);
    internal static Unit Unit(int id, decimal hp = 100, decimal dps = 10, decimal? current = null) => new(Id(id), hp, dps, current);
    internal static Army Army(int id, int owner, int location, params Unit[] units) => new(Id(id), Id(owner), Id(location), units);

    internal static WorldState Combat()
    {
        var connection = new MapConnection(Id(2), Id(1), TimeSpan.FromSeconds(3).Add(TimeSpan.FromTicks(1)));
        var arriving = Army(13, 21, 2, Unit(107));
        arriving.StartMovement(connection, Start);
        var world = new WorldState(new GameMap([
            new(Id(1), "Battlefield", Id(99), 2), new(Id(2), "Origin", Id(21)),
            new(Id(3), "Second battle"), new(Id(4), "Empty")], [connection]),
            [Army(11, 21, 1, Unit(101), Unit(102, 20), Unit(103, 35)),
             Army(12, 22, 1, Unit(104, 90, 7), Unit(105, 30), Unit(106, 40)), arriving,
             Army(14, 23, 3, Unit(108, 1, 1)), Army(15, 24, 3, Unit(109, 2, 1)),
             Army(16, 26, 4), Army(17, 25, 2, Unit(110, dps: 0))], Start,
            [new(Id(21), Id(25))], ulong.MaxValue);
        WorldSimulator.AdvanceTo(world, Start.AddSeconds(0.5));
        return world;
    }

    internal static WorldState Retreat(bool enemyAtOrigin = false)
    {
        var connection = new MapConnection(Id(2), Id(1), TimeSpan.FromSeconds(1));
        var army = Army(11, 21, 2, Unit(101, dps: 0, current: 70), Unit(102, dps: 0));
        army.StartMovement(connection, Start);
        var armies = new List<Army> { army, Army(12, 22, 1, Unit(103, dps: 0)) };
        if (enemyAtOrigin) armies.Add(Army(13, 23, 2, Unit(104, dps: 0)));
        var world = new WorldState(new GameMap([new(Id(1), "Battlefield"), new(Id(2), "Origin")], [connection]), armies, Start, randomSeed: 456);
        WorldSimulator.AdvanceTo(world, Start.AddSeconds(1));
        return world;
    }
}
