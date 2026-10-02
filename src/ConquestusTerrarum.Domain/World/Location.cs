namespace ConquestusTerrarum.Domain.World;

public sealed class Location
{
    public Location(
        Guid id,
        string name,
        Guid? ownerId = null,
        int combatSlotCount = 1)
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

        if (combatSlotCount <= 0) throw new ArgumentOutOfRangeException(nameof(combatSlotCount));
        CombatSlotCount = combatSlotCount;
        Id = id;
        Name = name;
        OwnerId = ownerId;
    }

    public int CombatSlotCount { get; }

    public Guid Id { get; }
    public string Name { get; }

    public Guid? OwnerId { get; private set; }

    internal void ChangeOwner(Guid? ownerId)
    {
        if (ownerId == Guid.Empty)
            throw new ArgumentException(
                "Owner id cannot be empty.",
                nameof(ownerId));

        OwnerId = ownerId;
    }
}