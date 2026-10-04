using System;
using System.Linq;
using UnityEngine;

namespace ConquestusTerrarum.Client
{
    public static class ClientBootstrap
    {
        [RuntimeInitializeOnLoadMethod(
            RuntimeInitializeLoadType.AfterSceneLoad)]
        private static void Initialize()
        {
            if (UnityEngine.Object
                    .FindAnyObjectByType<MatchController>() != null)
            {
                return;
            }

            GameObject root =
                new GameObject(
                    "ConquestusTerrarumClient");

            UnityEngine.Object
                .DontDestroyOnLoad(root);

            root.AddComponent<MatchController>();
            root.AddComponent<MapPresenter>();
            root.AddComponent<PointerInput>();
            root.AddComponent<DebugHud>();
        }
    }

    public sealed class DebugHud : MonoBehaviour
    {
        private MatchController _controller;
        private string _joinCode = "";
        private Vector2 _scrollPosition;
        public static Rect PanelRect => new Rect(15, 15, Mathf.Min(420, Screen.width * 0.35f), Mathf.Min(800, Screen.height - 30));

        private void Awake()
        {
            _controller =
                GetComponent<MatchController>();
        }

        private void OnGUI()
        {
            if (_controller == null)
                return;

            GUILayout.BeginArea(
                PanelRect,
                GUI.skin.box);
            _scrollPosition = GUILayout.BeginScrollView(_scrollPosition);

            GUIStyle titleStyle =
                new GUIStyle(
                    GUI.skin.label);

            titleStyle.richText = true;
            titleStyle.fontSize = 18;

            GUILayout.Label(
                "<b>Conquestus Terrarum</b>",
                titleStyle);

            GUILayout.Space(8);

            GUILayout.Label(
                $"Status: {_controller.Status}");

            MatchStateDto state =
                _controller.State;

            var session = _controller.Session;
            if (session != null)
            {
                GUILayout.Label($"Match: {session.MatchId}");
                GUILayout.Label($"Local player: {_controller.LocalPlayerId}");
                if (!string.IsNullOrEmpty(session.JoinCode))
                {
                    GUILayout.Label("Join code: " + session.JoinCode);
                    GUILayout.Label("Share this join code with Player 2.");
                    if (GUILayout.Button("Copy Join Code", GUILayout.Height(30)))
                        GUIUtility.systemCopyBuffer = session.JoinCode;
                }
            }

            if (state != null)
            {
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

            ArmyDto selected =
                _controller.SelectedArmy;

            if (selected != null)
            {
                GUILayout.Space(12);

                GUILayout.Label(
                    "<b>Selected army</b>",
                    titleStyle);

                GUILayout.Label(
                    $"Army: {ShortId(selected.id)}");

                GUILayout.Label(
                    $"Owner: {ShortId(selected.ownerId)}");
                GUILayout.Label(_controller.IsLocalArmy(selected) ? "YOUR ARMY" : "OPPONENT ARMY — inspection only");

                GUILayout.Label(
                    $"Moving: {selected.isMoving}");

                GUILayout.Label(
                    $"In battle: {selected.isInBattle}");

                GUILayout.Label(
                    $"Can retreat: {selected.canRetreat}");

                UnitDto[] units =
                    selected.units ??
                    Array.Empty<UnitDto>();

                int livingUnits =
                    units.Count(
                        unit =>
                            unit != null &&
                            unit.isAlive);

                double currentHp =
                    units
                        .Where(
                            unit =>
                                unit != null)
                        .Sum(
                            unit =>
                                unit.currentHitPoints);

                double maxHp =
                    units
                        .Where(
                            unit =>
                                unit != null)
                        .Sum(
                            unit =>
                                unit.maxHitPoints);

                GUILayout.Label(
                    $"Units: {livingUnits}/{units.Length}");

                GUILayout.Label(
                    $"HP: {currentHp:0.#}/{maxHp:0.#}");

                DrawRelations(
                    state,
                    selected,
                    titleStyle);
            }

            if (!string.IsNullOrWhiteSpace(
                    _controller.LastError))
            {
                GUILayout.Space(12);

                GUIStyle errorStyle =
                    new GUIStyle(
                        GUI.skin.label);

                errorStyle.wordWrap = true;

                errorStyle.normal.textColor =
                    new Color(
                        1f,
                        0.45f,
                        0.4f);

                GUILayout.Label(
                    $"ERROR: {_controller.LastError}",
                    errorStyle);
            }

            GUILayout.Space(16);

            GUI.enabled =
                !_controller.IsBusy;

            if (session == null)
            {
                if (_controller.HasSavedSession)
                {
                    if (GUILayout.Button("Continue Game", GUILayout.Height(36))) _controller.ContinueGame();
                    GUILayout.Label("Forget the saved game to create or join another. This removes your local access to its occupied slot.");
                }
                else
                {
                    if (GUILayout.Button("Create Game", GUILayout.Height(36))) _controller.CreateGame();
                    GUILayout.Label("Join code:");
                    _joinCode = GUILayout.TextField(_joinCode, 32);
                    if (GUILayout.Button("Join Game", GUILayout.Height(36))) _controller.JoinGame(_joinCode);
                }
            }
            else
            {
                if (GUILayout.Button(state == null ? "Retry Load" : "Refresh", GUILayout.Height(36))) _controller.Refresh();
                GUI.enabled = !_controller.IsBusy && _controller.IsLocalArmy(selected) && selected.canRetreat;
                if (GUILayout.Button("Retreat Selected", GUILayout.Height(36))) _controller.RetreatSelectedArmy();
                GUI.enabled = true;
                if (GUILayout.Button("Back to Menu", GUILayout.Height(30))) _controller.Disconnect();
            }

            GUI.enabled = true;
            if (_controller.HasSavedSession || session != null)
                if (GUILayout.Button("Forget Saved Game", GUILayout.Height(30))) _controller.ForgetSavedGame();

            GUILayout.Space(12);

            if (state != null)
            {
                if (GUILayout.Button(
                        "Copy Match ID",
                        GUILayout.Height(30)))
                {
                    GUIUtility.systemCopyBuffer =
                        state.matchId;

                    Debug.Log(
                        "Copied Match ID: " +
                        state.matchId);
                }
            }

            GUILayout.Space(12);

            if (state != null)
            {
                GUILayout.Label("<b>Relation colors after selection</b>", titleStyle);
                DrawLegendLine("SELECTED", new Color(1f, 0.85f, 0.15f));
                DrawLegendLine("SAME OWNER", new Color(0.25f, 0.75f, 1f));
                DrawLegendLine("ALLY", new Color(0.25f, 0.95f, 0.4f));
                DrawLegendLine("HOSTILE", new Color(1f, 0.25f, 0.25f));
                GUILayout.Space(8);
                GUILayout.Label("Select your army, then click an adjacent location to move. Opponent armies are inspection only.",
                    new GUIStyle(GUI.skin.label) { wordWrap = true });
            }

            GUILayout.EndScrollView();
            GUILayout.EndArea();
        }

        private static void DrawRelations(
            MatchStateDto state,
            ArmyDto selected,
            GUIStyle titleStyle)
        {
            if (state == null ||
                state.armies == null)
            {
                return;
            }

            GUILayout.Space(10);

            GUILayout.Label(
                "<b>Relations</b>",
                titleStyle);

            ArmyDto[] otherArmies =
                state.armies
                    .Where(
                        army =>
                            army != null &&
                            !army.isDestroyed &&
                            !string.Equals(
                                army.id,
                                selected.id,
                                StringComparison.Ordinal))
                    .OrderBy(
                        army =>
                            army.ownerId,
                        StringComparer.Ordinal)
                    .ThenBy(
                        army =>
                            army.id,
                        StringComparer.Ordinal)
                    .ToArray();

            foreach (ArmyDto army
                     in otherArmies)
            {
                ArmyRelation relation =
                    ArmyRelations.Between(
                        state,
                        selected,
                        army);

                string location =
                    army.isMoving
                        ? "MOVING"
                        : ShortId(
                            army.locationId);

                GUILayout.Label(
                    $"{ArmyRelations.Label(relation),-10} " +
                    $"Army {ShortId(army.id)} " +
                    $"Owner {ShortId(army.ownerId)} " +
                    $"Location {location}");
            }
        }

        private static void DrawLegendLine(
            string text,
            Color color)
        {
            GUIStyle style =
                new GUIStyle(
                    GUI.skin.label);

            style.normal.textColor =
                color;

            GUILayout.Label(
                "● " + text,
                style);
        }

        private static string ShortId(
            string value)
        {
            if (string.IsNullOrWhiteSpace(
                    value))
            {
                return "-";
            }

            return value.Length <= 6
                ? value
                : value.Substring(0, 6);
        }
    }
}
