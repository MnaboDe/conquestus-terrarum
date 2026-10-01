namespace ConquestusTerrarum.Domain.World;

public sealed class WorldState
{
    private readonly Dictionary<Guid, Army> _armies;
    private readonly Dictionary<Guid, Battle> _battles = new();
    private readonly List<PlayerAlliance> _alliances;

    public GameMap Map { get; }

    public DateTimeOffset CheckpointTime { get; private set; }

    public IReadOnlyCollection<Army> Armies =>
        _armies.Values;

    public IReadOnlyCollection<Battle> Battles =>
        _battles.Values;

    public IReadOnlyCollection<PlayerAlliance> Alliances =>
        _alliances;

    public WorldState(
        GameMap map,
        IEnumerable<Army> armies,
        DateTimeOffset checkpointTime,
        IEnumerable<PlayerAlliance>? alliances = null)
    {
        ArgumentNullException.ThrowIfNull(map);
        ArgumentNullException.ThrowIfNull(armies);

        Map = map;
        CheckpointTime = checkpointTime;

        _armies = armies.ToDictionary(
            army => army.Id);

        _alliances = alliances?.ToList()
            ?? new List<PlayerAlliance>();

        ValidateArmyLocations();

        ReconcileAllBattles(
            checkpointTime);
    }

    public Army GetArmy(Guid armyId)
    {
        if (!_armies.TryGetValue(
                armyId,
                out var army))
        {
            throw new InvalidOperationException(
                $"Army '{armyId}' does not exist.");
        }

        return army;
    }

    public Battle? GetBattleAt(
        Guid locationId)
    {
        _battles.TryGetValue(
            locationId,
            out var battle);

        return battle;
    }

    public bool AreAllies(
        Guid firstPlayerId,
        Guid secondPlayerId)
    {
        if (firstPlayerId == secondPlayerId)
            return true;

        return _alliances.Any(
            alliance => alliance.Connects(
                firstPlayerId,
                secondPlayerId));
    }

    public bool AreHostile(
        Guid firstPlayerId,
        Guid secondPlayerId)
    {
        return !AreAllies(
            firstPlayerId,
            secondPlayerId);
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

        var originLocationId =
            army.LocationId.Value;

        if (GetBattleAt(originLocationId) is not null)
        {
            throw new InvalidOperationException(
                "Army cannot start normal movement while in battle.");
        }

        Map.GetLocation(
            destinationId);

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
        Map.GetLocation(
            locationId);

        var armiesAtLocation = _armies.Values
            .Where(army =>
                army.LocationId == locationId)
            .ToArray();

        var hasHostilePair =
            HasHostilePair(
                armiesAtLocation);

        if (hasHostilePair)
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

        _battles.Remove(
            locationId);
    }

    private bool HasHostilePair(
        IReadOnlyList<Army> armies)
    {
        for (var firstIndex = 0;
             firstIndex < armies.Count;
             firstIndex++)
        {
            for (var secondIndex = firstIndex + 1;
                 secondIndex < armies.Count;
                 secondIndex++)
            {
                var firstArmy =
                    armies[firstIndex];

                var secondArmy =
                    armies[secondIndex];

                if (AreHostile(
                        firstArmy.OwnerId,
                        secondArmy.OwnerId))
                {
                    return true;
                }
            }
        }

        return false;
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
                Map.GetLocation(
                    locationId);
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