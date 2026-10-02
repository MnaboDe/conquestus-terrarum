using System;
using System.Collections;
using System.Linq;
using UnityEngine;

namespace ConquestusTerrarum.Client
{
    public sealed class MatchController : MonoBehaviour
    {
        private const double RefreshSafetyDelaySeconds = 0.10;

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
                if (State == null || State.armies == null)
                    return null;

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

        private Coroutine _scheduledRefresh;
        private int _stateGeneration;

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
            CancelScheduledRefresh();
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

            CancelScheduledRefresh();

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
                string.IsNullOrWhiteSpace(
                    State.matchId))
            {
                return;
            }

            CancelScheduledRefresh();

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

            CancelScheduledRefresh();

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

            CancelScheduledRefresh();

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

        private void ApplyState(
            MatchStateDto state)
        {
            CancelScheduledRefresh();

            State = state;

            if (!DateTimeOffset.TryParse(
                    state.serverTime,
                    out _serverTimeAnchor))
            {
                _serverTimeAnchor = default;
            }

            _realtimeAnchor =
                Time.realtimeSinceStartupAsDouble;

            _stateGeneration++;

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

            ScheduleNextKnownEvent();
        }

        private void HandleError(
            string error)
        {
            CancelScheduledRefresh();

            IsBusy = false;
            LastError = error;
            Status = "Request failed.";

            Debug.LogError(error);
        }

        private void ScheduleNextKnownEvent()
        {
            if (State == null)
                return;

            DateTimeOffset? nextEvent =
                FindEarliestKnownFutureEvent(
                    State);

            if (!nextEvent.HasValue)
                return;

            int generation =
                _stateGeneration;

            _scheduledRefresh =
                StartCoroutine(
                    WaitForKnownEventAndRefresh(
                        nextEvent.Value,
                        generation));
        }

        private IEnumerator WaitForKnownEventAndRefresh(
            DateTimeOffset eventTime,
            int expectedGeneration)
        {
            double secondsUntilEvent =
                (eventTime -
                 PresentationServerTime)
                .TotalSeconds;

            double delay =
                Math.Max(
                    0.05,
                    secondsUntilEvent +
                    RefreshSafetyDelaySeconds);

            Debug.Log(
                "Next authoritative refresh in " +
                delay.ToString("0.00") +
                "s for event at " +
                eventTime.ToString("O"));

            yield return new WaitForSecondsRealtime(
                (float)delay);

            if (expectedGeneration !=
                _stateGeneration)
            {
                yield break;
            }

            if (IsBusy ||
                State == null)
            {
                yield break;
            }

            // Coroutine больше не считается
            // запланированной до начала GET.
            _scheduledRefresh = null;

            BeginRefresh(
                "Refreshing after server event...");
        }

        private static DateTimeOffset?
            FindEarliestKnownFutureEvent(
                MatchStateDto state)
        {
            DateTimeOffset serverTime;

            if (!DateTimeOffset.TryParse(
                    state.serverTime,
                    out serverTime))
            {
                return null;
            }

            DateTimeOffset? earliest =
                null;

            if (state.armies != null)
            {
                foreach (ArmyDto army
                         in state.armies)
                {
                    if (army == null ||
                        !army.isMoving ||
                        army.movement == null)
                    {
                        continue;
                    }

                    ConsiderTimestamp(
                        army.movement.arriveAt,
                        serverTime,
                        ref earliest);
                }
            }

            if (state.activeBattles != null)
            {
                foreach (BattleDto battle
                         in state.activeBattles)
                {
                    if (battle == null ||
                        battle.combatSlots == null)
                    {
                        continue;
                    }

                    foreach (CombatSlotDto slot
                             in battle.combatSlots)
                    {
                        if (slot == null)
                            continue;

                        ConsiderTimestamp(
                            slot.firstDeathAt,
                            serverTime,
                            ref earliest);

                        ConsiderTimestamp(
                            slot.secondDeathAt,
                            serverTime,
                            ref earliest);
                    }
                }
            }

            return earliest;
        }

        private static void ConsiderTimestamp(
            string value,
            DateTimeOffset serverTime,
            ref DateTimeOffset? earliest)
        {
            if (string.IsNullOrWhiteSpace(
                    value))
            {
                return;
            }

            DateTimeOffset parsed;

            if (!DateTimeOffset.TryParse(
                    value,
                    out parsed))
            {
                return;
            }

            // Уже обработанные события не должны
            // создавать бесконечные GET циклы.
            if (parsed <= serverTime)
                return;

            if (!earliest.HasValue ||
                parsed < earliest.Value)
            {
                earliest = parsed;
            }
        }

        private void CancelScheduledRefresh()
        {
            if (_scheduledRefresh == null)
                return;

            StopCoroutine(
                _scheduledRefresh);

            _scheduledRefresh = null;
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