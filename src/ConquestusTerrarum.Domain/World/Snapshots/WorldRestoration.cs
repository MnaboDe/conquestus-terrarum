using System.Collections.Immutable;

namespace ConquestusTerrarum.Domain.World;

public sealed partial class WorldState
{
    // This path deliberately avoids normal construction's reconciliation, entry-time initialization and RNG use.
    private WorldState(WorldSnapshot snapshot)
    {
        Map = new GameMap(snapshot.Locations.Select(l => new Location(l.Id, l.Name, l.OwnerId, l.CombatSlotCount)),
            snapshot.Connections.Select(c =>
                new MapConnection(c.FromLocationId, c.ToLocationId, TimeSpan.FromTicks(c.TravelTimeTicks))));
        CheckpointTime = snapshot.CheckpointTime;
        RandomSeed = snapshot.RandomSeed;
        _armies = snapshot.Armies.Select(a => new Army(a)).ToDictionary(a => a.Id);
        _alliances = snapshot.Alliances.Select(a => new PlayerAlliance(a.FirstPlayerId, a.SecondPlayerId)).ToList();
        if (Armies.SelectMany(a => a.Units).Select(u => u.Id).Distinct().Count() != Armies.Sum(a => a.Units.Count))
            throw new ArgumentException("Duplicate unit identities in snapshot.");
        ValidateArmyLocations();
        foreach (var army in Armies) ValidateRestoredArmy(army);
        foreach (var battle in snapshot.Battles)
        {
            if (battle.Slots.IsDefault || battle.Slots.Any(s => s is null) || battle.StartedAt > CheckpointTime)
                throw new ArgumentException("Invalid battle snapshot.");
            Map.GetLocation(battle.LocationId);
            _battles.Add(battle.LocationId, new Battle(battle, this));
        }

        ValidateRestoredBattles();
    }

    public WorldSnapshot CreateSnapshot()
    {
        return new WorldSnapshot(1, CheckpointTime, RandomSeed,
            Map.Locations.OrderBy(l => l.Id)
                .Select(l => new LocationSnapshot(l.Id, l.Name, l.OwnerId, l.CombatSlotCount)).ToImmutableArray(),
            // Connections are a list; retain its order, including any pre-existing parallel edges.
            Map.Connections.Select(c => new ConnectionSnapshot(c.FromLocationId, c.ToLocationId, c.TravelTime.Ticks))
                .ToImmutableArray(),
            Armies.OrderBy(a => a.Id).Select(a => a.CreateSnapshot()).ToImmutableArray(),
            Alliances.OrderBy(a => a.FirstPlayerId).ThenBy(a => a.SecondPlayerId)
                .Select(a => new AllianceSnapshot(a.FirstPlayerId, a.SecondPlayerId)).ToImmutableArray(),
            Battles.OrderBy(b => b.LocationId).Select(b => b.CreateSnapshot()).ToImmutableArray());
    }

    public static WorldState Restore(WorldSnapshot snapshot)
    {
        ArgumentNullException.ThrowIfNull(snapshot);
        if (snapshot.FormatVersion != 1)
            throw new ArgumentException("Unsupported world snapshot format version.", nameof(snapshot));
        if (snapshot.Locations.IsDefault || snapshot.Connections.IsDefault || snapshot.Armies.IsDefault ||
            snapshot.Alliances.IsDefault || snapshot.Battles.IsDefault)
            throw new ArgumentException("Snapshot collections must be present.", nameof(snapshot));
        if (snapshot.Locations.Any(x => x is null) || snapshot.Connections.Any(x => x is null) ||
            snapshot.Armies.Any(x => x is null) || snapshot.Alliances.Any(x => x is null) ||
            snapshot.Battles.Any(x => x is null))
            throw new ArgumentException("Snapshot collections cannot contain null elements.", nameof(snapshot));
        return new WorldState(snapshot);
    }

    private void ValidateRestoredArmy(Army army)
    {
        if (army.PreviousLocationId is { } previous) Map.GetLocation(previous);
        if (army.RetreatLocationId is { } retreat) Map.GetLocation(retreat);
        if (army.IsDestroyed)
        {
            if (army.LocationId is not null || army.Movement is not null || army.IsInBattle ||
                army.EnteredLocationAt is not null || army.RetreatLocationId is not null)
                throw new ArgumentException("Destroyed army has active state.");
        }
        else if (army.Movement is { } movement)
        {
            if (army.LocationId is not null || army.EnteredLocationAt is not null || army.IsInBattle ||
                army.RetreatLocationId is not null || movement.StartedAt > CheckpointTime ||
                movement.ArriveAt < CheckpointTime ||
                (movement.Kind == MovementKind.Retreat && !army.RetreatLocked))
                throw new ArgumentException("Invalid moving army state.");
            if (!Map.Connections.Any(connection =>
                    connection.Connects(movement.FromLocationId, movement.ToLocationId) &&
                    movement.ArriveAt - movement.StartedAt == connection.TravelTime))
                throw new ArgumentException("Movement duration differs from map connection.");
        }
        else if (army.LocationId is null || army.EnteredLocationAt is null || army.EnteredLocationAt > CheckpointTime)
        {
            throw new ArgumentException("Stationary army has invalid location entry state.");
        }

        var peers = Armies.Where(a => a.LocationId is not null && a.LocationId == army.LocationId).ToArray();
        if (army.IsInBattle != HasHostileArmy(army, peers))
            throw new ArgumentException("Army battle participation differs from hostile presence.");
        var expectedRetreat = army.IsInBattle && !army.RetreatLocked ? army.PreviousLocationId : null;
        if (army.RetreatLocationId != expectedRetreat)
            throw new ArgumentException("Invalid retreat destination or lock state.");
    }

    private void ValidateRestoredBattles()
    {
        var usedUnits = new HashSet<Guid>();
        foreach (var location in Map.Locations)
        {
            var armies = Armies.Where(a => a.LocationId == location.Id).ToArray();
            var battle = GetBattleAt(location.Id);
            if (HasHostilePair(armies) != battle is not null)
                throw new ArgumentException("Battle presence differs from hostile presence.");
            if (battle is null) continue;
            var indices = new HashSet<int>();
            foreach (var slot in battle.Slots)
                if (slot.Index < 0 || slot.Index >= location.CombatSlotCount || !indices.Add(slot.Index) ||
                    !usedUnits.Add(slot.FirstUnitId) || !usedUnits.Add(slot.SecondUnitId) ||
                    slot.PairedAt < battle.StartedAt || !slot.FirstUnit.IsAlive || !slot.SecondUnit.IsAlive ||
                    GetArmy(slot.FirstArmyId).LocationId != location.Id ||
                    GetArmy(slot.SecondArmyId).LocationId != location.Id ||
                    !AreHostile(GetArmy(slot.FirstArmyId).OwnerId, GetArmy(slot.SecondArmyId).OwnerId))
                    throw new ArgumentException("Invalid or duplicate combat pairing.");
            if (battle.Slots.Count < location.CombatSlotCount)
            {
                var idleArmies = armies.Where(a => a.Units.Any(u => u.IsAlive && !usedUnits.Contains(u.Id))).ToArray();
                if (HasHostilePair(idleArmies))
                    throw new ArgumentException("Snapshot has unfilled eligible combat slots.");
            }
        }
    }
}