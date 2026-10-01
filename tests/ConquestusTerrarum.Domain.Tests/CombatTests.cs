using ConquestusTerrarum.Domain.World;

namespace ConquestusTerrarum.Domain.Tests;

public class CombatTests
{
    private static readonly DateTimeOffset Start = new(2026, 1, 1, 10, 0, 0, TimeSpan.Zero);
    private static Guid Id(int value) => new(value, 0, 0, new byte[8]);
    private static Unit Unit(int id, decimal hp = 100, decimal dps = 10, decimal? current = null) => new(Id(id), hp, dps, current);
    private static Army Army(int id, int owner, int location, params Unit[] units) => new(Id(id), Id(owner), Id(location), units);
    private static WorldState World(Army[] armies, int slots = 1, ulong seed = 0, PlayerAlliance[]? alliances = null) =>
        new(new GameMap(new[] { new Location(Id(1), "Battlefield", combatSlotCount: slots) }, []), armies, Start, alliances, seed);
    private static void Advance(WorldState world, decimal seconds) => WorldSimulator.AdvanceTo(world, Start.AddTicks((long)(seconds * TimeSpan.TicksPerSecond)));
    private static string Snapshot(WorldState world) => string.Join("|", world.Armies.OrderBy(a => a.Id).Select(a =>
        $"{a.Id}:{a.LocationId}:{a.IsDestroyed}:{a.IsInBattle}:{a.CanRetreat}:{a.EnteredLocationAt:O}:{a.Movement?.ArriveAt:O}:" +
        string.Join(",", a.Units.OrderBy(u => u.Id).Select(u => $"{u.Id}:{u.CurrentHitPoints}:{u.IsAlive}")))) +
        string.Join("|", world.Battles.OrderBy(b => b.LocationId).Select(b => $"{b.LocationId}:{b.StartedAt:O}:{b.RandomState}:" +
            string.Join(",", b.Slots.Select(s => $"{s.Index}:{s.FirstUnitId}:{s.SecondUnitId}:{s.PairedAt:O}:{s.FirstStartingHitPoints}:{s.SecondStartingHitPoints}")))) +
        string.Join("|", world.Map.Locations.OrderBy(l => l.Id).Select(l => $"{l.Id}:{l.OwnerId}")) + $"{world.CheckpointTime:O}";

    [Fact]
    public void BeforeDeath_BothUnitsTakeSimultaneousDamage()
    {
        var a = Unit(101, dps: 20); var b = Unit(102, dps: 10);
        var world = World([Army(11, 21, 1, a), Army(12, 22, 1, b)]);
        Advance(world, 2);
        Assert.Equal(80, a.CurrentHitPoints); Assert.Equal(60, b.CurrentHitPoints);
        Assert.True(a.IsWounded); Assert.True(b.IsAlive); Assert.Single(world.Battles);
    }

    [Fact]
    public void PastDeath_SurvivorKeepsHpAndTakesOwnership()
    {
        var a = Unit(101, dps: 20); var b = Unit(102, dps: 10);
        var winner = Army(11, 21, 1, a); var loser = Army(12, 22, 1, b);
        var world = World([winner, loser]);
        Advance(world, 100000);
        Assert.Equal(50, a.CurrentHitPoints); Assert.Equal(0, b.CurrentHitPoints);
        Assert.True(loser.IsDestroyed); Assert.Null(loser.LocationId); Assert.False(loser.IsInBattle);
        Assert.Empty(world.Battles); Assert.Equal(winner.OwnerId, world.Map.GetLocation(Id(1)).OwnerId);
        Assert.Equal(Start.AddSeconds(100000), world.CheckpointTime);
        Assert.Throws<InvalidOperationException>(() => world.StartMovement(loser.Id, Id(1), world.CheckpointTime));
        Assert.Throws<InvalidOperationException>(() => world.Retreat(loser.Id, world.CheckpointTime));
    }

    [Fact]
    public void SimultaneousMutualDeath_DestroysBothArmies()
    {
        var armies = new[] { Army(11, 21, 1, Unit(101)), Army(12, 22, 1, Unit(102)) };
        var world = World(armies);
        Advance(world, 10);
        Assert.All(armies, a => { Assert.True(a.IsDestroyed); Assert.Null(a.LocationId); });
        Assert.Empty(world.Battles);
    }

