using ConquestusTerrarum.Domain.World;

namespace ConquestusTerrarum.Persistence;

public sealed record MatchSessionCreation(
    Guid MatchId, Guid FirstPlayerId, Guid SecondPlayerId, byte[] FirstTokenHash, string JoinCode);

public sealed record MatchPlayerIdentity(Guid MatchId, Guid PlayerId);

public interface IMatchSessionRepository
{
    Task CreateAsync(MatchSessionCreation session, Func<DateTimeOffset, WorldState> worldFactory,
        CancellationToken cancellationToken = default);

    Task<MatchPlayerIdentity> JoinAsync(string joinCode, byte[] tokenHash,
        CancellationToken cancellationToken = default);

    Task<Guid?> ResolvePlayerAsync(Guid matchId, byte[] tokenHash,
        CancellationToken cancellationToken = default);
}

public sealed class JoinCodeCollisionException() : Exception("Join code is already in use.");
public sealed class JoinCodeNotFoundException() : Exception("Join code was not found.");
public sealed class MatchFullException() : Exception("Both player slots are already claimed.");
