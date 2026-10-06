using ConquestusTerrarum.Client;
using ConquestusTerrarum.Client.UI;

namespace ConquestusTerrarum.Api.Tests;

public sealed class UiFlowTests
{
    [Fact]
    public void StartupLeavesBootForMainMenuWithoutAutoConnecting()
    {
        var flow = new UiFlowStateMachine();
        Assert.Equal(ClientUiState.Boot, flow.State);
        Assert.True(flow.Synchronize(0, false, false));
        Assert.Equal(ClientUiState.MainMenu, flow.State);
        Assert.Equal(new[] { "Boot", "MainMenu", "Connecting", "World" }, Enum.GetNames<ClientUiState>());
    }

    [Theory]
    [InlineData("Create")] [InlineData("Join")] [InlineData("Continue")]
    public void SessionOperationsPresentConnectingThenAuthoritativeWorld(string intent)
    {
        Assert.Contains(intent, new[] { "Create", "Join", "Continue" });
        var flow = new UiFlowStateMachine();
        flow.Synchronize(1, false, false);
        Assert.True(flow.Synchronize(2, false, true));
        Assert.Equal(ClientUiState.Connecting, flow.State);
        // The initial GET is still busy while credentials have already been issued/saved.
        Assert.False(flow.Synchronize(3, false, true));
        Assert.True(flow.Synchronize(4, true, false));
        Assert.Equal(ClientUiState.World, flow.State);
    }

    [Fact]
    public void FailedInitialLoadReturnsRecoverableMenuAndRetryCanEnterWorld()
    {
        var flow = new UiFlowStateMachine();
        flow.Synchronize(1, false, true);
        flow.Synchronize(2, false, false);
        Assert.Equal(ClientUiState.MainMenu, flow.State);
        flow.Synchronize(3, false, true);
        Assert.Equal(ClientUiState.Connecting, flow.State);
        flow.Synchronize(4, true, false);
        Assert.Equal(ClientUiState.World, flow.State);
    }

    [Theory]
    [InlineData("waitingForPlayers")] [InlineData("active")]
    public void ParticipationAndZeroArmiesDoNotCreateAnotherScreen(string phase)
    {
        var state = new MatchStateDto { phase = phase, armies = [] };
        Assert.True(ClientMatchLifecycle.Valid(state));
        var flow = new UiFlowStateMachine();
        flow.Synchronize(1, true, false);
        Assert.False(flow.Synchronize(2, true, true)); // Command or activation resync keeps the World root.
        Assert.False(flow.Synchronize(3, true, false));
        Assert.Equal(ClientUiState.World, flow.State);
    }

    [Fact]
    public void BackToMenuRejectsOldCompletionAndForgetCannotBeUndoneByAnOldObservation()
    {
        var flow = new UiFlowStateMachine();
        flow.Synchronize(5, true, false);
        flow.Synchronize(6, false, false);
        Assert.Equal(ClientUiState.MainMenu, flow.State);
        Assert.False(flow.Synchronize(5, true, false));
        Assert.Equal(ClientUiState.MainMenu, flow.State);
        flow.Synchronize(7, false, false);
        Assert.False(flow.Synchronize(6, false, true));
        Assert.Equal(7, flow.LastRevision);
        Assert.Equal(ClientUiState.MainMenu, flow.State);
    }

    [Theory]
    [InlineData(null, false)] [InlineData("", false)] [InlineData("ABC", false)]
    [InlineData("ABC234", true)] [InlineData("  abc234  ", true)] [InlineData("ABO234", false)]
    public void JoinValidationUsesExistingNormalizationAndAlphabet(string? input, bool valid)
    {
        Assert.Equal(valid, ClientInputValidation.JoinCodeError(input) == null);
        Assert.Equal(valid, MultiplayerSession.ValidJoinCode(MultiplayerSession.NormalizeJoinCode(input)));
    }
}
