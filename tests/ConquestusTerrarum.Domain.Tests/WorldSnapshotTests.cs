using System.Collections.Immutable;
using System.Text.Json;
using ConquestusTerrarum.Domain.World;
using ConquestusTerrarum.Testing;
using static ConquestusTerrarum.Testing.SnapshotWorlds;

namespace ConquestusTerrarum.Domain.Tests;

public class WorldSnapshotTests
{
    private static void Equal(WorldSnapshot expected, WorldSnapshot actual) =>
        Assert.True(JsonElement.DeepEquals(JsonSerializer.SerializeToElement(expected), JsonSerializer.SerializeToElement(actual)),
            "World snapshots differ logically.");

    [Fact]
    public void SimpleWorldRoundTrips()
    {
        var world = new WorldState(new GameMap([new(Id(1), "Home", Id(21), 3)], []), [Army(11, 21, 1, Unit(101))], Start, randomSeed: 789);
        Equal(world.CreateSnapshot(), WorldState.Restore(world.CreateSnapshot()).CreateSnapshot());
    }

    [Fact]
    public void ActiveWorldRoundTripsWithoutReconciliationOrRandomConsumption()
    {
        var world = Combat();
        Equal(world.CreateSnapshot(), WorldState.Restore(world.CreateSnapshot()).CreateSnapshot());
    }

    [Fact]
    public void SlotPairingsIndicesAnchorsAndDeathBoundariesArePreserved()
    {
        var world = Combat(); var restored = WorldState.Restore(world.CreateSnapshot());
        Assert.Equal(world.CreateSnapshot().Battles.SelectMany(b => b.Slots), restored.CreateSnapshot().Battles.SelectMany(b => b.Slots));
        Assert.Equal(world.Battles.Select(b => b.RandomState), restored.Battles.Select(b => b.RandomState));
    }

    [Fact]
    public void MovingArmyPreservesExactArrivalAndOffsets()
    {
        var snapshot = Combat().CreateSnapshot(); var restored = WorldState.Restore(snapshot);
        var before = snapshot.Armies.Single(a => a.Id == Id(13)).Movement!;
        var after = restored.GetArmy(Id(13)).Movement!;
        Assert.Equal(before.ArriveAt.Ticks, after.ArriveAt.Ticks);
        Assert.Equal(before.ArriveAt.Offset, after.ArriveAt.Offset);
        Assert.Equal(before.StartedAt.Offset, after.StartedAt.Offset);
        Assert.Equal(Start.AddSeconds(3).AddTicks(1), after.ArriveAt);
        Assert.Equal(before.Kind, after.Kind);
    }

    [Fact]
    public void UnitHpAndStatsAreExactAndDetached()
    {
        var world = Combat(); var snapshot = world.CreateSnapshot();
        var restored = WorldState.Restore(snapshot);
        Assert.Equal(snapshot.Armies.SelectMany(a => a.Units), restored.CreateSnapshot().Armies.SelectMany(a => a.Units));
        WorldSimulator.AdvanceTo(world, Start.AddSeconds(10));
        Equal(snapshot, restored.CreateSnapshot());
    }

    [Fact]
    public void OwnershipAlliancesSeedAndSlotCountsArePreserved()
    {
        var world = WorldState.Restore(Combat().CreateSnapshot());
        Assert.Equal(Id(99), world.Map.GetLocation(Id(1)).OwnerId);
        Assert.Equal(2, world.Map.GetLocation(Id(1)).CombatSlotCount);
        Assert.Equal(ulong.MaxValue, world.RandomSeed);
        Assert.True(world.AreAllies(Id(21), Id(25)));
        Assert.True(world.AreHostile(Id(21), Id(22)));
    }

    [Fact]
    public void DestroyedArmyRemainsDestroyedAndUnlocated()
    {
        var world = Combat(); WorldSimulator.AdvanceTo(world, Start.AddSeconds(1));
        var restored = WorldState.Restore(world.CreateSnapshot());
        Assert.True(restored.GetArmy(Id(14)).IsDestroyed);
        Assert.True(restored.GetArmy(Id(16)).IsDestroyed);
        Assert.Null(restored.GetArmy(Id(14)).LocationId);
        Assert.False(restored.GetArmy(Id(14)).IsInBattle);
        Equal(world.CreateSnapshot(), restored.CreateSnapshot());
    }

    [Fact]
    public void FutureSimulationMatchesAcrossSeveralDeathBoundaries()
    {
        var world = Combat(); var restored = WorldState.Restore(world.CreateSnapshot());
        foreach (var seconds in new[] { 1, 3, 4, 7, 12, 20, 60 })
        {
            WorldSimulator.AdvanceTo(world, Start.AddSeconds(seconds));
            WorldSimulator.AdvanceTo(restored, Start.AddSeconds(seconds));
            Equal(world.CreateSnapshot(), restored.CreateSnapshot());
        }
    }

