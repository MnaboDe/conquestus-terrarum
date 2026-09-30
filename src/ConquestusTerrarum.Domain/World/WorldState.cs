namespace ConquestusTerrarum.Domain.World;

public sealed class WorldState
{
    private readonly Dictionary<Guid, Army> _armies;
    private readonly Dictionary<Guid, Battle> _battles = new();

    public GameMap Map { get; }

    public DateTimeOffset CheckpointTime { get; private set; }

    public IReadOnlyCollection<Army> Armies => _armies.Values;
    public IReadOnlyCollection<Battle> Battles => _battles.Values;

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
        ReconcileAllBattles(checkpointTime);
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

    public Battle? GetBattleAt(Guid locationId)
    {
        _battles.TryGetValue(locationId, out var battle);
        return battle;
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

        WorldSimulator.AdvanceTo(
            this,
            commandTime);

        var army = GetArmy(armyId);

        if (army.LocationId is null)
        {
            throw new InvalidOperationException(
                "Army is not currently located anywhere.");
        }

        var originLocationId = army.LocationId.Value;

        if (GetBattleAt(originLocationId) is not null)
        {
            throw new InvalidOperationException(
                "Army cannot start normal movement while in battle.");
        }

        Map.GetLocation(destinationId);

        var connection = Map.GetConnection(
            originLocationId,
            destinationId);

        army.StartMovement(
            connection,
            commandTime);
    }

    internal void SetCheckpointTime(
        DateTimeOffset checkpointTime)
    {
        CheckpointTime = checkpointTime;
    }

    internal void ReconcileBattleAt(
        Guid locationId,
        DateTimeOffset logicalTime)
    {
        Map.GetLocation(locationId);

        var ownerCount = _armies.Values
            .Where(army => army.LocationId == locationId)
            .Select(army => army.OwnerId)
            .Distinct()
            .Take(2)
            .Count();

        var hasEnemies = ownerCount >= 2;

        if (hasEnemies)
        {
            if (!_battles.ContainsKey(locationId))
            {
                _battles[locationId] =
                    new Battle(
                        locationId,
                        logicalTime);
            }

            return;
        }

        _battles.Remove(locationId);
    }

    private void ReconcileAllBattles(
        DateTimeOffset logicalTime)
    {
        foreach (var location in Map.Locations)
        {
            ReconcileBattleAt(
                location.Id,
                logicalTime);
        }
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
                Map.GetLocation(
                    movement.FromLocationId);

                Map.GetLocation(
                    movement.ToLocationId);
            }
        }
    }
}