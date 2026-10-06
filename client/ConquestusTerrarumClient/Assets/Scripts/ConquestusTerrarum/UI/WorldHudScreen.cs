using System;
using UnityEngine;
using UnityEngine.UI;
using TMPro;

namespace ConquestusTerrarum.Client.UI
{
    public sealed class WorldHudScreen : UiScreen
    {
        [SerializeField] private WorldStatusPanel statusPanel;
        [SerializeField] private ArmyPanel armyPanel;
        [SerializeField] private WorldMapViewport mapArea;
        [SerializeField] private Button menuButton;
        [SerializeField] private TMP_Text localPlayerText;
        public event Action MenuRequested;
        public ArmyPanel ArmyPanel => armyPanel;
        private void Awake() => menuButton.onClick.AddListener(() => MenuRequested?.Invoke());
        public void Initialize(MapPresenter map) => mapArea.Initialize(map);
        public void Refresh(MatchStateDto state, ArmyDto selected, bool local, bool canMove, bool canRetreat,
            string status, bool busy, string joinCode, string localPlayerId)
        {
            // Render controller-provided command eligibility; do not introduce gameplay rules here.
            statusPanel.Refresh(state, status, busy, joinCode);
            armyPanel.Refresh(state, selected, local, canMove, canRetreat);
            localPlayerText.text = "Player " + localPlayerId;
        }
    }
}