    [Fact]
    public void SurvivorReturnsToPool_AndCanFightAgainWithoutHealing()
    {
        var a = Unit(101, 100, 20);
        var enemy = Army(12, 22, 1, Unit(102, 20, 10), Unit(103, 20, 10));
        var world = World([Army(11, 21, 1, a), enemy]);
        Advance(world, 1);
        Assert.Equal(90, a.CurrentHitPoints);
        Assert.Contains(world.Battles.Single().Slots.Single().FirstUnitId == a.Id ?
            world.Battles.Single().Slots.Single().SecondUnitId : world.Battles.Single().Slots.Single().FirstUnitId,
            enemy.Units.Where(u => u.IsAlive).Select(u => u.Id));
        Advance(world, 2);
        Assert.Equal(80, a.CurrentHitPoints); Assert.True(enemy.IsDestroyed);
    }

    [Fact]
    public void AlliedUnitsNeverPair_AndSurvivePeacefullyAfterEnemyDeath()
    {
        var a = Army(11, 21, 1, Unit(101, dps: 100));
        var b = Army(12, 22, 1, Unit(102, 10, 0));
        var c = Army(13, 23, 1, Unit(103, dps: 100));
        var world = World([a, b, c], 3, alliances: [new(a.OwnerId, c.OwnerId)]);
        var slot = Assert.Single(world.Battles.Single().Slots);
        Assert.True(slot.FirstArmyId == b.Id || slot.SecondArmyId == b.Id);
        Advance(world, 1);
        Assert.True(b.IsDestroyed); Assert.False(a.IsDestroyed); Assert.False(c.IsDestroyed);
        Assert.False(a.IsInBattle); Assert.False(c.IsInBattle); Assert.Empty(world.Battles);
        Assert.Null(world.Map.GetLocation(Id(1)).OwnerId); // Equal earliest entries across allied players.
    }

    [Fact]
    public void AlliesAloneHaveNoSlotsOrBattle()
    {
        var a = Army(11, 21, 1, Unit(101)); var b = Army(12, 22, 1, Unit(102));
        Assert.Empty(World([a, b], alliances: [new(a.OwnerId, b.OwnerId)]).Battles);
        Assert.Empty(World([Army(14, 21, 1, Unit(104)), Army(15, 21, 1, Unit(105))]).Battles);
    }

    [Fact]
    public void FreeForAll_ContinuesAfterFirstDeath_ThenEnds()
    {
        var world = World([Army(11, 21, 1, Unit(101, 10, 0)), Army(12, 22, 1, Unit(102, 100, 1)), Army(13, 23, 1, Unit(103, 100, 10))]);
        var first = world.Battles.Single().Slots.Single().NextDeathAt!.Value;
        WorldSimulator.AdvanceTo(world, first);
        Assert.Single(world.Battles);
        Assert.Equal(2, world.Armies.Count(a => !a.IsDestroyed));
        Assert.Single(world.Battles.Single().Slots);
        Advance(world, 30);
        Assert.Empty(world.Battles);
    }

    [Fact]
    public void MultipleSlotsNeverReuseUnits_AndDamageAllPairsTogether()
    {
        var armies = new[] { Army(11, 21, 1, Unit(101), Unit(102)), Army(12, 22, 1, Unit(103), Unit(104)) };
        var world = World(armies, 3);
        Assert.Equal(2, world.Battles.Single().Slots.Count);
        var ids = world.Battles.Single().Slots.SelectMany(s => new[] { s.FirstUnitId, s.SecondUnitId }).ToArray();
        Assert.Equal(ids.Length, ids.Distinct().Count());
        Advance(world, 3);
        Assert.All(armies.SelectMany(a => a.Units), u => Assert.Equal(70, u.CurrentHitPoints));
        Advance(world, 10);
        Assert.All(armies, a => Assert.True(a.IsDestroyed)); Assert.Empty(world.Battles);
    }

