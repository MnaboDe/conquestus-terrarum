# UI artist guide

Open `client/ConquestusTerrarumClient` in Unity 6000.6.1f1, then open
`Assets/Scenes/SampleScene.unity`. The scene contains the camera/light,
`ConquestusTerrarumClient` composition root, a nested `UiRoot`, and an EventSystem.
Press Play to see Main Menu. Create or Continue to preview the World with real data.
Use editor + standalone for independent player profiles.

## Assets to edit

- `Assets/UI/Prefabs/UiRoot.prefab`: Canvas, 1920x1080 CanvasScaler, screen instances and overlays.
- `Screens/MainMenuScreen.prefab`: title, Create, join field, Continue, Retry Load, Forget and inline feedback.
- `Screens/ConnectingScreen.prefab`: indeterminate loading presentation.
- `Screens/WorldHudScreen.prefab`: top bar, map area, army panel and menu navigation.
- `Components/Panel.prefab` and `PrimaryButton.prefab`: shared surfaces and button styling.
- Other component prefabs: WorldStatusPanel, JoinCodePanel, ArmyPanel, ErrorPanel and LoadingOverlay.
- `Dialogs/ConfirmationDialog.prefab`: modal backdrop, Forget warning and confirm/cancel controls.

Paths above are relative to `Assets/UI/Prefabs`. Open a screen in Prefab Mode for layout work.
World can be previewed without a server in Prefab Mode; placeholder labels have no gameplay authority.
For the whole Canvas preview, instantiate UiRoot in a temporary scene and enable only the screen being inspected.
Do not save preview-only visibility changes into SampleScene. Runtime owns screen visibility.

Edit Images, colors, sprites, TMP fonts, RectTransforms, layout groups and LayoutElements normally.
Placeholder panels/buttons use Image colors; there is no required custom sprite. The bundled TMP essentials
and Liberation Sans font/license live in `Assets/TextMesh Pro`. Keep text readable and controls within the
Canvas at 1280x720, 1920x1080 and 2560x1440. Use prefab overrides for local variants.

`WorldMapArea` defines the camera viewport. Moving/resizing it moves the usable map area; keep it clear of
the army panel and navigation. Visible interactive UI should have a raycastable Image/Graphic beneath it.
Decorative labels have Raycast Target off; screen backgrounds block clicks, while the World root/map area do not.

## Wiring and boundaries

Scripts in `Assets/Scripts/ConquestusTerrarum/UI` present data and collect intent. Preserve their serialized
references, buttons and input bindings when replacing visuals. UiFlowController coordinates Boot, MainMenu,
Connecting and World. Waiting/Active, army inspection and future feature panels belong inside World.
WorldMapViewport only measures layout. It does not determine gameplay state.

Do not change MatchController, networking/session/storage, map gameplay or pointer command code for styling.
Never put player tokens, saved records or Authorization headers in labels, prefabs, scenes or screenshots.
Only the intended public join code is displayed. Army IDs are public inspection labels.

`Assets/Editor/UiFoundationBuilder.cs` authored the initial assets once. It is **not needed** to open, play,
edit or build the project, and refuses to overwrite existing UiRoot assets. Normal runtime loads the saved prefabs.
Use **Conquestus Terrarum > Build Windows Client** after edits; smoke harnesses remain outside Assets.
The builder's identity/confirmation upgrade only fills missing wiring; it does not regenerate existing screens.
Use **Conquestus Terrarum > Validate UI Foundation** after saving open scenes to check fonts, scripts and references.
See `UI_FOUNDATION.md` for architecture, hierarchy, confirmation semantics and the designer review checklist.

Economy, diplomacy, research, construction, recruitment, territory management, accounts, audio, localization
and final art are intentionally unimplemented. Add future panels within World rather than global feature screens.
