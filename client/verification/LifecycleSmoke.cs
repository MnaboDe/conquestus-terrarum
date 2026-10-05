// Copy ONLY into an isolated Unity project's Assets. Normal production builds exclude this file.
using System;
using System.Collections;
using System.IO;
using System.Linq;
using System.Reflection;
using ConquestusTerrarum.Client;
using UnityEngine;

public sealed class LifecycleSmoke : MonoBehaviour
{
    [Serializable] private sealed class PublicInfo { public string matchId, playerId, joinCode; }
    private sealed class JoinerRecords : ISessionRecordStore
    {
        private const string Key = "ConquestusLifecycleSmoke.Joiner";
        public bool HasRecord => PlayerPrefs.HasKey(Key);
        public string Read() => PlayerPrefs.GetString(Key);
        public void Write(string value) { PlayerPrefs.SetString(Key, value); PlayerPrefs.Save(); }
        public void Clear() { PlayerPrefs.DeleteKey(Key); PlayerPrefs.Save(); }
    }
    private MatchController _client;
    private ISessionStorage _store;
    private string _role, _share, _result;
    private bool _finished;
    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
    private static void Initialize()
    {
        if (Arg("--ct-lifecycle-role") != null) new GameObject("LifecycleVerification").AddComponent<LifecycleSmoke>();
    }
    private static string Arg(string name)
    {
        var args = Environment.GetCommandLineArgs(); var index = Array.IndexOf(args, name);
        return index >= 0 && index + 1 < args.Length ? args[index + 1] : null;
    }
    private void Start()
    {
        _role = Arg("--ct-lifecycle-role"); _share = Arg("--ct-share"); _result = Arg("--ct-result");
        Application.logMessageReceived += OnLog;
        StartCoroutine(Run());
    }
    private IEnumerator Run()
    {
        yield return null;
        _client = UnityEngine.Object.FindAnyObjectByType<MatchController>();
        _store = new SessionStorage(_role == "join" ? (ISessionRecordStore)new JoinerRecords() : new PlayerPrefsSessionRecordStore());
        _client.SetSessionStorage(_store);
        if (_role == "resume")
        {
            var expected = JsonUtility.FromJson<PublicInfo>(File.ReadAllText(_share));
            var saved = _store.Load();
            Require(_client.HasSavedSession && saved != null, "Finished save detected on process restart");
            _client.ContinueGame();
            yield return Wait(() => _client.IsConnected && !_client.IsBusy, "finished reconnect");
            Require(_client.Session.MatchId == expected.matchId && _client.LocalPlayerId == expected.playerId &&
                _client.Session.PlayerToken == saved.PlayerToken && _client.State.phase == "finished" && _client.LifecycleLabel == "Victory",
                "Same credential/identity and authoritative final result after restart");
            Require((long)typeof(MatchController).GetField("_updateCursor", BindingFlags.NonPublic | BindingFlags.Instance).GetValue(_client)
                == _client.State.updateCursor, "Snapshot cursor after final reconnect");
            CheckCommandsBlocked();
            _client.ForgetSavedGame();
        }
        else
        {
            Require(!_client.HasSavedSession, "Fresh isolated PlayerPrefs identity required");
            if (_role == "create") _client.CreateGame();
            else
            {
                yield return Wait(() => File.Exists(_share), "creator invitation");
                _client.JoinGame(JsonUtility.FromJson<PublicInfo>(File.ReadAllText(_share)).joinCode);
            }
            yield return Wait(() => _client.IsConnected && !_client.IsBusy, "initial snapshot");
            if (_role == "create")
            {
                Require(_client.State.phase == "waitingForPlayers" && _client.LifecycleLabel == "Waiting for second player", "Creator waiting phase");
                CheckCommandsBlocked();
                // Waiting reconnect is allowed, still no new slot or token is requested.
                var saved = _store.Load();
                _client.Disconnect(); _client.ContinueGame();
                yield return Wait(() => _client.IsConnected && !_client.IsBusy, "waiting reconnect");
                Require(_client.State.phase == "waitingForPlayers" && _client.Session.PlayerToken == saved.PlayerToken, "Waiting recovery");
                File.WriteAllText(_share, JsonUtility.ToJson(new PublicInfo { matchId = _client.Session.MatchId,
                    playerId = _client.LocalPlayerId, joinCode = _client.Session.JoinCode }));
                // No Refresh: durable MatchStarted must drive the existing feed/resync flow.
                yield return Wait(() => _client.State.phase == "active", "automatic start through feed");
            }
            Require(_client.State.phase == "active" && _client.LifecycleLabel == "Game Active", "Both active");
            var enemy = _client.State.armies.First(a => !_client.IsLocalArmy(a));
            _client.SelectArmy(enemy.id);
            var unchanged = _client.State;
            _client.ClickLocation(_client.State.locations.Single(l => l.name == "Crossroads").id);
            Require(!_client.IsBusy && ReferenceEquals(unchanged, _client.State), "Enemy remains inspection-only");
            File.WriteAllText(_share + ".ready." + _role, "ready");
            yield return Wait(() => File.Exists(_share + ".configured"), "isolated combat fixture configured");
            var center = _client.State.locations.Single(l => l.name == "Crossroads").id;
            foreach (var id in _client.State.armies.Where(_client.IsLocalArmy).Select(a => a.id).ToArray())
            {
                _client.SelectArmy(id); _client.ClickLocation(center);
                yield return Wait(() => !_client.IsBusy && _client.State.armies.Single(a => a.id == id).isMoving, "own movement");
            }
            yield return Wait(() => _client.State.armies.Any(a => a.isInBattle), "battle automatically discovered");
            _client.SelectArmy(_client.State.armies.First(a => a.isInBattle).id);
            var hp = _client.SelectedArmy.units.Sum(u => u.currentHitPoints);
            yield return Wait(() => _client.SelectedArmy.units.Sum(u => u.currentHitPoints) < hp, "battle detail HP");
            // Neither client manually refreshes: elimination must be discovered via normal synchronization.
            yield return Wait(() => _client.State.phase == "finished", "automatic elimination finish");
            var expectedLabel = _role == "create" ? "Victory" : "Defeat";
            Require(_client.LifecycleLabel == expectedLabel && !_client.State.isDraw, "Authoritative " + expectedLabel);
            Require(_client.State.armies.Where(a => a.ownerId != _client.State.winnerPlayerId).All(a => a.isDestroyed), "Loser has no living army");
            Require(_store.HasSavedSession && _client.IsConnected, "Finished map and credential remain available");
            CheckCommandsBlocked();
            // Server remains authoritative even when UI guards are bypassed.
            ApiFailure rejected = null;
            var own = _client.State.armies.First(_client.IsLocalArmy);
            yield return _client.GetComponent<ConquestusApiClient>().RetreatArmy(_client.Session.MatchId, own.id,
                _ => Require(false, "Post-finish API command accepted"), error => rejected = error);
            Require(rejected != null && rejected.Status == 409, "Server rejects finished command");
            if (_role == "join") _client.ForgetSavedGame();
        }
        Finish(true, _role + " lifecycle passed");
    }
    private void CheckCommandsBlocked()
    {
        var state = _client.State;
        var own = state.armies.First(_client.IsLocalArmy);
        Require(!_client.CanCommand(own), "Phase disables commands");
        _client.SelectArmy(own.id);
        _client.ClickLocation(state.locations.First().id); _client.RetreatSelectedArmy();
        Require(!_client.IsBusy && ReferenceEquals(state, _client.State) && _client.SelectedArmy != null, "Inspection remains while commands are blocked");
    }
    private static IEnumerator Wait(Func<bool> condition, string label)
    {
        var deadline = Time.realtimeSinceStartupAsDouble + 90;
        while (!condition())
        {
            Require(Time.realtimeSinceStartupAsDouble < deadline, "Timed out: " + label);
            yield return new WaitForSecondsRealtime(0.05f);
        }
    }
    private static void Require(bool condition, string message) { if (!condition) throw new InvalidOperationException(message); }
    private void OnLog(string message, string stack, LogType type)
    { if (type == LogType.Exception && !_finished) Finish(false, "Verification exception; inspect sanitized player log."); }
    private void Finish(bool passed, string message)
    {
        if (_finished) return;
        _finished = true;
        File.WriteAllText(_result, (passed ? "PASS: " : "FAIL: ") + message);
        Application.Quit(passed ? 0 : 1);
    }
}
