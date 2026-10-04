# Local two-player test

Use Unity **6000.6.1f1**, the system **.NET 10** SDK, and two independent clients talking to
`http://localhost:5108`. Sessions live only in memory. Stop/Disconnect loses that client's credential;
an occupied slot cannot be reclaimed by entering its join code again. Use a new game for a fresh test.

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
3. Press Play in the editor for Client A. It should start **Disconnected**, with **Create Game**, a join-code field,
   and **Join Game**, without creating any match automatically.
4. Click **Create Game**. Wait for the authoritative map to load. The HUD shows the match ID, local player ID,
   and six-character join code. It never displays a player token.
5. Click **Copy Join Code**, or read the code from the HUD.
6. Launch the standalone executable for Client B. Alternatively launch the executable twice instead of using the editor.
   Both standalone processes continue updating in the background; leave the editor running in Play Mode.
7. Enter A's code in B's join-code field and click **Join Game**. Lowercase and surrounding whitespace are accepted.
8. Verify A and B show the **same match ID**, the same four locations, and the same army IDs/owners. Their **local player IDs differ**.
   The initial world has two armies for each player, including the second player's armies before they join.
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
19. If the battle has already finished, create a new match and repeat, or send the remaining armies from the two camps to Crossroads.
20. Use **Refresh** to compare the authoritative snapshot with the displayed map. No periodic full-refresh button use should be
    needed for movement or normal feed synchronization.

The HUD scrolls if the window is small. The map uses the viewport beside it and fits after window resizing;
clicks inside the HUD are excluded from map input.

## Failure and session checks

- Enter a malformed code, then a valid-shaped unknown code: show readable errors and remain disconnected.
- Try joining a full match from a third process: show that it already has two players, without showing a map or credentials.
- Stop the API and try Create/Join: show a backend-unavailable message. Restart it, then retry.
- If creation/join succeeds but the first snapshot load fails, use **Retry Load**. The issued credential remains in memory;
  this does not claim another slot. **Disconnect** intentionally clears it and returns to Create/Join.
- Start a refresh/command, then Disconnect and create another match. Late callbacks must not overwrite the new session,
  cursor, map, status, or selected-army details. Old polling callbacks are guarded by state generations.
- A 401 is shown as a rejected player session, a 403 as an ownership error, and a 404 as a missing match/code.
  Server/parser failures display sanitized messages. Response bodies and Authorization headers are never logged or shown.

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
