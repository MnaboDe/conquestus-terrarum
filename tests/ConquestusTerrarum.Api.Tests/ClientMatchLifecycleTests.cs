using System.Text.Json;
using ConquestusTerrarum.Api.Contracts;
using ConquestusTerrarum.Client;
using ConquestusTerrarum.Domain.World;
using static ConquestusTerrarum.Testing.SnapshotWorlds;

namespace ConquestusTerrarum.Api.Tests;

public sealed class ClientMatchLifecycleTests
{
    private static readonly JsonSerializerOptions Options = new(JsonSerializerDefaults.Web)
    { IncludeFields = true, Converters = { new System.Text.Json.Serialization.JsonStringEnumConverter(JsonNamingPolicy.CamelCase) } };
    private static MultiplayerSession Session() => MultiplayerSession.Restore(Id(90).ToString(), Id(21).ToString(), new string('A', 43), "ABC234");

    [Theory]
    [InlineData(MatchPhase.WaitingForPlayers, 0, false, "Waiting for second player")]
    [InlineData(MatchPhase.Active, 0, false, "Game Active")]
    [InlineData(MatchPhase.Finished, 21, false, "Victory")]
    [InlineData(MatchPhase.Finished, 22, false, "Defeat")]
    [InlineData(MatchPhase.Finished, 0, true, "Draw")]
    public void ActualServerContractRestoresLifecycleResultAndCommandEligibility(MatchPhase phase, int winner, bool draw, string label)
    {
        var response = new MatchStateResponse(Id(90), 4, 27, Start, Start, [], [], [], [], [],
            phase, winner == 0 ? null : Id(winner), draw);
        var state = JsonSerializer.Deserialize<MatchStateDto>(JsonSerializer.Serialize(response, Options), Options)!;
        Assert.True(ClientMatchLifecycle.Valid(state));
        Assert.Equal(label, ClientMatchLifecycle.Label(state, Id(21).ToString().ToUpperInvariant()));
        Assert.Equal(27, state.updateCursor);
        var own = new ArmyDto { ownerId = Id(21).ToString() };
        Assert.Equal(phase == MatchPhase.Active, ClientMatchLifecycle.CanCommand(state, Session(), own));
        Assert.False(ClientMatchLifecycle.CanCommand(state, Session(), new ArmyDto { ownerId = Id(22).ToString() }));
        own.isDestroyed = true;
        Assert.False(ClientMatchLifecycle.CanCommand(state, Session(), own));
    }

    [Theory]
    [InlineData(null, null, false)]
    [InlineData("Finished", null, true)]
    [InlineData("active", "bad", false)]
    [InlineData("finished", null, false)]
    [InlineData("finished", "bad", false)]
    [InlineData("finished", "10000000-0000-0000-0000-000000000001", true)]
    public void InconsistentLifecycleResponsesFailClosed(string? phase, string? winner, bool draw)
    {
        Assert.False(ClientMatchLifecycle.Valid(new MatchStateDto { phase = phase, winnerPlayerId = winner, isDraw = draw }));
    }

    [Fact]
    public void LifecyclePageLeavesStateAndCursorUnchangedForOneResync()
    {
        var state = new MatchStateDto { phase = "finished", winnerPlayerId = Id(21).ToString(), updateCursor = 3,
            armies = [], locations = [], connections = [], alliances = [], activeBattles = [] };
        // Unsafe lifecycle/event pages leave the complete old state/cursor unchanged for one controller resync.
        var page = new MatchUpdatesDto { updates = [new() { sequence = 4, type = "MatchStarted", occurredAt = Start.ToString("O"), payload = new() },
            new() { sequence = 5, type = "MatchFinished", occurredAt = Start.ToString("O"), payload = new() }], nextCursor = 5 };
        Assert.False(MatchUpdateApplier.TryApplyPage(state, page, 3, out var unchanged, out var cursor));
        Assert.Same(state, unchanged);
        Assert.Equal(3, cursor);
    }

    [Fact]
    public void SafeIncrementalPageCopiesAuthoritativeResult()
    {
        var state = new MatchStateDto { phase = "finished", winnerPlayerId = Id(21).ToString(), updateCursor = 3,
            armies = [], locations = [new() { id = Id(1).ToString() }], connections = [], alliances = [], activeBattles = [] };
        var page = new MatchUpdatesDto { nextCursor = 4, updates = [new() { sequence = 4,
            type = "LocationOwnerChanged", occurredAt = Start.ToString("O"), payload = new() { locationId = Id(1).ToString(), ownerId = Id(21).ToString() } }] };
        Assert.True(MatchUpdateApplier.TryApplyPage(state, page, 3, out var applied, out var cursor));
        Assert.Equal("Victory", ClientMatchLifecycle.Label(applied, Id(21).ToString()));
        Assert.Equal(4, cursor);
    }
}
