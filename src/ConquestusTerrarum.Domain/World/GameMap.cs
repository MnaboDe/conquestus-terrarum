namespace ConquestusTerrarum.Domain.World;

public sealed class GameMap
{
    private readonly Dictionary<Guid, Location> _locations;
    private readonly List<MapConnection> _connections;

    public IReadOnlyCollection<Location> Locations => _locations.Values;
    public IReadOnlyCollection<MapConnection> Connections => _connections;

    public GameMap(
        IEnumerable<Location> locations,
        IEnumerable<MapConnection> connections)
    {
        ArgumentNullException.ThrowIfNull(locations);
        ArgumentNullException.ThrowIfNull(connections);

        _locations = locations.ToDictionary(location => location.Id);
        _connections = connections.ToList();

        ValidateConnections();
    }

    public Location GetLocation(Guid locationId)
    {
        if (!_locations.TryGetValue(locationId, out var location))
        {
            throw new InvalidOperationException(
                $"Location '{locationId}' does not exist.");
        }

        return location;
    }

    public MapConnection GetConnection(
        Guid firstLocationId,
        Guid secondLocationId)
    {
        var connection = _connections.FirstOrDefault(
            connection => connection.Connects(
                firstLocationId,
                secondLocationId));

        if (connection is null)
        {
            throw new InvalidOperationException(
                $"Locations '{firstLocationId}' and '{secondLocationId}' are not connected.");
        }

        return connection;
    }

    private void ValidateConnections()
    {
        foreach (var connection in _connections)
        {
            if (!_locations.ContainsKey(connection.FromLocationId))
            {
                throw new ArgumentException(
                    $"Connection references unknown location '{connection.FromLocationId}'.");
            }

            if (!_locations.ContainsKey(connection.ToLocationId))
            {
                throw new ArgumentException(
                    $"Connection references unknown location '{connection.ToLocationId}'.");
            }
        }
    }
}