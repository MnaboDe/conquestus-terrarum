using System.Text.Json;
using System.Text.Json.Serialization;
using ConquestusTerrarum.Domain.World;

namespace ConquestusTerrarum.Persistence;

public sealed class WorldSnapshotSerializer
{
    private static readonly JsonSerializerOptions Options = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        RespectRequiredConstructorParameters = true,
        RespectNullableAnnotations = true,
        UnmappedMemberHandling = JsonUnmappedMemberHandling.Disallow
    };

    public string Serialize(WorldSnapshot snapshot)
    {
        // Validation also rejects impossible snapshots produced by an invalid command callback.
        WorldState.Restore(snapshot);
        return JsonSerializer.Serialize(snapshot, Options);
    }

    public WorldSnapshot Deserialize(string json)
    {
        try
        {
            var snapshot = JsonSerializer.Deserialize<WorldSnapshot>(json, Options)
                ?? throw new JsonException("The world snapshot is null.");
            WorldState.Restore(snapshot);
            return snapshot;
        }
        catch (Exception ex) when (ex is JsonException or ArgumentException or InvalidOperationException or OverflowException)
        {
            throw new InvalidWorldStateException("The stored world snapshot is invalid or uses an unsupported format.", ex);
        }
    }
}
