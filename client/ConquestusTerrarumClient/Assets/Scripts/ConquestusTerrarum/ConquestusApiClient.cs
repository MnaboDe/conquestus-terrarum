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

        public IEnumerator CreateDevelopmentMatch(
            Action<MatchStateDto> onSuccess,
            Action<string> onError)
        {
            var request = new UnityWebRequest(
                $"{BaseUrl}/api/v1/dev/matches",
                UnityWebRequest.kHttpVerbPOST);

            request.downloadHandler = new DownloadHandlerBuffer();

            yield return Send(request, onSuccess, onError);
        }

        public IEnumerator GetMatch(
            string matchId,
            Action<MatchStateDto> onSuccess,
            Action<string> onError)
        {
            var request = UnityWebRequest.Get(
                $"{BaseUrl}/api/v1/matches/{matchId}");

            yield return Send(request, onSuccess, onError);
        }
        
        public IEnumerator GetUpdates(
    string matchId,
    long after,
    Action<MatchUpdatesDto> onSuccess,
    Action<string> onError)
{
    var request = UnityWebRequest.Get(
        $"{BaseUrl}/api/v1/matches/{matchId}/updates?after={after}");

    request.timeout = 10;

    using (request)
    {
        yield return request.SendWebRequest();

        var body =
            request.downloadHandler?.text ?? "";

        if (request.result ==
            UnityWebRequest.Result.Success)
        {
            try
            {
                var updates =
                    JsonUtility.FromJson<MatchUpdatesDto>(
                        body);

                if (updates == null)
                {
                    onError?.Invoke(
                        "API returned invalid updates JSON.");

                    yield break;
                }

                onSuccess?.Invoke(
                    updates);
            }
            catch (Exception exception)
            {
                onError?.Invoke(
                    $"Updates JSON error: {exception.Message}");
            }

            yield break;
        }

        var error =
            $"HTTP {request.responseCode}: {request.error}";

        if (!string.IsNullOrWhiteSpace(body))
        {
            try
            {
                var problem =
                    JsonUtility.FromJson<ProblemDetailsDto>(
                        body);

                if (problem != null)
                {
                    if (!string.IsNullOrWhiteSpace(
                            problem.code))
                    {
                        error +=
                            $" [{problem.code}]";
                    }

                    if (!string.IsNullOrWhiteSpace(
                            problem.detail))
                    {
                        error +=
                            $" {problem.detail}";
                    }
                }
            }
            catch
            {
                // Keep generic HTTP error.
            }
        }

        onError?.Invoke(
            error);
    }
}

        public IEnumerator MoveArmy(
            string matchId,
            string armyId,
            string destinationLocationId,
            Action<MatchStateDto> onSuccess,
            Action<string> onError)
        {
            var body = new MoveArmyRequestDto
            {
                destinationLocationId = destinationLocationId
            };

            var json = JsonUtility.ToJson(body);

            var request = new UnityWebRequest(
                $"{BaseUrl}/api/v1/matches/{matchId}/armies/{armyId}/move",
                UnityWebRequest.kHttpVerbPOST);

            request.uploadHandler =
                new UploadHandlerRaw(Encoding.UTF8.GetBytes(json));

            request.downloadHandler = new DownloadHandlerBuffer();

            request.SetRequestHeader(
                "Content-Type",
                "application/json");

            yield return Send(request, onSuccess, onError);
        }

        public IEnumerator RetreatArmy(
            string matchId,
            string armyId,
            Action<MatchStateDto> onSuccess,
            Action<string> onError)
        {
            var request = new UnityWebRequest(
                $"{BaseUrl}/api/v1/matches/{matchId}/armies/{armyId}/retreat",
                UnityWebRequest.kHttpVerbPOST);

            request.downloadHandler = new DownloadHandlerBuffer();

            yield return Send(request, onSuccess, onError);
        }

        private static IEnumerator Send(
            UnityWebRequest request,
            Action<MatchStateDto> onSuccess,
            Action<string> onError)
        {
            request.timeout = 10;

            using (request)
            {
                yield return request.SendWebRequest();

                var body = request.downloadHandler?.text ?? "";

                if (request.result == UnityWebRequest.Result.Success)
                {
                    try
                    {
                        var state =
                            JsonUtility.FromJson<MatchStateDto>(body);

                        if (state == null ||
                            string.IsNullOrWhiteSpace(state.matchId))
                        {
                            onError?.Invoke(
                                "API returned invalid match JSON.");

                            yield break;
                        }

                        onSuccess?.Invoke(state);
                    }
                    catch (Exception exception)
                    {
                        onError?.Invoke(
                            $"JSON error: {exception.Message}");
                    }

                    yield break;
                }

                var error =
                    $"HTTP {request.responseCode}: {request.error}";

                if (!string.IsNullOrWhiteSpace(body))
                {
                    try
                    {
                        var problem =
                            JsonUtility.FromJson<ProblemDetailsDto>(
                                body);

                        if (problem != null)
                        {
                            if (!string.IsNullOrWhiteSpace(problem.code))
                                error += $" [{problem.code}]";

                            if (!string.IsNullOrWhiteSpace(problem.detail))
                                error += $" {problem.detail}";
                        }
                    }
                    catch
                    {
                        // Keep the generic HTTP error.
                    }
                }

                onError?.Invoke(error);
            }
        }
    }
}