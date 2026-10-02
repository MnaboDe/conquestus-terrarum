using ConquestusTerrarum.Domain.World;

namespace ConquestusTerrarum.Persistence;

/// <summary>
/// Transactional match operations plus durable client change-feed reads.
/// </summary>
public interface IMatchRepository
{
    Task CreateAsync(
        Guid matchId,
        Func<DateTimeOffset, WorldState> worldFactory,
        CancellationToken cancellationToken = default);

    Task<MatchExecutionResult<WorldSnapshot>> LoadCurrentAsync(
        Guid matchId,
        CancellationToken cancellationToken = default);

    Task<MatchExecutionResult<T>> ExecuteAsync<T>(
        Guid matchId,
        Func<WorldState, DateTimeOffset, Task<T>> operation,
        CancellationToken cancellationToken = default);

    Task<MatchUpdatesPage> ReadUpdatesAsync(
        Guid matchId,
        long after,
        int limit = 100,
        CancellationToken cancellationToken = default);
}