    [Fact]
    public void FutureSimulationMatchesAcrossDifferentCheckpointFrequencies()
    {
        var world = Combat(); var restored = WorldState.Restore(world.CreateSnapshot());
        WorldSimulator.AdvanceTo(world, Start.AddSeconds(60));
        foreach (var seconds in new[] { 1, 3, 4, 7, 12, 20, 60 }) WorldSimulator.AdvanceTo(restored, Start.AddSeconds(seconds));
        Equal(world.CreateSnapshot(), restored.CreateSnapshot());
    }

    [Fact]
    public void FutureMovementArrivalMatches()
    {
        var world = Combat(); var restored = WorldState.Restore(world.CreateSnapshot());
        var arrival = world.GetArmy(Id(13)).Movement!.ArriveAt;
        WorldSimulator.AdvanceTo(world, arrival); WorldSimulator.AdvanceTo(restored, arrival);
        Assert.Null(restored.GetArmy(Id(13)).Movement);
        Assert.Equal(arrival, restored.GetArmy(Id(13)).EnteredLocationAt);
        Equal(world.CreateSnapshot(), restored.CreateSnapshot());
    }

    [Fact]
    public void RetreatAfterRestorePreservesCasualtiesDestinationAndTravel()
    {
        var world = Retreat(); var restored = WorldState.Restore(world.CreateSnapshot());
        Assert.True(restored.GetArmy(Id(11)).CanRetreat);
        world.Retreat(Id(11), Start.AddSeconds(1)); restored.Retreat(Id(11), Start.AddSeconds(1));
        Assert.False(restored.GetArmy(Id(11)).Units.Single(u => u.Id == Id(101)).IsAlive);
        Assert.Equal(MovementKind.Retreat, restored.GetArmy(Id(11)).Movement!.Kind);
        Equal(world.CreateSnapshot(), restored.CreateSnapshot());
    }

    [Fact]
    public void RetreatLockSurvivesTransitAndHostileArrivalRestoration()
    {
        var world = Retreat(true); world.Retreat(Id(11), Start.AddSeconds(1));
        var restored = WorldState.Restore(world.CreateSnapshot());
        Equal(world.CreateSnapshot(), restored.CreateSnapshot());
        WorldSimulator.AdvanceTo(world, Start.AddSeconds(2)); WorldSimulator.AdvanceTo(restored, Start.AddSeconds(2));
        restored = WorldState.Restore(restored.CreateSnapshot());
        Assert.True(restored.GetArmy(Id(11)).IsInBattle);
        Assert.False(restored.GetArmy(Id(11)).CanRetreat);
        Assert.Throws<InvalidOperationException>(() => restored.Retreat(Id(11), Start.AddSeconds(2)));
        Equal(world.CreateSnapshot(), restored.CreateSnapshot());
    }

    [Fact]
    public void SimultaneousDeathsAndArrivalsRemainDeterministic()
    {
        var connection = new MapConnection(Id(2), Id(1), TimeSpan.FromSeconds(10));
        var a = Army(13, 23, 2, Unit(103)); var b = Army(14, 24, 2, Unit(104));
        a.StartMovement(connection, Start); b.StartMovement(connection, Start);
        var world = new WorldState(new GameMap([new(Id(1), "Battle", combatSlotCount: 2), new(Id(2), "Origin")], [connection]),
            [Army(11, 21, 1, Unit(101)), Army(12, 22, 1, Unit(102)), a, b], Start, randomSeed: 100);
        WorldSimulator.AdvanceTo(world, Start.AddSeconds(3));
        var restored = WorldState.Restore(world.CreateSnapshot());
        foreach (var seconds in new[] { 10, 11, 20 })
        {
            WorldSimulator.AdvanceTo(world, Start.AddSeconds(seconds)); WorldSimulator.AdvanceTo(restored, Start.AddSeconds(seconds));
            Equal(world.CreateSnapshot(), restored.CreateSnapshot());
        }
    }

    [Fact]
    public void RestoringReorderedCollectionsDoesNotChangeContinuation()
    {
        var world = Combat(); var snapshot = world.CreateSnapshot();
        var restored = WorldState.Restore(snapshot with {
            Armies = snapshot.Armies.Reverse().ToImmutableArray(), Locations = snapshot.Locations.Reverse().ToImmutableArray(),
            Battles = snapshot.Battles.Reverse().Select(b => b with { Slots = b.Slots.Reverse().ToImmutableArray() }).ToImmutableArray() });
        WorldSimulator.AdvanceTo(world, Start.AddSeconds(30)); WorldSimulator.AdvanceTo(restored, Start.AddSeconds(30));
        Equal(world.CreateSnapshot(), restored.CreateSnapshot());
    }

