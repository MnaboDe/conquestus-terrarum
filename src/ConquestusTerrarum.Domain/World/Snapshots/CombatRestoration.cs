using System.Collections.Immutable;

namespace ConquestusTerrarum.Domain.World;

public sealed partial class Army
{
    internal bool RetreatLocked => _retreatLocked;

    internal ArmySnapshot CreateSnapshot() => new(Id, OwnerId, LocationId, EnteredLocationAt,
        PreviousLocationId, RetreatLocationId, _retreatLocked, IsInBattle,
        Movement is { } m ? new(m.FromLocationId, m.ToLocationId, m.StartedAt, m.ArriveAt, m.Kind) : null,
        Units.OrderBy(u => u.Id).Select(u => new UnitSnapshot(u.Id, u.MaxHitPoints, u.CurrentHitPoints, u.DamagePerSecond)).ToImmutableArray());

    internal Army(ArmySnapshot snapshot)
    {
        if (snapshot.Id == Guid.Empty || snapshot.OwnerId == Guid.Empty || snapshot.Units.IsDefault || snapshot.Units.Any(u => u is null))
            throw new ArgumentException("Invalid army identity or units.");
        Id = snapshot.Id;
        OwnerId = snapshot.OwnerId;
        Units = snapshot.Units.Select(u => new Unit(u.Id, u.MaxHitPoints, u.DamagePerSecond, u.CurrentHitPoints)).ToList().AsReadOnly();
        LocationId = snapshot.LocationId;
        EnteredLocationAt = snapshot.EnteredLocationAt;
        PreviousLocationId = snapshot.PreviousLocationId;
        RetreatLocationId = snapshot.RetreatLocationId;
        _retreatLocked = snapshot.RetreatLocked;
        IsInBattle = snapshot.IsInBattle;
        if (snapshot.Movement is { } m)
        {
            if (!Enum.IsDefined(m.Kind)) throw new ArgumentException("Unknown movement kind.");
            // Preserve the original DateTimeOffset representation as well as the arrival instant.
            Movement = MovementOrder.Restore(m);
        }
    }
}

public sealed partial class Battle
{
    internal BattleSnapshot CreateSnapshot() => new(LocationId, StartedAt, RandomState,
        Slots.OrderBy(s => s.Index).Select(s => s.CreateSnapshot()).ToImmutableArray());

    internal Battle(BattleSnapshot snapshot, WorldState world)
    {
        LocationId = snapshot.LocationId;
        StartedAt = snapshot.StartedAt;
        RandomState = snapshot.RandomState;
        // Bypass seeding and pairing: restoring must never consume the random stream.
        foreach (var slot in snapshot.Slots.OrderBy(s => s.Index)) _slots.Add(new CombatSlot(slot, world));
        Slots = _slots.AsReadOnly();
    }
}

public sealed partial class CombatSlot
{
    internal CombatSlotSnapshot CreateSnapshot() => new(Index, FirstArmyId, FirstUnitId, SecondArmyId,
        SecondUnitId, PairedAt, FirstStartingHitPoints, SecondStartingHitPoints, FirstDeathAt, SecondDeathAt);

    internal CombatSlot(CombatSlotSnapshot s, WorldState world)
    {
        Index = s.Index;
        FirstArmyId = s.FirstArmyId;
        SecondArmyId = s.SecondArmyId;
        FirstUnit = world.GetArmy(FirstArmyId).Units.Single(u => u.Id == s.FirstUnitId);
        SecondUnit = world.GetArmy(SecondArmyId).Units.Single(u => u.Id == s.SecondUnitId);
        PairedAt = s.PairedAt;
        FirstStartingHitPoints = s.FirstStartingHitPoints;
        SecondStartingHitPoints = s.SecondStartingHitPoints;
        FirstDeathAt = s.FirstDeathAt;
        SecondDeathAt = s.SecondDeathAt;
        if (FirstStartingHitPoints <= 0 || FirstStartingHitPoints > FirstUnit.MaxHitPoints ||
            SecondStartingHitPoints <= 0 || SecondStartingHitPoints > SecondUnit.MaxHitPoints ||
            FirstDeathAt != DeathAt(PairedAt, FirstStartingHitPoints, SecondUnit.DamagePerSecond) ||
            SecondDeathAt != DeathAt(PairedAt, SecondStartingHitPoints, FirstUnit.DamagePerSecond))
            throw new ArgumentException("Invalid combat anchors or death timestamps.");
        if (PairedAt > world.CheckpointTime || NextDeathAt <= world.CheckpointTime)
            throw new ArgumentException("Combat slot has an invalid time boundary.");
        var ticks = (world.CheckpointTime - PairedAt).Ticks;
        if (FirstUnit.CurrentHitPoints != RemainingHitPoints(FirstStartingHitPoints, SecondUnit.DamagePerSecond, ticks) ||
            SecondUnit.CurrentHitPoints != RemainingHitPoints(SecondStartingHitPoints, FirstUnit.DamagePerSecond, ticks))
            throw new ArgumentException("Unit HP does not match combat anchors at the checkpoint.");
    }
}
