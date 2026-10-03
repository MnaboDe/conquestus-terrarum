using ConquestusTerrarum.Domain.World;
using ConquestusTerrarum.Persistence;
using ConquestusTerrarum.Api.Sessions;
using System.Net.Http.Headers;
using ConquestusTerrarum.Testing;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Logging;

namespace ConquestusTerrarum.Api.Tests;

internal sealed class ApiFactory(RecordingMatchRepository repository, string environment = "Development")
    : WebApplicationFactory<Program>
{
    public RecordingSessionRepository Sessions { get; } = new(repository);

    protected override void ConfigureClient(HttpClient client)
    {
        base.ConfigureClient(client);
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", RecordingSessionRepository.TestToken);
    }

    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        builder.UseEnvironment(environment);
        builder.ConfigureLogging(logging => logging.ClearProviders().AddConsole());
        builder.ConfigureServices(services =>
        {
            services.RemoveAll<IMatchRepository>();
            services.AddSingleton<IMatchRepository>(repository);
            services.RemoveAll<IMatchSessionRepository>();
            services.AddSingleton<IMatchSessionRepository>(Sessions);
        });
    }
}

internal sealed class RecordingSessionRepository(RecordingMatchRepository matches) : IMatchSessionRepository
{
    public const string TestToken = "AAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAA";
    private MatchSessionCreation? _session;
    private byte[]? _secondHash;
    public int CollisionsRemaining { get; set; }
    public int CreationAttempts { get; private set; }

    public async Task CreateAsync(MatchSessionCreation session, Func<DateTimeOffset, WorldState> worldFactory,
        CancellationToken cancellationToken = default)
    {
        CreationAttempts++;
        if (CollisionsRemaining-- > 0) throw new JoinCodeCollisionException();
        await matches.CreateAsync(session.MatchId, worldFactory, cancellationToken);
        _session = session;
        _secondHash = null;
    }

    public Task<MatchPlayerIdentity> JoinAsync(string joinCode, byte[] tokenHash,
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        if (_session?.JoinCode != joinCode) throw new JoinCodeNotFoundException();
        if (_secondHash is not null) throw new MatchFullException();
        _secondHash = tokenHash;
        return Task.FromResult(new MatchPlayerIdentity(_session.MatchId, _session.SecondPlayerId));
    }

    public Task<Guid?> ResolvePlayerAsync(Guid matchId, byte[] tokenHash,
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        if (matchId != matches.MatchId) throw new MatchNotFoundException(matchId);
        Guid? playerId = null;
        if (_session is null && tokenHash.SequenceEqual(PlayerCredentials.HashToken(TestToken))) playerId = SnapshotWorlds.Id(21);
        if (_session is not null && tokenHash.SequenceEqual(_session.FirstTokenHash)) playerId = _session.FirstPlayerId;
        if (_session is not null && _secondHash is not null && tokenHash.SequenceEqual(_secondHash)) playerId = _session.SecondPlayerId;
        return Task.FromResult(playerId);
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
