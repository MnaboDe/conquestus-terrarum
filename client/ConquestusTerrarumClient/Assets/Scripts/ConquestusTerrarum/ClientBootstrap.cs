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
            if (UnityEngine.Object.FindFirstObjectByType<MatchController>() != null)
                return;

            GameObject root =
                new GameObject("ConquestusTerrarumClient");

            UnityEngine.Object.DontDestroyOnLoad(root);

            root.AddComponent<MatchController>();
            root.AddComponent<MapPresenter>();
            root.AddComponent<PointerInput>();
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
            if (_controller == null)
                return;

            GUILayout.BeginArea(
                new Rect(15, 15, 560, 620),
                GUI.skin.box);

            GUIStyle titleStyle =
                new GUIStyle(GUI.skin.label);

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

            if (state != null)
            {
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

            ArmyDto selected =
                _controller.SelectedArmy;

            if (selected != null)
            {
                GUILayout.Space(12);

                GUILayout.Label(
                    "<b>Selected army</b>",
                    titleStyle);

                GUILayout.Label(
                    $"Army: {selected.id}");

                GUILayout.Label(
                    $"Owner: {selected.ownerId}");

                GUILayout.Label(
                    $"Moving: {selected.isMoving}");

                GUILayout.Label(
                    $"In battle: {selected.isInBattle}");

                GUILayout.Label(
                    $"Can retreat: {selected.canRetreat}");

                UnitDto[] units =
                    selected.units ?? Array.Empty<UnitDto>();

                int livingUnits =
                    units.Count(
                        unit =>
                            unit != null &&
                            unit.isAlive);

                double currentHp =
                    units
                        .Where(unit => unit != null)
                        .Sum(unit => unit.currentHitPoints);

                double maxHp =
                    units
                        .Where(unit => unit != null)
                        .Sum(unit => unit.maxHitPoints);

                GUILayout.Label(
                    $"Units: {livingUnits}/{units.Length}");

                GUILayout.Label(
                    $"HP: {currentHp:0.#}/{maxHp:0.#}");
            }

            if (!string.IsNullOrWhiteSpace(
                    _controller.LastError))
            {
                GUILayout.Space(12);

                GUIStyle errorStyle =
                    new GUIStyle(GUI.skin.label);

                errorStyle.wordWrap = true;
                errorStyle.normal.textColor =
                    new Color(1f, 0.45f, 0.4f);

                GUILayout.Label(
                    $"ERROR: {_controller.LastError}",
                    errorStyle);
            }

            GUILayout.Space(16);

            GUI.enabled =
                !_controller.IsBusy;

            if (GUILayout.Button(
                    "New Demo Match",
                    GUILayout.Height(36)))
            {
                _controller.CreateDemoMatch();
            }

            GUI.enabled =
                !_controller.IsBusy &&
                _controller.State != null;

            if (GUILayout.Button(
                    "Refresh",
                    GUILayout.Height(36)))
            {
                _controller.Refresh();
            }

            GUI.enabled =
                !_controller.IsBusy &&
                _controller.SelectedArmy != null &&
                _controller.SelectedArmy.canRetreat;

            if (GUILayout.Button(
                    "Retreat Selected",
                    GUILayout.Height(36)))
            {
                _controller.RetreatSelectedArmy();
            }

            GUI.enabled = true;

            GUILayout.Space(12);

            GUILayout.Label(
                "Click an army, then click an adjacent location.");

            GUILayout.Label(
                "Moving armies are animated locally using server timestamps.");

            GUILayout.EndArea();
        }
    }
}