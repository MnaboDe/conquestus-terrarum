using System.Text.Json;
using ConquestusTerrarum.Domain.World;
using static ConquestusTerrarum.Testing.SnapshotWorlds;

namespace ConquestusTerrarum.Domain.Tests;

public sealed class PostBattleWoundTests
{
    private static WorldState RetreatBattle(bool healthyRetreater = true, bool continuing = false)
    {
        var road = new MapConnection(Id(2), Id(1), TimeSpan.FromSeconds(1));
        var leaving = Army(11, 21, 2, Unit(101, dps: 0, current: 40), Unit(102, dps: 0, current: 20),
            Unit(103, dps: 0, current: 0), Unit(104, dps: 0, current: healthyRetreater ? 100 : 10));
        leaving.StartMovement(road, Start);
        var armies = new List<Army>
        {
            leaving,
            Army(12, 22, 1, Unit(105, dps: 0, current: 30), Unit(106, dps: 0, current: 25),
                Unit(107, dps: 0), Unit(108, dps: 0, current: 0)),
            Army(13, 22, 1, Unit(109, dps: 0, current: 60))
        };
        if (continuing) armies.Add(Army(14, 23, 1, Unit(110, dps: 0, current: 50)));
        var world = new WorldState(new GameMap([new Location(Id(1), "Battle"), new Location(Id(2), "Origin")],
            [road]), armies, Start, randomSeed: 17);
        WorldSimulator.AdvanceTo(world, Start.AddSeconds(1));
        return world;
    }

    [Fact]
    public void SuccessfulRetreatResolvesMixedArmiesAndAllRemainingParticipants()
    {
        var world = RetreatBattle();
        world.Retreat(Id(11), Start.AddSeconds(1));
        var leaving = world.GetArmy(Id(11));
        Assert.Equal(new decimal[] { 0, 0, 0, 100 }, leaving.Units.Select(u => u.CurrentHitPoints));
        Assert.True(leaving.Units.Last().IsAlive);
        Assert.False(leaving.IsDestroyed);
        Assert.False(leaving.IsInBattle);
        Assert.Equal(MovementKind.Retreat, leaving.Movement!.Kind);
        Assert.Equal(Id(2), leaving.Movement.ToLocationId);
        Assert.Equal(Start.AddSeconds(2), leaving.Movement.ArriveAt);
        Assert.Equal(new decimal[] { 100, 100, 100, 0 }, world.GetArmy(Id(12)).Units.Select(u => u.CurrentHitPoints));
        Assert.Equal(100, world.GetArmy(Id(13)).Units.Single().CurrentHitPoints);
        Assert.All(world.Armies, a => Assert.False(a.IsInBattle));
        Assert.Empty(world.Battles);
    }

    [Fact]
    public void AllWoundedRetreatDestroysArmyWithoutTravelAndHealsRemainingSide()
    {
        var world = RetreatBattle(healthyRetreater: false);
        world.Retreat(Id(11), Start.AddSeconds(1));
        Assert.True(world.GetArmy(Id(11)).IsDestroyed);
        Assert.Null(world.GetArmy(Id(11)).Movement);
        Assert.Null(world.GetArmy(Id(11)).LocationId);
        Assert.Equal(new decimal[] { 100, 100, 100, 0 }, world.GetArmy(Id(12)).Units.Select(u => u.CurrentHitPoints));
        Assert.Empty(world.Battles);
    }

    [Fact]
    public void WithdrawalFromContinuingMultiParticipantBattleDoesNotHealRemainingCombatants()
    {
        var world = RetreatBattle(continuing: true);
        world.Retreat(Id(11), Start.AddSeconds(1));
        Assert.Equal(new decimal[] { 0, 0, 0, 100 }, world.GetArmy(Id(11)).Units.Select(u => u.CurrentHitPoints));
        Assert.Equal(30, world.GetArmy(Id(12)).Units[0].CurrentHitPoints);
        Assert.Equal(60, world.GetArmy(Id(13)).Units[0].CurrentHitPoints);
        Assert.Equal(50, world.GetArmy(Id(14)).Units[0].CurrentHitPoints);
        Assert.Single(world.Battles);
        Assert.True(world.GetArmy(Id(12)).IsInBattle);
    }

    private static WorldState EliminationBattle()
    {
        return new WorldState(new GameMap([new Location(Id(1), "Battle")], []),
            [Army(11, 21, 1, Unit(101, dps: 10, current: 30), Unit(102, dps: 10),
                 Unit(103, dps: 10, current: 50), Unit(104, current: 0)),
             Army(12, 22, 1, Unit(105, hp: 10, dps: 1))], Start, randomSeed: 19);
    }

    [Fact]
    public void EliminationHealsAllLivingRemainingUnitsAtTheExactEndBoundaryWithoutRevivingDeaths()
    {
        var world = EliminationBattle();
        WorldSimulator.AdvanceTo(world, Start.AddSeconds(1).AddTicks(-1));
        Assert.Single(world.Battles);
        Assert.True(world.GetArmy(Id(11)).Units[0].IsWounded);
        WorldSimulator.AdvanceTo(world, Start.AddSeconds(1));
        Assert.Empty(world.Battles);
        Assert.Equal(new decimal[] { 100, 100, 100, 0 }, world.GetArmy(Id(11)).Units.Select(u => u.CurrentHitPoints));
        Assert.True(world.GetArmy(Id(12)).IsDestroyed);
        Assert.Equal(0, world.GetArmy(Id(12)).Units.Single().CurrentHitPoints);
    }

