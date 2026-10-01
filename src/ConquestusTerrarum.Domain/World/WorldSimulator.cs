namespace ConquestusTerrarum.Domain.World;

public static class WorldSimulator
{
    public static void AdvanceTo(WorldState world, DateTimeOffset targetTime)
    {
        ArgumentNullException.ThrowIfNull(world);
        if (targetTime < world.CheckpointTime)
            throw new InvalidOperationException("World cannot be advanced backwards in time.");
        if (world.Armies.Any(army => army.Movement is { } movement && movement.ArriveAt < world.CheckpointTime))
            throw new InvalidOperationException("Unprocessed movement event before the world checkpoint.");

        do
        {
            var boundary = targetTime;
            foreach (var army in world.Armies)
                if (army.Movement is { } movement && movement.ArriveAt < boundary) boundary = movement.ArriveAt;
            foreach (var slot in world.Battles.SelectMany(battle => battle.Slots))
                if (slot.NextDeathAt is { } death && death < boundary) boundary = death;

            // A: Advance continuous damage for ALL battles before any discrete event.
            foreach (var slot in world.Battles.SelectMany(battle => battle.Slots)) slot.AdvanceDamage(boundary);
            // B: Resolve all simultaneous deaths, releasing survivors together.
            foreach (var battle in world.Battles) battle.RemoveInvalidSlots(world);
            // C: Destroyed armies cease occupying locations.
            foreach (var army in world.Armies) army.RemoveIfDestroyed();
            // D: Complete ALL arrivals at this timestamp as one batch.
            foreach (var army in world.Armies.Where(army => army.Movement?.ArriveAt == boundary).ToArray())
                army.CompleteMovement();
            // E: Reconcile pairwise hostility and battle lifetime.
            var endedLocations = world.ReconcileWorld(boundary);
            // F: Refill only after all deaths, destruction, arrivals and reconciliation.
            world.RefillBattles(boundary);
            // G: Resolve ownership only after reconciliation and slot refill.
            world.ResolvePostBattleOwnership(endedLocations);
            // H: Commit the world checkpoint.
            world.SetCheckpointTime(boundary);
        } while (world.CheckpointTime < targetTime);
    }
}