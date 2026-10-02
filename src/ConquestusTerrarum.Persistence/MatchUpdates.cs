namespace ConquestusTerrarum.Persistence;

/// <summary>
///     One durable entry in the per-match client change feed.
///     PayloadJson contains an event-specific JSON object.
/// </summary>
public sealed record MatchUpdateEntry(
    long Sequence,
    DateTimeOffset OccurredAt,
    string Type,
    string PayloadJson);

/// <summary>
///     One page of change-feed entries.
///     NextCursor is the sequence the client should use as "after"
///     for the next request.
/// </summary>
public sealed record MatchUpdatesPage(
    long NextCursor,
    bool HasMore,
    IReadOnlyList<MatchUpdateEntry> Updates);