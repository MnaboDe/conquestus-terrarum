#nullable disable
using System;

namespace ConquestusTerrarum.Client
{
    public static class ClientMatchLifecycle
    {
        public static bool CanCommand(MatchStateDto state, MultiplayerSession session, ArmyDto army) =>
            state != null && state.phase == "active" && session != null && session.Matches(state.matchId) &&
            session.IsLocalArmy(army) && !army.isDestroyed;

        public static bool Valid(MatchStateDto state)
        {
            if (state == null) return false;
            if (state.phase == "waitingForPlayers" || state.phase == "active")
                return !state.isDraw && string.IsNullOrEmpty(state.winnerPlayerId);
            return state.phase == "finished" && (state.isDraw
                ? string.IsNullOrEmpty(state.winnerPlayerId)
                : Guid.TryParse(state.winnerPlayerId, out var winner) && winner != Guid.Empty);
        }

        public static string Label(MatchStateDto state, string localPlayerId)
        {
            if (state == null) return "";
            if (state.phase == "waitingForPlayers") return "Waiting for second player";
            if (state.phase == "active") return "Game Active";
            if (state.phase == "finished") return state.isDraw ? "Draw" :
                string.Equals(state.winnerPlayerId, localPlayerId, StringComparison.OrdinalIgnoreCase) ? "Victory" : "Defeat";
            return "Unknown match phase";
        }
    }
}
