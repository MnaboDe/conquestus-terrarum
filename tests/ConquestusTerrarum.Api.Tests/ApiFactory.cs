using ConquestusTerrarum.Domain.World;
using ConquestusTerrarum.Persistence;
using ConquestusTerrarum.Testing;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace ConquestusTerrarum.Api.Tests;

internal sealed class ApiFactory(RecordingMatchRepository repository, string environment = "Development")
    : WebApplicationFactory<Program>
{
    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        builder.UseEnvironment(environment);
        builder.ConfigureServices(services =>
        {
            services.RemoveAll<IMatchRepository>();
            services.AddSingleton<IMatchRepository>(repository);
        });
    }
}

internal sealed class RecordingMatchRepository(WorldState world, DateTimeOffset databaseTime) : IMatchRepository
{
    public Guid MatchId { get; private set; } = SnapshotWorlds.Id(9000);
    public DateTimeOffset DatabaseTime { get; } = databaseTime;
    public WorldSnapshot Stored { get; private set; } = world.CreateSnapshot();
    public long Version { get; private set; } = 40;
    public int Executions { get; private set; }
    public int Loads { get; private set; }
    public int Creations { get; private set; }
    public Exception? Failure { get; set; }
    public Func<CancellationToken, Task>? BeforeExecution { get; set; }
    public CancellationToken LastCancellationToken { get; private set; }

    public List<MatchUpdateEntry> Updates { get; } = [];

    public int UpdateReads { get; private set; }
    
    public int Queries { get; private set; }

    public Task CreateAsync(Guid matchId, Func<DateTimeOffset, WorldState> worldFactory,
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        LastCancellationToken = cancellationToken;
        if (Failure is { } error) throw error;
        var created = worldFactory(DatabaseTime);
        Assert.Equal(DatabaseTime, created.CheckpointTime);
        MatchId = matchId;
        Stored = created.CreateSnapshot();
        Version = 0;
        Creations++;
        return Task.CompletedTask;
    }

    public Task<MatchExecutionResult<WorldSnapshot>> LoadCurrentAsync(Guid matchId,
        CancellationToken cancellationToken = default)
    {
        Loads++;
        return ExecuteAsync(matchId, (current, _) => Task.FromResult(current.CreateSnapshot()), cancellationToken);
    }

    public async Task<MatchExecutionResult<T>> ExecuteAsync<T>(Guid matchId,
        Func<WorldState, DateTimeOffset, Task<T>> operation,
        CancellationToken cancellationToken = default)
    {
        LastCancellationToken = cancellationToken;
        cancellationToken.ThrowIfCancellationRequested();
        Executions++;
        if (BeforeExecution is not null) await BeforeExecution(cancellationToken);
        if (Failure is { } error) throw error;
        if (matchId != MatchId) throw new MatchNotFoundException(matchId);
        var working = WorldState.Restore(Stored);
        WorldSimulator.AdvanceTo(working, DatabaseTime);
        var value = await operation(working, DatabaseTime);
        cancellationToken.ThrowIfCancellationRequested();
        Stored = working.CreateSnapshot();
        return new MatchExecutionResult<T>(value, ++Version, DatabaseTime);
    }
    
    public Task<MatchExecutionResult<T>> QueryCurrentAsync<T>(
        Guid matchId,
        Func<WorldSnapshot, DateTimeOffset, T> query,
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();

        LastCancellationToken =
            cancellationToken;

        Queries++;

        if (Failure is { } error)
            throw error;

        if (matchId != MatchId)
            throw new MatchNotFoundException(matchId);

        var working =
            WorldState.Restore(Stored);

        WorldSimulator.AdvanceTo(
            working,
            DatabaseTime);

        var snapshot =
            working.CreateSnapshot();

        var value =
            query(
                snapshot,
                DatabaseTime);

        return Task.FromResult(
            new MatchExecutionResult<T>(
                value,
                Version,
                DatabaseTime));
    }

    public Task<MatchUpdatesPage> ReadUpdatesAsync(
        Guid matchId,
        long after,
        int limit = 100,
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();

        LastCancellationToken =
            cancellationToken;

        UpdateReads++;

        if (Failure is { } error)
            throw error;

        if (matchId != MatchId)
            throw new MatchNotFoundException(matchId);

        var matching =
            Updates
                .Where(update =>
                    update.Sequence > after)
                .OrderBy(update =>
                    update.Sequence)
                .Take(limit + 1)
                .ToList();

        var hasMore =
            matching.Count > limit;

        if (hasMore)
            matching.RemoveAt(
                matching.Count - 1);

        var nextCursor =
            matching.Count == 0
                ? after
                : matching[^1].Sequence;

        return Task.FromResult(
            new MatchUpdatesPage(
                nextCursor,
                hasMore,
                matching));
    }
}