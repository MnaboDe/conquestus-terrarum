// MapConnection.cs
namespace ConquestusTerrarum.Domain.World;

public sealed class MapConnection
{
    public Guid FromLocationId { get; }
    public Guid ToLocationId { get; }
    public TimeSpan TravelTime { get; }

    public MapConnection(
        Guid fromLocationId,
        Guid toLocationId,
        TimeSpan travelTime)
    {
        if (fromLocationId == Guid.Empty)
            throw new ArgumentException("From location id cannot be empty.", nameof(fromLocationId));

        if (toLocationId == Guid.Empty)
            throw new ArgumentException("To location id cannot be empty.", nameof(toLocationId));

        if (fromLocationId == toLocationId)
            throw new ArgumentException("A location cannot be connected to itself.");

        if (travelTime <= TimeSpan.Zero)
            throw new ArgumentOutOfRangeException(
                nameof(travelTime),
                "Travel time must be greater than zero.");

        FromLocationId = fromLocationId;
        ToLocationId = toLocationId;
        TravelTime = travelTime;
    }

    public bool Connects(Guid first, Guid second)
    {
        return
            FromLocationId == first && ToLocationId == second ||
            FromLocationId == second && ToLocationId == first;
    }
}