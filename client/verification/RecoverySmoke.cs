// Copy into Assets ONLY in an isolated verification project. Never ship this harness.
// Public test metadata contains IDs/code only; credentials stay in the test player's PlayerPrefs.
using System;
using System.Collections;
using System.IO;
using System.Linq;
using System.Reflection;
using ConquestusTerrarum.Client;
using UnityEngine;

public sealed class RecoverySmoke : MonoBehaviour
{
    [Serializable] private sealed class PublicSession
    {
        public string matchId, playerId, joinCode, armyId;
    }
    private sealed class JoinerRecords : ISessionRecordStore
    {
        private const string Key = "ConquestusRecoverySmoke.Joiner";
        public bool HasRecord => PlayerPrefs.HasKey(Key);
        public string Read() => PlayerPrefs.GetString(Key);
        public void Write(string value) { PlayerPrefs.SetString(Key, value); PlayerPrefs.Save(); }
        public void Clear() { PlayerPrefs.DeleteKey(Key); PlayerPrefs.Save(); }
    }
    private MatchController _client;
    private ISessionStorage _store;
    private string _mode, _meta, _result;
    private bool _finished;
    private const BindingFlags Private = BindingFlags.NonPublic | BindingFlags.Instance;

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
    private static void Initialize()
    {
        if (Arg("--ct-mode") != null) new GameObject("RecoveryVerification").AddComponent<RecoverySmoke>();
    }
    private static string Arg(string key)
    {
        var args = Environment.GetCommandLineArgs();
        var i = Array.IndexOf(args, key);
        return i >= 0 && i + 1 < args.Length ? args[i + 1] : null;
    }
    private void Start()
    {
        _mode = Arg("--ct-mode"); _meta = Arg("--ct-meta"); _result = Arg("--ct-result");
        Application.logMessageReceived += OnLog;
        StartCoroutine(Run());
    }
    private IEnumerator Run()
    {
        yield return null;
        _client = UnityEngine.Object.FindAnyObjectByType<MatchController>();
        Require(_client != null && _client.Session == null && _client.State == null, "Disconnected startup");
        _store = new SessionStorage(Arg("--ct-profile") == "join" ? (ISessionRecordStore)new JoinerRecords() : new PlayerPrefsSessionRecordStore());
        _client.SetSessionStorage(_store);
        if (_mode == "empty")
        {
            Require(!_client.HasSavedSession && _store.Load() == null, "Forget survived process restart");
        }
        else if (_mode == "create" || _mode == "join")
        {
            Require(!_client.HasSavedSession, "Clean isolated test profile required");
            if (_mode == "create") _client.CreateGame();
            else _client.JoinGame(JsonUtility.FromJson<PublicSession>(File.ReadAllText(Arg("--ct-share"))).joinCode);
            yield return Connected();
            CheckSavedIdentity();
            var own = _client.State.armies.First(_client.IsLocalArmy);
            WriteMeta(own.id);
            if (_mode == "create")
            {
                Require(_client.State.phase == "waitingForPlayers", "New match waits for second player");
                File.WriteAllText(_meta + ".waiting", "ready");
            }
            // MatchStarted reaches the creator via normal feed polling, without manual Refresh.
            yield return Wait(() => _client.State.phase == "active", "second player activates match");
            _client.SelectArmy(own.id);
            _client.ClickLocation(_client.State.locations.Single(l => l.name == "Crossroads").id);
            yield return Wait(() => !_client.IsBusy && _client.SelectedArmy.isMoving, "move before closing client");
            Require(DateTimeOffset.Parse(_client.SelectedArmy.movement.arriveAt) > _client.PresentationServerTime, "Authoritative moving timestamps");
        }
        else if (_mode == "issued-offline")
        {
            Require(!_client.HasSavedSession, "Clean profile for issued credentials");
            MultiplayerSession issued = null;
            yield return _client.GetComponent<ConquestusApiClient>().CreateMatch(
                dto => issued = MultiplayerSession.FromCreate(dto), error => Require(false, error.Message));
            Require(issued != null, "Production credentials issued");
            File.WriteAllText(_meta + ".issued", "ready");
            yield return Wait(() => File.Exists(_meta + ".offline"), "backend stopped after credential issue");
            // Same callback target used by CreateGame/JoinGame, with the first GET deliberately offline.
            typeof(MatchController).GetMethod("Connect", Private).Invoke(_client, new object[] { issued, true });
            yield return Wait(() => !_client.IsBusy, "initial snapshot failure");
            Require(_client.State == null && _client.LastError != null, "Initial GET failed");
            Require(_store.Load().PlayerToken == issued.PlayerToken, "Saved before failed initial GET");
            WriteMeta(null);
        }
        else if (_mode == "offline" || _mode == "offline-retry")
        {
            Require(_client.HasSavedSession, "Saved session detected");
            var saved = _store.Load();
            _client.ContinueGame();
            yield return Wait(() => !_client.IsBusy, "offline GET failure");
            Require(!_client.IsConnected && _client.LastError.Contains("Retry"), "Retryable offline failure");
            Require(_store.Load().PlayerToken == saved.PlayerToken, "Offline preserves credential");
            if (_mode == "offline-retry")
            {
                File.WriteAllText(_meta + ".retry-ready", "ready");
                yield return Wait(() => File.Exists(_meta + ".online"), "backend restarted for Retry Load");
                _client.Refresh();
                yield return Connected();
                Require(_client.Session.PlayerToken == saved.PlayerToken && _client.LocalPlayerId == saved.PlayerId,
                    "Retry Load recovers same identity in the same process");
            }
            _client.Disconnect();
            Require(_client.HasSavedSession, "Back to Menu preserves credential after failure");
        }
        else if (_mode == "invalid" || _mode == "missing")
        {
            var saved = _store.Load();
            _store.Clear();
            _store.Save(MultiplayerSession.Restore(_mode == "missing" ? Guid.NewGuid().ToString() : saved.MatchId,
                saved.PlayerId, _mode == "invalid" ? new string('B', 43) : saved.PlayerToken, saved.JoinCode));
            _client.ContinueGame();
            yield return Wait(() => !_client.IsBusy, "definitive rejection");
            Require(_client.State == null && _client.LastError.Contains("cannot be resumed") &&
                _client.LastError.Contains(_mode == "invalid" ? "401" : "404"), "Definitive error with Forget action");
            Require(_client.HasSavedSession && _store.HasSavedSession, "No implicit deletion on rejection");
            _client.ForgetSavedGame();
            Require(!_client.HasSavedSession && !_store.HasSavedSession, "Explicit Forget clears rejected save");
        }
        else
        {
            Require(_client.HasSavedSession, "Saved session detected at startup");
            var saved = _store.Load();
            var expected = JsonUtility.FromJson<PublicSession>(File.ReadAllText(_meta));
            _client.CreateGame(); _client.JoinGame(expected.joinCode);
            Require(!_client.IsBusy && _client.Session == null && _store.Load().PlayerToken == saved.PlayerToken,
                "Create/Join blocked until explicit Forget");
            _client.ContinueGame();
            yield return Connected();
            Require(_client.Session.MatchId == expected.matchId && _client.LocalPlayerId == expected.playerId &&
                _client.Session.PlayerToken == saved.PlayerToken, "Same recovered identity and credential");
            Require((long)typeof(MatchController).GetField("_updateCursor", Private).GetValue(_client) == _client.State.updateCursor,
                "Reconnect initializes from authoritative snapshot cursor");
            CheckSavedIdentity();
            var own = _client.State.armies.First(_client.IsLocalArmy);
            var enemy = _client.State.armies.First(a => !_client.IsLocalArmy(a));
            _client.SelectArmy(enemy.id);
            var before = _client.State;
            _client.ClickLocation(_client.State.locations.Single(l => l.name == "Crossroads").id);
            _client.RetreatSelectedArmy();
            Require(!_client.IsBusy && ReferenceEquals(before, _client.State), "Recovered opponent remains inspection-only");
            if (_mode == "resume-moving")
            {
                var moved = _client.State.armies.Single(a => a.id == expected.armyId);
                Require(moved.isMoving || _client.State.locations.Single(l => l.id == moved.locationId).name == "Crossroads",
                    "Authoritative movement or arrival recovered");
                if (moved.isMoving) Require(DateTimeOffset.Parse(moved.movement.arriveAt) > DateTimeOffset.Parse(moved.movement.startedAt), "Movement anchors recovered");
                File.WriteAllText(_result + ".movement", moved.isMoving ? "moving" : "arrived");
            }
            if (_mode == "resume-battle")
            {
                yield return Wait(() => _client.State.armies.Any(a => _client.IsLocalArmy(a) && a.isInBattle), "active battle after restart");
                own = _client.State.armies.First(a => _client.IsLocalArmy(a) && a.isInBattle);
                _client.SelectArmy(own.id);
                var hp = own.units.Sum(u => u.currentHitPoints);
                yield return Wait(() => _client.SelectedArmy.units.Sum(u => u.currentHitPoints) < hp, "focused HP polling after reconnect");
            }
            if (_mode == "resume-ended")
            {
                Require(_client.State.activeBattles.Length == 0 && !_client.State.armies.Any(a => a.isInBattle), "Ended battle recovered authoritatively");
                Require(_client.State.phase == "active" && _client.LifecycleLabel == "Game Active",
                    "Post-combat reconnect keeps the persistent world Active");
            }
            if (_mode == "races")
            {
                var oldGeneration = typeof(MatchController).GetField("_stateGeneration", Private).GetValue(_client);
                var oldSession = _client.Session;
                var oldMatch = _client.State.matchId;
                _client.Refresh(); _client.Disconnect();
                Require(_store.HasSavedSession, "Menu preserves storage");
                yield return new WaitForSecondsRealtime(1);
                Require(_client.State == null && _client.Session == null && !_client.IsBusy, "Late GET after menu ignored");
                _client.ContinueGame(); _client.Disconnect(); _client.ContinueGame();
                yield return Connected();
                Require(!(bool)typeof(MatchController).GetMethod("IsCurrentForeground", Private).Invoke(_client, new[] { oldGeneration, oldSession }), "Old foreground generation rejected");
                Require(!(bool)typeof(MatchController).GetMethod("IsCurrentRequest", Private).Invoke(_client, new[] { oldGeneration, oldMatch }), "Old feed/detail generation rejected");
                _client.Refresh(); _client.ForgetSavedGame(); _client.CreateGame();
                yield return Connected();
                Require(_client.Session.MatchId != oldMatch, "New session survives old callbacks after Forget");
                WriteMeta(null);
            }
            if (_mode == "forget") _client.ForgetSavedGame();
        }
        Finish(true, _mode + " passed");
    }
    private void CheckSavedIdentity()
    {
        var saved = _store.Load();
        Require(saved != null && saved.MatchId == _client.Session.MatchId && saved.PlayerId == _client.LocalPlayerId &&
            saved.PlayerToken == _client.Session.PlayerToken, "Persisted credential matches active identity");
    }
    private void WriteMeta(string army)
    {
        File.WriteAllText(_meta, JsonUtility.ToJson(new PublicSession { matchId = _client.Session.MatchId,
            playerId = _client.LocalPlayerId, joinCode = _client.Session.JoinCode, armyId = army }));
    }
    private IEnumerator Connected() => Wait(() => !_client.IsBusy && _client.IsConnected, "authoritative full snapshot");
    private static IEnumerator Wait(Func<bool> condition, string label)
    {
        var deadline = Time.realtimeSinceStartupAsDouble + 30;
        while (!condition())
        {
            Require(Time.realtimeSinceStartupAsDouble < deadline, "Timed out: " + label);
            yield return new WaitForSecondsRealtime(0.05f);
        }
    }
    private static void Require(bool condition, string message)
    {
        if (!condition) throw new InvalidOperationException(message);
    }
    private void OnLog(string message, string stack, LogType type)
    {
        if (type == LogType.Exception && !_finished) Finish(false, "Verification exception; inspect sanitized player log.");
    }
    private void Finish(bool passed, string message)
    {
        if (_finished) return;
        _finished = true;
        File.WriteAllText(_result, (passed ? "PASS: " : "FAIL: ") + message);
        Application.Quit(passed ? 0 : 1);
    }
}
