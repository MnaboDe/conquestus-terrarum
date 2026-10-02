using System;
using System.Collections;
using System.Linq;
using UnityEngine;

namespace ConquestusTerrarum.Client
{
    public sealed class MatchController : MonoBehaviour
    {
        private const float UpdatePollIntervalSeconds = 2f;
        private const float BattleDetailPollIntervalSeconds = 0.75f;

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

        private Coroutine _battleDetailPolling;
        private bool _battleDetailRequestInProgress;

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
            StopBattleDetailPolling();
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
            StopBattleDetailPolling();

            _updateCursor = 0;
            _pollRequestInProgress = false;
            _battleDetailRequestInProgress = false;

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
        /// Manual full refresh remains useful for debugging.
        /// Normal global synchronization is driven by the change feed.
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

            SelectedArmyId =
                army.id;

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
            if (state == null)
                return;

            State = state;

            if (state.updateCursor >
                _updateCursor)
            {
                _updateCursor =
                    state.updateCursor;
            }

            if (!DateTimeOffset.TryParse(
                    state.serverTime,
                    out _serverTimeAnchor))
            {
                _serverTimeAnchor =
                    default;
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
                    SelectedArmyId =
                        null;
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
            StartBattleDetailPolling();
        }

        // ------------------------------------------------------------
        // Global change-feed polling
        // ------------------------------------------------------------

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

                if (IsBusy ||
                    _pollRequestInProgress)
                {
                    continue;
                }

                _pollRequestInProgress =
                    true;

                yield return StartCoroutine(
                    _api.GetUpdates(
                        State.matchId,
                        _updateCursor,
                        HandleUpdates,
                        HandlePollingError));

                _pollRequestInProgress =
                    false;
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

            bool requiresFullRefresh =
                false;

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

                if (!TryApplySimpleUpdate(
                        update))
                {
                    requiresFullRefresh =
                        true;
                }
            }

            if (State != null)
            {
                State.updateCursor =
                    _updateCursor;
            }

            if (requiresFullRefresh)
            {
                if (!IsBusy)
                {
                    BeginRefresh(
                        "Applying server updates...");
                }

                return;
            }

            LastError = null;

