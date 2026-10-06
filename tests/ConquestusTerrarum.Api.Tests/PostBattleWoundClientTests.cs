using System.Text.Json;
using System.Text.Json.Serialization;
using ConquestusTerrarum.Api.Mapping;
using ConquestusTerrarum.Client;
using ConquestusTerrarum.Domain.World;
using static ConquestusTerrarum.Testing.SnapshotWorlds;

namespace ConquestusTerrarum.Api.Tests;

public sealed class PostBattleWoundClientTests
{
    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public void ResolvedServerHealthIsAcceptedAndDestroyedArmiesStayNonCommandableInActiveWorld(bool retreat)
    {
        var road = new MapConnection(Id(1), Id(2), TimeSpan.FromSeconds(1));
        var leaving = Army(11, 21, 2, Unit(101, hp: 100, dps: 0, current: 40));
        leaving.StartMovement(road, Start);
        var world = new WorldState(new GameMap([new Location(Id(1), "Battle"), new Location(Id(2), "Origin")], [road]),
            [leaving, Army(12, 22, 1, Unit(102, dps: retreat ? 0 : 40, current: 30), Unit(103, current: 0))], Start);
        WorldSimulator.AdvanceTo(world, Start.AddSeconds(1));
        if (retreat) world.Retreat(Id(11), Start.AddSeconds(1));
        else WorldSimulator.AdvanceTo(world, Start.AddSeconds(2));
        var response = MatchStateMapper.Map(Id(90), world, world.CheckpointTime, 4, 7);
        var options = new JsonSerializerOptions(JsonSerializerDefaults.Web)
        { IncludeFields = true, Converters = { new JsonStringEnumConverter(JsonNamingPolicy.CamelCase) } };
        var state = JsonSerializer.Deserialize<MatchStateDto>(JsonSerializer.Serialize(response, options), options)!;
        Assert.True(ClientMatchLifecycle.Valid(state));
        Assert.Equal("Game Active", ClientMatchLifecycle.Label(state));
        Assert.Empty(state.activeBattles);
        var dead = state.armies.Single(a => a.id == Id(11).ToString());
        Assert.True(dead.isDestroyed);
        Assert.False(dead.units.Single().isAlive);
        Assert.Equal(0, dead.units.Single().currentHitPoints);
        var session = MultiplayerSession.Restore(Id(90).ToString(), Id(21).ToString(), new string('A', 43), "ABC234");
        Assert.False(ClientMatchLifecycle.CanCommand(state, session, dead));
        var remaining = state.armies.Single(a => a.id == Id(12).ToString());
        Assert.Equal(100, remaining.units.Single(u => u.id == Id(102).ToString()).currentHitPoints);
        Assert.True(remaining.units.Single(u => u.id == Id(102).ToString()).isAlive);
        Assert.False(remaining.units.Single(u => u.id == Id(103).ToString()).isAlive);
        var page = new MatchUpdatesDto { nextCursor = 8, updates = [new() { sequence = 8, type = "BattleEnded",
            occurredAt = world.CheckpointTime.ToString("O"), payload = new() { locationId = Id(1).ToString() } }] };
        Assert.False(MatchUpdateApplier.TryApplyPage(state, page, 7, out var unchanged, out var cursor));
        Assert.Same(state, unchanged); // Existing coarse event requests authoritative resync, never client-side healing.
        Assert.Equal(7, cursor);
    }
}