    [Fact]
    public void SimultaneousDeathsReleaseAllSurvivorsBeforeRefill()
    {
        var a = Army(11, 21, 1, Unit(101, 100, 10), Unit(102, 100, 10));
        var b = Army(12, 22, 1, Unit(103, 10, 1), Unit(104, 10, 1), Unit(105, 10, 1), Unit(106, 10, 1));
        var world = World([a, b], 2);
        Advance(world, 1);
        Assert.Equal(2, b.Units.Count(u => !u.IsAlive));
        Assert.All(a.Units, u => Assert.Equal(99, u.CurrentHitPoints));
        Assert.Equal(2, world.Battles.Single().Slots.Count);
        Assert.All(world.Battles.Single().Slots, s => Assert.Equal(Start.AddSeconds(1), s.PairedAt));
        Advance(world, 2);
        Assert.True(b.IsDestroyed); Assert.All(a.Units, u => Assert.Equal(98, u.CurrentHitPoints));
    }

    private static WorldState ArrivalWorld(bool reverse = false, bool atDeath = false)
    {
        var locations = new[] { new Location(Id(1), "Battlefield", combatSlotCount: 2), new Location(Id(2), "Origin") };
        var connection = new MapConnection(Id(1), Id(2), TimeSpan.FromSeconds(atDeath ? 10 : 3));
        var armies = new[] { Army(11, 21, 1, Unit(101)), Army(12, 22, 1, Unit(102)), Army(13, 21, 2, Unit(103)), Army(14, 22, 2, Unit(104)) };
        armies[2].StartMovement(connection, Start); armies[3].StartMovement(connection, Start);
        return new(new GameMap(locations, [connection]), reverse ? armies.Reverse() : armies, Start, randomSeed: 123);
    }

    [Fact]
    public void ArrivalInterruptsCombat_AndSimultaneousArrivalsPairOnlyAfterBatch()
    {
        var world = ArrivalWorld();
        Advance(world, 3);
        Assert.Equal(70, world.GetArmy(Id(11)).Units.Single().CurrentHitPoints);
        Assert.Equal(70, world.GetArmy(Id(12)).Units.Single().CurrentHitPoints);
        Assert.Equal(100, world.GetArmy(Id(13)).Units.Single().CurrentHitPoints);
        Assert.Equal(100, world.GetArmy(Id(14)).Units.Single().CurrentHitPoints);
        Assert.Equal(2, world.Battles.Single().Slots.Count);
        Assert.Contains(world.Battles.Single().Slots, s => s.PairedAt == Start.AddSeconds(3));
        Advance(world, 4);
        Assert.Equal(90, world.GetArmy(Id(13)).Units.Single().CurrentHitPoints);
        var reversed = ArrivalWorld(true); Advance(reversed, 4);
        Assert.Equal(Snapshot(world), Snapshot(reversed));
    }

    [Fact]
    public void DeathAndArrivalAtSameTimestamp_DeathsResolveBeforeArrivalAndBattleReconciliation()
    {
        var world = ArrivalWorld(atDeath: true);
        var original = world.Battles.Single();
        Advance(world, 10);
        Assert.True(world.GetArmy(Id(11)).IsDestroyed); Assert.True(world.GetArmy(Id(12)).IsDestroyed);
        Assert.All(new[] { 13, 14 }, id => Assert.Equal(100, world.GetArmy(Id(id)).Units.Single().CurrentHitPoints));
        Assert.Same(original, world.Battles.Single()); // Hostile arrivals keep the location's battle alive through E.
        Assert.Equal(Start.AddSeconds(10), world.Battles.Single().Slots.Single().PairedAt);
    }

    [Fact]
    public void SeparateBattlesShareTimeline_AndFinishAtDifferentBoundaries()
    {
        var armies = new[] { Army(11, 21, 1, Unit(101, 10, 1)), Army(12, 22, 1, Unit(102, 100, 1)),
            Army(13, 23, 2, Unit(103, 20, 1)), Army(14, 24, 2, Unit(104, 100, 1)) };
        var world = new WorldState(new GameMap([new(Id(1), "One"), new(Id(2), "Two")], []), armies, Start);
        Advance(world, 15);
        Assert.True(armies[0].IsDestroyed); Assert.False(armies[2].IsDestroyed);
        Assert.Equal(85, armies[3].Units.Single().CurrentHitPoints); Assert.Single(world.Battles);
        Advance(world, 30);
        Assert.Equal(80, armies[3].Units.Single().CurrentHitPoints); Assert.True(armies[2].IsDestroyed); Assert.Empty(world.Battles);
    }

