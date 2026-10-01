using ConquestusTerrarum.Domain.World;

namespace ConquestusTerrarum.Domain.Tests;

internal static class TestArmies
{
    // Movement/diplomacy tests use explicit non-damaging units to isolate their original rules.
    public static Army Create(Guid id, Guid ownerId, Guid locationId) =>
        new(id, ownerId, locationId, new[] { new Unit(id, 100m, 0m) });
}
