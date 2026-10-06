using System;
using System.Linq;
using ConquestusTerrarum.Client.UI;
using TMPro;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.InputSystem.UI;
using UnityEngine.UI;

namespace ConquestusTerrarum.Client.Editor
{
    // Read-only asset/reference checks, with a scene-save check in isolated batch verification.
    public static class UiFoundationValidation
    {
        private const string ScenePath = "Assets/Scenes/SampleScene.unity";
        private const string Root = "Assets/UI/Prefabs/";
        private static readonly string[] Prefabs =
        {
            "UiRoot", "Screens/MainMenuScreen", "Screens/ConnectingScreen", "Screens/WorldHudScreen",
            "Components/Panel", "Components/PrimaryButton", "Components/ArmyPanel", "Components/WorldStatusPanel",
            "Components/JoinCodePanel", "Components/ErrorPanel", "Components/LoadingOverlay", "Dialogs/ConfirmationDialog"
        };

        // Batch entry point for the additive milestone upgrade and its asset checks.
        public static void UpgradeAndValidate()
        {
            UiFoundationBuilder.ApplyReview();
            Validate();
        }

        [MenuItem("Conquestus Terrarum/Validate UI Foundation")]
        public static void Validate()
        {
            if (!Application.isBatchMode)
                for (var i = 0; i < UnityEngine.SceneManagement.SceneManager.sceneCount; i++)
                    Check(!UnityEngine.SceneManagement.SceneManager.GetSceneAt(i).isDirty, "Save open scene changes before validation.");

            foreach (var path in Prefabs)
            {
                Check(AssetDatabase.LoadAssetAtPath<GameObject>(Root + path + ".prefab") != null, "Missing prefab: " + path);
                var prefab = PrefabUtility.LoadPrefabContents(Root + path + ".prefab");
                try { ValidateHierarchy(prefab); }
                finally { PrefabUtility.UnloadPrefabContents(prefab); }
            }

            Check(EditorBuildSettings.scenes.Any(s => s.enabled && s.path == ScenePath), "Playable scene is not enabled in Build Settings.");
            var scene = EditorSceneManager.OpenScene(ScenePath, OpenSceneMode.Single);
            var roots = scene.GetRootGameObjects();
            foreach (var root in roots) ValidateHierarchy(root);
            var canvas = One<Canvas>(roots);
            Check(canvas.renderMode == RenderMode.ScreenSpaceOverlay && canvas.GetComponent<GraphicRaycaster>() != null,
                "UI requires an overlay Canvas with a graphic raycaster.");
            var scaler = canvas.GetComponent<CanvasScaler>();
            Check(scaler != null && scaler.uiScaleMode == CanvasScaler.ScaleMode.ScaleWithScreenSize &&
                scaler.referenceResolution.x > 0 && scaler.referenceResolution.y > 0, "CanvasScaler is invalid.");
            var eventSystem = One<EventSystem>(roots);
            var input = eventSystem.GetComponent<InputSystemUIInputModule>();
            Check(input != null && input.actionsAsset != null && input.point != null && input.leftClick != null && input.submit != null,
                "EventSystem requires bound Input System UI actions.");
            var flow = One<UiFlowController>(roots);
            Check(flow.gameObject == canvas.gameObject, "UI flow must belong to the scene Canvas.");
            Check(One<MainMenuScreen>(roots).gameObject.activeSelf && !One<ConnectingScreen>(roots).gameObject.activeSelf &&
                !One<WorldHudScreen>(roots).gameObject.activeSelf, "Authored launch visibility must show Main Menu only.");
            Check(!One<ConfirmationDialog>(roots).gameObject.activeSelf, "Forget confirmation must be hidden at launch.");
            var bootstrap = One<ClientBootstrap>(roots);
            Check(Reference(bootstrap, "ui") == flow && Reference(bootstrap, "controller") == One<MatchController>(roots) &&
                Reference(bootstrap, "map") == One<MapPresenter>(roots), "Bootstrap references do not match the playable scene.");
            One<PointerInput>(roots);
            One<ConquestusApiClient>(roots);
            Check(PrefabUtility.IsPartOfPrefabInstance(flow), "Runtime UI must use the authored UiRoot prefab.");

            // Ensure the validation also rejects a real missing required reference.
            var probe = new GameObject("MissingReferenceValidationProbe");
            try
            {
                var rejected = false;
                try { Required(probe.AddComponent<ConfirmationDialog>(), "confirmButton", "cancelButton"); }
                catch (InvalidOperationException) { rejected = true; }
                Check(rejected, "Missing-reference validation did not reject an unwired dialog.");
            }
            finally { UnityEngine.Object.DestroyImmediate(probe); }
            if (Application.isBatchMode) Check(EditorSceneManager.SaveScene(scene), "Playable scene did not save successfully.");
            Debug.Log("UI FOUNDATION VALIDATION PASSED: 12 prefabs, scene load/save, launch visibility, Canvas/EventSystem/TMP, no missing scripts or required references; negative reference probe rejected.");
        }