    [Fact]
    public void DeathsAcrossLocationsAtSameTimestamp_AreAllResolved()
    {
        var armies = new[] { Army(11, 21, 1, Unit(101)), Army(12, 22, 1, Unit(102)), Army(13, 23, 2, Unit(103)), Army(14, 24, 2, Unit(104)) };
        var world = new WorldState(new GameMap([new(Id(1), "One"), new(Id(2), "Two")], []), armies, Start);
        Advance(world, 10);
        Assert.All(armies, a => Assert.True(a.IsDestroyed)); Assert.Empty(world.Battles);
    }

    private static WorldState RetreatWorld(bool healthy = true, bool enemyAtOrigin = false)
    {
        var connection = new MapConnection(Id(1), Id(2), TimeSpan.FromSeconds(1));
        var retreating = Army(11, 21, 2, healthy ? [Unit(101, 100, 0, 50), Unit(102, 100, 0, 99), Unit(103, 100, 0)] : [Unit(101, 100, 0, 50), Unit(102, 100, 0, 99)]);
        retreating.StartMovement(connection, Start);
        var armies = new List<Army> { retreating, Army(12, 22, 1, Unit(104, 100, 0)) };
        if (enemyAtOrigin) armies.Add(Army(13, 23, 2, Unit(105, 100, 0)));
        return new(new GameMap([new(Id(1), "Battlefield"), new(Id(2), "Origin"), new(Id(3), "Exit")],
            [connection, new(Id(2), Id(3), TimeSpan.FromSeconds(1))]), armies, Start);
    }

    [Fact]
    public void RetreatKillsEveryWoundedUnit_AndPreservesHealthyUnits()
    {
        var world = RetreatWorld();
        world.Retreat(Id(11), Start.AddSeconds(1));
        var army = world.GetArmy(Id(11));
        Assert.Equal(2, army.Units.Count(u => !u.IsAlive)); Assert.Equal(100, army.Units.Single(u => u.IsAlive).CurrentHitPoints);
        Assert.True(army.IsMoving); Assert.Equal(MovementKind.Retreat, army.Movement!.Kind);
        Assert.Equal(Start.AddSeconds(2), army.Movement.ArriveAt); Assert.Empty(world.Battles);
        Assert.False(army.IsDestroyed);
    }

    [Fact]
    public void AllWoundedRetreat_DestroysArmyWithoutMovement_AndEndsBattle()
    {
        var world = RetreatWorld(false);
        world.Retreat(Id(11), Start.AddSeconds(1));
        var army = world.GetArmy(Id(11));
        Assert.True(army.IsDestroyed); Assert.False(army.IsMoving); Assert.Null(army.LocationId);
        Assert.Empty(world.Battles); Assert.Equal(Id(22), world.Map.GetLocation(Id(1)).OwnerId);
    }

    [Fact]
    public void RetreatAdvancesCombatBeforeDeterminingWounds()
    {
        var connection = new MapConnection(Id(1), Id(2), TimeSpan.FromSeconds(1));
        var army = Army(11, 21, 2, Unit(101, 100, 1));
        army.StartMovement(connection, Start);
        var world = new WorldState(new GameMap([new(Id(1), "Battlefield"), new(Id(2), "Origin")], [connection]),
            [army, Army(12, 22, 1, Unit(102, 100, 1))], Start);
        world.Retreat(army.Id, Start.AddSeconds(2));
        Assert.True(army.IsDestroyed); Assert.Null(army.Movement);
        Assert.Equal(99, world.GetArmy(Id(12)).Units.Single().CurrentHitPoints);
        Assert.Equal(Start.AddSeconds(2), world.CheckpointTime);
        Assert.Empty(world.Battles);
    }
    [Fact]
    public void RetreatIntoEnemyBattle_LocksSecondRetreat()
    {
        var world = RetreatWorld(enemyAtOrigin: true);
        world.Retreat(Id(11), Start.AddSeconds(1));
        Advance(world, 2);
        var army = world.GetArmy(Id(11));
        Assert.True(army.IsInBattle); Assert.False(army.CanRetreat);
        Assert.Throws<InvalidOperationException>(() => world.Retreat(army.Id, Start.AddSeconds(2)));
    }

