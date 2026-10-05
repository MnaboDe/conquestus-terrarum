using System.Net;
using System.Net.Http.Json;
using System.Net.Http.Headers;
using ConquestusTerrarum.Api.Sessions;
using ConquestusTerrarum.Api.Contracts;
using ConquestusTerrarum.Persistence;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Npgsql;
using static ConquestusTerrarum.Testing.SnapshotWorlds;

namespace ConquestusTerrarum.Api.Tests;

public sealed class ApiPostgresFactAttribute : FactAttribute
{
    public ApiPostgresFactAttribute()
    {
        if (string.IsNullOrWhiteSpace(Environment.GetEnvironmentVariable("CT_TEST_CONNECTION_STRING")))
            Skip = "Set CT_TEST_CONNECTION_STRING to run PostgreSQL API end-to-end tests.";
    }
}

public sealed class ApiPostgresTheoryAttribute : TheoryAttribute
{
    public ApiPostgresTheoryAttribute()
    {
        if (string.IsNullOrWhiteSpace(Environment.GetEnvironmentVariable("CT_TEST_CONNECTION_STRING")))
            Skip = "Set CT_TEST_CONNECTION_STRING to run PostgreSQL API end-to-end tests.";
    }
}

public sealed class ApiPostgresFixture : IAsyncLifetime
{
    private readonly string _schema = "ct_api_test_" + Guid.NewGuid().ToString("N");
    private string? _baseConnectionString;
    public string ConnectionString { get; private set; } = null!;
    public NpgsqlDataSource DataSource { get; private set; } = null!;

    public async Task InitializeAsync()
    {
        _baseConnectionString = Environment.GetEnvironmentVariable("CT_TEST_CONNECTION_STRING");
        if (string.IsNullOrWhiteSpace(_baseConnectionString)) return;
        await using var connection = new NpgsqlConnection(_baseConnectionString);
        await connection.OpenAsync();
        await using var command = new NpgsqlCommand($"CREATE SCHEMA \"{_schema}\"", connection);
        await command.ExecuteNonQueryAsync();
        ConnectionString = new NpgsqlConnectionStringBuilder(_baseConnectionString) { SearchPath = _schema }
            .ConnectionString;
        DataSource = NpgsqlDataSource.Create(ConnectionString);
        await new PostgresMigrator(DataSource).MigrateAsync();
    }

    public async Task DisposeAsync()
    {
        if (DataSource is null) return;
        await DataSource.DisposeAsync();
        await using var connection = new NpgsqlConnection(_baseConnectionString);
        await connection.OpenAsync();
        await using var command = new NpgsqlCommand($"DROP SCHEMA \"{_schema}\" CASCADE", connection);
        await command.ExecuteNonQueryAsync();
    }
}

internal sealed class PostgresApiFactory(string connectionString) : WebApplicationFactory<Program>
{
    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        builder.UseEnvironment("Development");
        builder.ConfigureLogging(logging => logging.ClearProviders().AddConsole());
        builder.ConfigureAppConfiguration((_, config) => config.AddInMemoryCollection(
            new Dictionary<string, string?> { ["ConnectionStrings:Postgres"] = connectionString }));
    }
}

[Trait("Category", "Postgres")]
public sealed class ApiPostgresTests(ApiPostgresFixture database) : IClassFixture<ApiPostgresFixture>
{
    private static async Task<MatchStateResponse> State(HttpResponseMessage response)
    {
        response.EnsureSuccessStatusCode();
        return (await response.Content.ReadFromJsonAsync<MatchStateResponse>(MatchEndpointTests.JsonOptions))!;
    }