    [Fact]
    public void ChunkedAdvanceAndCheckpointRestoreProduceTheSameResolvedHealth()
    {
        var direct = EliminationBattle();
        var chunked = EliminationBattle();
        WorldSimulator.AdvanceTo(chunked, Start.AddSeconds(.5));
        var restored = WorldState.Restore(chunked.CreateSnapshot());
        WorldSimulator.AdvanceTo(direct, Start.AddSeconds(10));
        WorldSimulator.AdvanceTo(chunked, Start.AddSeconds(10));
        WorldSimulator.AdvanceTo(restored, Start.AddSeconds(10));
        Assert.Equal(JsonSerializer.Serialize(direct.CreateSnapshot()), JsonSerializer.Serialize(chunked.CreateSnapshot()));
        Assert.Equal(JsonSerializer.Serialize(direct.CreateSnapshot()), JsonSerializer.Serialize(restored.CreateSnapshot()));
    }

    [Fact]
    public void RepeatedResolvedAdvanceAndRestorationAreIdempotent()
    {
        var world = RetreatBattle();
        world.Retreat(Id(11), Start.AddSeconds(1));
        var resolved = JsonSerializer.Serialize(world.CreateSnapshot());
        WorldSimulator.AdvanceTo(world, world.CheckpointTime);
        Assert.Equal(resolved, JsonSerializer.Serialize(world.CreateSnapshot()));
        var restored = WorldState.Restore(world.CreateSnapshot());
        WorldSimulator.AdvanceTo(restored, Start.AddSeconds(10));
        Assert.Equal(new decimal[] { 0, 0, 0, 100 }, restored.GetArmy(Id(11)).Units.Select(u => u.CurrentHitPoints));
        Assert.Equal(new decimal[] { 100, 100, 100, 0 }, restored.GetArmy(Id(12)).Units.Select(u => u.CurrentHitPoints));
    }

    [Fact]
    public void RejectedRetreatWithoutDestinationDoesNotKillOrHealWounds()
    {
        var world = new WorldState(new GameMap([new Location(Id(1), "Battle")], []),
            [Army(11, 21, 1, Unit(101, dps: 0, current: 40)), Army(12, 22, 1, Unit(102, dps: 0, current: 30))], Start);
        var before = world.CreateSnapshot();
        Assert.Throws<InvalidOperationException>(() => world.Retreat(Id(11), Start));
        Assert.Equal(before.Armies.SelectMany(a => a.Units), world.CreateSnapshot().Armies.SelectMany(a => a.Units));
        Assert.Equal(before.Battles.Select(b => b.RandomState), world.Battles.Select(b => b.RandomState));
        Assert.All(world.Armies, a => Assert.True(a.IsInBattle));
    }

    [Fact]
    public void StaleRetreatAfterEliminationObservesAlreadyHealedSurvivors()
    {
        var world = EliminationBattle();
        WorldSimulator.AdvanceTo(world, Start.AddSeconds(1));
        var before = JsonSerializer.Serialize(world.CreateSnapshot());
        Assert.Throws<InvalidOperationException>(() => world.Retreat(Id(11), Start.AddSeconds(1)));
        Assert.Equal(before, JsonSerializer.Serialize(world.CreateSnapshot()));
    }

    [Fact]
    public void WoundedUnitsOutsideAnEndingBattleDoNotRegenerateOnReads()
    {
        var battle = EliminationBattle().CreateSnapshot();
        var other = Army(13, 23, 2, Unit(106, dps: 0, current: 40));
        var outside = new WorldState(new GameMap([new Location(Id(2), "Outside")], []), [other], Start).CreateSnapshot();
        var world = WorldState.Restore(battle with
        {
            Locations = battle.Locations.AddRange(outside.Locations),
            Armies = battle.Armies.AddRange(outside.Armies)
        });
        WorldSimulator.AdvanceTo(world, Start.AddSeconds(1));
        WorldSimulator.AdvanceTo(world, Start.AddSeconds(50));
        Assert.Equal(40, world.GetArmy(Id(13)).Units.Single().CurrentHitPoints);
    }

    [Fact]
    public void NonParticipantAlliedToBothSidesIsNotHealedWhenTheirBattleEnds()
    {
        var world = new WorldState(new GameMap([new Location(Id(1), "Battle")], []),
            [Army(11, 21, 1, Unit(101, dps: 10, current: 30)),
             Army(12, 22, 1, Unit(102, hp: 10, dps: 1)), Army(13, 23, 1, Unit(103, dps: 0, current: 40))],
            Start, [new PlayerAlliance(Id(21), Id(23)), new PlayerAlliance(Id(22), Id(23))]);
        Assert.False(world.GetArmy(Id(13)).IsInBattle);
        WorldSimulator.AdvanceTo(world, Start.AddSeconds(1));
        Assert.Empty(world.Battles);
        Assert.Equal(100, world.GetArmy(Id(11)).Units.Single().CurrentHitPoints);
        Assert.Equal(40, world.GetArmy(Id(13)).Units.Single().CurrentHitPoints);
    }

    [Fact]
    public void BattleEndsWithHealthySurvivorsAndSimultaneousDeathsRemainDead()
    {
        var world = new WorldState(new GameMap([new Location(Id(1), "Battle")], []),
            [Army(11, 21, 1, Unit(101, dps: 10)), Army(12, 22, 1, Unit(102, hp: 10, dps: 0))], Start);
        WorldSimulator.AdvanceTo(world, Start.AddSeconds(1));
        Assert.Equal(100, world.GetArmy(Id(11)).Units.Single().CurrentHitPoints);
        var mutual = new WorldState(world.Map,
            [Army(13, 23, 1, Unit(103, hp: 10, dps: 10)), Army(14, 24, 1, Unit(104, hp: 10, dps: 10))], Start);
        WorldSimulator.AdvanceTo(mutual, Start.AddSeconds(1));
        Assert.Empty(mutual.Battles);
        Assert.All(mutual.Armies, a => Assert.True(a.IsDestroyed));
    }
}
