namespace ConquestusTerrarum.Domain.World;

public sealed class WorldState
{
    private readonly Dictionary<Guid, Army> _armies;

    public GameMap Map { get; }

    public DateTimeOffset CheckpointTime { get; private set; }

    public IReadOnlyCollection<Army> Armies => _armies.Values;

    public WorldState(
        GameMap map,
        IEnumerable<Army> armies,
        DateTimeOffset checkpointTime)
    {
        ArgumentNullException.ThrowIfNull(map);
        ArgumentNullException.ThrowIfNull(armies);

        Map = map;
        CheckpointTime = checkpointTime;

        _armies = armies.ToDictionary(army => army.Id);

        ValidateArmyLocations();
    }

    public Army GetArmy(Guid armyId)
    {
        if (!_armies.TryGetValue(armyId, out var army))
        {
            throw new InvalidOperationException(
                $"Army '{armyId}' does not exist.");
        }

        return army;
    }
    
    public void StartMovement(
        Guid armyId,
        Guid destinationId,
        DateTimeOffset commandTime)
    {
        if (commandTime < CheckpointTime)
        {
            throw new InvalidOperationException(
                "Command time cannot be before the world checkpoint.");
        }

        WorldSimulator.AdvanceTo(this, commandTime);

        var army = GetArmy(armyId);

        if (army.LocationId is null)
        {
            throw new InvalidOperationException(
                "Army is not currently located anywhere.");
        }

        Map.GetLocation(destinationId);

        var connection = Map.GetConnection(
            army.LocationId.Value,
            destinationId);

        army.StartMovement(
            connection,
            commandTime);
    }

    internal void SetCheckpointTime(DateTimeOffset checkpointTime)
    {
        CheckpointTime = checkpointTime;
    }

    private void ValidateArmyLocations()
    {
        foreach (var army in _armies.Values)
        {
            if (army.LocationId is { } locationId)
            {
                Map.GetLocation(locationId);
            }

            if (army.Movement is { } movement)
            {
                Map.GetLocation(movement.FromLocationId);
                Map.GetLocation(movement.ToLocationId);
            }
        }
    }
}