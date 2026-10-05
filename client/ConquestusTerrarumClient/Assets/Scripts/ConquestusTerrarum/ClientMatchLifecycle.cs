#nullable disable

namespace ConquestusTerrarum.Client
{
    public static class ClientMatchLifecycle
    {
        public static bool CanCommand(MatchStateDto state, MultiplayerSession session, ArmyDto army) =>
            state != null && state.phase == "active" && session != null && session.Matches(state.matchId) &&
            session.IsLocalArmy(army) && !army.isDestroyed;

        public static bool Valid(MatchStateDto state) =>
            state != null && (state.phase == "waitingForPlayers" || state.phase == "active");

        public static string Label(MatchStateDto state)
        {
            if (state == null) return "";
            if (state.phase == "waitingForPlayers") return "Waiting for second player";
            if (state.phase == "active") return "Game Active";
            return "Unknown match phase";
        }
    }
}
