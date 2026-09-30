namespace ConquestusTerrarum.Domain.World;

public static class WorldSimulator
{
    public static void AdvanceTo(
        WorldState world,
        DateTimeOffset targetTime)
    {
        ArgumentNullException.ThrowIfNull(world);

        if (targetTime < world.CheckpointTime)
        {
            throw new InvalidOperationException(
                "World cannot be advanced backwards in time.");
        }

        EnsureNoEventsBeforeCheckpoint(world);

        while (true)
        {
            var nextArmyToArrive = world.Armies
                .Where(army =>
                    army.Movement is not null &&
                    army.Movement.ArriveAt <= targetTime)
                .OrderBy(army => army.Movement!.ArriveAt)
                .FirstOrDefault();

            if (nextArmyToArrive is null)
                break;

            var eventTime =
                nextArmyToArrive.Movement!.ArriveAt;

            var arrivingArmies = world.Armies
                .Where(army =>
                    army.Movement is not null &&
                    army.Movement.ArriveAt == eventTime)
                .ToArray();

            // Все прибытия с одинаковым logical time
            // применяются одним batch.
            foreach (var army in arrivingArmies)
            {
                army.CompleteMovement();
            }

            world.SetCheckpointTime(eventTime);
        }

        world.SetCheckpointTime(targetTime);
    }

    private static void EnsureNoEventsBeforeCheckpoint(
        WorldState world)
    {
        var staleArmy = world.Armies.FirstOrDefault(
            army =>
                army.Movement is not null &&
                army.Movement.ArriveAt < world.CheckpointTime);

        if (staleArmy is not null)
        {
            throw new InvalidOperationException(
                $"Army '{staleArmy.Id}' has an unprocessed movement event before the world checkpoint.");
        }
    }
}