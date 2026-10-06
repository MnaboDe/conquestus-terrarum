# UI foundation

Open `client/ConquestusTerrarumClient` in Unity **6000.6.1f1** and load
`Assets/Scenes/SampleScene.unity`, the enabled Windows build scene. Press Play for Main Menu.
Use **Conquestus Terrarum > Build Windows Client** for the standalone client.

## Architecture and flow

The pure `UiFlowStateMachine` owns **Boot → MainMenu → Connecting → World** presentation.
`UiFlowController` subscribes to MatchController's batched `PresentationChanged` event and forwards
screen intents to the existing controller. It never sends HTTP, calculates ownership or decides gameplay rules.
Screens/panels receive authoritative DTOs and controller-supplied command eligibility. Subscriptions are removed
on disable and rebound once on enable. No static UI event bus, runtime UI generator or primary IMGUI HUD exists.

WaitingForPlayers and Active are server participation phases **inside World**, not global screens.
The same World prefab instance stays present during activation, command loading and army elimination.
There is no Results/Victory/Defeat/Draw destination. Future feature panels belong within this persistent shell.

Create/Join show Connecting until a snapshot is available. Invalid local codes stay inline in Main Menu and
send no request. A failed initial load returns to a usable menu with sanitized feedback and Retry Load.
Continue reuses saved credentials and loads a fresh snapshot/cursor; it never claims another player slot.
Back to Menu clears the connected view while retaining the save. Forget opens a confirmation: **Keep saved world**
changes nothing; **Forget Saved World** clears local access. The server world/player slot remain occupied,
and the invitation code cannot recover that credential. Temporary outages and existing 401/404 semantics retain
the save until explicit confirmation. Broader World errors use ErrorPanel; World loading uses LoadingOverlay.

## Actual assets and scene

All paths below are relative to `client/ConquestusTerrarumClient/Assets`.

- `UI/Prefabs/UiRoot.prefab`: Canvas, CanvasScaler (1920x1080, Scale With Screen Size, match 0.5), raycaster and screen/overlay references.
- `UI/Prefabs/Screens/`: MainMenuScreen, ConnectingScreen and WorldHudScreen.
- `UI/Prefabs/Components/`: ArmyPanel, WorldStatusPanel, JoinCodePanel, ErrorPanel, LoadingOverlay, Panel and PrimaryButton.
- `UI/Prefabs/Dialogs/ConfirmationDialog.prefab`: modal backdrop, accurate Forget warning and confirm/cancel controls.
- `Scripts/ConquestusTerrarum/UI/`: presentation behavior; `ClientInputValidation` shares existing code/alphabet normalization.
- `TextMesh Pro/`: bundled font, material/shader resources and font license. No custom art/package was introduced.

```text
SampleScene
  Main Camera / Global Light 2D
  ConquestusTerrarumClient (bootstrap, API, MatchController, MapPresenter, PointerInput)
    UiRoot (Canvas, CanvasScaler, GraphicRaycaster, UiFlowController)
      MainMenuScreen / ConnectingScreen / WorldHudScreen
      OverlayLayer (ErrorPanel, LoadingOverlay, ConfirmationDialog)
    RuntimeMap (created by existing map rendering while connected)
  EventSystem (InputSystemUIInputModule with bound default UI actions)
```

The World map remains the main content. Its RectTransform, WorldMapArea, supplies the camera viewport as Canvas
layout renders. PointerInput raycasts the current mouse/touch screen position before map hit-testing and rejects
UI hits or positions outside the camera. This avoids stale pointer-over-UI state and hardcoded HUD rectangles.
Confirmation's full-screen graphic blocks pointer input; Main Menu's CanvasGroup disables background controls
for keyboard navigation too. Public player ID is a secondary World navigation label; bearer tokens are never labels.

Artists may restyle Images, TMP fonts, colors, sprites, anchors, sizes, layout spacing and button states in Prefab Mode.
Preserve serialized behavior references, Canvas/EventSystem bindings and intended raycast surfaces. See
`UI_ARTIST_GUIDE.md` for preview steps. Do not style through MatchController, networking, storage or synchronization.
Never place credentials in scenes, prefabs, labels, logs or screenshots.

`Editor/UiFoundationBuilder.cs` is retained for first authoring and a focused, idempotent identity/confirmation
upgrade; it preserves existing screen layouts. It is not run by normal startup/build and refuses full regeneration
over an existing UiRoot. `Editor/UiFoundationValidation.cs` checks all 12 prefabs, required references, fonts/materials,
missing scripts, Canvas/EventSystem, launch visibility, bootstrap wiring and Build Settings. Use
**Conquestus Terrarum > Validate UI Foundation** after saving open scenes. Batch validation loads and saves an
isolated copy of SampleScene; an intentionally unwired temporary dialog must be rejected by the validator.

## Game-designer review and manual QA

Start the local backend using `MULTIPLAYER_TEST.md`. Use editor + standalone for independent player profiles;
two normal standalone processes share a local save. Do not export a saved record. If a prior save is present,
use Continue or deliberately confirm Forget before a fresh two-player test.

1. Launch A; Main Menu is the only visible top-level screen.
2. Create World; Connecting presents status and prevents duplicate submission.
3. World opens waiting, with a readable copyable invitation and disabled orders.
4. Launch B; enter empty, short and malformed codes. Check inline feedback and no red Console stack trace.
5. Enter the correct code (lowercase/outer whitespace accepted); Connecting leads to B's World.
6. A automatically becomes Active; waiting invitation disappears and the same World shell stays visible.
7. Compare secondary public player IDs: different players, same shared map.
8. Click buttons, input fields and army panel surfaces. No map action may occur underneath.
9. Select an owned army; check identity, ownership, location/movement, unit count, HP and command hint.
10. Move to a connected location; the other client observes movement and interpolation.
11. Inspect an enemy; it cannot receive commands. Destroyed armies also remain non-commandable.
12. Fight, observe live HP/details, then Retreat while eligible; both clients see it.
13. Back to Menu; Continue restores the same player/world, phase and polling. Repeat after process restart.
14. Stop the API and Continue; verify recoverable feedback and saved access. Restart it and Retry Load.
15. Open Forget; verify accurate loss warning, disabled background controls and no save removal yet.
16. Keep saved world; verify the menu is usable and Continue still works. Reopen and confirm Forget.
17. Restart: Continue is absent, Create/Join return; server slot remains occupied.
18. Eliminate armies; World stays Active/inspectable/resumable and no end/result UI appears.
19. Repeat at 1280x720, 1920x1080 and 2560x1440; inspect text, spacing, map fit and dialog margins.

Known limits: intentionally plain default art, desktop 16:9 layouts, one anonymous local save and two world slots.
No account recovery, mobile redesign, settings, economy, diplomacy, research, construction or recruitment UI exists.
Connecting has no invented cancellation operation. Runtime automation dispatches Unity events and captures the
rendered assets; it does **not** replace human mouse/keyboard/focus QA. Existing lifecycle/recovery harnesses remain
outside Assets, and normal builds exclude all smoke code. See `MULTIPLAYER_TEST.md` for repeatable automated commands.
