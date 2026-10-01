using ConquestusTerrarum.Domain.World;
using Npgsql;

namespace ConquestusTerrarum.Persistence.Tests;

public class PostgresMatchRepositoryValidationTests
{
    [Fact]
    public async Task EmptyMatchIdIsRejectedByEveryOperationBeforeConnecting()
    {
        await using var source = NpgsqlDataSource.Create("Host=127.0.0.1;Port=1;Database=unused;Username=unused");
        var repository = new PostgresMatchRepository(source);
        var world = new WorldState(new GameMap([], []), [], DateTimeOffset.UnixEpoch);
        Assert.Equal("matchId", (await Assert.ThrowsAsync<ArgumentException>(() => repository.CreateAsync(Guid.Empty, world))).ParamName);
        Assert.Equal("matchId", (await Assert.ThrowsAsync<ArgumentException>(() => repository.CreateAsync(Guid.Empty, _ => world))).ParamName);
        Assert.Equal("matchId", (await Assert.ThrowsAsync<ArgumentException>(() => repository.ExecuteAsync(Guid.Empty, (_, _) => Task.FromResult(0)))).ParamName);
        Assert.Equal("matchId", (await Assert.ThrowsAsync<ArgumentException>(() => repository.LoadCurrentAsync(Guid.Empty))).ParamName);
    }

    [Fact]
    public async Task NullFactoryIsRejectedBeforeConnecting()
    {
        await using var source = NpgsqlDataSource.Create("Host=127.0.0.1;Port=1;Database=unused;Username=unused");
        var repository = new PostgresMatchRepository(source);
        Assert.Equal("worldFactory", (await Assert.ThrowsAsync<ArgumentNullException>(() =>
            repository.CreateAsync(Guid.NewGuid(), (Func<DateTimeOffset, WorldState>)null!))).ParamName);
    }
}
