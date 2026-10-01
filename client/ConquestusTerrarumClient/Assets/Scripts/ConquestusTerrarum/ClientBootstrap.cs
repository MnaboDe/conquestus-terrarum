using UnityEngine;

namespace ConquestusTerrarum.Client
{
    public static class ClientBootstrap
    {
        [RuntimeInitializeOnLoadMethod(
            RuntimeInitializeLoadType.AfterSceneLoad)]
        private static void Initialize()
        {
            if (Object.FindFirstObjectByType<MatchController>() != null)
                return;

            var root =
                new GameObject("ConquestusTerrarumClient");

            Object.DontDestroyOnLoad(root);

            root.AddComponent<MatchController>();
            root.AddComponent<DebugHud>();
        }
    }

    public sealed class DebugHud : MonoBehaviour
    {
        private MatchController _controller;

        private void Awake()
        {
            _controller =
                GetComponent<MatchController>();
        }

        private void OnGUI()
        {
            GUILayout.BeginArea(
                new Rect(15, 15, 520, 500),
                GUI.skin.box);

            GUILayout.Label(
                "<b>Conquestus Terrarum</b>",
                RichLabel());

            GUILayout.Space(8);

            GUILayout.Label(
                $"Status: {_controller.Status}");

            if (_controller.State != null)
            {
                var state = _controller.State;

                GUILayout.Label(
                    $"Match: {state.matchId}");

                GUILayout.Label(
                    $"Version: {state.version}");

                GUILayout.Label(
                    $"Locations: {state.locations?.Length ?? 0}");

                GUILayout.Label(
                    $"Armies: {state.armies?.Length ?? 0}");

                GUILayout.Label(
                    $"Battles: {state.activeBattles?.Length ?? 0}");

                GUILayout.Label(
                    $"Server: {_controller.PresentationServerTime:O}");
            }

            if (!string.IsNullOrWhiteSpace(
                    _controller.LastError))
            {
                GUILayout.Space(8);

                GUILayout.Label(
                    $"ERROR: {_controller.LastError}");
            }

            GUILayout.Space(12);

            GUI.enabled = !_controller.IsBusy;

            if (GUILayout.Button(
                    "New Demo Match",
                    GUILayout.Height(35)))
            {
                _controller.CreateDemoMatch();
            }

            GUI.enabled =
                !_controller.IsBusy &&
                _controller.State != null;

            if (GUILayout.Button(
                    "Refresh",
                    GUILayout.Height(35)))
            {
                _controller.Refresh();
            }

            GUI.enabled = true;

            GUILayout.EndArea();
        }

        private static GUIStyle RichLabel()
        {
            var style =
                new GUIStyle(GUI.skin.label);

            style.richText = true;
            style.fontSize = 18;

            return style;
        }
    }
}