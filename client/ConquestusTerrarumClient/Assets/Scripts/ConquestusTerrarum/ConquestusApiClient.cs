using System;
using System.Collections;
using System.Text;
using UnityEngine;
using UnityEngine.Networking;

namespace ConquestusTerrarum.Client
{
    public sealed class ConquestusApiClient : MonoBehaviour
    {
        public const string BaseUrl = "http://localhost:5108";
        private MultiplayerSession _session;

        public void SetSession(MultiplayerSession session) => _session = session;

        public IEnumerator CreateMatch(Action<CreateMatchDto> onSuccess, Action<ApiFailure> onError)
        {
            yield return Send(Post("/api/v1/matches"), onSuccess, onError,
                dto => MultiplayerSession.FromCreate(dto) != null, authenticated: false);
        }

        public IEnumerator JoinMatch(string joinCode, Action<JoinMatchDto> onSuccess, Action<ApiFailure> onError)
        {
            var code = MultiplayerSession.NormalizeJoinCode(joinCode);
            yield return Send(Post("/api/v1/matches/join", new JoinMatchRequestDto { joinCode = code }),
                onSuccess, onError, dto => MultiplayerSession.FromJoin(dto, code) != null, authenticated: false);
        }

        public IEnumerator GetMatch(string matchId, Action<MatchStateDto> onSuccess, Action<ApiFailure> onError)
        {
            yield return Send(UnityWebRequest.Get($"{BaseUrl}/api/v1/matches/{matchId}"), onSuccess, onError,
                state => ValidState(state, matchId), matchId);
        }

        public IEnumerator GetUpdates(string matchId, long after, Action<MatchUpdatesDto> onSuccess, Action<ApiFailure> onError)
        {
            yield return Send(UnityWebRequest.Get($"{BaseUrl}/api/v1/matches/{matchId}/updates?after={after}"),
                onSuccess, onError, page => page != null && page.updates != null && page.nextCursor >= 0, matchId);
        }

        public IEnumerator GetArmyDetails(string matchId, string armyId, Action<ArmyDetailsDto> onSuccess, Action<ApiFailure> onError)
        {
            yield return Send(UnityWebRequest.Get($"{BaseUrl}/api/v1/matches/{matchId}/armies/{armyId}/details"),
                onSuccess, onError, details => details != null && details.units != null &&
                    string.Equals(details.armyId, armyId, StringComparison.OrdinalIgnoreCase) &&
                    DateTimeOffset.TryParse(details.serverTime, out _), matchId);
        }

        public IEnumerator MoveArmy(string matchId, string armyId, string destinationLocationId,
            Action<MatchStateDto> onSuccess, Action<ApiFailure> onError)
        {
            yield return Send(Post($"/api/v1/matches/{matchId}/armies/{armyId}/move",
                    new MoveArmyRequestDto { destinationLocationId = destinationLocationId }),
                onSuccess, onError, state => ValidState(state, matchId), matchId);
        }

        public IEnumerator RetreatArmy(string matchId, string armyId, Action<MatchStateDto> onSuccess, Action<ApiFailure> onError)
        {
            yield return Send(Post($"/api/v1/matches/{matchId}/armies/{armyId}/retreat"),
                onSuccess, onError, state => ValidState(state, matchId), matchId);
        }

        private static UnityWebRequest Post(string path, object body = null)
        {
            var request = new UnityWebRequest(BaseUrl + path, UnityWebRequest.kHttpVerbPOST);
            request.downloadHandler = new DownloadHandlerBuffer();
            if (body != null)
            {
                request.uploadHandler = new UploadHandlerRaw(Encoding.UTF8.GetBytes(JsonUtility.ToJson(body)));
                request.SetRequestHeader("Content-Type", "application/json");
            }
            return request;
        }

        private IEnumerator Send<T>(UnityWebRequest request, Action<T> onSuccess, Action<ApiFailure> onError,
            Func<T, bool> validate, string matchId = null, bool authenticated = true) where T : class
        {
            using (request)
            {
                // Capture credentials once per request. Session changes cannot retarget in-flight requests.
                var session = _session;
                if (authenticated)
                {
                    if (session == null || !session.Matches(matchId))
                    {
                        onError?.Invoke(new ApiFailure(0, "Connect to this match before requesting game state."));
                        yield break;
                    }
                    request.SetRequestHeader("Authorization", "Bearer " + session.PlayerToken);
                }
                request.timeout = 10;
                yield return request.SendWebRequest();
                var body = request.downloadHandler?.text ?? "";
                if (request.result != UnityWebRequest.Result.Success)
                {
                    onError?.Invoke(new ApiFailure(request.responseCode, ReadError(request.responseCode, body)));
                    yield break;
                }

                T response;
                try
                {
                    response = JsonUtility.FromJson<T>(body);
                    if (!validate(response)) response = null;
                }
                catch (Exception)
                {
                    // Parser messages may contain response data, including create/join credentials.
                    response = null;
                }
                if (response == null) onError?.Invoke(new ApiFailure(0, "The server returned an invalid response. Try again or check the API version."));
                else onSuccess?.Invoke(response);
            }
        }

        private static bool ValidState(MatchStateDto state, string matchId) => state != null &&
            ClientMatchLifecycle.Valid(state) &&
            string.Equals(state.matchId, matchId, StringComparison.OrdinalIgnoreCase) &&
            state.locations != null && state.connections != null && state.armies != null &&
            state.alliances != null && state.activeBattles != null && state.updateCursor >= 0 &&
            DateTimeOffset.TryParse(state.serverTime, out _);

        private static string ReadError(long status, string body)
        {
            ProblemDetailsDto problem = null;
            try { problem = JsonUtility.FromJson<ProblemDetailsDto>(body); }
            catch (Exception) { }
            // Display known errors only; never expose arbitrary bodies, credentials, SQL, or stack traces.
            switch (problem?.code)
            {
                case "join_code_not_found": return "Join code not found. Check the code and try again.";
                case "match_full": return "This match already has two players. Create or join another game.";
                case "match_waiting_for_players": return "Waiting for the second player. Gameplay commands are disabled.";
                case "invalid_player_token": return "Your player session was rejected (401). Return to the menu to retry or forget the saved game.";
                case "army_not_owned": return "You can only command your own armies (403).";
                case "match_not_found": return "This match no longer exists (404).";
            }
            switch (status)
            {
                case 0: return "Cannot reach the backend at " + BaseUrl + ". Check that the API is running.";
                case 400: return "The request is invalid. Check the join code or selected command.";
                case 401: return "Your player session was rejected (401). Return to the menu to retry or forget the saved game.";
                case 403: return "You can only command your own armies (403).";
                case 404: return "The requested match or join code was not found (404).";
                case 409: return "The command conflicts with the current game state. Refresh and try again.";
                default: return "The backend could not complete the request (HTTP " + status + "). Try again shortly.";
            }
        }
    }
}
