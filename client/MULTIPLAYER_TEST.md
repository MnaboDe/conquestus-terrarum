# Local two-player test

Use Unity **6000.6.1f1**, the system **.NET 10** SDK, and two independent clients talking to
`http://localhost:5108`. One anonymous session is saved locally after successful Create/Join, before the first
snapshot GET. Startup stays disconnected and offers **Continue Game** when a saved record exists.
**Back to Menu** (`Disconnect()` in code) clears only the active view and keeps the save. **Forget Saved Game**
removes the local credential permanently; it does not free the occupied server slot. Forget before creating/joining
another game. The join code cannot recover a previously occupied slot.

The MVP stores only match ID, player ID, opaque player token, and optional join code in a single versioned PlayerPrefs
record (`ConquestusTerrarum.AnonymousSession.v1`). This is local anonymous-session persistence, **not hardened account
credential storage**: PlayerPrefs is not encrypted and can be read by someone with access to the local profile.
Do not share/export its contents or log tokens. `ISessionStorage` can later be replaced with Android Keystore/iOS Keychain
storage. No hard-coded encryption, permanent account, token rotation, or platform-specific runtime API is used.
Snapshots, movement, HP, selections, and feed cursors are never saved. PostgreSQL keeps the token hash without a client
restart expiry. Continue reuses the original token in Authorization and loads a full snapshot, then polls after that
snapshot's `updateCursor`.

## Match lifecycle

The server returns `phase` as `waitingForPlayers`, `active`, or `finished`, plus nullable `winnerPlayerId` and `isDraw`.
Unity displays the authoritative result; it does not calculate victory from local HP or territory. No new migration is
required: participation comes from the two persisted player slots, and survival comes from authoritative armies.

1. Create a fresh game in A. The map appears with **Waiting for second player** and the copyable join code. Select both
   owned and enemy armies for inspection. Move/Retreat must do nothing. A protected API command bypassing client guards
   must return 409 `match_waiting_for_players` without changing snapshot, version, cursor or feed.
2. Close/reopen A and Continue while waiting. Verify the same IDs/code, waiting label and disabled commands.
3. Join in B. B sees **Game Active**. Leave A untouched: within normal ~2-second feed polling plus the one snapshot resync,
   A must display **Game Active** too. The durable `MatchStarted` update starts this; no Ready button or extra lobby poll exists.
   After start the HUD marks the invitation closed. A third player still gets match-full and cannot claim another slot.
4. Move/retreat each player's own armies as in the movement tests below. Enemy inspection remains allowed and enemy commands
   remain blocked locally/403 on the server. Continue while Active restores ordinary play from the full snapshot cursor.
5. Move all opposing armies into battle and continue until one player has no army containing a living unit. The remaining
   player sees **Victory**, the other **Defeat**, with the authoritative winner ID. If all units die on both sides, both see
   **Draw** (`finished`, null winner, `isDraw=true`). Default equal armies can produce a draw; the isolated automated fixture
   below changes only pre-combat unit damage to exercise a reproducible Victory/Defeat without writing a result.
6. Without Refresh, both clients must automatically discover finish through the durable `MatchFinished` feed update or
   a structural battle-detail resync. Final map/army inspection stays available. Move and Retreat are disabled; bypassing
   client guards returns 409 `match_finished` without any command mutation. Repeated reads must not create duplicate finishes.
7. Close/restart a finished client and Continue. Verify the same identity, final phase, Victory/Defeat/Draw and snapshot
   cursor. The save remains until explicit Forget; finish never returns to the menu or removes the credential automatically.

Lazy simulation still wakes on reads/polls/commands using database time. A command that discovers elimination rolls its
transaction back, leaving persistence unchanged; a subsequent read commits the world and finish event together. There
are no simulation workers or global HP events. In an old/imported already-eliminated world, the first eligible read
announces the derived finish once; joining such a waiting world commits start and immediate finish together.

