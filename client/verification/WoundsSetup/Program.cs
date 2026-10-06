// Isolated verification fixture only. No API endpoint or Unity runtime assembly exposes it.
using System.Collections.Immutable;
using ConquestusTerrarum.Domain.World;
using ConquestusTerrarum.Persistence;
using Npgsql;
using NpgsqlTypes;

if (args.Length != 3 || !Guid.TryParse(args[0], out var matchId) || !Guid.TryParse(args[1], out var first) ||
    !Guid.TryParse(args[2], out var second) || first == second)
    throw new ArgumentException("Pass the isolated world ID and its two public player IDs.");
await using var source = NpgsqlDataSource.Create(Environment.GetEnvironmentVariable("CT_TEST_CONNECTION_STRING")
    ?? throw new InvalidOperationException("Configure the local verification database."));
await using var connection = await source.OpenConnectionAsync();
await using var transaction = await connection.BeginTransactionAsync();
await using var read = new NpgsqlCommand("SELECT state::text FROM matches WHERE id = $1 FOR UPDATE", connection, transaction);
read.Parameters.AddWithValue(matchId);
var serializer = new WorldSnapshotSerializer();
var snapshot = serializer.Deserialize(await read.ExecuteScalarAsync() as string
    ?? throw new InvalidOperationException("Verification world not found."));
if (snapshot.Battles.Length != 0 || snapshot.Armies.Any(a => a.Movement != null || a.IsInBattle) ||
    !snapshot.Armies.Select(a => a.OwnerId).Distinct().Order().SequenceEqual(new[] { first, second }.Order()) ||
    snapshot.Armies.Any(a => a.Units.Length != 3 || a.Units.Any(u => u.CurrentHitPoints != u.MaxHitPoints)))
    throw new InvalidOperationException("Fixture requires a fresh isolated two-player world before orders/combat.");
// Two slots engage two of three living units on each side, leaving one healthy reserve.
// Low DPS/high HP provide a stable observation window; all consequences use real combat/retreat code.
snapshot = snapshot with { Armies = snapshot.Armies.Select(a => a with
{
    Units = a.Units.Select(u => u with { MaxHitPoints = 10000, CurrentHitPoints = 10000, DamagePerSecond = 1 })
        .Append(new UnitSnapshot(Guid.NewGuid(), 10000, 0, 1)).ToImmutableArray()
}).ToImmutableArray() };
await using var update = new NpgsqlCommand("UPDATE matches SET state = $1, version = version + 1 WHERE id = $2", connection, transaction);
update.Parameters.AddWithValue(NpgsqlDbType.Jsonb, serializer.Serialize(snapshot));
update.Parameters.AddWithValue(matchId);
await update.ExecuteNonQueryAsync();
await transaction.CommitAsync();
Console.WriteLine("Verification-only HP/DPS/dead-unit fixture configured; participation and battle outcomes untouched.");
