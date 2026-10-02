using System.Text.Json;
using ConquestusTerrarum.Domain.World;

namespace ConquestusTerrarum.Persistence;

/// <summary>
/// A change detected between two authoritative world snapshots,
/// but not yet assigned a durable per-match sequence.
/// </summary>
public sealed record PendingMatchUpdate(
    DateTimeOffset OccurredAt,
    string Type,
    string PayloadJson);

/// <summary>
/// Produces coarse client-facing map updates from authoritative snapshots.
///
/// This is deliberately NOT an event-sourcing log.
/// It describes meaningful observable state changes for client synchronization.
/// Continuous HP changes are intentionally omitted.
/// </summary>
public static class WorldUpdateDetector
{
    public static IReadOnlyList<PendingMatchUpdate> Detect(
        WorldSnapshot before,
        WorldSnapshot after,
        DateTimeOffset observedAt)
    {
        ArgumentNullException.ThrowIfNull(before);
        ArgumentNullException.ThrowIfNull(after);

        var updates =
            new List<PendingMatchUpdate>();

        DetectArmyChanges(
            before,
            after,
            observedAt,
            updates);

        DetectBattleChanges(
            before,
            after,
            observedAt,
            updates);

        DetectLocationChanges(
            before,
            after,
            observedAt,
            updates);

        // Sequence is a delivery cursor, not gameplay ordering.
        // Still keep output deterministic for tests and diagnostics.
        return updates
            .OrderBy(update => update.OccurredAt)
            .ThenBy(
                update => update.Type,
                StringComparer.Ordinal)
            .ThenBy(
                update => update.PayloadJson,
                StringComparer.Ordinal)
            .ToArray();
    }

    private static void DetectArmyChanges(
        WorldSnapshot before,
        WorldSnapshot after,
        DateTimeOffset observedAt,
        ICollection<PendingMatchUpdate> updates)
    {
        var beforeArmies =
            before.Armies.ToDictionary(
                army => army.Id);

        foreach (var afterArmy in after.Armies)
        {
            if (!beforeArmies.TryGetValue(
                    afterArmy.Id,
                    out var beforeArmy))
            {
                continue;
            }

            DetectMovementStarted(
                beforeArmy,
                afterArmy,
                updates);

            DetectArrival(
                beforeArmy,
                afterArmy,
                updates);

            DetectDestroyedArmy(
                beforeArmy,
                afterArmy,
                observedAt,
                updates);
        }
    }

    private static void DetectMovementStarted(
        ArmySnapshot before,
        ArmySnapshot after,
        ICollection<PendingMatchUpdate> updates)
    {
        if (before.Movement is not null ||
            after.Movement is null)
        {
            return;
        }

        var movement =
            after.Movement;

        var type =
            movement.Kind == MovementKind.Retreat
                ? "ArmyRetreatStarted"
                : "ArmyMovementStarted";

        var payload =
            JsonSerializer.Serialize(
                new
                {
                    armyId = after.Id,
                    ownerId = after.OwnerId,
                    fromLocationId =
                        movement.FromLocationId,
                    toLocationId =
                        movement.ToLocationId,
                    startedAt =
                        movement.StartedAt,
                    arriveAt =
                        movement.ArriveAt
                });

        updates.Add(
            new PendingMatchUpdate(
                movement.StartedAt,
                type,
                payload));
    }

    private static void DetectArrival(
        ArmySnapshot before,
        ArmySnapshot after,
        ICollection<PendingMatchUpdate> updates)
    {
        if (before.Movement is null ||
            after.Movement is not null)
        {
            return;
        }

        var movement =
            before.Movement;

        if (after.LocationId !=
            movement.ToLocationId)
        {
            return;
        }

        var payload =
            JsonSerializer.Serialize(
                new
                {
                    armyId = after.Id,
                    ownerId = after.OwnerId,
                    locationId =
                        movement.ToLocationId,
                    movementKind =
                        movement.Kind.ToString()
                });

        updates.Add(
            new PendingMatchUpdate(
                movement.ArriveAt,
                "ArmyArrived",
                payload));
    }

    private static void DetectDestroyedArmy(
        ArmySnapshot before,
        ArmySnapshot after,
        DateTimeOffset observedAt,
        ICollection<PendingMatchUpdate> updates)
    {
        if (IsDestroyed(before) ||
            !IsDestroyed(after))
        {
            return;
        }

        // If the army was moving when a long lazy advance began,
        // its eventual battle location is the movement destination.
        var lastKnownLocation =
            before.LocationId ??
            before.Movement?.ToLocationId;

        var payload =
            JsonSerializer.Serialize(
                new
                {
                    armyId = after.Id,
                    ownerId = after.OwnerId,
                    locationId = lastKnownLocation
                });

        updates.Add(
            new PendingMatchUpdate(
                observedAt,
                "ArmyDestroyed",
                payload));
    }

    private static void DetectBattleChanges(
        WorldSnapshot before,
        WorldSnapshot after,
        DateTimeOffset observedAt,
        ICollection<PendingMatchUpdate> updates)
    {
        var beforeBattles =
            before.Battles.ToDictionary(
                battle => battle.LocationId);

        var afterBattles =
            after.Battles.ToDictionary(
                battle => battle.LocationId);

        foreach (var battle in after.Battles)
        {
            if (beforeBattles.ContainsKey(
                    battle.LocationId))
            {
                continue;
            }

            var payload =
                JsonSerializer.Serialize(
                    new
                    {
                        locationId =
                            battle.LocationId
                    });

            updates.Add(
                new PendingMatchUpdate(
                    battle.StartedAt,
                    "BattleStarted",
                    payload));
        }

        foreach (var battle in before.Battles)
        {
            if (afterBattles.ContainsKey(
                    battle.LocationId))
            {
                continue;
            }

            var payload =
                JsonSerializer.Serialize(
                    new
                    {
                        locationId =
                            battle.LocationId
                    });

            updates.Add(
                new PendingMatchUpdate(
                    observedAt,
                    "BattleEnded",
                    payload));
        }
    }

    private static void DetectLocationChanges(
        WorldSnapshot before,
        WorldSnapshot after,
        DateTimeOffset observedAt,
        ICollection<PendingMatchUpdate> updates)
    {
        var beforeLocations =
            before.Locations.ToDictionary(
                location => location.Id);

        foreach (var afterLocation
                 in after.Locations)
        {
            if (!beforeLocations.TryGetValue(
                    afterLocation.Id,
                    out var beforeLocation))
            {
                continue;
            }

            if (beforeLocation.OwnerId ==
                afterLocation.OwnerId)
            {
                continue;
            }

            var payload =
                JsonSerializer.Serialize(
                    new
                    {
                        locationId =
                            afterLocation.Id,
                        previousOwnerId =
                            beforeLocation.OwnerId,
                        ownerId =
                            afterLocation.OwnerId
                    });

            updates.Add(
                new PendingMatchUpdate(
                    observedAt,
                    "LocationOwnerChanged",
                    payload));
        }
    }

    private static bool IsDestroyed(
        ArmySnapshot army)
    {
        return !army.Units.Any(
            unit =>
                unit.CurrentHitPoints > 0m);
    }
}