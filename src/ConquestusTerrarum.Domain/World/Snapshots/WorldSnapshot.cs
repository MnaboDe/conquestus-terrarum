using System.Collections.Immutable;

namespace ConquestusTerrarum.Domain.World;

// Detached immutable DTOs. FormatVersion versions the snapshot contract, independently of row versions.
public sealed record WorldSnapshot(
    int FormatVersion,
    DateTimeOffset CheckpointTime,
    ulong RandomSeed,
    ImmutableArray<LocationSnapshot> Locations,
    ImmutableArray<ConnectionSnapshot> Connections,
    ImmutableArray<ArmySnapshot> Armies,
    ImmutableArray<AllianceSnapshot> Alliances,
    ImmutableArray<BattleSnapshot> Battles);

public sealed record LocationSnapshot(Guid Id, string Name, Guid? OwnerId, int CombatSlotCount);

public sealed record ConnectionSnapshot(Guid FromLocationId, Guid ToLocationId, long TravelTimeTicks);

public sealed record AllianceSnapshot(Guid FirstPlayerId, Guid SecondPlayerId);

public sealed record ArmySnapshot(
    Guid Id,
    Guid OwnerId,
    Guid? LocationId,
    DateTimeOffset? EnteredLocationAt,
    Guid? PreviousLocationId,
    Guid? RetreatLocationId,
    bool RetreatLocked,
    bool IsInBattle,
    MovementSnapshot? Movement,
    ImmutableArray<UnitSnapshot> Units);

public sealed record MovementSnapshot(
    Guid FromLocationId,
    Guid ToLocationId,
    DateTimeOffset StartedAt,
    DateTimeOffset ArriveAt,
    MovementKind Kind);

public sealed record UnitSnapshot(Guid Id, decimal MaxHitPoints, decimal CurrentHitPoints, decimal DamagePerSecond);

public sealed record BattleSnapshot(
    Guid LocationId,
    DateTimeOffset StartedAt,
    ulong RandomState,
    ImmutableArray<CombatSlotSnapshot> Slots);

public sealed record CombatSlotSnapshot(
    int Index,
    Guid FirstArmyId,
    Guid FirstUnitId,
    Guid SecondArmyId,
    Guid SecondUnitId,
    DateTimeOffset PairedAt,
    decimal FirstStartingHitPoints,
    decimal SecondStartingHitPoints,
    DateTimeOffset? FirstDeathAt,
    DateTimeOffset? SecondDeathAt);