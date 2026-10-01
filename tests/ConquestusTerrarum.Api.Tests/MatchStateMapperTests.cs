using System.Text.Json;
using ConquestusTerrarum.Api.Mapping;
using ConquestusTerrarum.Domain.World;
using static ConquestusTerrarum.Testing.SnapshotWorlds;

namespace ConquestusTerrarum.Api.Tests;

public class MatchStateMapperTests
{
    [Fact]
    public void ReorderedDomainCollectionsProduceIdenticalClientJson()
    {
        WorldState Create(bool reverse)
        {
            var locations = new Location[] { new(Id(2), "Two", combatSlotCount: 2), new(Id(1), "One", combatSlotCount: 2), new(Id(3), "Three") };
            var connections = new MapConnection[] { new(Id(2), Id(3), TimeSpan.FromSeconds(2)), new(Id(1), Id(2), TimeSpan.FromSeconds(1)) };
            Unit[] Units(int id) => reverse ? [Unit(id), Unit(id + 1)] : [Unit(id + 1), Unit(id)];
            var armies = new[] { Army(14, 24, 2, Units(107)), Army(12, 22, 1, Units(103)), Army(13, 23, 2, Units(105)), Army(11, 21, 1, Units(101)) };
            var alliances = new PlayerAlliance[] { new(Id(21), Id(26)), new(Id(25), Id(26)), new(Id(21), Id(25)) };
            return new WorldState(new GameMap(reverse ? locations.Reverse() : locations, reverse ? connections.Reverse() : connections),
                reverse ? armies.Reverse() : armies, Start, reverse ? alliances.Reverse() : alliances, randomSeed: 444);
        }
        var first = MatchStateMapper.Map(Id(9000), Create(false), Start, 1);
        var second = MatchStateMapper.Map(Id(9000), Create(true), Start, 1);
        Assert.Equal(JsonSerializer.Serialize(first, MatchEndpointTests.JsonOptions), JsonSerializer.Serialize(second, MatchEndpointTests.JsonOptions));
        Assert.Equal(first.Locations.Select(l => l.Id).Order(), first.Locations.Select(l => l.Id));
        Assert.Equal(first.Armies.Select(a => a.Id).Order(), first.Armies.Select(a => a.Id));
        Assert.All(first.Armies, a => Assert.Equal(a.Units.Select(u => u.Id).Order(), a.Units.Select(u => u.Id)));
        Assert.Equal(first.ActiveBattles.Select(b => b.LocationId).Order(), first.ActiveBattles.Select(b => b.LocationId));
        Assert.All(first.ActiveBattles, b => Assert.Equal(b.CombatSlots.Select(s => s.Index).Order(), b.CombatSlots.Select(s => s.Index)));
        Assert.Equal(first.Alliances.OrderBy(a => a.FirstPlayerId).ThenBy(a => a.SecondPlayerId), first.Alliances);
        Assert.Equal(first.Connections.OrderBy(c => c.FromLocationId).ThenBy(c => c.ToLocationId).ThenBy(c => c.TravelTimeTicks), first.Connections);
    }
}
