# Local two-player test

Use Unity **6000.6.1f1**, the system **.NET 10** SDK, and two independent clients talking to
`http://localhost:5108`. One anonymous session is saved locally after successful Create/Join, before the first
snapshot GET. Startup stays disconnected and offers **Continue** when a saved record exists.
**Back to Menu** (`Disconnect()` in code) clears only the active view and keeps the save. **Forget Saved World**
opens a confirmation before removing the local credential permanently; it does not free the occupied server slot. Forget before creating/joining
another game. The join code cannot recover a previously occupied slot.

The MVP stores only match ID, player ID, opaque player token, and optional join code in a single versioned PlayerPrefs
record (`ConquestusTerrarum.AnonymousSession.v1`). This is local anonymous-session persistence, **not hardened account
credential storage**: PlayerPrefs is not encrypted and can be read by someone with access to the local profile.
Do not share/export its contents or log tokens. `ISessionStorage` can later be replaced with Android Keystore/iOS Keychain
storage. No hard-coded encryption, permanent account, token rotation, or platform-specific runtime API is used.
Snapshots, movement, HP, selections, and feed cursors are never saved. PostgreSQL keeps the token hash without a client
restart expiry. Continue reuses the original token in Authorization and loads a full snapshot, then polls after that
snapshot's `updateCursor`.

## Persistent-world participation

Conquestus Terrarum is a persistent strategy world. Current lifecycle is **WaitingForPlayers -> Active**.
Active is open-ended. Army elimination does not end the world, globally defeat a player, or produce a winner/draw.
No recruitment/economy/territory survival system is implemented yet. A player with no living army can still inspect
and reconnect; only their destroyed armies cannot receive ordinary army commands.

The server returns `phase` as `waitingForPlayers` or `active`, without result fields. Participation comes from the two
persisted player slots, independently of armies/HP. No new migration is required.

1. Create a fresh game in A. The map appears with **Waiting for second player** and the copyable join code. Select owned
   and enemy armies for inspection. Move/Retreat must do nothing. An API command bypassing client guards returns 409
   `match_waiting_for_players` without changing snapshot, version, cursor or feed.
2. Close/reopen A and Continue while waiting. Verify the same invitation/army identity labels, waiting label and disabled commands.
3. Join in B. B sees **Active world**. Leave A untouched: normal feed polling and one snapshot resync must automatically
   show **Active world**. The durable `MatchStarted` update commits atomically with the slot claim. After start the HUD
   marks the invitation closed. A third player still gets match-full.
4. Move/retreat each player's own armies using the tests below. Enemy inspection remains allowed; enemy commands remain
   blocked locally/403 on the server. Continue while Active restores play from the full snapshot cursor.
5. Send all opposing armies into battle. When one side's last army dies, both clients must still show **Active world**.
   Surviving owned armies can move normally. The player with no living army may inspect all world state. With equal armies,
   combat may destroy every army; both clients must still be Active. No global result presentation or finish event appears.
6. Without Refresh, both clients discover destruction through ordinary army/battle feed updates or structural detail resync.
   HP continues to come from focused battle detail polling. Only ordinary domain rules block destroyed-army commands.
7. Close/restart each client after elimination and Continue. Verify the same ownership, Active phase and restored armies.
   Automated harnesses also verify token equality and authoritative cursor without logging the token. The credential remains saved until explicit Forget.

Lazy simulation wakes on reads/polls/commands using database time. A valid survivor command may itself discover elimination
and still commit. Meaningful world changes and ordinary feed events commit together. There are no simulation workers or
global HP events. Joining an old/imported empty or already-eliminated waiting world emits only the normal start event.
Old unknown/obsolete feed entries trigger safe snapshot resync rather than determining the world's phase.

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
combat eliminate one side. It verifies the world stays Active, surviving commands, HP/detail polling and post-elimination restart
recovery. The utility is outside production projects/Unity Assets, uses existing Npgsql, writes no participation state or credentials,
and must only target the newly created isolated verification match. Successful runs explicitly remove their test saves.
This is automated runtime verification, not interactive UI verification. A failed run may leave a save in its test profile;
use a new unique verification product identity for a fresh run.

## UI Foundation manual QA

Use editor + standalone with independent local saves and the API running. Inspect 1280x720,
1920x1080 and 2560x1440: labels should fit, buttons should remain reachable and the map should fit
WorldMapArea. There is no primary OnGUI HUD. See `UI_ARTIST_GUIDE.md` for prefab editing.