    [Fact]
    public void TimestampOffsetRepresentationIsRetained()
    {
        var snapshot = Combat().CreateSnapshot();
        var restored = WorldState.Restore(snapshot).CreateSnapshot();
        Assert.Equal(snapshot.CheckpointTime.Offset, restored.CheckpointTime.Offset);
        Equal(snapshot, restored);
    }

    [Fact]
    public void MissingBattleIsRejectedInsteadOfRecreated()
    {
        var snapshot = Combat().CreateSnapshot();
        Assert.Throws<ArgumentException>(() => WorldState.Restore(snapshot with { Battles = [] }));
    }

    [Fact]
    public void InvalidSlotAnchorsAreRejected()
    {
        var snapshot = Combat().CreateSnapshot(); var battle = snapshot.Battles[0];
        var corrupt = battle with { Slots = battle.Slots.SetItem(0, battle.Slots[0] with { FirstStartingHitPoints = 999999 }) };
        Assert.Throws<ArgumentException>(() => WorldState.Restore(snapshot with { Battles = snapshot.Battles.SetItem(0, corrupt) }));
    }

    [Fact]
    public void MissingSlotsAreRejectedInsteadOfRepairedWithNewRandomness()
    {
        var snapshot = Combat().CreateSnapshot(); var battle = snapshot.Battles[0] with { Slots = [] };
        Assert.Throws<ArgumentException>(() => WorldState.Restore(snapshot with { Battles = snapshot.Battles.SetItem(0, battle) }));
    }

    [Fact]
    public void UnsupportedFormatAndDuplicateIdentitiesAreRejected()
    {
        var snapshot = Combat().CreateSnapshot();
        Assert.Throws<ArgumentException>(() => WorldState.Restore(snapshot with { FormatVersion = 99 }));
        Assert.Throws<ArgumentException>(() => WorldState.Restore(snapshot with { Armies = snapshot.Armies.Add(snapshot.Armies[0]) }));
    }

    [Fact]
    public void ParallelConnectionsAndChosenMovementDurationRoundTrip()
    {
        var shortRoute = new MapConnection(Id(1), Id(2), TimeSpan.FromSeconds(1));
        var longRoute = new MapConnection(Id(1), Id(2), TimeSpan.FromSeconds(3));
        var army = Army(11, 21, 1, Unit(101));
        army.StartMovement(longRoute, Start);
        var world = new WorldState(new GameMap([new(Id(1), "Home"), new(Id(2), "Next")], [shortRoute, longRoute]), [army], Start);
        var restored = WorldState.Restore(world.CreateSnapshot());
        Equal(world.CreateSnapshot(), restored.CreateSnapshot());
        WorldSimulator.AdvanceTo(restored, Start.AddSeconds(3));
        Assert.Equal(Id(2), restored.GetArmy(Id(11)).LocationId);
    }

    [Fact]
    public void InconsistentUnitHpIsRejected()
    {
        var snapshot = Combat().CreateSnapshot();
        var slot = snapshot.Battles[0].Slots[0];
        var armyIndex = snapshot.Armies.IndexOf(snapshot.Armies.Single(a => a.Id == slot.FirstArmyId));
        var army = snapshot.Armies[armyIndex];
        var unitIndex = army.Units.IndexOf(army.Units.Single(u => u.Id == slot.FirstUnitId));
        army = army with { Units = army.Units.SetItem(unitIndex, army.Units[unitIndex] with { CurrentHitPoints = 0.01m }) };
        Assert.Throws<ArgumentException>(() => WorldState.Restore(snapshot with { Armies = snapshot.Armies.SetItem(armyIndex, army) }));
    }

    [Fact]
    public void DuplicateCombatUnitIsRejected()
    {
        var snapshot = Combat().CreateSnapshot(); var battle = snapshot.Battles[0];
        battle = battle with { Slots = battle.Slots.SetItem(1, battle.Slots[0] with { Index = 1 }) };
        Assert.Throws<ArgumentException>(() => WorldState.Restore(snapshot with { Battles = snapshot.Battles.SetItem(0, battle) }));
    }

    [Fact]
    public void MissingRetreatLockIsRejectedDuringRetreatMovement()
    {
        var world = Retreat(); world.Retreat(Id(11), Start.AddSeconds(1));
        var snapshot = world.CreateSnapshot(); var army = snapshot.Armies[0] with { RetreatLocked = false };
        Assert.Throws<ArgumentException>(() => WorldState.Restore(snapshot with { Armies = snapshot.Armies.SetItem(0, army) }));
    }
}