    [ApiPostgresFact]
    [Trait("Category", "PostgresApi")]
    public async Task DevelopmentCreateGetMoveAndArrivalUseAuthoritativeDatabaseTime()
    {
        await using var factory = new PostgresApiFactory(database.ConnectionString);
        using var client = factory.CreateClient();
        Assert.Same(factory.Services.GetRequiredService<NpgsqlDataSource>(),
            factory.Services.GetRequiredService<NpgsqlDataSource>());
        Assert.IsType<PostgresMatchRepository>(factory.Services.GetRequiredService<IMatchRepository>());
        var create = await client.PostAsync("/api/v1/dev/matches", null);
        Assert.Equal(HttpStatusCode.Created, create.StatusCode);
        var initial = await State(create);
        Assert.Equal(0, initial.Version);
        Assert.Equal(initial.ServerTime, initial.CheckpointTime);
        var initialRow = await Row(initial.MatchId);
        Assert.Equal(0, initialRow.Version);
        Assert.Equal(initial.ServerTime, initialRow.Created);
        Assert.Equal(initial.ServerTime, new WorldSnapshotSerializer().Deserialize(initialRow.Json).CheckpointTime);

        var path = $"/api/v1/matches/{initial.MatchId}";
        var movingOwner = initial.Armies.First(a => a.LocationId == initial.Locations.Single(l => l.Name == "West camp").Id).OwnerId;
        await Authenticate(client, initial.MatchId, movingOwner);
        var current = await State(await client.GetAsync(path));
        Assert.Equal(1, current.Version);
        var choice = (from army in current.Armies
            from connection in current.Connections
            where connection.TravelTimeTicks == TimeSpan.FromSeconds(5).Ticks &&
                  army.OwnerId == movingOwner &&
                  (army.LocationId == connection.FromLocationId || army.LocationId == connection.ToLocationId)
            select (Army: army, Connection: connection)).First();
        var destination = choice.Army.LocationId == choice.Connection.FromLocationId
            ? choice.Connection.ToLocationId
            : choice.Connection.FromLocationId;
        var moved = await State(await client.PostAsJsonAsync($"{path}/armies/{choice.Army.Id}/move",
            new { destinationLocationId = destination }));
        Assert.Equal(2, moved.Version);
        var moving = moved.Armies.Single(a => a.Id == choice.Army.Id);
        Assert.True(moving.IsMoving);
        Assert.Null(moving.LocationId);
        Assert.Equal(moved.ServerTime, moving.Movement!.StartedAt);
        Assert.Equal(moved.ServerTime.AddTicks(choice.Connection.TravelTimeTicks), moving.Movement.ArriveAt);
        Assert.Equal(2, (await Row(initial.MatchId)).Version);

        // Wait only inside this optional test, until PostgreSQL reaches the movement boundary.
        // There is no API worker or application-clock-based simulation.
        await using var wait = database.DataSource.CreateCommand(
            "SELECT pg_sleep(GREATEST(0, EXTRACT(EPOCH FROM ($1::timestamptz - clock_timestamp()))) + 0.01)");
        wait.Parameters.AddWithValue(moving.Movement.ArriveAt);
        await wait.ExecuteNonQueryAsync();
        var arrived = await State(await client.GetAsync(path));
        Assert.Equal(3, arrived.Version);
        var armyAfter = arrived.Armies.Single(a => a.Id == choice.Army.Id);
        Assert.False(armyAfter.IsMoving);
        Assert.Equal(destination, armyAfter.LocationId);
        Assert.Equal(moving.Movement.ArriveAt, armyAfter.EnteredLocationAt);
        Assert.True(arrived.ServerTime >= moving.Movement.ArriveAt);
    }

    [ApiPostgresFact]
    [Trait("Category", "PostgresApi")]
    public async Task RetreatReturnsTheSameCommittedStateAndDatabaseTimestamp()
    {
        var id = Guid.NewGuid();
        await new PostgresMatchRepository(database.DataSource).CreateAsync(id, Retreat());
        await using var factory = new PostgresApiFactory(database.ConnectionString);
        using var client = factory.CreateClient();
        await Authenticate(client, id, Id(21));
        var response = await State(await client.PostAsync($"/api/v1/matches/{id}/armies/{Id(11)}/retreat", null));
        var army = response.Armies.Single(a => a.Id == Id(11));
        Assert.Equal(1, response.Version);
        Assert.Equal(response.ServerTime, army.Movement!.StartedAt);
        Assert.Equal(MovementKindResponse.Retreat, army.Movement.Kind);
        Assert.False(army.Units.Single(u => u.Id == Id(101)).IsAlive);
        var saved = await Row(id);
        var snapshot = new WorldSnapshotSerializer().Deserialize(saved.Json);
        Assert.Equal(response.Version, saved.Version);
        Assert.Equal(response.ServerTime, snapshot.CheckpointTime);
        Assert.Equal(response.ServerTime, snapshot.Armies.Single(a => a.Id == Id(11)).Movement!.StartedAt);
    }

    [ApiPostgresFact]
    [Trait("Category", "PostgresApi")]
    public async Task RejectedHttpCommandRollsBackLazyAdvanceAndVersion()
    {
        var id = Guid.NewGuid();
        await new PostgresMatchRepository(database.DataSource).CreateAsync(id, Retreat());
        var before = await Row(id);
        await using var factory = new PostgresApiFactory(database.ConnectionString);
        using var client = factory.CreateClient();
        await Authenticate(client, id, Id(21));
        await MatchEndpointTests.AssertProblem(
            await client.PostAsJsonAsync($"/api/v1/matches/{id}/armies/{Id(11)}/move",
                new { destinationLocationId = Id(2) }), 409);
        Assert.Equal(before, await Row(id));
        var current = await State(await client.GetAsync($"/api/v1/matches/{id}"));
        Assert.Equal(1, current.Version);
    }

