namespace ConquestusTerrarum.Domain.World;

public sealed class WorldState
{
    private readonly Dictionary<Guid, Army> _armies;
    private readonly Dictionary<Guid, Battle> _battles = new();
    private readonly List<PlayerAlliance> _alliances;

    public ulong RandomSeed { get; }

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
        IEnumerable<PlayerAlliance>? alliances = null,
        ulong randomSeed = 0)
    {
        ArgumentNullException.ThrowIfNull(map);
        ArgumentNullException.ThrowIfNull(armies);

        RandomSeed = randomSeed;
        Map = map;
        CheckpointTime = checkpointTime;

        _armies = armies.ToDictionary(
            army => army.Id);

        _alliances = alliances?.ToList()
            ?? new List<PlayerAlliance>();

        if (_armies.Values.SelectMany(army => army.Units).Select(unit => unit.Id).Distinct().Count() !=
            _armies.Values.Sum(army => army.Units.Count))
            throw new ArgumentException("Unit ids must be unique across the world.", nameof(armies));
        ValidateArmyLocations();
        InitializeArmyEntryTimes();

        ReconcileAllBattles(
            checkpointTime);
        RefillBattles(checkpointTime);
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

        if (army.IsInBattle)
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
        DateTimeOffset logicalTime,
        bool resolveOwnership = true)
    {
        var location =
            Map.GetLocation(locationId);

        var armiesAtLocation = _armies.Values
            .Where(army =>
                !army.IsDestroyed && army.LocationId == locationId)
            .ToArray();

        var hadBattle =
            _battles.ContainsKey(locationId);

        var hasHostilePair =
            HasHostilePair(
                armiesAtLocation);

        if (hasHostilePair)
        {
            if (!hadBattle)
            {
                _battles[locationId] =
                    new Battle(
                        locationId,
                        logicalTime,
                        RandomSeed);
            }

            foreach (var army in armiesAtLocation)
            {
                if (HasHostileArmy(
                        army,
                        armiesAtLocation))
                {
                    army.EnterBattle();
                }
                else
                {
                    army.LeaveBattle();
                }
            }

            return;
        }

        if (!hadBattle)
        {
            foreach (var army in armiesAtLocation)
            {
                army.LeaveBattle();
            }

            return;
        }

        _battles.Remove(locationId);

        foreach (var army in armiesAtLocation)
        {
            army.LeaveBattle();
        }

        if (resolveOwnership) ResolveOwnerAfterBattle(
            location,
            armiesAtLocation);
    }
    
    private static void ResolveOwnerAfterBattle(
        Location location,
        IReadOnlyCollection<Army> survivingArmies)
    {
        var armiesWithEntryTime =
            survivingArmies
                .Where(army =>
                    army.EnteredLocationAt is not null)
                .ToArray();

        if (armiesWithEntryTime.Length == 0)
        {
            return;
        }

        var earliestEntryTime =
            armiesWithEntryTime
                .Min(army =>
                    army.EnteredLocationAt!.Value);

        var earliestOwners =
            armiesWithEntryTime
                .Where(army =>
                    army.EnteredLocationAt ==
                    earliestEntryTime)
                .Select(army => army.OwnerId)
                .Distinct()
                .ToArray();

        if (earliestOwners.Length == 1)
        {
            location.ChangeOwner(
                earliestOwners[0]);

            return;
        }

        // Два разных союзника прибыли абсолютно одновременно.
        // Пока у нас нет gameplay-rule для такого tie,
        // поэтому не отдаём клетку случайно одному из них.
        location.ChangeOwner(null);
    }
    
    public void Retreat(
        Guid armyId,
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

        var army =
            GetArmy(armyId);

        if (!army.CanRetreat)
        {
            throw new InvalidOperationException(
                "Army cannot retreat from the current battle.");
        }

        if (army.LocationId is null ||
            army.RetreatLocationId is null)
        {
            throw new InvalidOperationException(
                "Army has no retreat destination.");
        }

        var battleLocationId =
            army.LocationId.Value;

        var retreatLocationId =
            army.RetreatLocationId.Value;

        var connection =
            Map.GetConnection(
                battleLocationId,
                retreatLocationId);

        foreach (var unit in army.Units.Where(unit => unit.IsWounded)) unit.Kill();
        army.RemoveIfDestroyed();
        if (!army.IsDestroyed) army.StartRetreat(
            connection,
            commandTime);

        ReconcileBattleAt(
            battleLocationId,
            commandTime);
        RefillBattles(commandTime);
    }

    internal void RefillBattles(DateTimeOffset time)
    {
        foreach (var battle in _battles.Values) battle.Refill(this, time);
    }

    internal Guid[] ReconcileWorld(DateTimeOffset time)
    {
        var previous = _battles.Keys.ToArray();
        ReconcileAllBattles(time, resolveOwnership: false);
        return previous.Where(id => !_battles.ContainsKey(id)).ToArray();
    }

    internal void ResolvePostBattleOwnership(IEnumerable<Guid> endedLocations)
    {
        foreach (var id in endedLocations)
            ResolveOwnerAfterBattle(Map.GetLocation(id), _armies.Values.Where(army => !army.IsDestroyed && army.LocationId == id).ToArray());
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
        DateTimeOffset logicalTime,
        bool resolveOwnership = true)
    {
        foreach (var location in Map.Locations)
        {
            ReconcileBattleAt(
                location.Id,
                logicalTime,
                resolveOwnership);
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
    
    private void InitializeArmyEntryTimes()
    {
        foreach (var army in _armies.Values)
        {
            army.InitializeLocationEntryTime(
                CheckpointTime);
        }
    }
    
    private bool HasHostileArmy(
        Army army,
        IReadOnlyCollection<Army> armiesAtLocation)
    {
        return armiesAtLocation.Any(
            otherArmy =>
                otherArmy.Id != army.Id &&
                AreHostile(
                    army.OwnerId,
                    otherArmy.OwnerId));
    }
}