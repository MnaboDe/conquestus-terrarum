using System;
using System.IO;
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
    // One-time authoring tool. Runtime loads the saved prefabs; normal builds never run this.
    public static class UiFoundationBuilder
    {
        private const string Root = "Assets/UI/Prefabs/";
        private static TMP_FontAsset _font;
        private static GameObject _panel, _button;
        private static readonly Color Background = new Color(0.035f, 0.055f, 0.08f);
        private static readonly Color Surface = new Color(0.08f, 0.115f, 0.15f);
        private static readonly Color Ink = new Color(0.88f, 0.91f, 0.92f);
        private static readonly Color Accent = new Color(0.78f, 0.65f, 0.40f);

        [MenuItem("Conquestus Terrarum/Author UI Foundation (first setup only)")]
        public static void Generate()
        {
            if (AssetDatabase.LoadAssetAtPath<GameObject>(Root + "UiRoot.prefab") != null)
                throw new InvalidOperationException("UI assets already exist. Edit the prefabs directly; this tool does not overwrite artist work.");
            Directory.CreateDirectory(Root + "Screens");
            Directory.CreateDirectory(Root + "Components");
            AssetDatabase.Refresh();
            _font = AssetDatabase.LoadAssetAtPath<TMP_FontAsset>("Assets/TextMesh Pro/Resources/Fonts & Materials/LiberationSans SDF.asset");
            if (_font == null) throw new InvalidOperationException("Import the bundled TMP Essential Resources before authoring UI.");

            var panel = Rect("Panel"); Image(panel, Surface);
            _panel = Save(panel, "Components/Panel.prefab");
            var button = Rect("PrimaryButton"); Image(button, Accent);
            var control = button.AddComponent<Button>(); control.targetGraphic = button.GetComponent<Image>();
            var colors = control.colors; colors.disabledColor = new Color(.45f, .45f, .45f, 1); control.colors = colors;
            Height(button, 64);
            var label = Text(button.transform, "Label", "Action", 28, TextAlignmentOptions.Center);
            label.color = Background; Stretch(label.gameObject, 12, 8, -12, -8);
            _button = Save(button, "Components/PrimaryButton.prefab");

            var invitation = Panel("JoinCodePanel"); var invite = invitation.AddComponent<JoinCodePanel>();
            Horizontal(invitation, 12, 12);
            var code = Text(invitation.transform, "Code", "ABC234", 30); Width(code.gameObject, 180);
            var copy = Button(invitation.transform, "CopyCode", "Copy code"); Width(copy.gameObject, 190); Height(copy.gameObject, 48);
            Wire(invite, "codeText", code); Wire(invite, "copyButton", copy);
            var invitePrefab = Save(invitation, "Components/JoinCodePanel.prefab");

            var status = Rect("WorldStatusPanel"); var statusView = status.AddComponent<WorldStatusPanel>();
            Horizontal(status, 16);
            var labels = Rect("Participation", status.transform); Vertical(labels, 2); Flexible(labels);
            var phase = Text(labels.transform, "Phase", "Waiting for second player", 30); Height(phase.gameObject, 38);
            var connection = Text(labels.transform, "Connection", "Connected", 24); Height(connection.gameObject, 30);
            var codeInstance = Instance(invitePrefab, status.transform); Width(codeInstance, 410);
            Wire(statusView, "phaseText", phase); Wire(statusView, "connectionText", connection);
            Wire(statusView, "invitation", codeInstance.GetComponent<JoinCodePanel>());
            var statusPrefab = Save(status, "Components/WorldStatusPanel.prefab");

            var army = Panel("ArmyPanel"); var armyView = army.AddComponent<ArmyPanel>(); Vertical(army, 18, 24);
            var armyTitle = Text(army.transform, "Title", "Select an army", 34); Height(armyTitle.gameObject, 52);
            var instruction = Text(army.transform, "Introduction", "Select a marker on the map to inspect an army and its orders.", 26); Height(instruction.gameObject, 110);
            var details = Rect("ArmyDetails", army.transform); Vertical(details, 16); Flexible(details);
            var owner = Text(details.transform, "Ownership", "YOUR ARMY", 25); Height(owner.gameObject, 62);
            var situation = Text(details.transform, "Situation", "At West camp", 28); Height(situation.gameObject, 100);
            var units = Text(details.transform, "Units", "0 / 0 units", 28); Height(units.gameObject, 48);
            var hp = Text(details.transform, "HitPoints", "HP 0 / 0", 32); Height(hp.gameObject, 52);
            var retreat = Button(details.transform, "Retreat", "Retreat");
            var hint = Text(details.transform, "OrderHint", "Click a connected location to move.", 25); Height(hint.gameObject, 150);
            Wire(armyView, "titleText", armyTitle); Wire(armyView, "ownershipText", owner); Wire(armyView, "situationText", situation);
            Wire(armyView, "unitsText", units); Wire(armyView, "hpText", hp); Wire(armyView, "hintText", hint);
            Wire(armyView, "details", details); Wire(armyView, "retreatButton", retreat);
            var armyPrefab = Save(army, "Components/ArmyPanel.prefab");

            var error = Panel("ErrorPanel"); error.GetComponent<Image>().color = new Color(.24f, .105f, .10f);
            var errorView = error.AddComponent<ErrorPanel>(); Horizontal(error, 20, 20);
            var message = Text(error.transform, "Message", "Connection interrupted. Your saved world is kept.", 26); Flexible(message.gameObject);
            var dismiss = Button(error.transform, "Dismiss", "Dismiss"); Width(dismiss.gameObject, 180);
            Wire(errorView, "messageText", message); Wire(errorView, "dismissButton", dismiss);
            var errorPrefab = Save(error, "Components/ErrorPanel.prefab");

            var loading = Panel("LoadingOverlay"); var loadingView = loading.AddComponent<LoadingOverlay>();
            var loadingText = Text(loading.transform, "Status", "Synchronizing world...", 26, TextAlignmentOptions.Center);
            Stretch(loadingText.gameObject, 20, 12, -20, -12); Wire(loadingView, "statusText", loadingText);
            var loadingPrefab = Save(loading, "Components/LoadingOverlay.prefab");

            var menu = Rect("MainMenuScreen"); Stretch(menu); Image(menu, Background); var menuView = menu.AddComponent<MainMenuScreen>();
            var card = Panel("WorldAccess", menu.transform); Center(card, 680, 0); Vertical(card, 18, 32);
            card.AddComponent<ContentSizeFitter>().verticalFit = ContentSizeFitter.FitMode.PreferredSize;
            var title = Text(card.transform, "Title", "CONQUESTUS TERRARUM", 38); Height(title.gameObject, 62);
            var subtitle = Text(card.transform, "Subtitle", "A persistent world. A state of your own.", 27); Height(subtitle.gameObject, 64);
            var saveInfo = Text(card.transform, "SessionHelp", "Create a world or enter an invitation code.", 25); Height(saveInfo.gameObject, 125);
            var create = Button(card.transform, "CreateWorld", "Create World");
            var joinForm = Rect("JoinWorld", card.transform); Vertical(joinForm, 12); Height(joinForm, 164);
            var input = Input(joinForm.transform);
            var join = Button(joinForm.transform, "JoinWorldButton", "Join World");
            var continueButton = Button(card.transform, "ContinueWorld", "Continue");
            var retry = Button(card.transform, "RetryLoad", "Retry Load");
            var forget = Button(card.transform, "ForgetSession", "Forget Saved World");
            var validation = Text(card.transform, "Validation", "", 25); validation.color = new Color(.97f, .72f, .55f); Height(validation.gameObject, 104);
            Wire(menuView, "createButton", create); Wire(menuView, "joinButton", join); Wire(menuView, "continueButton", continueButton);
            Wire(menuView, "forgetButton", forget); Wire(menuView, "retryButton", retry); Wire(menuView, "joinForm", joinForm);
            Wire(menuView, "joinInput", input); Wire(menuView, "validationText", validation); Wire(menuView, "savedSessionText", saveInfo);
            var menuPrefab = Save(menu, "Screens/MainMenuScreen.prefab");

            var connectingScreen = Rect("ConnectingScreen"); Stretch(connectingScreen); Image(connectingScreen, Background);
            var connectingView = connectingScreen.AddComponent<ConnectingScreen>();
            var connectCard = Panel("ConnectionStatus", connectingScreen.transform); Center(connectCard, 720, 300); Vertical(connectCard, 16, 36);
            var connectTitle = Text(connectCard.transform, "Title", "Connecting to your world", 36); Height(connectTitle.gameObject, 72);
            var connectStatus = Text(connectCard.transform, "Status", "Loading world...", 28); Height(connectStatus.gameObject, 90);
            var wait = Text(connectCard.transform, "LoadingIndication", "Please wait...", 25); Height(wait.gameObject, 52);
            Wire(connectingView, "statusText", connectStatus);
            var connectPrefab = Save(connectingScreen, "Screens/ConnectingScreen.prefab");

            var world = Rect("WorldHudScreen"); Stretch(world); var worldView = world.AddComponent<WorldHudScreen>();
            var top = Panel("TopBar", world.transform); Anchors(top, new Vector2(0, 1), Vector2.one, new Vector2(0, -104), Vector2.zero); Horizontal(top, 24, 16);
            var brand = Text(top.transform, "Title", "CONQUESTUS TERRARUM", 28); Width(brand.gameObject, 430);
            var statusInstance = Instance(statusPrefab, top.transform); Flexible(statusInstance);
            var mapArea = Rect("WorldMapArea", world.transform); Stretch(mapArea, 24, 92, -436, -128); var viewport = mapArea.AddComponent<WorldMapViewport>();
            var armyInstance = Instance(armyPrefab, world.transform);
            Anchors(armyInstance, new Vector2(1, 0), Vector2.one, new Vector2(-412, 92), new Vector2(-24, -128));
            var nav = Panel("WorldNavigation", world.transform); Anchors(nav, Vector2.zero, new Vector2(1, 0), Vector2.zero, new Vector2(0, 72)); Horizontal(nav, 24, 12);
            var mapLabel = Text(nav.transform, "CurrentSection", "WORLD MAP", 26); Flexible(mapLabel.gameObject);
            var back = Button(nav.transform, "BackToMenu", "Back to Menu"); Width(back.gameObject, 260);
            Wire(worldView, "statusPanel", statusInstance.GetComponent<WorldStatusPanel>()); Wire(worldView, "armyPanel", armyInstance.GetComponent<ArmyPanel>());
            Wire(worldView, "mapArea", viewport); Wire(worldView, "menuButton", back);
            var worldPrefab = Save(world, "Screens/WorldHudScreen.prefab");

            var ui = Rect("UiRoot"); var canvas = ui.AddComponent<Canvas>(); canvas.renderMode = RenderMode.ScreenSpaceOverlay;
            var scaler = ui.AddComponent<CanvasScaler>(); scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
            scaler.referenceResolution = new Vector2(1920, 1080); scaler.matchWidthOrHeight = .5f;
            ui.AddComponent<GraphicRaycaster>(); var flow = ui.AddComponent<UiFlowController>();
            var menuInstance = Instance(menuPrefab, ui.transform);
            var connectInstance = Instance(connectPrefab, ui.transform);
            var worldInstance = Instance(worldPrefab, ui.transform);
            var overlays = Rect("OverlayLayer", ui.transform); Stretch(overlays);
            var errorInstance = Instance(errorPrefab, overlays.transform);
            Anchors(errorInstance, Vector2.zero, new Vector2(1, 0), new Vector2(24, 100), new Vector2(-436, 270));
            var loadingInstance = Instance(loadingPrefab, overlays.transform); Center(loadingInstance, 760, 70);
            Wire(flow, "mainMenu", menuInstance.GetComponent<MainMenuScreen>()); Wire(flow, "connecting", connectInstance.GetComponent<ConnectingScreen>());
            Wire(flow, "world", worldInstance.GetComponent<WorldHudScreen>()); Wire(flow, "errorOverlay", errorInstance.GetComponent<ErrorPanel>());
            Wire(flow, "loadingOverlay", loadingInstance.GetComponent<LoadingOverlay>());
            connectInstance.SetActive(false); worldInstance.SetActive(false); errorInstance.SetActive(false); loadingInstance.SetActive(false);
            var uiPrefab = Save(ui, "UiRoot.prefab");

            var scene = EditorSceneManager.OpenScene("Assets/Scenes/SampleScene.unity", OpenSceneMode.Single);
            var client = new GameObject("ConquestusTerrarumClient");
            client.AddComponent<ConquestusApiClient>(); var controller = client.AddComponent<MatchController>();
            var map = client.AddComponent<MapPresenter>(); client.AddComponent<PointerInput>();
            var uiInstance = Instance(uiPrefab, client.transform);
            var bootstrap = client.AddComponent<ClientBootstrap>(); Wire(bootstrap, "controller", controller);
            Wire(bootstrap, "ui", uiInstance.GetComponent<UiFlowController>()); Wire(bootstrap, "map", map);
            var eventSystem = new GameObject("EventSystem", typeof(EventSystem), typeof(InputSystemUIInputModule));
            eventSystem.GetComponent<InputSystemUIInputModule>().AssignDefaultActions();
            EditorSceneManager.SaveScene(scene);
            AssetDatabase.SaveAssets();
            ApplyReview();
            Debug.Log("UI foundation assets authored; edit the saved prefabs from now on.");
        }

        [MenuItem("Conquestus Terrarum/Upgrade UI Foundation (identity and confirmation)")]
        public static void ApplyReview()
        {
            // A focused, repeatable upgrade: never regenerate existing screen layouts or styles.
            _font = AssetDatabase.LoadAssetAtPath<TMP_FontAsset>("Assets/TextMesh Pro/Resources/Fonts & Materials/LiberationSans SDF.asset");
            _panel = AssetDatabase.LoadAssetAtPath<GameObject>(Root + "Components/Panel.prefab");
            _button = AssetDatabase.LoadAssetAtPath<GameObject>(Root + "Components/PrimaryButton.prefab");
            if (_font == null || _panel == null || _button == null)
                throw new InvalidOperationException("Existing UI foundation assets are required for this upgrade.");

            var menu = PrefabUtility.LoadPrefabContents(Root + "Screens/MainMenuScreen.prefab");
            try
            {
                var view = menu.GetComponent<MainMenuScreen>();
                if (Reference(view, "interactionGroup") == null)
                {
                    // Prefab preview scenes may supply a temporary DontSave CanvasGroup.
                    // Only bind a persistent component that will survive prefab serialization.
                    var group = menu.GetComponents<CanvasGroup>().FirstOrDefault(g => (g.hideFlags & HideFlags.DontSaveInEditor) == 0)
                        ?? menu.AddComponent<CanvasGroup>();
                    group.hideFlags = HideFlags.None;
                    Wire(view, "interactionGroup", group);
                    PrefabUtility.SaveAsPrefabAsset(menu, Root + "Screens/MainMenuScreen.prefab");
                }
            }
            finally { PrefabUtility.UnloadPrefabContents(menu); }

            var world = PrefabUtility.LoadPrefabContents(Root + "Screens/WorldHudScreen.prefab");
            try
            {
                var view = world.GetComponent<WorldHudScreen>();
                if (Reference(view, "localPlayerText") == null)
                {
                    var nav = world.transform.Find("WorldNavigation");
                    if (nav == null) throw new InvalidOperationException("WorldNavigation is required for the identity label.");
                    var identity = Text(nav, "LocalPlayerIdentity", "Player identity", 22, TextAlignmentOptions.MidlineRight);
                    identity.color = new Color(.6f, .68f, .72f); Width(identity.gameObject, 560);
                    identity.transform.SetSiblingIndex(nav.childCount - 2);
                    Wire(view, "localPlayerText", identity);
                    PrefabUtility.SaveAsPrefabAsset(world, Root + "Screens/WorldHudScreen.prefab");
                }
            }
            finally { PrefabUtility.UnloadPrefabContents(world); }

            const string dialogPath = "Dialogs/ConfirmationDialog.prefab";
            var dialogPrefab = AssetDatabase.LoadAssetAtPath<GameObject>(Root + dialogPath);
            if (dialogPrefab == null)
            {
                Directory.CreateDirectory(Root + "Dialogs"); AssetDatabase.Refresh();
                var dialog = Rect("ConfirmationDialog"); Stretch(dialog); Image(dialog, new Color(0, 0, 0, .65f));
                var view = dialog.AddComponent<ConfirmationDialog>();
                var card = Panel("ConfirmationCard", dialog.transform); Center(card, 760, 400); Vertical(card, 18, 32);
                var title = Text(card.transform, "ConfirmationTitle", "Forget saved world?", 34); Height(title.gameObject, 64);
                var warning = Text(card.transform, "ConfirmationMessage",
                    "This removes this device's access to your player in this world. The world and its occupied player slot remain on the server. An invitation code cannot restore this credential.", 26);
                Height(warning.gameObject, 140);
                var actions = Rect("ConfirmationActions", card.transform); Horizontal(actions, 18); Height(actions, 64);
                actions.GetComponent<HorizontalLayoutGroup>().childAlignment = TextAnchor.MiddleCenter;
                var cancel = Button(actions.transform, "CancelForget", "Keep saved world"); Width(cancel.gameObject, 300);
                var confirm = Button(actions.transform, "ConfirmForget", "Forget Saved World"); Width(confirm.gameObject, 300);
                Wire(view, "confirmButton", confirm); Wire(view, "cancelButton", cancel);
                dialogPrefab = Save(dialog, dialogPath);
            }

            // Import changed nested dependencies before loading their parent prefab.
            AssetDatabase.Refresh(ImportAssetOptions.ForceSynchronousImport);
            var root = PrefabUtility.LoadPrefabContents(Root + "UiRoot.prefab");
            try
            {
                var flow = root.GetComponent<UiFlowController>();
                if (Reference(flow, "forgetConfirmation") == null)
                {
                    var overlays = root.transform.Find("OverlayLayer");
                    if (overlays == null) throw new InvalidOperationException("OverlayLayer is required for confirmation.");
                    var dialog = Instance(dialogPrefab, overlays); Stretch(dialog); dialog.SetActive(false);
                    Wire(flow, "forgetConfirmation", dialog.GetComponent<ConfirmationDialog>());
                    PrefabUtility.SaveAsPrefabAsset(root, Root + "UiRoot.prefab");
                }
            }
            finally { PrefabUtility.UnloadPrefabContents(root); }
            AssetDatabase.SaveAssets();
            AssetDatabase.Refresh(ImportAssetOptions.ForceSynchronousImport);
            Debug.Log("UI identity and Forget confirmation assets are wired; existing artist layouts were retained.");
        }

        private static UnityEngine.Object Reference(UnityEngine.Object component, string property) =>
            new SerializedObject(component).FindProperty(property).objectReferenceValue;

        private static GameObject Rect(string name, Transform parent = null)
        {
            var result = new GameObject(name, typeof(RectTransform));
            if (parent != null) result.transform.SetParent(parent, false);
            return result;
        }
        private static GameObject Panel(string name, Transform parent = null)
        {
            var result = Instance(_panel, parent); result.name = name; return result;
        }
        private static GameObject Instance(GameObject prefab, Transform parent) => (GameObject)PrefabUtility.InstantiatePrefab(prefab, parent);
        private static GameObject Save(GameObject instance, string path)
        {
            var prefab = PrefabUtility.SaveAsPrefabAsset(instance, Root + path); UnityEngine.Object.DestroyImmediate(instance); return prefab;
        }
        private static Image Image(GameObject target, Color color) { var image = target.AddComponent<Image>(); image.color = color; return image; }
        private static TMP_Text Text(Transform parent, string name, string content, int size, TextAlignmentOptions align = TextAlignmentOptions.TopLeft)
        {
            var target = Rect(name, parent); var text = target.AddComponent<TextMeshProUGUI>(); text.font = _font;
            text.text = content; text.fontSize = size; text.color = Ink; text.alignment = align; text.raycastTarget = false;
            text.textWrappingMode = TextWrappingModes.Normal; return text;
        }
        private static Button Button(Transform parent, string name, string text)
        {
            var target = Instance(_button, parent); target.name = name; target.GetComponentInChildren<TMP_Text>().text = text; return target.GetComponent<Button>();
        }
        private static TMP_InputField Input(Transform parent)
        {
            var target = Panel("JoinCodeInput", parent); target.GetComponent<Image>().color = new Color(.14f, .19f, .23f); Height(target, 72);
            var field = target.AddComponent<TMP_InputField>(); field.targetGraphic = target.GetComponent<Image>();
            var viewport = Rect("TextArea", target.transform); Stretch(viewport, 18, 12, -18, -12); viewport.AddComponent<RectMask2D>();
            var text = Text(viewport.transform, "Text", "", 28, TextAlignmentOptions.MidlineLeft); Stretch(text.gameObject);
            var placeholder = Text(viewport.transform, "Placeholder", "Invitation code", 28, TextAlignmentOptions.MidlineLeft); Stretch(placeholder.gameObject);
            placeholder.color = new Color(.6f, .68f, .72f);
            field.textViewport = (RectTransform)viewport.transform; field.textComponent = text; field.placeholder = placeholder;
            field.characterLimit = 32; field.lineType = TMP_InputField.LineType.SingleLine; return field;
        }
        private static void Vertical(GameObject target, int gap, int padding = 0)
        {
            var group = target.AddComponent<VerticalLayoutGroup>(); group.padding = new RectOffset(padding, padding, padding, padding);
            group.spacing = gap; group.childControlWidth = group.childControlHeight = true; group.childForceExpandHeight = false; group.childForceExpandWidth = true;
        }
        private static void Horizontal(GameObject target, int gap, int padding = 0)
        {
            var group = target.AddComponent<HorizontalLayoutGroup>(); group.padding = new RectOffset(padding, padding, padding, padding);
            group.spacing = gap; group.childControlWidth = group.childControlHeight = true; group.childForceExpandWidth = false; group.childForceExpandHeight = true;
            group.childAlignment = TextAnchor.MiddleLeft;
        }
        private static LayoutElement Layout(GameObject target) => target.GetComponent<LayoutElement>() ?? target.AddComponent<LayoutElement>();
        private static void Height(GameObject target, int height) { var item = Layout(target); item.minHeight = item.preferredHeight = height; }
        private static void Width(GameObject target, int width) { var item = Layout(target); item.minWidth = item.preferredWidth = width; }
        private static void Flexible(GameObject target) => Layout(target).flexibleWidth = 1;
        private static void Center(GameObject target, int width, int height)
        {
            var rect = (RectTransform)target.transform; rect.anchorMin = rect.anchorMax = rect.pivot = new Vector2(.5f, .5f); rect.sizeDelta = new Vector2(width, height);
        }
        private static void Stretch(GameObject target, int left = 0, int bottom = 0, int right = 0, int top = 0) =>
            Anchors(target, Vector2.zero, Vector2.one, new Vector2(left, bottom), new Vector2(right, top));
        private static void Anchors(GameObject target, Vector2 min, Vector2 max, Vector2 offsetMin, Vector2 offsetMax)
        {
            var rect = (RectTransform)target.transform; rect.anchorMin = min; rect.anchorMax = max; rect.offsetMin = offsetMin; rect.offsetMax = offsetMax;
        }
        private static void Wire(UnityEngine.Object component, string property, UnityEngine.Object value)
        {
            var serialized = new SerializedObject(component); var field = serialized.FindProperty(property);
            if (field == null) throw new InvalidOperationException("Missing UI reference: " + property);
            field.objectReferenceValue = value; serialized.ApplyModifiedPropertiesWithoutUndo();
        }
    }
}
