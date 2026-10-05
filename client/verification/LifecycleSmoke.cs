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
            Require(_client.HasSavedSession && saved != null, "Active world save detected on process restart");
            _client.ContinueGame();
            yield return Wait(() => _client.IsConnected && !_client.IsBusy, "post-elimination reconnect");
            Require(_client.Session.MatchId == expected.matchId && _client.LocalPlayerId == expected.playerId &&
                _client.Session.PlayerToken == saved.PlayerToken && _client.State.phase == "active" && _client.LifecycleLabel == "Game Active",
                "Same credential/identity and Active world after restart");
            Require((long)typeof(MatchController).GetField("_updateCursor", BindingFlags.NonPublic | BindingFlags.Instance).GetValue(_client)
                == _client.State.updateCursor, "Snapshot cursor after post-elimination reconnect");
            CheckOpenWorld(expected.playerId);
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
            // Neither client refreshes manually. Combat resolution is ordinary world synchronization.
            var creator = JsonUtility.FromJson<PublicInfo>(File.ReadAllText(_share));
            yield return Wait(() => _client.State.armies.Where(a => a.ownerId != creator.playerId).All(a => a.isDestroyed)
                && _client.State.activeBattles.Length == 0, "army elimination through normal feed/detail sync");
            CheckOpenWorld(creator.playerId);
            Require(_store.HasSavedSession && _client.IsConnected, "Active map and credential remain available after elimination");
            MatchUpdatesDto feed = null;
            yield return _client.GetComponent<ConquestusApiClient>().GetUpdates(_client.Session.MatchId, 0,
                page => feed = page, _ => Require(false, "Post-elimination feed unavailable"));
            Require(feed != null && feed.updates.All(u => u.type != "MatchFinished"), "No global finish event emitted");
            if (_role == "create")
            {
                var remaining = _client.State.armies.First(a => _client.IsLocalArmy(a) && !a.isDestroyed);
                _client.SelectArmy(remaining.id);
                _client.ClickLocation(_client.State.locations.Single(l => l.name == "West camp").id);
                yield return Wait(() => !_client.IsBusy && _client.State.armies.Single(a => a.id == remaining.id).isMoving,
                    "remaining army can move after elimination");
            }
            else
            {
                var own = _client.State.armies.First(_client.IsLocalArmy);
                Require(own.isDestroyed && !_client.CanCommand(own), "Destroyed own army cannot receive commands");
                _client.SelectArmy(own.id);
                Require(_client.SelectedArmy != null, "Eliminated player may inspect world");
                ApiFailure rejected = null;
                yield return _client.GetComponent<ConquestusApiClient>().RetreatArmy(_client.Session.MatchId, own.id,
                    _ => Require(false, "Destroyed army retreat accepted"), error => rejected = error);
                Require(rejected != null && rejected.Status == 409, "Ordinary destroyed-army domain rejection");
            }
            if (_role == "join") _client.ForgetSavedGame();
        }
        Finish(true, _role + " lifecycle passed");
    }
    private void CheckOpenWorld(string strongerPlayer)
    {
        Require(_client.State.phase == "active" && _client.LifecycleLabel == "Game Active", "Elimination leaves world Active");
        Require(_client.State.armies.Where(a => a.ownerId != strongerPlayer).All(a => a.isDestroyed), "One player has no living army");
        foreach (var army in _client.State.armies)
            Require(_client.CanCommand(army) == (_client.IsLocalArmy(army) && !army.isDestroyed), "Active ownership eligibility");
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
