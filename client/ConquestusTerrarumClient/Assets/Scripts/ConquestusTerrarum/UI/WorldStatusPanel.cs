using TMPro;
using UnityEngine;

namespace ConquestusTerrarum.Client.UI
{
    public sealed class WorldStatusPanel : MonoBehaviour
    {
        [SerializeField] private TMP_Text phaseText, connectionText;
        [SerializeField] private JoinCodePanel invitation;
        public void Refresh(MatchStateDto state, string status, bool busy, string joinCode)
        {
            phaseText.text = state.phase == "waitingForPlayers" ? "Waiting for second player" : "Active world";
            connectionText.text = busy ? "Synchronizing..." : status.Contains("failed") ? "Connection interrupted · retrying" : "Connected";
            invitation.Refresh(state.phase == "waitingForPlayers", joinCode);
        }
    }
}
