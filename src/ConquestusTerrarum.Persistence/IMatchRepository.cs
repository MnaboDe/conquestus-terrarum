using ConquestusTerrarum.Domain.World;

namespace ConquestusTerrarum.Persistence;

/// <summary>Transactional match operations; production creation starts at the supplied database time.</summary>
public interface IMatchRepository
{
    Task CreateAsync(Guid matchId, Func<DateTimeOffset, WorldState> worldFactory, CancellationToken cancellationToken = default);
    Task<MatchExecutionResult<WorldSnapshot>> LoadCurrentAsync(Guid matchId, CancellationToken cancellationToken = default);
    Task<MatchExecutionResult<T>> ExecuteAsync<T>(Guid matchId, Func<WorldState, DateTimeOffset, Task<T>> operation,
        CancellationToken cancellationToken = default);
}
