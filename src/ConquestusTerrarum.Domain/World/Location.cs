namespace ConquestusTerrarum.Domain.World;

public sealed class Location
{
    public Guid Id { get; }
    public string Name { get; }

    public Guid? OwnerId { get; private set; }

    public Location(
        Guid id,
        string name,
        Guid? ownerId = null)
    {
        if (id == Guid.Empty)
            throw new ArgumentException(
                "Location id cannot be empty.",
                nameof(id));

        if (string.IsNullOrWhiteSpace(name))
            throw new ArgumentException(
                "Location name cannot be empty.",
                nameof(name));

        if (ownerId == Guid.Empty)
            throw new ArgumentException(
                "Owner id cannot be empty.",
                nameof(ownerId));

        Id = id;
        Name = name;
        OwnerId = ownerId;
    }

    internal void ChangeOwner(Guid? ownerId)
    {
        if (ownerId == Guid.Empty)
            throw new ArgumentException(
                "Owner id cannot be empty.",
                nameof(ownerId));

        OwnerId = ownerId;
    }
}