    [Fact]
    public void SameSeedAndCommands_AreDeterministicDespiteCollectionOrderAndCheckpoints()
    {
        var direct = ArrivalWorld(); var stepped = ArrivalWorld(true);
        Advance(direct, 30);
        foreach (var time in new decimal[] { 0.0000001m, 1.2345678m, 3, 5.9876543m, 10, 13, 30 }) Advance(stepped, time);
        Assert.Equal(Snapshot(direct), Snapshot(stepped));
    }

    [Fact]
    public void DifferentSeedsCanSelectDifferentPairs_AndEachIsReproducible()
    {
        WorldState Create(ulong seed) => World([Army(11, 21, 1, Unit(101), Unit(102)), Army(12, 22, 1, Unit(103), Unit(104))], seed: seed);
        var pairings = new HashSet<string>();
        for (ulong seed = 0; seed < 20; seed++)
        {
            var first = Create(seed); var second = Create(seed);
            Assert.Equal(Snapshot(first), Snapshot(second));
            var slot = first.Battles.Single().Slots.Single();
            pairings.Add($"{slot.FirstUnitId}:{slot.SecondUnitId}");
            Advance(first, 15); Advance(second, 15); Assert.Equal(Snapshot(first), Snapshot(second));
        }
        Assert.True(pairings.Count > 1);
    }

    [Fact]
    public void FractionalDeathTimeRoundsUp_NeverKillsEarly()
    {
        var a = Unit(101, 1, 0); var b = Unit(102, 10, 3);
        var world = World([Army(11, 21, 1, a), Army(12, 22, 1, b)]);
        Assert.Equal(Start.AddTicks(3333334), world.Battles.Single().Slots.Single().NextDeathAt);
        WorldSimulator.AdvanceTo(world, Start.AddTicks(3333333)); Assert.True(a.IsAlive);
        WorldSimulator.AdvanceTo(world, Start.AddTicks(3333334)); Assert.False(a.IsAlive);
    }

    [Fact]
    public void TinyHpDoesNotRoundToPrematureDeath()
    {
        var a = Unit(101, 0.0000000000000000000000000001m, 0);
        var b = Unit(102, 1, 0.0000000000000000000000000003m);
        var world = World([Army(11, 21, 1, a), Army(12, 22, 1, b)]);
        WorldSimulator.AdvanceTo(world, Start.AddTicks(3333333)); Assert.True(a.IsAlive);
        WorldSimulator.AdvanceTo(world, Start.AddTicks(3333334)); Assert.False(a.IsAlive);
    }

    [Fact]
    public void ZeroDpsCombatCanAdvanceToDistantTargetWithoutDeathEvents()
    {
        var world = World([Army(11, 21, 1, Unit(101, dps: 0)), Army(12, 22, 1, Unit(102, dps: 0))]);
        Assert.Null(world.Battles.Single().Slots.Single().NextDeathAt);
        Advance(world, 100000000); Assert.All(world.Armies.SelectMany(a => a.Units), u => Assert.Equal(100, u.CurrentHitPoints));
    }

    [Fact]
    public void DeadAndEmptyArmiesNeverOccupyLocationsOrStartBattles()
    {
        var empty = Army(11, 21, 1); var dead = Army(12, 22, 1, Unit(102, current: 0));
        var world = World([empty, dead, Army(13, 23, 1, Unit(103))]);
        Assert.Empty(world.Battles); Assert.Null(empty.LocationId); Assert.Null(dead.LocationId);
        Assert.False(dead.Units.Single().IsWounded);
    }

