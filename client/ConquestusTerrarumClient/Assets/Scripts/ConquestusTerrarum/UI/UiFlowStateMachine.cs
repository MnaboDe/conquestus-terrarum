#nullable disable
namespace ConquestusTerrarum.Client.UI
{
    public enum ClientUiState { Boot, MainMenu, Connecting, World }

    // Presentation only. Revisions reject stale observations; MatchController owns session truth.
    public sealed class UiFlowStateMachine
    {
        public ClientUiState State { get; private set; } = ClientUiState.Boot;
        public long LastRevision { get; private set; } = -1;

        public bool Synchronize(long revision, bool hasWorld, bool busy)
        {
            if (revision < LastRevision) return false;
            LastRevision = revision;
            var next = hasWorld ? ClientUiState.World : busy ? ClientUiState.Connecting : ClientUiState.MainMenu;
            if (next == State) return false;
            State = next;
            return true;
        }
    }
}
