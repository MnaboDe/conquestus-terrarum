using System;
using System.Collections;
using System.Linq;
using UnityEngine;

namespace ConquestusTerrarum.Client
{
    public sealed class MatchController : MonoBehaviour
    {
        private const float UpdatePollIntervalSeconds = 2f;

        public MatchStateDto State { get; private set; }

        public string Status { get; private set; } =
            "Starting...";

        public string LastError { get; private set; }

        public bool IsBusy { get; private set; }

        public string SelectedArmyId { get; private set; }

        public ArmyDto SelectedArmy
        {
            get
            {
                if (State == null ||
                    State.armies == null)
                {
                    return null;
                }

                return State.armies.FirstOrDefault(
                    army =>
                        army != null &&
                        string.Equals(
                            army.id,
                            SelectedArmyId,
                            StringComparison.Ordinal));
            }
        }

        private ConquestusApiClient _api;

        private DateTimeOffset _serverTimeAnchor;
        private double _realtimeAnchor;

        private Coroutine _updatePolling;
        private long _updateCursor;
        private bool _pollRequestInProgress;

        private void Awake()
        {
            _api =
                GetComponent<ConquestusApiClient>();

            if (_api == null)
            {
                _api =
                    gameObject.AddComponent<ConquestusApiClient>();
            }
        }

        private void Start()
        {
            CreateDemoMatch();
        }

        private void OnDestroy()
        {
            StopUpdatePolling();
        }

        public DateTimeOffset PresentationServerTime
        {
            get
            {
                if (State == null)
                    return default;

                double elapsed =
                    Time.realtimeSinceStartupAsDouble -
                    _realtimeAnchor;

                return _serverTimeAnchor.AddSeconds(
                    elapsed);
            }
        }

        public void CreateDemoMatch()
        {
            if (IsBusy)
                return;

            StopUpdatePolling();

            _updateCursor = 0;
            _pollRequestInProgress = false;

            IsBusy = true;
            SelectedArmyId = null;
            LastError = null;
            Status = "Creating demo match...";

            StartCoroutine(
                _api.CreateDevelopmentMatch(
                    ApplyState,
                    HandleError));
        }

        /// <summary>
        /// Manual full refresh remains useful for debugging,
        /// but normal synchronization is driven by the change feed.
        /// </summary>
        public void Refresh()
        {
            if (IsBusy ||
                State == null ||
                string.IsNullOrWhiteSpace(
                    State.matchId))
            {
                return;
            }

            BeginRefresh(
                "Refreshing...");
        }

        public void SelectArmy(
            string armyId)
        {
            if (State == null ||
                State.armies == null)
            {
                return;
            }

            ArmyDto army =
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
                "Selected army " +
                ShortId(army.id);
        }

