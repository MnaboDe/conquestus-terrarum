using System;
using System.Linq;
using UnityEngine;

namespace ConquestusTerrarum.Client
{
    public sealed class MatchController : MonoBehaviour
    {
        public MatchStateDto State { get; private set; }

        public string Status { get; private set; } =
            "Starting...";

        public string LastError { get; private set; }

        public bool IsBusy { get; private set; }

        public string SelectedArmyId { get; private set; }

        public ArmyDto SelectedArmy =>
            State?.armies?.FirstOrDefault(
                army =>
                    army != null &&
                    string.Equals(
                        army.id,
                        SelectedArmyId,
                        StringComparison.Ordinal));

        private ConquestusApiClient _api;

        private DateTimeOffset _serverTimeAnchor;
        private double _realtimeAnchor;

        private void Awake()
        {
            _api = GetComponent<ConquestusApiClient>();

            if (_api == null)
                _api = gameObject.AddComponent<ConquestusApiClient>();
        }

        private void Start()
        {
            CreateDemoMatch();
        }

        public DateTimeOffset PresentationServerTime
        {
            get
            {
                if (State == null)
                    return default;

                var elapsed =
                    Time.realtimeSinceStartupAsDouble -
                    _realtimeAnchor;

                return _serverTimeAnchor.AddSeconds(elapsed);
            }
        }

        public void CreateDemoMatch()
        {
            if (IsBusy)
                return;

            IsBusy = true;
            SelectedArmyId = null;
            LastError = null;
            Status = "Creating demo match...";

            StartCoroutine(
                _api.CreateDevelopmentMatch(
                    ApplyState,
                    HandleError));
        }

        public void Refresh()
        {
            if (IsBusy ||
                State == null ||
                string.IsNullOrWhiteSpace(State.matchId))
                return;

            IsBusy = true;
            LastError = null;
            Status = "Refreshing...";

            StartCoroutine(
                _api.GetMatch(
                    State.matchId,
                    ApplyState,
                    HandleError));
        }

        public void SelectArmy(string armyId)
        {
            if (State?.armies == null)
                return;

            var army =
                State.armies.FirstOrDefault(
                    candidate =>
                        candidate != null &&
                        string.Equals(
                            candidate.id,
                            armyId,
                            StringComparison.Ordinal));

            if (army == null)
                return;

            SelectedArmyId = army.id;

            Status =
                $"Selected army {ShortId(army.id)}";
        }

        public void ClickLocation(string locationId)
        {
            if (IsBusy)
                return;

            var army = SelectedArmy;

            if (army == null)
            {
                Status = "Select an army first.";
                return;
            }

            if (army.isDestroyed)
            {
                Status = "Destroyed army cannot move.";
                return;
            }

            if (army.isMoving)
            {
                Status = "Army is already moving.";
                return;
            }

            if (army.isInBattle)
            {
                Status =
                    "Army is in battle. Use Retreat when available.";

                return;
            }

            if (string.IsNullOrWhiteSpace(army.locationId))
            {
                Status = "Army has no current location.";
                return;
            }

            if (string.Equals(
                    army.locationId,
                    locationId,
                    StringComparison.Ordinal))
            {
                return;
            }

            if (!IsConnected(
                    army.locationId,
                    locationId))
            {
                Status =
                    "That location is not directly connected.";

                return;
            }

            MoveSelectedArmy(locationId);
        }

        public void RetreatSelectedArmy()
        {
            if (IsBusy)
                return;

            var army = SelectedArmy;

            if (army == null || !army.canRetreat)
                return;

            IsBusy = true;
            LastError = null;
            Status = "Retreating...";

            StartCoroutine(
                _api.RetreatArmy(
                    State.matchId,
                    army.id,
                    ApplyState,
                    HandleError));
        }

        private void MoveSelectedArmy(
            string destinationLocationId)
        {
            var army = SelectedArmy;

            if (army == null)
                return;

            IsBusy = true;
            LastError = null;
            Status = "Sending move command...";

            StartCoroutine(
                _api.MoveArmy(
                    State.matchId,
                    army.id,
                    destinationLocationId,
                    ApplyState,
                    HandleError));
        }

        private bool IsConnected(
            string firstLocationId,
            string secondLocationId)
        {
            if (State?.connections == null)
                return false;

            return State.connections.Any(
                connection =>
                    connection != null &&
                    (
                        string.Equals(
                            connection.fromLocationId,
                            firstLocationId,
                            StringComparison.Ordinal) &&
                        string.Equals(
                            connection.toLocationId,
                            secondLocationId,
                            StringComparison.Ordinal)
                        ||
                        string.Equals(
                            connection.fromLocationId,
                            secondLocationId,
                            StringComparison.Ordinal) &&
                        string.Equals(
                            connection.toLocationId,
                            firstLocationId,
                            StringComparison.Ordinal)
                    ));
        }

        private void ApplyState(MatchStateDto state)
        {
            State = state;

            if (!DateTimeOffset.TryParse(
                    state.serverTime,
                    out _serverTimeAnchor))
            {
                _serverTimeAnchor = default;
            }

            _realtimeAnchor =
                Time.realtimeSinceStartupAsDouble;

            if (!string.IsNullOrWhiteSpace(SelectedArmyId) &&
                (state.armies == null ||
                 !state.armies.Any(
                     army =>
                         army != null &&
                         string.Equals(
                             army.id,
                             SelectedArmyId,
                             StringComparison.Ordinal))))
            {
                SelectedArmyId = null;
            }

            IsBusy = false;
            LastError = null;

            Status =
                $"Connected. Version {state.version}";

            Debug.Log(
                $"Match {state.matchId}: " +
                $"{state.locations?.Length ?? 0} locations, " +
                $"{state.armies?.Length ?? 0} armies.");
        }

        private void HandleError(string error)
        {
            IsBusy = false;
            LastError = error;
            Status = "Request failed.";

            Debug.LogError(error);
        }

        private static string ShortId(string id)
        {
            if (string.IsNullOrWhiteSpace(id))
                return "?";

            return id.Length <= 6
                ? id
                : id[..6];
        }
    }
}