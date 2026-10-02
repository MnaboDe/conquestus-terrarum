using ConquestusTerrarum.Persistence;
using Npgsql;

var connectionString = Environment.GetEnvironmentVariable("CT_CONNECTION_STRING");
if (string.IsNullOrWhiteSpace(connectionString))
{
    Console.Error.WriteLine("Set CT_CONNECTION_STRING before applying migrations. See db/README.md.");
    return 1;
}

await using var dataSource = NpgsqlDataSource.Create(connectionString);
await new PostgresMigrator(dataSource).MigrateAsync();
Console.WriteLine("PostgreSQL migrations are up to date.");
return 0;