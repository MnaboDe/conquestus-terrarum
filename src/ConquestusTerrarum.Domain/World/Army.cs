namespace ConquestusTerrarum.Domain.World;

public sealed class Army
{
    private bool _retreatLocked;

    public Guid Id { get; }
    public Guid OwnerId { get; }

    public Guid? LocationId { get; private set; }

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
        Guid locationId)
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

        Id = id;
        OwnerId = ownerId;
        LocationId = locationId;
    }

    public void StartMovement(
        MapConnection connection,
        DateTimeOffset startedAt)
    {
        ArgumentNullException.ThrowIfNull(connection);

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

        LocationId = null;
    }

    internal void StartRetreat(
        MapConnection connection,
        DateTimeOffset startedAt)
    {
        ArgumentNullException.ThrowIfNull(connection);

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

        if (completedMovement.Kind ==
            MovementKind.Normal)
        {
            // Нормальное перемещение снова разрешает
            // будущий retreat.
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

    internal void LeaveBattle()
    {
        IsInBattle = false;
        RetreatLocationId = null;
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