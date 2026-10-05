// Verification fixture only. Never expose this operation through the API or ship it in Unity.
using System.Collections.Immutable;
using ConquestusTerrarum.Domain.World;
using ConquestusTerrarum.Persistence;
using Npgsql;
using NpgsqlTypes;

if (args.Length != 2 || !Guid.TryParse(args[0], out var matchId) || !Guid.TryParse(args[1], out var winner))
    throw new ArgumentException("Pass the isolated verification match ID and intended combat winner ID.");
var connectionString = Environment.GetEnvironmentVariable("CT_TEST_CONNECTION_STRING")
    ?? throw new InvalidOperationException("Configure the isolated verification database.");
await using var source = NpgsqlDataSource.Create(connectionString);
await using var connection = await source.OpenConnectionAsync();
await using var transaction = await connection.BeginTransactionAsync();
await using var read = new NpgsqlCommand("SELECT state::text FROM matches WHERE id = $1 FOR UPDATE", connection, transaction);
read.Parameters.AddWithValue(matchId);
var json = await read.ExecuteScalarAsync() as string ?? throw new InvalidOperationException("Verification match not found.");
var serializer = new WorldSnapshotSerializer();
var snapshot = serializer.Deserialize(json);
if (snapshot.Battles.Length != 0 || snapshot.Armies.Any(a => a.Movement != null) ||
    snapshot.Armies.Select(a => a.OwnerId).Distinct().Count() != 2 || !snapshot.Armies.Any(a => a.OwnerId == winner))
    throw new InvalidOperationException("Fixture must be a fresh isolated two-player match before movement/combat.");
// Different DPS makes a winner reproducible. All deaths/result still come from the real simulator.
snapshot = snapshot with { Armies = snapshot.Armies.Select(a => a with
{ Units = a.Units.Select(u => u with { DamagePerSecond = a.OwnerId == winner ? 20m : 5m }).ToImmutableArray() }).ToImmutableArray() };
await using var update = new NpgsqlCommand("UPDATE matches SET state = $1, version = version + 1 WHERE id = $2", connection, transaction);
update.Parameters.AddWithValue(NpgsqlDbType.Jsonb, serializer.Serialize(snapshot));
update.Parameters.AddWithValue(matchId);
await update.ExecuteNonQueryAsync();
await transaction.CommitAsync();
Console.WriteLine("Verification-only combat stats configured; no phase/winner was written.");
