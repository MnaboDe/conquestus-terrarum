#nullable disable
using System;
using System.Globalization;
using System.Linq;

namespace ConquestusTerrarum.Client
{
    /// <summary>
    /// Applies a whole delivery page on detached DTOs. Failure leaves both state and
    /// cursor unchanged; the caller performs one full resync, never one per event.
    /// The feed is a coarse synchronization hint, not a simulation/event-sourcing log.
    /// </summary>
    public static class MatchUpdateApplier
    {
        public static bool TryApplyPage(MatchStateDto current, MatchUpdatesDto page, long cursor,
            out MatchStateDto state, out long nextCursor)
        {
            state = current;
            nextCursor = cursor;
            if (current == null || page == null || page.updates == null || cursor < 0 || page.nextCursor < 0)
                return false;

            var candidate = Copy(current);
            long appliedCursor = cursor;
            long previousSequence = 0;
            foreach (var update in page.updates)
            {
                if (update == null || update.sequence <= previousSequence || update.sequence > page.nextCursor)
                    return false;
                previousSequence = update.sequence;
                // A command/full snapshot already includes events through its updateCursor.
                if (update.sequence <= cursor) continue;
                if (appliedCursor == long.MaxValue || update.sequence != appliedCursor + 1 ||
                    !TryApply(candidate, update))
                    return false;
                appliedCursor = update.sequence;
            }
            if (page.nextCursor > appliedCursor) return false;
            if (appliedCursor == cursor) return true;
            candidate.updateCursor = appliedCursor;
            state = candidate;
            nextCursor = appliedCursor;
            return true;
        }

        private static bool TryApply(MatchStateDto state, MatchUpdateDto update)
        {
            if (update.payload == null || !TryTime(update.occurredAt, out var occurredAt)) return false;
            switch (update.type)
            {
                case "ArmyMovementStarted": return ApplyMovement(state, update.payload, occurredAt);
                case "ArmyArrived": return ApplyArrival(state, update.payload, occurredAt);
                case "LocationOwnerChanged": return ApplyOwner(state, update.payload);
                // These payloads contain only identities, not slots, flags and unit HP.
                // ArmyRetreatStarted also lacks authoritative casualty state.
                case "BattleStarted":
                case "BattleEnded":
                case "ArmyDestroyed":
                case "MatchStarted":
                case "MatchFinished":
                default: return false;
            }
        }

        private static bool ApplyMovement(MatchStateDto state, MatchUpdatePayloadDto payload, DateTimeOffset occurredAt)
        {
            var army = FindArmy(state, payload.armyId);
            if (army == null || !Same(army.ownerId, payload.ownerId) || army.isDestroyed || army.isInBattle ||
                army.canRetreat || !string.IsNullOrEmpty(army.retreatLocationId) || army.isMoving || army.movement != null ||
                !Same(army.locationId, payload.fromLocationId) || !ValidId(payload.toLocationId) ||
                !TryTime(payload.startedAt, out var started) || !TryTime(payload.arriveAt, out var arrived) ||
                occurredAt != started || arrived <= started ||
                !HasConnection(state, payload.fromLocationId, payload.toLocationId, arrived - started))
                return false;

            army.locationId = null;
            army.enteredLocationAt = null;
            army.isMoving = true;
            army.movement = new MovementDto
            {
                fromLocationId = payload.fromLocationId, toLocationId = payload.toLocationId,
                startedAt = payload.startedAt, arriveAt = payload.arriveAt, kind = "normal"
            };
            return true;
        }

        private static bool ApplyArrival(MatchStateDto state, MatchUpdatePayloadDto payload, DateTimeOffset occurredAt)
        {
            var army = FindArmy(state, payload.armyId);
            if (army == null || !Same(army.ownerId, payload.ownerId) || !ValidId(payload.locationId) ||
                army.isDestroyed || army.isInBattle || army.canRetreat || !string.IsNullOrEmpty(army.retreatLocationId) ||
                !army.isMoving || army.movement == null || !string.IsNullOrEmpty(army.locationId) ||
                !string.IsNullOrEmpty(army.enteredLocationAt)) return false;

            var movement = army.movement;
            if (!Same(movement.toLocationId, payload.locationId) ||
                !(Same(movement.kind, "normal") || Same(movement.kind, "retreat")) ||
                !string.Equals(movement.kind, payload.movementKind, StringComparison.OrdinalIgnoreCase) ||
                !TryTime(movement.startedAt, out var started) || !TryTime(movement.arriveAt, out var arrived) ||
                occurredAt != arrived || arrived <= started ||
                !HasConnection(state, movement.fromLocationId, movement.toLocationId, arrived - started) ||
                state.activeBattles == null ||
                state.activeBattles.Any(b => b == null || Same(b.locationId, payload.locationId))) return false;

            // A coarse arrival alone cannot restore combat/retreat flags at an occupied
            // destination. Be conservative even if a battle event is on a later page.
            if (state.armies.Any(a => a == null || (a != army && !a.isDestroyed &&
                Same(a.locationId, payload.locationId) && !Same(a.ownerId, army.ownerId)))) return false;

            army.previousLocationId = movement.fromLocationId;
            army.locationId = payload.locationId;
            // The domain's authoritative arrival timestamp is the movement anchor.
            army.enteredLocationAt = movement.arriveAt;
            army.movement = null;
            army.isMoving = false;
            return true;
        }

