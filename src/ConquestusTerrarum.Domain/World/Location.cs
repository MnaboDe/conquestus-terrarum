// Location.cs
namespace ConquestusTerrarum.Domain.World;

public sealed class Location
{
    public Guid Id { get; }
    public string Name { get; }

    public Location(Guid id, string name)
    {
        if (id == Guid.Empty)
            throw new ArgumentException("Location id cannot be empty.", nameof(id));

        if (string.IsNullOrWhiteSpace(name))
            throw new ArgumentException("Location name cannot be empty.", nameof(name));

        Id = id;
        Name = name;
    }
}