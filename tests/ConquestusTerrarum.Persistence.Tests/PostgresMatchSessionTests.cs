using System.Security.Cryptography;
using ConquestusTerrarum.Domain.World;
using Npgsql;

namespace ConquestusTerrarum.Persistence.Tests;

[Trait("Category", "Postgres")]
public sealed class PostgresMatchSessionTests(PostgresFixture database) : IClassFixture<PostgresFixture>
{
    private static MatchSessionCreation Session(string code) =>
        new(Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(), SHA256.HashData(RandomNumberGenerator.GetBytes(32)), code);

    private static WorldState World(DateTimeOffset time, MatchSessionCreation session)
    {
        var location = Guid.NewGuid();
        return new WorldState(new GameMap([new Location(location, "Home")], []),
            [new Army(Guid.NewGuid(), session.FirstPlayerId, location, [new Unit(Guid.NewGuid(), 100, 0)]),
             new Army(Guid.NewGuid(), session.SecondPlayerId, location, [new Unit(Guid.NewGuid(), 100, 0)])], time);
    }

    [PostgresFact]
    public async Task CreationReservesTwoIdentities_AndCredentialsSurviveNewDataSources()
    {
        var session = Session("PERS23");
        await new PostgresMatchSessionRepository(database.DataSource).CreateAsync(session, time => World(time, session));
        await using var recreated = NpgsqlDataSource.Create(database.ConnectionString);
        var sessions = new PostgresMatchSessionRepository(recreated);
        Assert.Equal(session.FirstPlayerId, await sessions.ResolvePlayerAsync(session.MatchId, session.FirstTokenHash));
        var hash = SHA256.HashData(RandomNumberGenerator.GetBytes(32));
        var second = await sessions.JoinAsync(session.JoinCode, hash);
        Assert.Equal(new MatchPlayerIdentity(session.MatchId, session.SecondPlayerId), second);
        Assert.Equal(session.SecondPlayerId,
            await new PostgresMatchSessionRepository(database.DataSource).ResolvePlayerAsync(session.MatchId, hash));
        Assert.Null(await sessions.ResolvePlayerAsync(session.MatchId, SHA256.HashData(new byte[32])));
        await Assert.ThrowsAsync<MatchFullException>(() => sessions.JoinAsync(session.JoinCode, SHA256.HashData(new byte[32])));
        await Assert.ThrowsAsync<JoinCodeNotFoundException>(() => sessions.JoinAsync("ABS234", hash));
        await Assert.ThrowsAsync<MatchNotFoundException>(() => sessions.ResolvePlayerAsync(Guid.NewGuid(), hash));
        await using var rows = recreated.CreateCommand("SELECT count(*) FROM match_players WHERE match_id = $1 AND joined_at IS NOT NULL");
        rows.Parameters.AddWithValue(session.MatchId);
        Assert.Equal(2L, await rows.ExecuteScalarAsync());
    }

    [PostgresFact]
    public async Task IndependentDatabaseConnectionsCannotBothClaimTheFinalSlot()
    {
        var session = Session("RACE23");
        await new PostgresMatchSessionRepository(database.DataSource).CreateAsync(session, time => World(time, session));
        // Hold the session lock until contenders have been submitted, so they compete for the same unclaimed slot.
        await using var connection = await database.DataSource.OpenConnectionAsync();
        await using var transaction = await connection.BeginTransactionAsync();
        await using var hold = new NpgsqlCommand("SELECT match_id FROM match_sessions WHERE match_id = $1 FOR UPDATE", connection, transaction);
        hold.Parameters.AddWithValue(session.MatchId);
        await hold.ExecuteScalarAsync();
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(15));
        var contenders = Enumerable.Range(0, 8).Select(async _ =>
        {
            await using var source = NpgsqlDataSource.Create(database.ConnectionString);
            var hash = SHA256.HashData(RandomNumberGenerator.GetBytes(32));
            try
            {
                var player = await new PostgresMatchSessionRepository(source).JoinAsync(session.JoinCode, hash, timeout.Token);
                return (Player: player, Hash: hash);
            }
            catch (MatchFullException) { return (Player: (MatchPlayerIdentity?)null, Hash: hash); }
        }).ToArray();
        await transaction.CommitAsync();
        var results = await Task.WhenAll(contenders);
        var winner = Assert.Single(results, r => r.Player is not null);
        Assert.Equal(session.SecondPlayerId, winner.Player!.PlayerId);
        foreach (var result in results)
        {
            var resolved = await new PostgresMatchSessionRepository(database.DataSource)
                .ResolvePlayerAsync(session.MatchId, result.Hash);
            Assert.Equal(result.Player?.PlayerId, resolved);
        }
    }

    [PostgresFact]
    public async Task SessionInsertFailureRollsBackTheAuthoritativeMatch_AndSlotInvariantsAreConstrained()
    {
        var original = Session("ATMM23");
        var repository = new PostgresMatchSessionRepository(database.DataSource);
        await repository.CreateAsync(original, time => World(time, original));
        var duplicateCode = Session(original.JoinCode);
        await Assert.ThrowsAsync<JoinCodeCollisionException>(() => repository.CreateAsync(duplicateCode,
            time => World(time, duplicateCode)));
        await using var orphan = database.DataSource.CreateCommand("SELECT count(*) FROM matches WHERE id = $1");
        orphan.Parameters.AddWithValue(duplicateCode.MatchId);
        Assert.Equal(0L, await orphan.ExecuteScalarAsync());

        // A player insertion failure must also roll back the already-inserted match and session.
        var duplicateToken = Session("HASH23") with { FirstTokenHash = original.FirstTokenHash };
        var duplicateHashError = await Assert.ThrowsAsync<PostgresException>(() => repository.CreateAsync(duplicateToken,
            time => World(time, duplicateToken)));
        Assert.Equal(PostgresErrorCodes.UniqueViolation, duplicateHashError.SqlState);
        await using var partial = database.DataSource.CreateCommand("""
            SELECT (SELECT count(*) FROM matches WHERE id = $1) +
                (SELECT count(*) FROM match_sessions WHERE match_id = $1) +
                (SELECT count(*) FROM match_players WHERE match_id = $1)
            """);
        partial.Parameters.AddWithValue(duplicateToken.MatchId);
        Assert.Equal(0L, await partial.ExecuteScalarAsync());

        foreach (var sql in new[]
        {
            "INSERT INTO match_players (match_id, slot, player_id) VALUES ($1, 3, $2)",
            "INSERT INTO match_players (match_id, slot, player_id) VALUES ($1, 2, $2)",
            "UPDATE match_players SET player_id = $2 WHERE match_id = $1 AND slot = 2",
            "UPDATE match_players SET token_hash = decode('00', 'hex'), joined_at = clock_timestamp() WHERE match_id = $1 AND slot = 2",
            "UPDATE match_players SET joined_at = clock_timestamp() WHERE match_id = $1 AND slot = 2",
            "UPDATE match_players SET token_hash = NULL, joined_at = NULL WHERE match_id = $1 AND slot = 1"
        })
        {
            await using var invalid = database.DataSource.CreateCommand(sql);
            invalid.Parameters.AddWithValue(original.MatchId);
            if (sql.Contains("$2", StringComparison.Ordinal)) invalid.Parameters.AddWithValue(original.FirstPlayerId);
            var error = await Assert.ThrowsAsync<PostgresException>(() => invalid.ExecuteNonQueryAsync());
            Assert.Contains(error.SqlState, new[] { PostgresErrorCodes.CheckViolation, PostgresErrorCodes.UniqueViolation });
        }
    }
}
