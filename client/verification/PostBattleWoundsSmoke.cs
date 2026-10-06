// Copy ONLY into an isolated Unity verification project. Never ship this harness.
using System;
using System.Collections;
using System.IO;
using System.Linq;
using ConquestusTerrarum.Client;
using UnityEngine;

public sealed class PostBattleWoundsSmoke : MonoBehaviour
{
    [Serializable] private sealed class PublicInfo { public string matchId, playerId, joinCode, armyId; }
    [Serializable] private sealed class HealthIds { public string armyId; public string[] wounded, healthy, dead; }
    private sealed class JoinerRecords : ISessionRecordStore
    {
        private const string Key = "ConquestusWoundsSmoke.Joiner";
        public bool HasRecord => PlayerPrefs.HasKey(Key);
        public string Read() => PlayerPrefs.GetString(Key);
        public void Write(string value) { PlayerPrefs.SetString(Key, value); PlayerPrefs.Save(); }
        public void Clear() { PlayerPrefs.DeleteKey(Key); PlayerPrefs.Save(); }
    }
    private MatchController _client;
    private ISessionStorage _store;
    private string _role, _profile, _share, _result;
    private bool _finished;

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
    private static void Initialize()
    {
        if (Arg("--ct-wounds-role") != null) new GameObject("PostBattleWoundsVerification").AddComponent<PostBattleWoundsSmoke>();
    }
    private static string Arg(string key)
    {
        var args = Environment.GetCommandLineArgs(); var index = Array.IndexOf(args, key);
        return index >= 0 && index + 1 < args.Length ? args[index + 1] : null;
    }
    private void Start()
    {
        _role = Arg("--ct-wounds-role"); _profile = _role.EndsWith("join") ? "join" : "create";
        _share = Arg("--ct-share"); _result = Arg("--ct-result");
        Application.logMessageReceived += OnLog;
        StartCoroutine(Run());
    }
    private IEnumerator Run()
    {
        yield return null;
        _client = UnityEngine.Object.FindAnyObjectByType<MatchController>();
        _store = new SessionStorage(_profile == "join" ? (ISessionRecordStore)new JoinerRecords() : new PlayerPrefsSessionRecordStore());
        _client.SetSessionStorage(_store);
        if (_role.StartsWith("resume"))
        {
            var saved = _store.Load();
            var expected = JsonUtility.FromJson<PublicInfo>(File.ReadAllText(InfoPath()));
            Require(saved != null, "Credential retained for restart");
            _client.ContinueGame();
            yield return Connected();
            Require(_client.Session.MatchId == expected.matchId && _client.LocalPlayerId == expected.playerId &&
                _client.Session.PlayerToken == saved.PlayerToken, "Same world/player/credential after restart");
            VerifyResolved();
            _client.ForgetSavedGame();
        }
        else
        {
            Require(!_client.HasSavedSession, "Fresh isolated save identity required");
            if (_profile == "create") _client.CreateGame();
            else
            {
                yield return Wait(() => File.Exists(_share), "creator invitation");
                _client.JoinGame(JsonUtility.FromJson<PublicInfo>(File.ReadAllText(_share)).joinCode);
            }
            yield return Connected();
            var own = _client.State.armies.First(_client.IsLocalArmy);
            File.WriteAllText(InfoPath(), JsonUtility.ToJson(new PublicInfo { matchId = _client.Session.MatchId,
                playerId = _client.LocalPlayerId, joinCode = _client.Session.JoinCode, armyId = own.id }));
            yield return Wait(() => _client.State.phase == "active", "activation through ordinary feed");
            File.WriteAllText(_share + ".ready." + _profile, "ready");
            yield return Wait(() => File.Exists(_share + ".configured"), "isolated health fixture");
            _client.Refresh(); yield return Connected();
            _client.SelectArmy(own.id);
            _client.ClickLocation(_client.State.locations.Single(l => l.name == "Crossroads").id);
            yield return Wait(() => !_client.IsBusy && _client.State.armies.Single(a => a.id == own.id).isInBattle, "arrival into battle");
            yield return Wait(() => _client.SelectedArmy.units.Any(u => u.isWounded) && _client.SelectedArmy.units.Any(u => u.isAlive && !u.isWounded),
                "living wounds and healthy reserve from focused detail polling");
            var units = _client.SelectedArmy.units;
            File.WriteAllText(_share + ".health." + _profile, JsonUtility.ToJson(new HealthIds { armyId = own.id,
                wounded = units.Where(u => u.isWounded).Select(u => u.id).ToArray(),
                healthy = units.Where(u => u.isAlive && !u.isWounded).Select(u => u.id).ToArray(),
                dead = units.Where(u => !u.isAlive).Select(u => u.id).ToArray() }));
            yield return Wait(() => File.Exists(_share + ".health.create") && File.Exists(_share + ".health.join"), "both armies wounded");
            if (_profile == "create")
            {
                Require(_client.CanRetreatSelected, "Authoritative retreat eligibility");
                _client.RetreatSelectedArmy();
                yield return Wait(() => !_client.IsBusy && !_client.State.armies.Single(a => a.id == own.id).isInBattle, "successful retreat");
            }
            // Both players consume authoritative state; the joiner depends on ordinary BattleEnded resync.
            yield return Wait(() => _client.State.activeBattles.Length == 0, "ordinary post-battle resync");
            VerifyResolved();
            Require(_store.HasSavedSession, "Recovery credential retained");
        }
        Finish(true, _role + " post-battle wounds passed");
    }
    private string InfoPath() => _profile == "create" ? _share : _share + ".joiner";
    private void VerifyResolved()
    {
        Require(_client.State.phase == "active" && _client.LifecycleLabel == "Game Active", "World remains Active");
        Require(_client.State.activeBattles.Length == 0, "Battle ended");
        foreach (var profile in new[] { "create", "join" })
        {
            var expected = JsonUtility.FromJson<HealthIds>(File.ReadAllText(_share + ".health." + profile));
            var army = _client.State.armies.Single(a => a.id == expected.armyId);
            Require(!army.isInBattle && !army.isDestroyed, "Healthy reserves survive");
            foreach (var id in expected.wounded)
            {
                var unit = army.units.Single(u => u.id == id);
                Require(profile == "create" ? !unit.isAlive && unit.currentHitPoints == 0 :
                    unit.isAlive && unit.currentHitPoints == unit.maxHitPoints, "Authoritative wounded retreat loss or remaining heal");
            }
            foreach (var id in expected.healthy)
            {
                var unit = army.units.Single(u => u.id == id);
                Require(unit.isAlive && unit.currentHitPoints == unit.maxHitPoints, "Healthy unit survives");
            }
            foreach (var id in expected.dead)
            {
                var unit = army.units.Single(u => u.id == id);
                Require(!unit.isAlive && unit.currentHitPoints == 0, "Dead unit never revived");
            }
        }
    }
    private IEnumerator Connected() => Wait(() => _client.IsConnected && !_client.IsBusy, "authoritative snapshot");
    private static IEnumerator Wait(Func<bool> condition, string label)
    {
        var deadline = Time.realtimeSinceStartupAsDouble + 90;
        while (!condition())
        {
            Require(Time.realtimeSinceStartupAsDouble < deadline, "Timed out: " + label);
            yield return new WaitForSecondsRealtime(.05f);
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
