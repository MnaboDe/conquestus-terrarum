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
    [InlineData(MatchPhase.WaitingForPlayers, "Waiting for second player")]
    [InlineData(MatchPhase.Active, "Game Active")]
    public void ActualServerContractRestoresParticipationAndCommandEligibility(MatchPhase phase, string label)
    {
        var response = new MatchStateResponse(Id(90), 4, 27, Start, Start, [], [], [], [], [], phase);
        var json = JsonSerializer.Serialize(response, Options);
        using var contract = JsonDocument.Parse(json);
        Assert.False(contract.RootElement.TryGetProperty("winnerPlayerId", out _));
        Assert.False(contract.RootElement.TryGetProperty("isDraw", out _));
        var state = JsonSerializer.Deserialize<MatchStateDto>(json, Options)!;
        Assert.True(ClientMatchLifecycle.Valid(state));
        Assert.Equal(label, ClientMatchLifecycle.Label(state));
        Assert.Equal(27, state.updateCursor);
        var own = new ArmyDto { ownerId = Id(21).ToString() };
        Assert.Equal(phase == MatchPhase.Active, ClientMatchLifecycle.CanCommand(state, Session(), own));
        Assert.False(ClientMatchLifecycle.CanCommand(state, Session(), new ArmyDto { ownerId = Id(22).ToString() }));
        Assert.False(ClientMatchLifecycle.CanCommand(state, null, own));
        state.matchId = Id(91).ToString();
        Assert.False(ClientMatchLifecycle.CanCommand(state, Session(), own));
        state.matchId = Id(90).ToString();
        own.isDestroyed = true;
        Assert.False(ClientMatchLifecycle.CanCommand(state, Session(), own));
        Assert.Equal(label, ClientMatchLifecycle.Label(state));
    }

    [Theory]
    [InlineData(null)] [InlineData("Active")] [InlineData("finished")] [InlineData("unknown")]
    public void UnknownParticipationResponsesFailClosed(string? phase)
    {
        Assert.False(ClientMatchLifecycle.Valid(new MatchStateDto { phase = phase }));
        Assert.False(ClientMatchLifecycle.Valid(null));
    }

    [Theory]
    [InlineData("MatchStarted")]
    [InlineData("MatchFinished")] // Obsolete persisted data follows the ordinary unknown-event path.
    [InlineData("UnknownEvent")]
    public void UnsafePageLeavesStateAndCursorUnchangedForOneResync(string kind)
    {
        var state = new MatchStateDto { phase = "waitingForPlayers", updateCursor = 3,
            armies = [], locations = [new() { id = Id(1).ToString() }], connections = [], alliances = [], activeBattles = [] };
        var page = new MatchUpdatesDto { updates = [new() { sequence = 4, type = "LocationOwnerChanged",
                occurredAt = Start.ToString("O"), payload = new() { locationId = Id(1).ToString(), ownerId = Id(21).ToString() } },
            new() { sequence = 5, type = kind, occurredAt = Start.ToString("O"), payload = new() }], nextCursor = 5 };
        Assert.False(MatchUpdateApplier.TryApplyPage(state, page, 3, out var unchanged, out var cursor));
        Assert.Same(state, unchanged);
        Assert.Null(state.locations[0].ownerId);
        Assert.Equal(3, cursor);
    }

    [Fact]
    public void SafeIncrementalPagePreservesActiveParticipationWithNoArmies()
    {
        var state = new MatchStateDto { phase = "active", updateCursor = 3,
            armies = [], locations = [new() { id = Id(1).ToString() }], connections = [], alliances = [], activeBattles = [] };
        var page = new MatchUpdatesDto { nextCursor = 4, updates = [new() { sequence = 4,
            type = "LocationOwnerChanged", occurredAt = Start.ToString("O"), payload = new() { locationId = Id(1).ToString(), ownerId = Id(21).ToString() } }] };
        Assert.True(MatchUpdateApplier.TryApplyPage(state, page, 3, out var applied, out var cursor));
        Assert.Equal("Game Active", ClientMatchLifecycle.Label(applied));
        Assert.Empty(applied.armies);
        Assert.Equal(4, cursor);
    }
}