    [Fact]
    public void InvalidStatsSlotsAndDuplicateUnitsAreRejected()
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => Unit(101, 0));
        Assert.Throws<ArgumentOutOfRangeException>(() => Unit(101, dps: -1));
        Assert.Throws<ArgumentOutOfRangeException>(() => Unit(101, current: 101));
        Assert.Throws<ArgumentOutOfRangeException>(() => Unit(101, current: -1));
        Assert.Throws<ArgumentException>(() => new Unit(Guid.Empty, 1, 1));
        Assert.Throws<ArgumentOutOfRangeException>(() => new Location(Id(1), "Invalid", combatSlotCount: 0));
        Assert.Throws<ArgumentException>(() => Army(11, 21, 1, Unit(101), Unit(101)));
        Assert.Throws<ArgumentException>(() => World([Army(11, 21, 1, Unit(101)), Army(12, 22, 1, Unit(101))]));
    }

    [Fact]
    public void CombatOwnershipUsesEarliestAlliedSurvivorAfterEnemyDies()
    {
        var connection = new MapConnection(Id(1), Id(2), TimeSpan.FromSeconds(2));
        var earliest = Army(11, 21, 1, Unit(101, 100, 10));
        var ally = Army(12, 22, 2, Unit(102, 100, 10));
        var enemy = Army(13, 23, 2, Unit(103, 1, 0));
        ally.StartMovement(connection, Start); enemy.StartMovement(connection, Start);
        var world = new WorldState(new GameMap([new(Id(1), "Battlefield"), new(Id(2), "Origin")], [connection]),
            [enemy, ally, earliest], Start, [new(earliest.OwnerId, ally.OwnerId)]);
        Advance(world, 3);
        Assert.True(enemy.IsDestroyed); Assert.Empty(world.Battles);
        Assert.Equal(earliest.OwnerId, world.Map.GetLocation(Id(1)).OwnerId);
        Assert.Equal(Id(1), ally.LocationId); Assert.Equal(Id(1), earliest.LocationId);
    }

    [Fact]
    public void SurvivingArrivalAtDeathBoundaryIsIncludedInOwnershipResolution()
    {
        var connection = new MapConnection(Id(1), Id(2), TimeSpan.FromSeconds(10));
        var arriving = Army(13, 23, 2, Unit(103)); arriving.StartMovement(connection, Start);
        var world = new WorldState(new GameMap([new(Id(1), "Battlefield", Id(99)), new(Id(2), "Origin")], [connection]),
            [Army(11, 21, 1, Unit(101)), Army(12, 22, 1, Unit(102)), arriving], Start);
        Advance(world, 10);
        Assert.Empty(world.Battles); Assert.Equal(arriving.OwnerId, world.Map.GetLocation(Id(1)).OwnerId);
        Assert.Equal(100, arriving.Units.Single().CurrentHitPoints);
    }

    [Fact]
    public void NormalMovementAfterRetreatRestoresEligibilityInLaterBattle()
    {
        var world = RetreatWorld();
        world.Retreat(Id(11), Start.AddSeconds(1));
        world.StartMovement(Id(11), Id(3), Start.AddSeconds(2));
        world.StartMovement(Id(11), Id(2), Start.AddSeconds(3));
        world.StartMovement(Id(11), Id(1), Start.AddSeconds(4));
        Advance(world, 5);
        var army = world.GetArmy(Id(11));
        Assert.True(army.IsInBattle); Assert.True(army.CanRetreat); Assert.Equal(Id(2), army.RetreatLocationId);
    }

    [Fact]
    public void RetreatDuringContinuingBattleReleasesHealthySlotOpponentForRefill()
    {
        var enemy = Army(12, 22, 1, Unit(104, 100, 0));
        var connection = new MapConnection(Id(1), Id(2), TimeSpan.FromSeconds(1));
        var leaving = Army(11, 21, 2, Unit(101, 100, 0)); leaving.StartMovement(connection, Start);
        var remaining = Army(13, 23, 1, Unit(103, 100, 0));
        var active = new WorldState(new GameMap([new(Id(1), "Battlefield", combatSlotCount: 2), new(Id(2), "Origin")], [connection]),
            [leaving, enemy, remaining], Start);
        active.Retreat(leaving.Id, Start.AddSeconds(1));
        var slot = Assert.Single(active.Battles.Single().Slots);
        Assert.DoesNotContain(leaving.Id, new[] { slot.FirstArmyId, slot.SecondArmyId });
        Assert.True(remaining.IsInBattle); Assert.True(enemy.IsInBattle);
    }

    [Fact]
    public void AllianceConstraintsHoldForManySeedsAndRefills()
    {
        for (ulong seed = 0; seed < 20; seed++)
        {
            var world = World([Army(11, 21, 1, Unit(101), Unit(102)), Army(12, 22, 1, Unit(103), Unit(104)),
                Army(13, 23, 1, Unit(105), Unit(106)), Army(14, 24, 1, Unit(107), Unit(108))], 3, seed,
                [new(Id(21), Id(23))]);
            for (var time = 0; time <= 30; time += 10)
            {
                Advance(world, time);
                var slots = world.Battles.SelectMany(b => b.Slots).ToArray();
                Assert.All(slots, slot => Assert.True(world.AreHostile(world.GetArmy(slot.FirstArmyId).OwnerId, world.GetArmy(slot.SecondArmyId).OwnerId)));
                var units = slots.SelectMany(slot => new[] { slot.FirstUnitId, slot.SecondUnitId }).ToArray();
                Assert.Equal(units.Length, units.Distinct().Count());
            }
        }
    }

    [Fact]
    public void EquivalentUtcInstantsHaveIdenticalRandomPairing()
    {
        var utc = World([Army(11, 21, 1, Unit(101), Unit(102)), Army(12, 22, 1, Unit(103), Unit(104))], seed: 123);
        var shifted = new WorldState(new GameMap([new(Id(1), "Battlefield")], []),
            [Army(11, 21, 1, Unit(101), Unit(102)), Army(12, 22, 1, Unit(103), Unit(104))], Start.ToOffset(TimeSpan.FromHours(2)), randomSeed: 123);
        Assert.Equal(utc.Battles.Single().RandomState, shifted.Battles.Single().RandomState);
        Assert.Equal(utc.Battles.Single().Slots.Single().FirstUnitId, shifted.Battles.Single().Slots.Single().FirstUnitId);
        Assert.Equal(utc.Battles.Single().Slots.Single().NextDeathAt, shifted.Battles.Single().Slots.Single().NextDeathAt);
    }

    [Fact]
    public void ExtremeDecimalStatsDoNotOverflowCombatDamage()
    {
        var world = World([Army(11, 21, 1, Unit(101, decimal.MaxValue, decimal.MaxValue)),
            Army(12, 22, 1, Unit(102, decimal.MaxValue, decimal.MaxValue))]);
        Advance(world, 0.5m);
        Assert.All(world.Armies.SelectMany(a => a.Units), unit => Assert.True(unit.IsAlive));
        Advance(world, 2);
        Assert.All(world.Armies, army => Assert.True(army.IsDestroyed));
    }

    [Fact]
    public void FractionalDamageIsIndependentOfCheckpointFrequency()
    {
        WorldState Create() => World([Army(11, 21, 1, Unit(101, 1, 0.1234567890123456789012345678m)),
            Army(12, 22, 1, Unit(102, 1, 0.9876543210987654321098765432m))]);
        var direct = Create(); var stepped = Create();
        Advance(direct, 2);
        foreach (var time in new decimal[] { 0.0000001m, 0.1234567m, 0.5432109m, 1, 1.2345678m, 2 }) Advance(stepped, time);
        Assert.Equal(Snapshot(direct), Snapshot(stepped));
    }

    [Fact]
    public void IdenticalRetreatCommandsProduceIdenticalWorlds()
    {
        var first = RetreatWorld(enemyAtOrigin: true); var second = RetreatWorld(enemyAtOrigin: true);
        first.Retreat(Id(11), Start.AddSeconds(1)); second.Retreat(Id(11), Start.AddSeconds(1));
        Advance(first, 3); Advance(second, 3);
        Assert.Equal(Snapshot(first), Snapshot(second));
    }

    [Fact]
    public void DeathBeyondTimestampRangeDoesNotOverflow()
    {
        var a = Unit(101, decimal.MaxValue, 0); var b = Unit(102, decimal.MaxValue, 0.0000000000000000000000000001m);
        var world = World([Army(11, 21, 1, a), Army(12, 22, 1, b)]);
        Assert.Null(world.Battles.Single().Slots.Single().NextDeathAt);
        WorldSimulator.AdvanceTo(world, DateTimeOffset.MaxValue);
        Assert.True(a.IsAlive); Assert.True(b.IsAlive);
    }
}