1. With no save, Main Menu offers Create World and a join field. Submit empty, short and invalid-alphabet
   codes: inline feedback, no red Unity console error, no network operation. Lowercase/outer whitespace normalize.
2. Create World: Connecting shows useful status without fake percentages, then World shows waiting status,
   copyable invitation and disabled orders. Select an army; its identity, ownership, location, units and HP appear.
3. In B, Join World with A's code. Connecting leads to World. A becomes Active automatically; the same World
   screen stays visible, the invitation closes and valid orders unlock. No Results/Defeat screen exists.
4. Select foreign/own armies. Foreign inspection has no retreat control; owned armies show command eligibility.
   Click a connected location to move, observe both clients, then select a battling army and watch HP update.
   Click Retreat when available and verify both clients observe it.
5. Click Copy code, Back to Menu, panel backgrounds, join field and error Dismiss. No underlying map selection
   or movement may occur. Click clear map space to confirm normal selection/movement still works.
6. Back to Menu retains the save; Continue shows Connecting and restores the same player/world. Restart the
   process and Continue again in waiting, moving, battle and Active states. Polling and ownership must recover.
7. Stop the backend after saving, restart the client and Continue. A readable inline error returns to Main Menu;
   the credential stays saved. Restart the API and Retry Load to recover. During an in-world command failure,
   the reusable error overlay shows sanitized feedback and Dismiss works; world inspection remains possible.
8. Back to Menu, open Forget Saved World and cancel: saved access remains and background controls resume.
   Reopen and confirm Forget, then restart: Continue is absent and Create/Join return. Forget does not
   release a server slot. Invalid credentials/missing world retain an explanatory error and explicit Forget.
9. During a pending command/resync, Back to Menu and Continue again. Old callbacks must not reopen a screen
   or overwrite the new session. Army elimination keeps World Active and resumable.

Automated UI verification uses `verification/UiFoundationSmoke.cs` in an isolated graphical build with a
unique company/product identity (never the normal profile). Copy it into only that copy's Assets and build;
run `verification/Run-UiFoundationSmoke.ps1 -PlayerPath <isolated exe> -OutputDirectory <fresh ignored folder>`.
It dispatches real uGUI button events, uses production map hit-testing/raycasts, captures all three resolutions,
and verifies two-player flow, retreat, outage/retry, restart and Forget. It is runtime automation rather than
human mouse/keyboard QA. Keep the manual checklist above for native focus, typing and pointer interaction.

## Backend setup

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
3. Press Play in the editor for Client A. With no saved game it starts at **Main Menu**, with **Create World**, a join-code
   field and **Join World**. If a prior save exists, explicitly Forget it before this fresh-match test.
4. Click **Create World**. Wait for the authoritative map to load in **Waiting for second player**. The waiting HUD shows
   the public six-character invitation code. It never displays a player token.
5. Click **Copy code**, or read the code from the HUD.
6. Launch the standalone executable for Client B. Use **editor + standalone** for independent local PlayerPrefs profiles.
   Two standalone instances of the same product share one local save and are not separate player profiles; use separate
   OS users or an isolated verification build with distinct test stores for that case. Leave the editor in Play Mode;
   standalone processes continue updating in the background.
7. Enter A's code in B's join-code field and click **Join World**. Lowercase and surrounding whitespace are accepted.
8. Verify A and B show the same four locations, army markers and army IDs. Ownership labels are relative to each player.
   The initial world has two armies for each player, including the second player's armies before they join.
   Both clients must become **Active world**, with A learning the start automatically before either issues movement.
9. In A, select an army at West camp. The HUD says **YOUR ARMY**. Select a B-owned army;
   it says **OTHER PLAYER · INSPECTION**. Clicking another location must not move it, and Retreat must be disabled.
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
18. Select your own eligible army and click **Retreat** while the battle is still active. Verify both clients observe
    the authoritative wounded-unit losses. Healthy reserves survive and travel to the existing retreat destination;
    an entirely wounded army is destroyed without travel. When the battle ends, remaining living wounded units immediately
    show full HP, and dead units remain dead. Select an opponent army and confirm Retreat remains disabled even if it can retreat.
19. If the battle has already ended, send remaining armies from the camps to test another battle in the same Active world.
20. Back to Menu and Continue loads a fresh authoritative snapshot for comparison. Normal movement/feed synchronization
    requires no refresh action.

The Canvas scales from 1920x1080. The map fits the authored WorldMapArea after resizing;
UI graphic raycasts protect map input at the current pointer position.

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

