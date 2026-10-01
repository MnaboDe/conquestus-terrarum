using System.Text.Json.Serialization;

namespace ConquestusTerrarum.Api.Contracts;

[JsonUnmappedMemberHandling(JsonUnmappedMemberHandling.Disallow)]
public sealed record MoveArmyRequest([property: JsonRequired] Guid DestinationLocationId);

public sealed record MatchStateResponse(Guid MatchId, long Version, DateTimeOffset ServerTime,
    DateTimeOffset CheckpointTime, IReadOnlyList<LocationResponse> Locations,
    IReadOnlyList<ConnectionResponse> Connections, IReadOnlyList<ArmyResponse> Armies,
    IReadOnlyList<AllianceResponse> Alliances, IReadOnlyList<BattleResponse> ActiveBattles);

public sealed record LocationResponse(Guid Id, string Name, Guid? OwnerId, int CombatSlotCount);
public sealed record ConnectionResponse(Guid FromLocationId, Guid ToLocationId, long TravelTimeTicks);
public sealed record ArmyResponse(Guid Id, Guid OwnerId, Guid? LocationId, DateTimeOffset? EnteredLocationAt,
    Guid? PreviousLocationId, Guid? RetreatLocationId, bool IsMoving, bool IsDestroyed, bool IsInBattle,
    bool CanRetreat, MovementResponse? Movement, IReadOnlyList<UnitResponse> Units);
public sealed record MovementResponse(Guid FromLocationId, Guid ToLocationId, DateTimeOffset StartedAt,
    DateTimeOffset ArriveAt, MovementKindResponse Kind);
public enum MovementKindResponse { Normal, Retreat }
public sealed record UnitResponse(Guid Id, decimal MaxHitPoints, decimal CurrentHitPoints,
    decimal DamagePerSecond, bool IsAlive, bool IsWounded);
public sealed record AllianceResponse(Guid FirstPlayerId, Guid SecondPlayerId);
public sealed record BattleResponse(Guid LocationId, DateTimeOffset StartedAt, IReadOnlyList<CombatSlotResponse> CombatSlots);
public sealed record CombatSlotResponse(int Index, Guid FirstArmyId, Guid FirstUnitId, Guid SecondArmyId,
    Guid SecondUnitId, DateTimeOffset PairedAt, DateTimeOffset? FirstDeathAt, DateTimeOffset? SecondDeathAt);
