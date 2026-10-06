using System;
using System.Linq;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace ConquestusTerrarum.Client.UI
{
    public sealed class ArmyPanel : MonoBehaviour
    {
        [SerializeField] private TMP_Text titleText, ownershipText, situationText, unitsText, hpText, hintText;
        [SerializeField] private GameObject details;
        [SerializeField] private Button retreatButton;
        public event Action RetreatRequested;
        private void Awake() => retreatButton.onClick.AddListener(() => RetreatRequested?.Invoke());

        public void Refresh(MatchStateDto state, ArmyDto army, bool local, bool canMove, bool canRetreat)
        {
            details.SetActive(army != null);
            titleText.text = army == null ? "Select an army" : "Army " + ShortId(army.id);
            if (army == null) return;
            ownershipText.text = local ? "YOUR ARMY" : "OTHER PLAYER · INSPECTION";
            situationText.text = army.isDestroyed ? "No living units" : army.isInBattle ? "In battle · live report" : army.isMoving
                ? "Moving to " + Location(state, army.movement?.toLocationId) : "At " + Location(state, army.locationId);
            var units = (army.units ?? Array.Empty<UnitDto>()).Where(u => u != null).ToArray();
            unitsText.text = units.Count(u => u.isAlive) + " / " + units.Length + " units";
            hpText.text = "HP " + units.Sum(u => u.currentHitPoints).ToString("0.#") + " / " + units.Sum(u => u.maxHitPoints).ToString("0.#");
            retreatButton.gameObject.SetActive(local);
            retreatButton.interactable = canRetreat;
            hintText.text = !local ? "Inspect this army. Orders are available only for your own armies."
                : canMove ? "Click a connected location on the map to move."
                : canRetreat ? "Retreat is available. Select it to return along the existing retreat route."
                : state.phase == "waitingForPlayers" ? "Orders unlock when the second player joins."
                : "Orders are currently unavailable for this army.";
        }

        private static string ShortId(string id) => string.IsNullOrEmpty(id) ? "?" : id.Substring(0, Math.Min(6, id.Length));
        private static string Location(MatchStateDto state, string id) =>
            state.locations?.FirstOrDefault(l => l != null && l.id == id)?.name ?? "an unknown location";
    }
}
