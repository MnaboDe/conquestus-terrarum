using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using Npgsql;

namespace ConquestusTerrarum.Persistence;

public sealed class PostgresMigrator(NpgsqlDataSource dataSource)
{
    public async Task MigrateAsync(CancellationToken cancellationToken = default)
    {
        await using var connection = await dataSource.OpenConnectionAsync(cancellationToken);
        await using var transaction = await connection.BeginTransactionAsync(cancellationToken);
        // Serialize concurrent migrators, including creation of the migration history table.
        await using (var command = new NpgsqlCommand("""
                                                     SELECT pg_advisory_xact_lock(485018481923);
                                                     CREATE TABLE IF NOT EXISTS schema_migrations (
                                                         version integer PRIMARY KEY,
                                                         name text NOT NULL,
                                                         checksum text NOT NULL,
                                                         applied_at timestamptz NOT NULL DEFAULT clock_timestamp()
                                                     );
                                                     """, connection, transaction))
        {
            await command.ExecuteNonQueryAsync(cancellationToken);
        }

        var assembly = typeof(PostgresMigrator).Assembly;
        foreach (var resource in assembly.GetManifestResourceNames()
                     .Where(n => n.StartsWith("Migrations.", StringComparison.Ordinal)).Order(StringComparer.Ordinal))
        {
            var name = resource["Migrations.".Length..];
            var version = int.Parse(name.Split('_')[0], CultureInfo.InvariantCulture);
            using var reader = new StreamReader(assembly.GetManifestResourceStream(resource)!);
            // Git may check SQL out with CRLF or LF; migration identity must be portable.
            var sql = (await reader.ReadToEndAsync(cancellationToken)).Replace("\r\n", "\n");
            var checksum = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(sql)));
            await using (var check = new NpgsqlCommand("SELECT checksum FROM schema_migrations WHERE version = $1",
                             connection, transaction))
            {
                check.Parameters.AddWithValue(version);
                var previous = await check.ExecuteScalarAsync(cancellationToken);
                if (previous is string saved)
                {
                    if (saved != checksum)
                        throw new InvalidOperationException($"Applied migration {name} has changed.");
                    continue;
                }
            }

            await using (var apply = new NpgsqlCommand(sql, connection, transaction))
            {
                await apply.ExecuteNonQueryAsync(cancellationToken);
            }

            await using (var record =
                         new NpgsqlCommand(
                             "INSERT INTO schema_migrations (version, name, checksum) VALUES ($1, $2, $3)", connection,
                             transaction))
            {
                record.Parameters.AddWithValue(version);
                record.Parameters.AddWithValue(name);
                record.Parameters.AddWithValue(checksum);
                await record.ExecuteNonQueryAsync(cancellationToken);
            }
        }

        await transaction.CommitAsync(cancellationToken);
    }
}