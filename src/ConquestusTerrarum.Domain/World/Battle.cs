namespace ConquestusTerrarum.Domain.World;

public sealed class Battle
{
    public Guid LocationId { get; }
    public DateTimeOffset StartedAt { get; }

    internal Battle(
        Guid locationId,
        DateTimeOffset startedAt)
    {
        if (locationId == Guid.Empty)
        {
            throw new ArgumentException(
                "Location id cannot be empty.",
                nameof(locationId));
        }

        LocationId = locationId;
        StartedAt = startedAt;
    }
}