For automated lifecycle verification, use an isolated Unity copy with a unique company/product name, and copy
`client/verification/LifecycleSmoke.cs` into that copy's Assets before building. Normal builds must exclude all smoke
harnesses. Use a fresh empty output directory, PostgreSQL migrated, solution built, and port 5108 free:

```powershell
& .\client\verification\Run-LifecycleSmoke.ps1 `
  -PlayerPath 'C:\path\to\isolated-project\Builds\Windows\ConquestusTerrarum.exe' `
  -OutputDirectory 'C:\path\to\ignored\Builds\LifecycleResults-new-run'
```

This runner owns only its API process, launches two real Windows clients, observes automatic start, configures only the
fresh test match's unit DPS through the separate .NET `client/verification/LifecycleSetup` utility, then lets real lazy
combat eliminate one side. It verifies Victory/Defeat, blocked client/server commands, HP/detail polling and final restart
recovery. The utility is outside production projects/Unity Assets, uses existing Npgsql, writes no phase/winner/token,
and must only target the newly created isolated verification match. Successful runs explicitly remove their test saves.
This is automated runtime verification, not interactive UI verification. A failed run may leave a save in its test profile;
use a new unique verification product identity for a fresh run.

## Backend

From `C:\Users\marin\Projects\ConquestusTerrarum` in PowerShell:

```powershell
docker compose up -d --wait
$env:CT_CONNECTION_STRING = 'Host=localhost;Port=5432;Database=conquestus_terrarum;Username=conquestus;Password=conquestus_local_only'
$env:ConnectionStrings__Postgres = $env:CT_CONNECTION_STRING
& 'C:\Program Files\dotnet\dotnet.exe' run --project .\tools\ConquestusTerrarum.Migrations
& 'C:\Program Files\dotnet\dotnet.exe' run --project .\src\ConquestusTerrarum.Api --launch-profile http
```

Keep this terminal running. The migration command is idempotent, including migration 003.
These credentials and HTTP address are for local development. No AWS services are needed.

## Clients and movement

1. Open `client/ConquestusTerrarumClient` in Unity 6000.6.1f1 and let scripts finish compiling.
2. Open `Assets/Scenes/SampleScene.unity`. Use **Conquestus Terrarum > Build Windows Client** to build the standalone
   executable to `client/ConquestusTerrarumClient/Builds/Windows/ConquestusTerrarum.exe`. The build uses the enabled scenes
   and Windows x64 development target. The build folder is ignored by Git.
3. Press Play in the editor for Client A. With no saved game it starts **Disconnected**, with **Create Game**, a join-code
   field and **Join Game**. If a prior save exists, explicitly Forget it before this fresh-match test.
4. Click **Create Game**. Wait for the authoritative map to load in **Waiting for second player**. The HUD shows the match ID, local player ID,
   and six-character join code. It never displays a player token.
5. Click **Copy Join Code**, or read the code from the HUD.
6. Launch the standalone executable for Client B. Use **editor + standalone** for independent local PlayerPrefs profiles.
   Two standalone instances of the same product share one local save and are not separate player profiles; use separate
   OS users or an isolated verification build with distinct test stores for that case. Leave the editor in Play Mode;
   standalone processes continue updating in the background.
7. Enter A's code in B's join-code field and click **Join Game**. Lowercase and surrounding whitespace are accepted.
8. Verify A and B show the **same match ID**, the same four locations, and the same army IDs/owners. Their **local player IDs differ**.
   The initial world has two armies for each player, including the second player's armies before they join.
   Both clients must become **Game Active**, with A learning the start automatically before either issues movement.
9. In A, select an army whose owner matches A's local player ID. The HUD says **YOUR ARMY**. Select a B-owned army;
   it says **OPPONENT ARMY — inspection only**. Clicking another location must not move it, and Retreat must be disabled.
10. Repeat in B, verifying B can control only B-owned armies. Relation colors still describe the selected army's relations;
    the HUD's YOUR/OPPONENT label independently identifies control ownership.