        public void ClickLocation(
            string locationId)
        {
            if (IsBusy)
                return;

            ArmyDto army =
                SelectedArmy;

            if (army == null)
            {
                Status =
                    "Select an army first.";

                return;
            }

            if (army.isDestroyed)
            {
                Status =
                    "Destroyed army cannot move.";

                return;
            }

            if (army.isMoving)
            {
                Status =
                    "Army is already moving.";

                return;
            }

            if (army.isInBattle)
            {
                Status =
                    "Army is in battle. Use Retreat when available.";

                return;
            }

            if (string.IsNullOrWhiteSpace(
                    army.locationId))
            {
                Status =
                    "Army has no current location.";

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

            MoveSelectedArmy(
                locationId);
        }

        public void RetreatSelectedArmy()
        {
            if (IsBusy)
                return;

            ArmyDto army =
                SelectedArmy;

            if (army == null ||
                !army.canRetreat)
            {
                return;
            }

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
            ArmyDto army =
                SelectedArmy;

            if (army == null)
                return;

            IsBusy = true;
            LastError = null;
            Status =
                "Sending move command...";

            StartCoroutine(
                _api.MoveArmy(
                    State.matchId,
                    army.id,
                    destinationLocationId,
                    ApplyState,
                    HandleError));
        }

        private void BeginRefresh(
            string status)
        {
            if (State == null ||
                string.IsNullOrWhiteSpace(
                    State.matchId))
            {
                return;
            }

            IsBusy = true;
            LastError = null;
            Status = status;

            StartCoroutine(
                _api.GetMatch(
                    State.matchId,
                    ApplyState,
                    HandleError));
        }

        private void ApplyState(
            MatchStateDto state)
        {
            State = state;
            if (state.updateCursor > _updateCursor)
            {
                _updateCursor =
                    state.updateCursor;
            }

            if (!DateTimeOffset.TryParse(
                    state.serverTime,
                    out _serverTimeAnchor))
            {
                _serverTimeAnchor = default;
            }

            _realtimeAnchor =
                Time.realtimeSinceStartupAsDouble;

            if (!string.IsNullOrWhiteSpace(
                    SelectedArmyId))
            {
                bool selectedStillExists =
                    state.armies != null &&
                    state.armies.Any(
                        army =>
                            army != null &&
                            string.Equals(
                                army.id,
                                SelectedArmyId,
                                StringComparison.Ordinal));

                if (!selectedStillExists)
                {
                    SelectedArmyId = null;
                }
            }

            IsBusy = false;
            LastError = null;

            Status =
                "Connected. Version " +
                state.version;

            Debug.Log(
                "Match " +
                state.matchId +
                ": " +
                (state.locations != null
                    ? state.locations.Length
                    : 0) +
                " locations, " +
                (state.armies != null
                    ? state.armies.Length
                    : 0) +
                " armies.");

            StartUpdatePolling();
        }

        private void StartUpdatePolling()
        {
            if (_updatePolling != null)
                return;

            if (State == null ||
                string.IsNullOrWhiteSpace(
                    State.matchId))
            {
                return;
            }

            _updatePolling =
                StartCoroutine(
                    PollUpdatesLoop());
        }

        private void StopUpdatePolling()
        {
            if (_updatePolling == null)
                return;

            StopCoroutine(
                _updatePolling);

            _updatePolling = null;
            _pollRequestInProgress = false;
        }

        private IEnumerator PollUpdatesLoop()
        {
            var wait =
                new WaitForSecondsRealtime(
                    UpdatePollIntervalSeconds);

            while (true)
            {
                yield return wait;

                if (State == null ||
                    string.IsNullOrWhiteSpace(
                        State.matchId))
                {
                    continue;
                }

                // Do not compete with a command or full refresh.
                if (IsBusy ||
                    _pollRequestInProgress)
                {
                    continue;
                }

                _pollRequestInProgress = true;

                yield return StartCoroutine(
                    _api.GetUpdates(
                        State.matchId,
                        _updateCursor,
                        HandleUpdates,
                        HandlePollingError));

                _pollRequestInProgress = false;
            }
        }

        private void HandleUpdates(
            MatchUpdatesDto page)
        {
            if (page == null)
                return;

            long previousCursor =
                _updateCursor;

            if (page.nextCursor >
                _updateCursor)
            {
                _updateCursor =
                    page.nextCursor;
            }

            int updateCount =
                page.updates != null
                    ? page.updates.Length
                    : 0;

            if (updateCount == 0)
                return;

            Debug.Log(
                "Received " +
                updateCount +
                " match update(s), cursor " +
                previousCursor +
                " -> " +
                _updateCursor +
                ".");

            foreach (MatchUpdateDto update
                     in page.updates)
            {
                if (update == null)
                    continue;

                Debug.Log(
                    "Update #" +
                    update.sequence +
                    ": " +
                    update.type);
            }

            // MVP behavior:
            // the feed tells us THAT something changed.
            // We then download one authoritative full snapshot.
            //
            // Later we can apply event payloads directly to the
            // local presentation state and avoid this GET.
            if (!IsBusy)
            {
                BeginRefresh(
                    "Applying server updates...");
            }
        }

        private void HandlePollingError(
            string error)
        {
            // A temporary polling failure should not destroy the
            // currently displayed playable state.
            LastError = error;
            Status = "Update polling failed.";

            Debug.LogWarning(
                "Change-feed polling failed: " +
                error);
        }

        private void HandleError(
            string error)
        {
            IsBusy = false;
            LastError = error;
            Status = "Request failed.";

            Debug.LogError(error);
        }

        private bool IsConnected(
            string firstLocationId,
            string secondLocationId)
        {
            if (State == null ||
                State.connections == null)
            {
                return false;
            }

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

        private static string ShortId(
            string id)
        {
            if (string.IsNullOrWhiteSpace(id))
                return "?";

            return id.Length <= 6
                ? id
                : id.Substring(0, 6);
        }
    }
}