            Status =
                "Synced. Cursor " +
                _updateCursor;
        }

        private bool TryApplySimpleUpdate(
            MatchUpdateDto update)
        {
            if (update == null ||
                update.payload == null ||
                State == null)
            {
                return false;
            }

            switch (update.type)
            {
                case "ArmyMovementStarted":
                    return ApplyArmyMovementStarted(
                        update);

                case "LocationOwnerChanged":
                    return ApplyLocationOwnerChanged(
                        update);

                default:
                    // Arrival, battle, retreat and destruction can
                    // modify several related pieces of state.
                    // For now we resync those with a full snapshot.
                    return false;
            }
        }

        private bool ApplyArmyMovementStarted(
            MatchUpdateDto update)
        {
            if (State == null ||
                State.armies == null)
            {
                return false;
            }

            MatchUpdatePayloadDto payload =
                update.payload;

            if (payload == null ||
                string.IsNullOrWhiteSpace(
                    payload.armyId) ||
                string.IsNullOrWhiteSpace(
                    payload.fromLocationId) ||
                string.IsNullOrWhiteSpace(
                    payload.toLocationId) ||
                string.IsNullOrWhiteSpace(
                    payload.startedAt) ||
                string.IsNullOrWhiteSpace(
                    payload.arriveAt))
            {
                return false;
            }

            ArmyDto army =
                State.armies.FirstOrDefault(
                    candidate =>
                        candidate != null &&
                        string.Equals(
                            candidate.id,
                            payload.armyId,
                            StringComparison.Ordinal));

            if (army == null)
                return false;

            if (!string.IsNullOrWhiteSpace(
                    army.locationId) &&
                !string.Equals(
                    army.locationId,
                    payload.fromLocationId,
                    StringComparison.Ordinal))
            {
                return false;
            }

            if (!string.IsNullOrWhiteSpace(
                    payload.ownerId) &&
                !string.Equals(
                    army.ownerId,
                    payload.ownerId,
                    StringComparison.Ordinal))
            {
                return false;
            }

            army.locationId = null;
            army.enteredLocationAt = null;

            army.isMoving = true;
            army.isInBattle = false;
            army.canRetreat = false;
            army.retreatLocationId = null;

            army.movement =
                new MovementDto
                {
                    fromLocationId =
                        payload.fromLocationId,

                    toLocationId =
                        payload.toLocationId,

                    startedAt =
                        payload.startedAt,

                    arriveAt =
                        payload.arriveAt,

                    kind =
                        "normal"
                };

            Debug.Log(
                "Applied ArmyMovementStarted locally for " +
                ShortId(army.id) +
                ".");

            return true;
        }

        private bool ApplyLocationOwnerChanged(
            MatchUpdateDto update)
        {
            if (State == null ||
                State.locations == null)
            {
                return false;
            }

            MatchUpdatePayloadDto payload =
                update.payload;

            if (payload == null ||
                string.IsNullOrWhiteSpace(
                    payload.locationId))
            {
                return false;
            }

            LocationDto location =
                State.locations.FirstOrDefault(
                    candidate =>
                        candidate != null &&
                        string.Equals(
                            candidate.id,
                            payload.locationId,
                            StringComparison.Ordinal));

            if (location == null)
                return false;

            location.ownerId =
                payload.ownerId;

            Debug.Log(
                "Applied LocationOwnerChanged locally for " +
                ShortId(location.id) +
                ".");

            return true;
        }

        private void HandlePollingError(
            string error)
        {
            LastError = error;
            Status = "Update polling failed.";

            Debug.LogWarning(
                "Change-feed polling failed: " +
                error);
        }

        // ------------------------------------------------------------
        // Selected battle army detail polling
        // ------------------------------------------------------------

        private void StartBattleDetailPolling()
        {
            if (_battleDetailPolling != null)
                return;

            _battleDetailPolling =
                StartCoroutine(
                    PollSelectedBattleArmyLoop());
        }

        private void StopBattleDetailPolling()
        {
            if (_battleDetailPolling == null)
                return;

            StopCoroutine(
                _battleDetailPolling);

            _battleDetailPolling = null;
            _battleDetailRequestInProgress = false;
        }

        private IEnumerator PollSelectedBattleArmyLoop()
        {
            var wait =
                new WaitForSecondsRealtime(
                    BattleDetailPollIntervalSeconds);

            while (true)
            {
                yield return wait;

                if (IsBusy ||
                    _battleDetailRequestInProgress ||
                    State == null ||
                    string.IsNullOrWhiteSpace(
                        State.matchId))
                {
                    continue;
                }

                ArmyDto army =
                    SelectedArmy;

                // Detail polling is active only while the
                // currently selected army participates in battle.
                if (army == null ||
                    army.isDestroyed ||
                    !army.isInBattle)
                {
                    continue;
                }

                string requestedArmyId =
                    army.id;

                string requestedMatchId =
                    State.matchId;

                _battleDetailRequestInProgress =
                    true;

                yield return StartCoroutine(
                    _api.GetArmyDetails(
                        requestedMatchId,
                        requestedArmyId,
                        details =>
                            ApplyArmyDetails(
                                requestedArmyId,
                                details),
                        HandleBattleDetailError));

                _battleDetailRequestInProgress =
                    false;
            }
        }

        private void ApplyArmyDetails(
            string requestedArmyId,
            ArmyDetailsDto details)
        {
            if (details == null ||
                State == null)
            {
                return;
            }

            // Selection may have changed while the HTTP
            // request was in flight.
            if (!string.Equals(
                    requestedArmyId,
                    SelectedArmyId,
                    StringComparison.Ordinal))
            {
                return;
            }

            if (!string.Equals(
                    requestedArmyId,
                    details.armyId,
                    StringComparison.Ordinal))
            {
                return;
            }

            ArmyDto army =
                SelectedArmy;

            if (army == null)
                return;

            army.isDestroyed =
                details.isDestroyed;

            army.isInBattle =
                details.isInBattle;

            army.canRetreat =
                details.canRetreat;

            army.retreatLocationId =
                details.retreatLocationId;

            if (details.units != null)
            {
                army.units =
                    details.units;
            }

            // The detail response was calculated at this
            // authoritative server timestamp.
            DateTimeOffset serverTime;

            if (DateTimeOffset.TryParse(
                    details.serverTime,
                    out serverTime))
            {
                _serverTimeAnchor =
                    serverTime;

                _realtimeAnchor =
                    Time.realtimeSinceStartupAsDouble;

                State.serverTime =
                    details.serverTime;

                State.checkpointTime =
                    details.serverTime;
            }

            Debug.Log(
                "Battle details updated for army " +
                ShortId(army.id) +
                ".");
        }

        private void HandleBattleDetailError(
            string error)
        {
            // Detail polling is supplemental. A temporary
            // failure must not destroy the displayed match state.
            Debug.LogWarning(
                "Battle detail polling failed: " +
                error);
        }

        // ------------------------------------------------------------
        // General helpers
        // ------------------------------------------------------------

        private void HandleError(
            string error)
        {
            IsBusy = false;
            LastError = error;
            Status = "Request failed.";

            Debug.LogError(
                error);
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
            if (string.IsNullOrWhiteSpace(
                    id))
            {
                return "?";
            }

            return id.Length <= 6
                ? id
                : id.Substring(
                    0,
                    6);
        }
    }
}