A. **Creator:** Create World, record the join code and visible army IDs. Close the client, reopen, and
click Continue. Verify all recorded IDs, code and ownership agree. The map comes from a fresh server snapshot;
Create/Join remain unavailable until explicit Forget. Have B join with the original code to show the second slot remains usable.

B. **Joiner:** Join A's code, record B's visible owned army IDs, close and restart B, then Continue. Verify the same
identity/control and A's unchanged identity. A repeat Join would return match-full; Continue must succeed on that full match.

C. **Moving army:** Move an owned army from its camp to Crossroads, close before the five-second arrival, wait briefly,
restart and Continue. If still moving, verify the visual position advances from the server `startedAt`/`arriveAt` anchors.
Repeat with restart after arrival and verify the army appears at Crossroads. No saved movement or cursor should be used.

D. **Battle:** Move armies from both camps to Crossroads. While the battle is active, close/restart each client and Continue.
Select an owned battling army and observe HP decrease through the existing ~0.75 second detail polling; select an enemy
and verify inspection still works while Move/Retreat remain blocked. Close again, let combat finish, then Continue and
verify authoritative destroyed/surviving armies and cleared battle markers. No continuous HP feed is added.

E. **Backend offline:** Save a valid session, stop the API, restart the client and Continue. Verify a retryable Main Menu error
and retained save. Restart the API and use Retry Load.
Verify the same identity/owned armies. Also interrupt between successful credential issuance and initial snapshot loading:
the isolated smoke harness below deliberately stops the API at this boundary and verifies the credential survives restart.

F. **Invalid credentials/missing match:** Use only an isolated test build/profile. The smoke harness substitutes a valid-format
fake token for its own saved credential to provoke 401, and a random match ID with the real credential to provoke 404.
Verify the explanatory “cannot be resumed” message, retained local save and explicit Forget action. Never edit a real player's
credential for this test and never paste a saved record into a terminal, log or source file.

G. **Forget:** Save a session, click Forget Saved World, confirm the loss of local access, close/restart, and verify Continue is absent and Create/Join are
available. Removing the local save does not release the server slot. Create a fresh match when a new slot is needed.

H. **Controller harness races:** Begin Continue then immediately Disconnect; no later map/status may appear. Repeat Continue, Back, Continue;
the newest snapshot must win. Begin Continue/Refresh then Forget and Create World; only the new match may appear. Repeat
while feed and selected battle detail requests are pending. Cursor, selection, HP and errors must never cross generations.
Connecting intentionally offers no cancellation button. Native UI races can be exercised with Back to Menu during a World command/resync.

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
checks same-process Retry Load and repeated process recovery, moving/active/ended battle snapshots, focused HP,
ownership/cursor, replacement policy, stale guards, 401/404 and Forget across restart. It writes sanitized pass/fail results
and public IDs/code only. It removes its test saves at the end of a successful run. A failed run may leave test saves;
use another unique verification product identity for a fresh run. Runtime automation does not constitute interactive HUD testing.

## Regression and batch build

### Focused post-battle wounds smoke

`client/verification/PostBattleWoundsSmoke.cs` uses the unchanged production client to Create/Join, move one army
from each player into combat, observe live wounds through focused details, retreat the creator, verify dead wounded
units/healthy reserves/full healing of remaining participants, then reconnect both players after an API/player restart.
World stays Active. The pre-existing dead fixture units must remain dead. Consequences come from authoritative snapshots;
the harness does not implement gameplay in Unity.

Use the same isolated-copy/save-identity precautions described above. Copy `PostBattleWoundsSmoke.cs` into that copy's
Assets and build with the existing helper. Normal runtime Assets must never contain this source. Build the solution,
keep API port 5108 free, and use a fresh output directory:

```powershell
& .\client\verification\Run-PostBattleWoundsSmoke.ps1 `
  -PlayerPath 'C:\path\to\isolated-project\Builds\Windows\ConquestusTerrarum.exe' `
  -OutputDirectory 'C:\path\to\ignored\Builds\WoundsResults-new-run'
```

`WoundsSetup` is a separate verification-only .NET executable. Under the isolated world's row lock it configures
low DPS/high HP, three living units and one dead unit per army before movement. Two existing combat slots leave healthy
reserves, providing time to observe both sides wounded. It changes no participation, outcomes or production rules.
The runner builds its API and fixture before player launch, starts/stops only its own API, restarts it after resolution,
checks four player results and clears both test
saves after successful reconnects. No credentials are written to result/public metadata. Native visual QA is separate.

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
