namespace ConquestusTerrarum.Domain.World;

public sealed class MovementOrder
{
    public Guid FromLocationId { get; }
    public Guid ToLocationId { get; }

    public DateTimeOffset StartedAt { get; }
    public DateTimeOffset ArriveAt { get; }

    public MovementKind Kind { get; }

    public MovementOrder(
        Guid fromLocationId,
        Guid toLocationId,
        DateTimeOffset startedAt,
        TimeSpan travelTime,
        MovementKind kind = MovementKind.Normal)
    {
        if (fromLocationId == Guid.Empty)
        {
            throw new ArgumentException(
                "From location id cannot be empty.",
                nameof(fromLocationId));
        }

        if (toLocationId == Guid.Empty)
        {
            throw new ArgumentException(
                "To location id cannot be empty.",
                nameof(toLocationId));
        }

        if (fromLocationId == toLocationId)
        {
            throw new ArgumentException(
                "Army cannot move to the same location.");
        }

        if (travelTime <= TimeSpan.Zero)
        {
            throw new ArgumentOutOfRangeException(
                nameof(travelTime),
                "Travel time must be greater than zero.");
        }

        FromLocationId = fromLocationId;
        ToLocationId = toLocationId;

        StartedAt = startedAt;
        ArriveAt = startedAt + travelTime;

        Kind = kind;
    }
}