        private static void ValidateHierarchy(GameObject root)
        {
            foreach (var node in root.GetComponentsInChildren<Transform>(true))
                Check(GameObjectUtility.GetMonoBehavioursWithMissingScriptCount(node.gameObject) == 0, "Missing script on " + node.name);
            foreach (var component in root.GetComponentsInChildren<MonoBehaviour>(true))
            {
                if (component is ClientBootstrap) Required(component, "controller", "ui", "map");
                else if (component is UiFlowController) Required(component, "mainMenu", "connecting", "world", "errorOverlay", "loadingOverlay", "forgetConfirmation");
                else if (component is MainMenuScreen) Required(component, "createButton", "joinButton", "continueButton", "forgetButton", "retryButton",
                    "joinForm", "joinInput", "validationText", "savedSessionText", "interactionGroup");
                else if (component is ConnectingScreen) Required(component, "statusText");
                else if (component is WorldHudScreen) Required(component, "statusPanel", "armyPanel", "mapArea", "menuButton", "localPlayerText");
                else if (component is ArmyPanel) Required(component, "titleText", "ownershipText", "situationText", "unitsText", "hpText", "hintText", "details", "retreatButton");
                else if (component is WorldStatusPanel) Required(component, "phaseText", "connectionText", "invitation");
                else if (component is JoinCodePanel) Required(component, "codeText", "copyButton");
                else if (component is ErrorPanel) Required(component, "messageText", "dismissButton");
                else if (component is LoadingOverlay) Required(component, "statusText");
                else if (component is ConfirmationDialog)
                {
                    Required(component, "confirmButton", "cancelButton");
                    var backdrop = component.GetComponent<Image>();
                    Check(backdrop != null && backdrop.raycastTarget, "Confirmation needs a raycastable backdrop.");
                }
            }
            foreach (var text in root.GetComponentsInChildren<TMP_Text>(true))
                Check(text.font != null && text.fontSharedMaterial != null, "Missing TMP font/material on " + text.name);
            foreach (var input in root.GetComponentsInChildren<TMP_InputField>(true))
                Check(input.textViewport != null && input.textComponent != null && input.placeholder != null, "Unwired input field: " + input.name);
            foreach (var button in root.GetComponentsInChildren<Button>(true))
                Check(button.targetGraphic != null, "Missing button target graphic: " + button.name);
        }

        private static T One<T>(GameObject[] roots) where T : Component
        {
            var matches = roots.SelectMany(r => r.GetComponentsInChildren<T>(true)).ToArray();
            Check(matches.Length == 1, "Expected exactly one scene " + typeof(T).Name + ", found " + matches.Length);
            return matches[0];
        }

        private static UnityEngine.Object Reference(UnityEngine.Object component, string name)
        {
            var property = new SerializedObject(component).FindProperty(name);
            Check(property != null, "Required serialized field is absent: " + component.GetType().Name + "." + name);
            return property.objectReferenceValue;
        }

        private static void Required(UnityEngine.Object component, params string[] fields)
        {
            foreach (var field in fields)
                Check(Reference(component, field) != null, "Missing required reference: " + component.name + "." + field);
        }

        private static void Check(bool condition, string message)
        {
            if (!condition) throw new InvalidOperationException("UI validation: " + message);
        }
    }
}
