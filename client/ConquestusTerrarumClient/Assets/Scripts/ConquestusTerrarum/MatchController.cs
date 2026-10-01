using System;
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
    }
}