11. In A, select one of A's armies at **West camp**, then click **Crossroads**. Its command response should show movement immediately.
12. Without pressing Refresh in B, verify B sees the same army begin moving within roughly two seconds through update polling.
13. In B, select one of B's armies at **East camp**, then click **Crossroads**.
14. Without pressing Refresh in A, verify A sees B's movement automatically. Switch focus between processes; both must continue polling.
15. Wait for both five-second movements to complete. Both clients should converge on arrival locations, ownership, movement flags,
    and battle markers. Feed events are applied locally when safe; battle transitions may cause one full snapshot resync.
16. Select an army in the Crossroads battle in each client. Verify HP changes in the HUD through focused detail polling
    (approximately every 0.75 seconds). Selecting the opponent's battling army for inspection is allowed.
17. Change selection while HP polling runs. The newly selected army must not receive the previous army's HP/flags.
18. Select your own eligible army and click **Retreat Selected** while the battle is still active. Verify both clients observe
    its retreat and eventual arrival. Select an opponent army and confirm Retreat remains disabled even if it can retreat.
19. If the battle has already finished, explicitly Forget and create a new match, or send the remaining armies from the camps.
20. Use **Refresh** to compare the authoritative snapshot with the displayed map. No periodic full-refresh button use should be
    needed for movement or normal feed synchronization.

The HUD scrolls if the window is small. The map uses the viewport beside it and fits after window resizing;
clicks inside the HUD are excluded from map input.

## Failure and session checks

- Enter a malformed code, then a valid-shaped unknown code: show readable errors and remain disconnected.
- Try joining a full match from a third process: show that it already has two players, without showing a map or credentials.
- Stop the API and try Create/Join: show a backend-unavailable message. Restart it, then retry.
- If creation/join succeeds but the first snapshot load fails, use **Retry Load**. The issued credential was already saved;
  closing/restarting and Continue also recovers it without claiming another slot. If saving itself fails, keep the client
  open and use Retry Load to retry saving; Back to Menu refuses to drop an unsaved issued credential.
- Start a refresh/command, then Back to Menu, Continue, or explicitly Forget and create another match. Late callbacks must not overwrite the new session,
  cursor, map, status, or selected-army details. Old polling callbacks are guarded by state generations.
- A 401 is shown as a rejected player session, a 403 as an ownership error, and a 404 as a missing match/code.
  Snapshot 401/404 errors explain that the save cannot be resumed and offer Forget; the credential is retained until that
  explicit action. Temporary network/503/parser failures keep the save and Retry Load available. Malformed local saves
  remain visible with an error and require explicit Forget before replacement. Storage errors are sanitized too.
  Response bodies, local records and Authorization headers are never logged or shown.

## Restart/reconnect scenarios

Run each identity in its own local profile as described above. Record only the public match/player IDs, army IDs and join
code; never record the player token. For editor restarts, stop Play, close/reopen the project, then enter Play again.

A. **Creator:** Create Game, record the join code, match ID, player ID and owned army IDs. Close the client, reopen, and
click Continue Game. Verify all recorded IDs, code and ownership agree. The map comes from a fresh server snapshot;
Create/Join remain unavailable until explicit Forget. Have B join with the original code to show the second slot remains usable.

B. **Joiner:** Join A's code, record B's match/player/owned army IDs, close and restart B, then Continue. Verify the same
identity/control and A's unchanged identity. A repeat Join would return match-full; Continue must succeed on that full match.

C. **Moving army:** Move an owned army from its camp to Crossroads, close before the five-second arrival, wait briefly,
restart and Continue. If still moving, verify the visual position advances from the server `startedAt`/`arriveAt` anchors.
Repeat with restart after arrival and verify the army appears at Crossroads. No saved movement or cursor should be used.

D. **Battle:** Move armies from both camps to Crossroads. While the battle is active, close/restart each client and Continue.
Select an owned battling army and observe HP decrease through the existing ~0.75 second detail polling; select an enemy
and verify inspection still works while Move/Retreat remain blocked. Close again, let combat finish, then Continue and
verify authoritative destroyed/surviving armies and cleared battle markers. No continuous HP feed is added.

