namespace ConquestusTerrarum.Domain.World;

public sealed class Army
{
    public Guid Id { get; }
    public Guid OwnerId { get; }

    public Guid? LocationId { get; private set; }
    public MovementOrder? Movement { get; private set; }

    public bool IsMoving => Movement is not null;

    public Army(
        Guid id,
        Guid ownerId,
        Guid locationId)
    {
        if (id == Guid.Empty)
            throw new ArgumentException(
                "Army id cannot be empty.",
                nameof(id));

        if (ownerId == Guid.Empty)
            throw new ArgumentException(
                "Owner id cannot be empty.",
                nameof(ownerId));

        if (locationId == Guid.Empty)
            throw new ArgumentException(
                "Location id cannot be empty.",
                nameof(locationId));

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
            throw new InvalidOperationException(
                "Army is already moving.");

        if (LocationId is null)
            throw new InvalidOperationException(
                "Army is not located anywhere.");

        Guid destinationId;

        if (connection.FromLocationId == LocationId.Value)
        {
            destinationId = connection.ToLocationId;
        }
        else if (connection.ToLocationId == LocationId.Value)
        {
            destinationId = connection.FromLocationId;
        }
        else
        {
            throw new InvalidOperationException(
                "Connection does not contain the army's current location.");
        }

        Movement = new MovementOrder(
            LocationId.Value,
            destinationId,
            startedAt,
            connection.TravelTime);

        LocationId = null;
    }

    public void CompleteMovement()
    {
        if (Movement is null)
            throw new InvalidOperationException(
                "Army is not moving.");

        LocationId = Movement.ToLocationId;
        Movement = null;
    }
}