        private static bool ApplyOwner(MatchStateDto state, MatchUpdatePayloadDto payload)
        {
            if (!ValidId(payload.locationId) || !OptionalId(payload.ownerId) || !OptionalId(payload.previousOwnerId) ||
                state.locations == null) return false;
            var matches = state.locations.Where(l => l != null && Same(l.id, payload.locationId)).ToArray();
            if (matches.Length != 1 || !Same(matches[0].ownerId, payload.previousOwnerId)) return false;
            matches[0].ownerId = payload.ownerId;
            return true;
        }

        private static ArmyDto FindArmy(MatchStateDto state, string id)
        {
            if (!ValidId(id) || state.armies == null) return null;
            var matches = state.armies.Where(a => a != null && Same(a.id, id)).ToArray();
            return matches.Length == 1 ? matches[0] : null;
        }

        private static bool HasConnection(MatchStateDto state, string from, string to, TimeSpan duration)
        {
            return ValidId(from) && ValidId(to) && !Same(from, to) && state.locations != null &&
                state.locations.Count(l => l != null && Same(l.id, from)) == 1 &&
                state.locations.Count(l => l != null && Same(l.id, to)) == 1 && state.connections != null &&
                state.connections.Any(c => c != null && c.travelTimeTicks == duration.Ticks &&
                    ((Same(c.fromLocationId, from) && Same(c.toLocationId, to)) ||
                     (Same(c.fromLocationId, to) && Same(c.toLocationId, from))));
        }

        private static bool TryTime(string text, out DateTimeOffset time)
        {
            return DateTimeOffset.TryParseExact(text,
                new[] { "yyyy-MM-dd'T'HH:mm:sszzz", "yyyy-MM-dd'T'HH:mm:ss.FFFFFFFzzz",
                    "yyyy-MM-dd'T'HH:mm:ss'Z'", "yyyy-MM-dd'T'HH:mm:ss.FFFFFFF'Z'" },
                CultureInfo.InvariantCulture, DateTimeStyles.AssumeUniversal, out time);
        }

        private static bool ValidId(string id) { return Guid.TryParse(id, out var parsed) && parsed != Guid.Empty; }
        private static bool OptionalId(string id) { return id == null || ValidId(id); }
        private static bool Same(string first, string second) { return string.Equals(first, second, StringComparison.Ordinal); }

        private static MatchStateDto Copy(MatchStateDto source)
        {
            return new MatchStateDto
            {
                matchId = source.matchId, version = source.version, updateCursor = source.updateCursor,
                phase = source.phase, winnerPlayerId = source.winnerPlayerId, isDraw = source.isDraw,
                serverTime = source.serverTime, checkpointTime = source.checkpointTime,
                connections = source.connections, alliances = source.alliances, activeBattles = source.activeBattles,
                locations = source.locations == null ? null : source.locations.Select(l => l == null ? null : new LocationDto
                { id = l.id, name = l.name, ownerId = l.ownerId, combatSlotCount = l.combatSlotCount }).ToArray(),
                armies = source.armies == null ? null : source.armies.Select(a => a == null ? null : new ArmyDto
                {
                    id = a.id, ownerId = a.ownerId, locationId = a.locationId, enteredLocationAt = a.enteredLocationAt,
                    previousLocationId = a.previousLocationId, retreatLocationId = a.retreatLocationId,
                    isMoving = a.isMoving, isDestroyed = a.isDestroyed, isInBattle = a.isInBattle, canRetreat = a.canRetreat,
                    movement = a.movement, units = a.units
                }).ToArray()
            };
        }
    }
}
