// Verification only: copy into an isolated Unity project's Assets; never ship this harness.
using System;
using System.Collections;
using System.IO;
using System.Linq;
using ConquestusTerrarum.Client;
using ConquestusTerrarum.Client.UI;
using TMPro;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;

public sealed class UiFoundationSmoke : MonoBehaviour
{
    [Serializable] private sealed class PublicWorld { public string matchId, playerId, joinCode, armyId; }
    private sealed class JoinRecords : ISessionRecordStore
    {
        private const string Key = "ConquestusUiSmoke.Joiner";
        public bool HasRecord => PlayerPrefs.HasKey(Key);
        public string Read() => PlayerPrefs.GetString(Key);
        public void Write(string value) { PlayerPrefs.SetString(Key, value); PlayerPrefs.Save(); }
        public void Clear() { PlayerPrefs.DeleteKey(Key); PlayerPrefs.Save(); }
    }
    private MatchController _client;
    private UiFlowController _ui;
    private PointerInput _pointer;
    private ISessionStorage _store;
    private string _mode, _share, _result, _output;
    private bool _done;
    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
    private static void Initialize()
    {
        if (Arg("--ct-ui-mode") != null) new GameObject("UiVerification").AddComponent<UiFoundationSmoke>();
    }
    private static string Arg(string key)
    {
        var args = Environment.GetCommandLineArgs(); var index = Array.IndexOf(args, key);
        return index >= 0 && index + 1 < args.Length ? args[index + 1] : null;
    }
    private void Start()
    {
        _mode = Arg("--ct-ui-mode"); _share = Arg("--ct-share"); _result = Arg("--ct-result"); _output = Arg("--ct-output");
        Application.logMessageReceived += OnLog;
        StartCoroutine(Run());
    }
    private IEnumerator Run()
    {
        yield return null;
        _client = UnityEngine.Object.FindAnyObjectByType<MatchController>();
        _ui = UnityEngine.Object.FindAnyObjectByType<UiFlowController>();
        _pointer = _client.GetComponent<PointerInput>();
        _store = new SessionStorage(_mode == "join" ? (ISessionRecordStore)new JoinRecords() : new PlayerPrefsSessionRecordStore());
        _client.SetSessionStorage(_store);
        yield return null;
        Require(_ui.State == ClientUiState.MainMenu && EventSystem.current != null, "Serialized Main Menu and EventSystem initialize");
        Require(_ui.GetComponent<Canvas>() != null && _ui.GetComponent<GraphicRaycaster>() != null, "Real uGUI root");
        if (_mode == "empty")
        {
            Require(!_client.HasSavedSession && !Control("ContinueWorld").gameObject.activeInHierarchy && Control("CreateWorld").gameObject.activeInHierarchy,
                "Forget survives restart and menu updates");
        }
        else if (_mode == "resume" || _mode == "offline")
        {
            var expected = JsonUtility.FromJson<PublicWorld>(File.ReadAllText(_share));
            var saved = _store.Load();
            Require(saved != null && Control("ContinueWorld").gameObject.activeInHierarchy, "Saved session offers Continue");
            Click("ContinueWorld");
            Require(_ui.State == ClientUiState.Connecting, "Continue presents Connecting");
            if (_mode == "offline")
            {
                yield return Wait(() => !_client.IsBusy && _ui.State == ClientUiState.MainMenu, "offline menu recovery");
                Require(_client.HasSavedSession && _store.Load().PlayerToken == saved.PlayerToken && !string.IsNullOrEmpty(Text("Validation").text),
                    "Offline error is visible and retains credential");
                Require(Control("RetryLoad").gameObject.activeInHierarchy, "Retry Load is available for issued credential");
                yield return Capture("offline-error", 1920, 1080);
                File.WriteAllText(_share + ".retry-ready", "ready");
                yield return Wait(() => File.Exists(_share + ".online"), "API recovery");
                Click("RetryLoad");
            }
            yield return Connected();
            Require(_client.Session.MatchId == expected.matchId && _client.Session.PlayerToken == saved.PlayerToken && _client.State.phase == "active",
                "Continue restores same Active world and token");
            Click("BackToMenu");
            Require(_ui.State == ClientUiState.MainMenu, "Back preserves menu flow");
            if (_mode == "resume")
            {
                yield return ForgetViaUi();
                Require(!_client.HasSavedSession && Control("CreateWorld").gameObject.activeInHierarchy, "Explicit Forget returns fresh menu");
            }
        }
        else
        {
            Require(!_client.HasSavedSession, "Fresh isolated UI profile");
            if (_mode == "create")
            {
                yield return Capture("main-menu-clean", 1920, 1080);
                var input = _ui.GetComponentInChildren<TMP_InputField>(true);
                foreach (var invalid in new[] { "", "ABC", "ABO234" })
                {
                    input.text = invalid; Click("JoinWorldButton");
                    Require(!_client.IsBusy && _client.Session == null && Text("Validation").text.Contains("six-character"), "Inline join validation");
                }
                _client.JoinGame("ABC"); // Public controller callers also must not emit a red error.
                yield return null;
                Require(_client.LastError != null, "Controller validation remains visible");
                _client.DismissError(); input.text = "";
                foreach (var size in new[] { new Vector2Int(1280, 720), new Vector2Int(1920, 1080), new Vector2Int(2560, 1440) })
                    yield return Capture("main-menu-" + size.x, size.x, size.y);
                Click("CreateWorld");
            }
            else
            {
                yield return Wait(() => File.Exists(_share), "creator invitation");
                var code = JsonUtility.FromJson<PublicWorld>(File.ReadAllText(_share)).joinCode;
                _ui.GetComponentInChildren<TMP_InputField>(true).text = "  " + code.ToLowerInvariant() + "  ";
                Click("JoinWorldButton");
            }
            Require(_ui.State == ClientUiState.Connecting && !Control("CreateWorld").gameObject.activeInHierarchy, "Requests use one Connecting root");
            yield return Connected();
            var worldRoot = _ui.GetComponentInChildren<WorldHudScreen>(true).gameObject.GetEntityId();
            Require(Text("LocalPlayerIdentity").text == "Player " + _client.LocalPlayerId &&
                !Text("LocalPlayerIdentity").text.Contains(_client.Session.PlayerToken), "Public player identity is displayed without credential");
            if (_mode == "create")
            {
                Require(_client.State.phase == "waitingForPlayers", "World screen contains Waiting phase");
                var own = _client.State.armies.First(_client.IsLocalArmy);
                SelectOnMap(own.id); yield return null;
                Require(!_client.CanMoveSelected && !Control("Retreat").interactable, "Waiting disables orders");
                Click("CopyCode"); Require(GUIUtility.systemCopyBuffer == _client.Session.JoinCode, "Copy invitation works");
                foreach (var size in new[] { new Vector2Int(1280, 720), new Vector2Int(1920, 1080), new Vector2Int(2560, 1440) })
                    yield return Capture("waiting-world-" + size.x, size.x, size.y);
                var saved = _store.Load();
                Click("BackToMenu"); Require(_ui.State == ClientUiState.MainMenu && _client.HasSavedSession, "Back keeps save");
                Click("ContinueWorld"); yield return Connected();
                Require(_client.State.phase == "waitingForPlayers" && _client.Session.PlayerToken == saved.PlayerToken, "Waiting Continue reuses credential");
                File.WriteAllText(_share, JsonUtility.ToJson(new PublicWorld { matchId = _client.Session.MatchId, playerId = _client.LocalPlayerId,
                    joinCode = _client.Session.JoinCode, armyId = own.id }));
            }
            yield return Wait(() => _client.State.phase == "active" && !_client.IsBusy, "automatic activation");
            Require(_ui.State == ClientUiState.World && worldRoot == _ui.GetComponentInChildren<WorldHudScreen>(true).gameObject.GetEntityId(),
                "Activation preserves the World prefab instance");
            yield return null;
            var enemy = _client.State.armies.First(a => !_client.IsLocalArmy(a));
            SelectOnMap(enemy.id); yield return null;
            Require(!Control("Retreat").gameObject.activeInHierarchy && Text("Ownership").text.Contains("INSPECTION"), "Foreign army is inspection-only");
            var controlled = _client.State.armies.First(_client.IsLocalArmy);
            SelectOnMap(controlled.id); yield return null;
            Require(Text("Ownership").text == "YOUR ARMY" && _client.CanMoveSelected, "Own army selection supplies orders");
            var protectedPoint = Center((RectTransform)Control("BackToMenu").transform);
            Require(PointerInput.IsUiBlocking(protectedPoint), "Graphic raycasts block UI pointer");
            var selected = _client.SelectedArmyId; var before = _client.State;
            _pointer.HandlePointer(protectedPoint);
            Require(selected == _client.SelectedArmyId && ReferenceEquals(before, _client.State) && !_client.IsBusy, "UI click cannot select or move underneath");
            File.WriteAllText(_share + ".ready." + _mode, "ready");
            yield return Wait(() => File.Exists(_share + ".ready.create") && File.Exists(_share + ".ready.join"), "both UI clients ready");
            var center = UnityEngine.Object.FindObjectsByType<LocationView>(FindObjectsSortMode.None)
                .Single(l => _client.State.locations.Single(d => d.id == l.LocationId).name == "Crossroads");
            var point = (Vector2)Camera.main.WorldToScreenPoint(center.transform.position);
            Require(!PointerInput.IsUiBlocking(point), "Map is not covered by invisible UI graphics");
            _pointer.HandlePointer(point);
            yield return Wait(() => !_client.IsBusy && _client.State.armies.Single(a => a.id == controlled.id).isMoving, "map click starts movement");
            Require(_ui.State == ClientUiState.World, "Command loading remains an overlay inside World");
            yield return Wait(() => _client.State.armies.Single(a => a.id == controlled.id).isInBattle, "battle through normal synchronization");
            _client.SelectArmy(controlled.id); yield return null;
            var hp = _client.SelectedArmy.units.Sum(u => u.currentHitPoints);
            yield return Wait(() => _client.SelectedArmy.units.Sum(u => u.currentHitPoints) < hp, "focused HP polling");
            yield return Wait(() => Control("Retreat").interactable, "retreat eligibility reaches panel");
            yield return Capture("battle-report", 1920, 1080);
            File.WriteAllText(_share + ".hp." + _mode, "ready");
            if (_mode == "create")
            {
                yield return Wait(() => File.Exists(_share + ".hp.join"), "joiner live HP");
                Click("Retreat");
                yield return Wait(() => !_client.IsBusy && _client.SelectedArmy.isMoving && _client.SelectedArmy.movement.kind == "retreat", "UI retreat order");
                File.WriteAllText(_share + ".retreated", "ready");
            }
            else
            {
                yield return Wait(() => File.Exists(_share + ".retreated"), "creator retreats");
                yield return Wait(() => _client.State.armies.Any(a => !_client.IsLocalArmy(a) &&
                    (a.movement?.kind == "retreat" || a.locationId == _client.State.locations.Single(l => l.name == "West camp").id)), "retreat feed synchronization");
            }
            var credential = _store.Load();
            Click("BackToMenu"); Require(_ui.State == ClientUiState.MainMenu, "World exits to menu");
            Click("ContinueWorld"); yield return Connected();
            Require(_client.Session.PlayerToken == credential.PlayerToken, "Active Continue preserves identity");
            Click("BackToMenu");
            if (_mode == "join") yield return ForgetViaUi();
        }
        Finish(true, _mode + " UI flow passed");
    }
    private Button Control(string name) => _ui.GetComponentsInChildren<Button>(true).Single(b => b.gameObject.name == name);
    private TMP_Text Text(string name) => _ui.GetComponentsInChildren<TMP_Text>(true).Single(t => t.gameObject.name == name);
    private void Click(string name)
    {
        var button = Control(name);
        Require(button.isActiveAndEnabled && button.IsInteractable(), "UI action available: " + name);
        ExecuteEvents.Execute(button.gameObject, new PointerEventData(EventSystem.current) { button = PointerEventData.InputButton.Left }, ExecuteEvents.pointerClickHandler);
    }
    private IEnumerator ForgetViaUi()
    {
        var saved = _store.Load();
        var dialog = _ui.GetComponentInChildren<ConfirmationDialog>(true);
        var menu = _ui.GetComponentInChildren<MainMenuScreen>(true).GetComponent<CanvasGroup>();
        Click("ForgetSession");
        Require(dialog.gameObject.activeInHierarchy && !menu.interactable && _client.HasSavedSession &&
            _store.Load().PlayerToken == saved.PlayerToken, "Opening confirmation keeps save and disables background controls");
        Canvas.ForceUpdateCanvases();
        yield return null; // Test the next pointer press after the newly opened dialog has rendered.
        var hits = new System.Collections.Generic.List<RaycastResult>();
        EventSystem.current.RaycastAll(new PointerEventData(EventSystem.current) { position = new Vector2(8, 8) }, hits);
        Require(hits.Count > 0 && hits[0].gameObject.GetComponentInParent<ConfirmationDialog>() == dialog,
            "Confirmation backdrop captures pointer outside dialog card; hit objects: " + string.Join(", ", hits.Select(h => h.gameObject.name)));
        yield return Capture("forget-confirmation", 1920, 1080);
        Click("CancelForget");
        Require(!dialog.gameObject.activeSelf && menu.interactable && _client.HasSavedSession &&
            _store.Load().PlayerToken == saved.PlayerToken, "Cancel restores menu and preserves credential");
        Click("ForgetSession"); Click("ConfirmForget");
        Require(!dialog.gameObject.activeSelf && menu.interactable && !_client.HasSavedSession,
            "Only explicit confirmation removes local access");
    }
    private void SelectOnMap(string id)
    {
        Physics2D.SyncTransforms();
        var view = UnityEngine.Object.FindObjectsByType<ArmyView>(FindObjectsSortMode.None).Single(a => a.ArmyId == id);
        var point = (Vector2)Camera.main.WorldToScreenPoint(view.transform.position);
        Require(!PointerInput.IsUiBlocking(point), "Army marker lies in the map area");
        _pointer.HandlePointer(point); Require(_client.SelectedArmyId == id, "Real map selection");
    }
    private IEnumerator Connected()
    {
        yield return Wait(() => _client.IsConnected && !_client.IsBusy && _ui.State == ClientUiState.World, "authoritative World presentation");
        yield return null; yield return null;
    }
    private IEnumerator Capture(string label, int width, int height)
    {
        Screen.SetResolution(width, height, FullScreenMode.Windowed);
        yield return Wait(() => Screen.width == width && Screen.height == height, "resolution " + width);
        yield return null; yield return null;
        Canvas.ForceUpdateCanvases();
        if (_ui.State == ClientUiState.World)
        {
            var area = (RectTransform)_ui.GetComponentInChildren<WorldMapViewport>().transform;
            var areaCorners = new Vector3[4]; area.GetWorldCorners(areaCorners);
            var cameraBounds = Camera.main.pixelRect;
            Require(Vector2.Distance(cameraBounds.min, areaCorners[0]) < 1 && Vector2.Distance(cameraBounds.max, areaCorners[2]) < 1,
                "Camera viewport follows current authored map layout after resize");
        }
        foreach (var text in _ui.GetComponentsInChildren<TMP_Text>())
        {
            text.ForceMeshUpdate();
            if (string.IsNullOrEmpty(text.text)) continue;
            Require(!text.isTextOverflowing, "Text fits: " + text.gameObject.name);
            var corners = new Vector3[4]; text.rectTransform.GetWorldCorners(corners);
            Require(corners[0].x >= -1 && corners[0].y >= -1 && corners[2].x <= Screen.width + 1 && corners[2].y <= Screen.height + 1,
                "Text remains in screen bounds: " + text.gameObject.name);
        }
        var file = Path.Combine(_output, _mode + "-" + label + ".png");
        // Capture after rendering, including newly launched outage/restart windows.
        // A file existing alone is insufficient: the initial swapchain can still be black.
        yield return new WaitForSecondsRealtime(.3f);
        yield return new WaitForEndOfFrame();
        var capture = ScreenCapture.CaptureScreenshotAsTexture();
        Require(capture != null && capture.GetPixel(width / 2, height / 2).maxColorComponent > .01f,
            "Screenshot contains the rendered UI");
        File.WriteAllBytes(file, capture.EncodeToPNG());
        UnityEngine.Object.Destroy(capture);
    }
    private static Vector2 Center(RectTransform rect)
    {
        var corners = new Vector3[4]; rect.GetWorldCorners(corners); return (corners[0] + corners[2]) * .5f;
    }
    private static IEnumerator Wait(Func<bool> condition, string label)
    {
        var deadline = Time.realtimeSinceStartupAsDouble + 60;
        while (!condition()) { Require(Time.realtimeSinceStartupAsDouble < deadline, "Timed out: " + label); yield return new WaitForSecondsRealtime(.05f); }
    }
    private static void Require(bool condition, string message) { if (!condition) throw new InvalidOperationException(message); }
    private void OnLog(string message, string stack, LogType type)
    { if ((type == LogType.Error || type == LogType.Exception) && !_done) Finish(false, "UI verification error; inspect sanitized player log."); }
    private void Finish(bool passed, string message)
    {
        if (_done) return; _done = true;
        File.WriteAllText(_result, (passed ? "PASS: " : "FAIL: ") + message);
        Application.Quit(passed ? 0 : 1);
    }
}