E. **Backend offline:** Save a valid session, stop the API, restart the client and Continue. Verify a retryable error and
that Back to Menu still offers Continue. Restart the API and use Retry Load (or Continue after returning to the menu).
Verify the same identity/owned armies. Also interrupt between successful credential issuance and initial snapshot loading:
the isolated smoke harness below deliberately stops the API at this boundary and verifies the credential survives restart.

F. **Invalid credentials/missing match:** Use only an isolated test build/profile. The smoke harness substitutes a valid-format
fake token for its own saved credential to provoke 401, and a random match ID with the real credential to provoke 404.
Verify the explanatory “cannot be resumed” message, retained local save and explicit Forget action. Never edit a real player's
credential for this test and never paste a saved record into a terminal, log or source file.

G. **Forget:** Save a session, click Forget Saved Game, close/restart, and verify Continue is absent and Create/Join are
available. Removing the local save does not release the server slot. Create a fresh match when a new slot is needed.

H. **Races:** Begin Continue then immediately Back to Menu; no later map/status may appear. Repeat Continue, Back, Continue;
the newest snapshot must win. Begin Continue/Refresh then Forget and Create Game; only the new match may appear. Repeat
while feed and selected battle detail requests are pending. Cursor, selection, HP and errors must never cross generations.

## Automated built-player recovery smoke

`client/verification/RecoverySmoke.cs` is verification source outside Unity Assets, so normal builds exclude it. To reproduce,
make an **isolated** project copy under ignored `Builds`, copying Assets/Packages/ProjectSettings. Change ONLY that copy's
company and product names to a unique verification identity (protects normal PlayerPrefs), copy `RecoverySmoke.cs` into its
Assets, and build using the existing `MultiplayerBuild.BuildWindows` method. The creator uses the production PlayerPrefs
store; the joiner uses a separate test key in that same isolated profile. Never ship the harness or use the normal product
identity for these fault tests. Do not copy the harness build over the clean delivered build.

With PostgreSQL migrated, the solution built, API port 5108 free, and a new empty output directory, run:

```powershell
& .\client\verification\Run-ReconnectSmoke.ps1 `
  -PlayerPath 'C:\path\to\isolated-project\Builds\Windows\ConquestusTerrarum.exe' `
  -OutputDirectory 'C:\path\to\ignored\Builds\ReconnectResults-new-run'
```

The runner owns/stops only its API process, deliberately creates an outage, launches real Windows player processes,
checks same-process Retry Load and repeated process recovery, moving/active/finished battle snapshots, focused HP,
ownership/cursor, replacement policy, stale guards, 401/404 and Forget across restart. It writes sanitized pass/fail results
and public IDs/code only. It removes its test saves at the end of a successful run. A failed run may leave test saves;
use another unique verification product identity for a fresh run. Runtime automation does not constitute interactive HUD testing.

## Regression and batch build

```powershell
& 'C:\Program Files\dotnet\dotnet.exe' build .\ConquestusTerrarum.sln
$env:CT_TEST_CONNECTION_STRING = 'Host=localhost;Port=5432;Database=conquestus_terrarum;Username=conquestus;Password=conquestus_local_only'
& 'C:\Program Files\dotnet\dotnet.exe' test .\ConquestusTerrarum.sln
```

With this Unity project closed, the same Windows build can run without interactive editor control:

```powershell
& 'C:\Program Files\Unity\Hub\Editor\6000.6.1f1\Editor\Unity.exe' `
  -batchmode -nographics -quit `
  -projectPath 'C:\Users\marin\Projects\ConquestusTerrarum\client\ConquestusTerrarumClient' `
  -executeMethod ConquestusTerrarum.Client.Editor.MultiplayerBuild.BuildWindows `
  -logFile 'C:\Users\marin\Projects\ConquestusTerrarum\client\ConquestusTerrarumClient\Logs\multiplayer-build.log'
```

Do not launch a second editor against a project already open in Unity. Use an isolated project copy for batch verification,
or close the editor first. No server simulation, session schema, or change-feed architecture changes are needed for this flow.
