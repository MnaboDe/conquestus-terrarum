namespace ConquestusTerrarum.Domain.World;

public sealed partial class Army
{
    private bool _retreatLocked;
    public IReadOnlyList<Unit> Units { get; }
    public bool IsDestroyed => !Units.Any(unit => unit.IsAlive);

    public Guid Id { get; }
    public Guid OwnerId { get; }

    public Guid? LocationId { get; private set; }
    
    public DateTimeOffset? EnteredLocationAt { get; private set; }

    public Guid? PreviousLocationId { get; private set; }

    public Guid? RetreatLocationId { get; private set; }

    public MovementOrder? Movement { get; private set; }

    public bool IsMoving => Movement is not null;

    public bool IsInBattle { get; private set; }

    public bool CanRetreat =>
        IsInBattle &&
        !IsMoving &&
        RetreatLocationId is not null;

    public Army(
        Guid id,
        Guid ownerId,
        Guid locationId,
        IEnumerable<Unit> units)
    {
        if (id == Guid.Empty)
        {
            throw new ArgumentException(
                "Army id cannot be empty.",
                nameof(id));
        }

        if (ownerId == Guid.Empty)
        {
            throw new ArgumentException(
                "Owner id cannot be empty.",
                nameof(ownerId));
        }

        if (locationId == Guid.Empty)
        {
            throw new ArgumentException(
                "Location id cannot be empty.",
                nameof(locationId));
        }

        ArgumentNullException.ThrowIfNull(units);
        var unitList = units.ToList();
        if (unitList.Any(unit => unit is null)) throw new ArgumentException("Units cannot contain null.", nameof(units));
        if (unitList.Select(unit => unit.Id).Distinct().Count() != unitList.Count)
            throw new ArgumentException("Unit ids must be unique within an army.", nameof(units));
        Units = unitList.AsReadOnly();
        Id = id;
        OwnerId = ownerId;
        LocationId = locationId;
        RemoveIfDestroyed();
    }

    public void StartMovement(
        MapConnection connection,
        DateTimeOffset startedAt)
    {
        ArgumentNullException.ThrowIfNull(connection);
        if (IsDestroyed) throw new InvalidOperationException("Destroyed armies cannot move or retreat.");

        if (IsMoving)
        {
            throw new InvalidOperationException(
                "Army is already moving.");
        }

        if (IsInBattle)
        {
            throw new InvalidOperationException(
                "Army cannot start normal movement while in battle.");
        }

        if (LocationId is null)
        {
            throw new InvalidOperationException(
                "Army is not located anywhere.");
        }

        var destinationId =
            GetDestination(connection, LocationId.Value);

        Movement = new MovementOrder(
            LocationId.Value,
            destinationId,
            startedAt,
            connection.TravelTime,
            MovementKind.Normal);

        EnteredLocationAt = null;
        LocationId = null;
    }

    internal void StartRetreat(
        MapConnection connection,
        DateTimeOffset startedAt)
    {
        ArgumentNullException.ThrowIfNull(connection);
        if (IsDestroyed) throw new InvalidOperationException("Destroyed armies cannot move or retreat.");

        if (!CanRetreat)
        {
            throw new InvalidOperationException(
                "Army cannot retreat.");
        }

        if (LocationId is null ||
            RetreatLocationId is null)
        {
            throw new InvalidOperationException(
                "Army has no valid retreat destination.");
        }

        var currentLocationId = LocationId.Value;
        var retreatLocationId = RetreatLocationId.Value;

        if (!connection.Connects(
                currentLocationId,
                retreatLocationId))
        {
            throw new InvalidOperationException(
                "Connection does not lead to the retreat location.");
        }

        Movement = new MovementOrder(
            currentLocationId,
            retreatLocationId,
            startedAt,
            connection.TravelTime,
            MovementKind.Retreat);

        EnteredLocationAt = null;
        LocationId = null;

        // После retreat нельзя сразу retreat ещё раз,
        // даже если в destination начнётся новый бой.
        _retreatLocked = true;

        IsInBattle = false;
        RetreatLocationId = null;
    }

    public void CompleteMovement()
    {
        if (Movement is null)
        {
            throw new InvalidOperationException(
                "Army is not moving.");
        }

        var completedMovement = Movement;

        PreviousLocationId =
            completedMovement.FromLocationId;

        LocationId =
            completedMovement.ToLocationId;

        EnteredLocationAt =
            completedMovement.ArriveAt;

        if (completedMovement.Kind ==
            MovementKind.Normal)
        {
            _retreatLocked = false;
        }

        Movement = null;
    }
    internal void EnterBattle()
    {
        if (IsInBattle)
            return;

        IsInBattle = true;

        RetreatLocationId =
            _retreatLocked
                ? null
                : PreviousLocationId;
    }
    
    internal void InitializeLocationEntryTime(
        DateTimeOffset enteredAt)
    {
        if (LocationId is not null &&
            EnteredLocationAt is null)
        {
            EnteredLocationAt = enteredAt;
        }
    }

    internal void LeaveBattle()
    {
        IsInBattle = false;
        RetreatLocationId = null;
    }

    internal void RemoveIfDestroyed()
    {
        if (!IsDestroyed) return;
        LocationId = null;
        EnteredLocationAt = null;
        Movement = null;
        LeaveBattle();
    }

    private static Guid GetDestination(
        MapConnection connection,
        Guid currentLocationId)
    {
        if (connection.FromLocationId ==
            currentLocationId)
        {
            return connection.ToLocationId;
        }

        if (connection.ToLocationId ==
            currentLocationId)
        {
            return connection.FromLocationId;
        }

        throw new InvalidOperationException(
            "Connection does not contain the army's current location.");
    }
}