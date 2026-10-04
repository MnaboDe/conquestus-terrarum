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
        public MultiplayerSession Session { get; private set; }
        public string LocalPlayerId => Session?.PlayerId;
        public bool IsConnected => Session != null && State != null;
        public bool IsLocalArmy(ArmyDto army) => Session != null && Session.IsLocalArmy(army);

        public string Status { get; private set; } =
            "Disconnected. Create or join a game.";

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
        private long _stateGeneration;
        private long _sessionGeneration;
        private bool _requiresFullRefresh;

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

        public void CreateGame()
        {
            if (IsBusy || Session != null)
                return;

            Disconnect();
            var generation = _sessionGeneration;
            IsBusy = true;
            Status = "Creating game...";
            StartCoroutine(_api.CreateMatch(
                dto => { if (generation == _sessionGeneration) Connect(MultiplayerSession.FromCreate(dto)); },
                error => { if (generation == _sessionGeneration) HandleError(error); }));
        }

        public void JoinGame(string joinCode)
        {
            if (IsBusy || Session != null) return;
            var code = MultiplayerSession.NormalizeJoinCode(joinCode);
            if (!MultiplayerSession.ValidJoinCode(code))
            {
                HandleError("Enter a six-character join code, using the letters and digits shown by Player 1.");
                return;
            }
            Disconnect();
            var generation = _sessionGeneration;
            IsBusy = true;
            Status = "Joining game...";
            StartCoroutine(_api.JoinMatch(code,
                dto => { if (generation == _sessionGeneration) Connect(MultiplayerSession.FromJoin(dto, code)); },
                error => { if (generation == _sessionGeneration) HandleError(error); }));
        }

        private void Connect(MultiplayerSession session)
        {
            if (session == null)
            {
                HandleError("The server returned invalid session credentials.");
                return;
            }
            Session = session;
            _api.SetSession(session);
            BeginRefresh("Loading authoritative match...");
        }

        public void Disconnect()
        {
            _sessionGeneration++;
            _stateGeneration++;
            StopUpdatePolling();
            StopBattleDetailPolling();
            _requiresFullRefresh = false;
            _updateCursor = 0;
            _pollRequestInProgress = false;
            _battleDetailRequestInProgress = false;
            State = null;
            Session = null;
            _api.SetSession(null);
            SelectedArmyId = null;
            IsBusy = false;
            LastError = null;
            Status = "Disconnected. Create or join a game.";
        }

        /// <summary>
        /// Manual full refresh remains useful for debugging.
        /// Normal global synchronization is driven by the change feed.
        /// </summary>
        public void Refresh()
        {
            if (IsBusy || Session == null)
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
            if (IsBusy || _requiresFullRefresh)
                return;

            ArmyDto army =
                SelectedArmy;

            if (army == null)
            {
                Status =
                    "Select an army first.";

                return;
            }

            if (!IsLocalArmy(army))
            {
                Status = "Opponent army selected for inspection. Only your armies can move.";
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

            if (!HasConnection(
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
            if (IsBusy || _requiresFullRefresh)
                return;

            ArmyDto army =
                SelectedArmy;

            if (army == null ||
                !IsLocalArmy(army) || !army.canRetreat)
            {
                return;
            }

            var generation = ++_stateGeneration;
            var session = Session;
            IsBusy = true;
            LastError = null;
            Status = "Retreating...";

            StartCoroutine(
                _api.RetreatArmy(
                    session.MatchId,
                    army.id,
                    state => { if (IsCurrentForeground(generation, session)) ApplyState(state); },
                    error => { if (IsCurrentForeground(generation, session)) HandleError(error); }));
        }

        private void MoveSelectedArmy(
            string destinationLocationId)
        {
            ArmyDto army =
                SelectedArmy;

            if (army == null || !IsLocalArmy(army))
                return;

            var generation = ++_stateGeneration;
            var session = Session;
            IsBusy = true;
            LastError = null;
            Status =
                "Sending move command...";

            StartCoroutine(
                _api.MoveArmy(
                    session.MatchId,
                    army.id,
                    destinationLocationId,
                    state => { if (IsCurrentForeground(generation, session)) ApplyState(state); },
                    error => { if (IsCurrentForeground(generation, session)) HandleError(error); }));
        }

        private void BeginRefresh(
            string status)
        {
            if (Session == null)
            {
                return;
            }

            var generation = ++_stateGeneration;
            var session = Session;
            IsBusy = true;
            LastError = null;
            Status = status;

            StartCoroutine(
                _api.GetMatch(
                    session.MatchId,
                    state => { if (IsCurrentForeground(generation, session)) ApplyState(state); },
                    error => { if (IsCurrentForeground(generation, session)) HandleError(error); }));
        }

        private bool IsCurrentForeground(long generation, MultiplayerSession session) =>
            generation == _stateGeneration && ReferenceEquals(Session, session);

        private void ApplyState(
            MatchStateDto state)
        {
            if (state == null || Session == null || !Session.Matches(state.matchId))
                return;

            _stateGeneration++;
            _requiresFullRefresh = false;
            State = state;

            _updateCursor = state.updateCursor;

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
                    _pollRequestInProgress || _battleDetailRequestInProgress)
                {
                    continue;
                }

                if (_requiresFullRefresh)
                {
                    BeginRefresh("Resyncing server state...");
                    continue;
                }

                var generation = _stateGeneration;
                var matchId = State.matchId;
                _pollRequestInProgress = true;
                yield return StartCoroutine(_api.GetUpdates(matchId, _updateCursor,
                    page => { if (IsCurrentRequest(generation, matchId)) HandleUpdates(page); },
                    error => { if (IsCurrentRequest(generation, matchId)) HandlePollingError(error); }));

                _pollRequestInProgress =
                    false;
            }
        }

        private void HandleUpdates(MatchUpdatesDto page)
        {
            MatchStateDto updated;
            long cursor;
            if (!MatchUpdateApplier.TryApplyPage(State, page, _updateCursor, out updated, out cursor))
            {
                RequireFullRefresh();
                return;
            }
            // Replace the DTO only after the entire page succeeds, so map views rebuild.
            State = updated;
            _updateCursor = cursor;
            LastError = null;
            Status = "Synced. Cursor " + cursor;
        }

        private void RequireFullRefresh()
        {
            // Do not acknowledge an unsafe page before a full snapshot succeeds.
            // Keep this pending on failure so polling cannot silently skip its events.
            _requiresFullRefresh = true;
            if (!IsBusy) BeginRefresh("Applying server updates...");
        }

        private bool IsCurrentRequest(long generation, string matchId)
        {
            return generation == _stateGeneration && !IsBusy && State != null &&
                string.Equals(State.matchId, matchId, StringComparison.Ordinal);
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
                    _battleDetailRequestInProgress || _pollRequestInProgress || _requiresFullRefresh ||
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

                var generation = _stateGeneration;
                _battleDetailRequestInProgress = true;
                yield return StartCoroutine(_api.GetArmyDetails(requestedMatchId, requestedArmyId,
                    details =>
                    {
                        if (IsCurrentRequest(generation, requestedMatchId))
                            ApplyArmyDetails(requestedArmyId, details);
                    },
                    error =>
                    {
                        if (IsCurrentRequest(generation, requestedMatchId)) HandleBattleDetailError(error);
                    }));

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

            // A detail response lacks map state and battle slots. It may refresh HP,
            // but a structural transition needs one authoritative full snapshot.
            if (details.units == null || army.isDestroyed != details.isDestroyed ||
                army.isInBattle != details.isInBattle || army.canRetreat != details.canRetreat ||
                !string.Equals(army.retreatLocationId, details.retreatLocationId, StringComparison.Ordinal))
            {
                RequireFullRefresh();
                return;
            }
            army.units = details.units;
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

        private bool HasConnection(
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