    [ApiPostgresFact]
    [Trait("Category", "PostgresApi")]
    public async Task RepeatedBattleDetailsReturnLiveHpWithoutWritingSnapshotOrFeed()
    {
        var id = Guid.NewGuid();
        await new PostgresMatchRepository(database.DataSource).CreateAsync(id, databaseTime =>
            new ConquestusTerrarum.Domain.World.WorldState(
                new ConquestusTerrarum.Domain.World.GameMap([new(Id(1), "Battlefield")], []),
                [Army(11, 21, 1, Unit(101, hp: 100000, dps: 10)), Army(12, 22, 1, Unit(102, hp: 100000, dps: 10))],
                databaseTime));
        await using var factory = new PostgresApiFactory(database.ConnectionString);
        using var client = factory.CreateClient();
        await Authenticate(client, id, Id(21));
        var before = await Storage();
        decimal hp = 100000m;
        for (var attempt = 0; attempt < 3; attempt++)
        {
            await using var delay = database.DataSource.CreateCommand("SELECT pg_sleep(0.03)");
            await delay.ExecuteNonQueryAsync();
            var response = await client.GetAsync($"/api/v1/matches/{id}/armies/{Id(11)}/details");
            response.EnsureSuccessStatusCode();
            var details = (await response.Content.ReadFromJsonAsync<ArmyDetailsResponse>())!;
            Assert.True(details.IsInBattle);
            Assert.True(details.Units.Single().CurrentHitPoints < hp);
            hp = details.Units.Single().CurrentHitPoints;
            Assert.Equal(before, await Storage());
        }

        async Task<(string Json, long Version, DateTimeOffset Updated, long Sequence, string RowTransaction, long Entries)> Storage()
        {
            await using var command = database.DataSource.CreateCommand("""
                SELECT state::text, version, updated_at, next_update_sequence, xmin::text,
                    (SELECT count(*) FROM match_updates WHERE match_id = $1)
                FROM matches WHERE id = $1
                """);
            command.Parameters.AddWithValue(id);
            await using var reader = await command.ExecuteReaderAsync();
            Assert.True(await reader.ReadAsync());
            return (reader.GetString(0), reader.GetInt64(1), new DateTimeOffset(reader.GetDateTime(2)),
                reader.GetInt64(3), reader.GetString(4), reader.GetInt64(5));
        }
    }

    private async Task Authenticate(HttpClient client, Guid matchId, Guid ownerId)
    {
        var token = PlayerCredentials.GenerateToken();
        // Existing gameplay fixtures are active two-player worlds, not waiting-session tests.
        var snapshot = new WorldSnapshotSerializer().Deserialize((await Row(matchId)).Json);
        var opponent = snapshot.Armies.First(a => a.OwnerId != ownerId).OwnerId;
        await using var command = database.DataSource.CreateCommand("""
            WITH session AS (
                INSERT INTO match_sessions (match_id, join_code, created_at) VALUES ($1, $2, clock_timestamp())
                RETURNING match_id
            )
            INSERT INTO match_players (match_id, slot, player_id, token_hash, joined_at)
            SELECT session.match_id, player.slot, player.id, player.hash, player.joined_at
            FROM session CROSS JOIN (VALUES (1, $3, $4, clock_timestamp()), (2, $5, $6, clock_timestamp()))
                AS player(slot, id, hash, joined_at)
            """);
        command.Parameters.AddWithValue(matchId);
        command.Parameters.AddWithValue(PlayerCredentials.GenerateJoinCode());
        command.Parameters.AddWithValue(ownerId);
        command.Parameters.AddWithValue(PlayerCredentials.HashToken(token));
        command.Parameters.AddWithValue(opponent);
        command.Parameters.AddWithValue(PlayerCredentials.HashToken(PlayerCredentials.GenerateToken()));
        await command.ExecuteNonQueryAsync();
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);
    }

    private async Task<(string Json, long Version, DateTimeOffset Created)> Row(Guid matchId)
    {
        await using var command =
            database.DataSource.CreateCommand("SELECT state::text, version, created_at FROM matches WHERE id = $1");
        command.Parameters.AddWithValue(matchId);
        await using var reader = await command.ExecuteReaderAsync();
        Assert.True(await reader.ReadAsync());
        return (reader.GetString(0), reader.GetInt64(1), new DateTimeOffset(reader.GetDateTime(2)));
    }
}
