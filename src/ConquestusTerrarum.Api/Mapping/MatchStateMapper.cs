using ConquestusTerrarum.Api.Contracts;
using ConquestusTerrarum.Domain.World;

namespace ConquestusTerrarum.Api.Mapping;

public static class MatchStateMapper
{
    public static MatchStateResponse Map(
        Guid matchId,
        WorldState world,
        DateTimeOffset serverTime,
        long version,
        long updateCursor = 0)
    {
        return new MatchStateResponse(
            matchId,
            version,
            updateCursor,
            serverTime,
            world.CheckpointTime,
            world.Map.Locations.OrderBy(l => l.Id)
                .Select(l => new LocationResponse(l.Id, l.Name, l.OwnerId, l.CombatSlotCount)).ToArray(),
            world.Map.Connections.OrderBy(c => c.FromLocationId).ThenBy(c => c.ToLocationId).ThenBy(c => c.TravelTime)
                .Select(c => new ConnectionResponse(c.FromLocationId, c.ToLocationId, c.TravelTime.Ticks)).ToArray(),
            world.Armies.OrderBy(a => a.Id).Select(a => new ArmyResponse(a.Id, a.OwnerId, a.LocationId,
                a.EnteredLocationAt,
                a.PreviousLocationId, a.RetreatLocationId, a.IsMoving, a.IsDestroyed, a.IsInBattle, a.CanRetreat,
                a.Movement is { } m
                    ? new MovementResponse(m.FromLocationId, m.ToLocationId, m.StartedAt, m.ArriveAt,
                        m.Kind switch
                        {
                            MovementKind.Normal => MovementKindResponse.Normal,
                            MovementKind.Retreat => MovementKindResponse.Retreat,
                            _ => throw new InvalidOperationException("Unknown movement kind.")
                        })
                    : null,
                a.Units.OrderBy(u => u.Id).Select(u => new UnitResponse(u.Id, u.MaxHitPoints, u.CurrentHitPoints,
                    u.DamagePerSecond, u.IsAlive, u.IsWounded)).ToArray())).ToArray(),
            world.Alliances.OrderBy(a => a.FirstPlayerId).ThenBy(a => a.SecondPlayerId)
                .Select(a => new AllianceResponse(a.FirstPlayerId, a.SecondPlayerId)).ToArray(),
            world.Battles.OrderBy(b => b.LocationId).Select(b => new BattleResponse(b.LocationId, b.StartedAt,
                b.Slots.OrderBy(s => s.Index).Select(s => new CombatSlotResponse(s.Index, s.FirstArmyId, s.FirstUnitId,
                    s.SecondArmyId, s.SecondUnitId, s.PairedAt, s.FirstDeathAt,
                    s.SecondDeathAt)).ToArray())).ToArray());
    }
}