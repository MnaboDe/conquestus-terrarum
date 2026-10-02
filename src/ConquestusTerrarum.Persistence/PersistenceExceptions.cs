namespace ConquestusTerrarum.Persistence;

public sealed class MatchNotFoundException(Guid matchId)
    : Exception($"Match '{matchId}' was not found.")
{
    public Guid MatchId { get; } = matchId;
}

public sealed class InvalidWorldStateException(string message, Exception? innerException = null)
    : Exception(message, innerException);

public sealed class DatabaseTimeBehindWorldException(DateTimeOffset databaseTime, DateTimeOffset checkpoint)
    : Exception(
        $"Database time '{databaseTime:O}' precedes world checkpoint '{checkpoint:O}'. The operation was not applied.");