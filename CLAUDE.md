# CLAUDE.md

This file provides guidance to Claude Code (claude.ai/code) when working with code in this repository.

## What this repo is

A **framework** Core Keeper mod. Other mods register their settings into a shared Options-menu screen: a consumer calls `ModSettings.Section(this)` in its `IMod.Init` (or `EarlyInit` for bake-time settings), chains a few widget declarations, and Mod Settings Menu renders them as a labelled box under **Options → Mod Settings** and persists every value through a CoreLib `ConfigFile`. The consumer writes no UI, prefab, or `System.IO` code.

Namespace / internal name `ModSettingsMenu`; displayName "Mod Settings Menu". `requiredOn: 3` (ClientAndServer). One runtime dependency: **CoreLib** (declared in the `.asset` `dependencies:` and the runtime asmdef). Consumers depend on **both** ModSettingsMenu and CoreLib. Personal-use, non-commercial (Pugstorm EULA).

The reference consumer is **Faster Talents**; in this mod family every other gameplay mod depends on it for its settings (all siblings except the standalone Simple Crafting Pool Extender). Distributed on mod.io (not Thunderstore/BepInEx).

The parent `../CLAUDE.md` holds the mod-agnostic SDK/CrossOver guidance shared by every mod under `core_keeper/`.

## Build and deploy

```bash
source .envrc           # exports UNITY_BIN, SDK_PATH, MOD_INSTALL_PATH, MOD_NAME, LOC_YAML, …
../utils/build.sh      # Unity batchmode build; on Darwin auto-runs install-macos.sh
```

Unity Editor must be closed (it locks the shared SDK project). `utils/link.sh` symlinks the repo's `unity/` mirror into `$SDK_PATH/Assets/`: one **directory** symlink for `unity/ModSettingsMenu/`, plus file symlinks for the Assets-level files beside it (`ModSettingsMenu.asset`, `.asset.meta`, `.meta`). `build.sh` invokes it idempotently on every run, so worktree switches and repo moves self-heal.

`unity/` is the canonical source — a 1:1 mirror of the SDK's `Assets/` tree holding **every** file the Editor generates for the mod: `.cs` sources, both `.asmdef` files, the ModBuilderSettings `.asset`, the prefab, the Art sprites, the generated localization `TextDataBlock`s, and all `.meta` GUID carriers. Edit in `unity/`; the SDK picks up the change on the next refresh.

The runtime `ModSettingsMenu.asmdef` starts from the SDK "Create New Mod" wizard's comprehensive game-DLL reference set, plus one added reference: **CoreLib** (for the `ConfigFile` API). No manual game-DLL wiring is needed.

No automated tests — verification is a manual in-game check: with the reference consumer (Faster Talents, or another migrated sibling) installed, open **Options → Mod Settings**, confirm the section box renders, edit a widget, and confirm the value persists across a relaunch.

Localization is generated at build: `LocalizationGenerator` (shared editor helper) templates `localization/localization.yaml` (EN/DE for the three framework UI terms) into native `TextDataBlock` assets under `unity/ModSettingsMenu/Localization/Generated/`, driven by `LOC_YAML`/`LOC_OUT`/`LOC_TABLE` in `.envrc`. `LOC_YAML` lives outside `unity/` so the ModBuilder doesn't pack the source yaml.

## Architecture

Harmony patch classes are **auto-discovered** by the loader — there is no `PatchAll()` call. The code splits into three namespaces plus the shared editor helpers symlinked in from `../utils/`.

### `ModSettingsMenu` (bootstrap + menu mount)

- **`ModSettingsMenuMod` (`IMod`)** — bootstrap. `EarlyInit` grabs the mod's own `AssetBundle` (`GetModInfo().AssetBundles[0]`); `ModObjectLoaded` keeps the `GameObject` carrying a `ModSettingsScreen` as `MenuPrefab`; `Update` runs two one-shot/deferred jobs — **PreWarm** the menu once on the first frame the instance exists and there is ≥1 consumer section, and a frame-countdown that fires the deferred restart prompt. Owns the free menu id `SettingsMenuType = (RadicalMenu.MenuType)29314` (outside the vanilla `RadicalMenu.MenuType` enum; distinct from GMCM's 1493 / HealthBars' 19901).
- **`MenuPatch`** (`[HarmonyPatch]`) — mounts the screen into the vanilla Options menu:
  - `MenuManager.Init` **prefix** — finds the "Go to UI settings" push-menu entry (`menuToPush == UI_OPTIONS`), clones it, repoints the clone at `SettingsMenuType`, inserts it right after the original, and sets its label with `SetText("ModSettingsMenu-UI/Title")` (NOT `Render` — see gotchas).
  - `MenuManager.Init` **postfix** — instantiates `MenuPrefab` under `Manager.camera.uiCamera.transform`, kept inactive; stores it as `MenuInstance`.
  - `RadicalMenu.TypeToMenu` **prefix** — resolves `SettingsMenuType` to `MenuInstance` (returns `false` to short-circuit vanilla); everything else falls through.
- **`Loc`** — resolves a loc term for the active language via `API.Localization.GetLocalizedTerm`; `T(term)` for framework-own strings (yaml guarantees a value) and `T(term, fallback)` for consumer strings (falls back to the raw key/token when the consumer ships no term).

### `ModSettingsMenu.Settings` (consumer API + persistence)

- **`ModSettings`** — the public entry point and section registry. `Section(IMod consumer)` resolves the consumer's `modId` (`Metadata.name`) + `displayName` from the `IMod` ref, and returns a `SectionBuilder`. `Register` de-dups by `modId` (first `Build()` wins, warns).
- **`SectionBuilder`** — fluent declaration. Each widget method (`Toggle`/`Slider`/`Stepper`/`Choice<T>`) binds a CoreLib `ConfigEntry` via `_file.Bind("Settings", key, def, desc)`, hands back a typed `SettingHandle<T>` via `out`, and records a `SettingDef`. `Hint`, `SortOptions`, `RequiresRestart` (marks the last-declared setting), and `Build` complete the chain. Loc term for a key is `<ModId>-Config/<key>`.
- **`SettingHandle<T>`** — the typed value façade the consumer holds. Delegate-backed so it can front either a `ConfigEntry<T>` directly (Toggle/Slider/Stepper) or a token-mapped `ConfigEntry<string>` (Choice<T>, whose token is `value.ToString()`). `Value` reads live / writes-persist; `OnChanged` fires on any change.
- **`SettingModel.cs`** — the non-generic descriptors the UI reads: `ModSection` (per-consumer box) and `SettingDef` (one setting: `Kind`, numeric bounds, derived loc `Term`, `RequiresRestart`, and the live `ConfigEntryBase Entry`), plus the enums `SettingKind {Toggle,Slider,Stepper,Choice}`, `SliderDisplay {Steps,Number,Percent}`, `OptionSort {AsDeclared,ByKey,ByLabel}`.
- **`ConfigStore`** — a `Dictionary<modId, ConfigFile>` cache. Creates one CoreLib `ConfigFile($"{modId}/config.cfg", saveOnInit: true, info)` per consumer. CoreLib does all `System.IO` in its own trusted assembly via `API.ConfigFilesystem`, so the framework (and consumers) stay **sandbox-clean** — no `skipSafetyChecks` (the `.asset` has `skipSafetyChecks: 0`). Auto-save (`SaveOnConfigSet`) is on, so every write persists immediately.

### `ModSettingsMenu.UI` (the rendered screen)

- **`ModSettingsScreen : RadicalMenu, IScrollable`** — this component *is* the adapted vanilla `UISettings` prefab (swapped in for CK's `RadicalOptionsMenu`), so it inherits CK's open/close, navigation, and scroll machinery. Open sequence is three steps for a reason: `Activate` → `Populate` (build structure + fill `menuOptions`) → `base.Activate` (hierarchy goes active) → `RenderContent` (render layouts *now*, because `LinearLayout` skips inactive children — heights would compute as 0 before activation). Rebuilds every open (vanilla `PugText`s free their glyphs on disable). Sections render **alphabetically by `DisplayName`**; options within a box follow the section's `OptionSort`. `PreWarm()` pays the one-time first-enable cost at load via a same-frame `SetActive(true)/SetActive(false)`. `Deactivate` consumes the restart-dirty flag and requests the deferred prompt.
- **`SectionBox`** — a tiny `MonoBehaviour` on the section-template prefab exposing `header`, `hint`, and `widgetContainer` as **serialized references** (the screen wires by reference, not by fragile `Find()` paths). The `widgetContainer` is a `LinearLayout` with a 9-slice border background that auto-sizes to its rows — the visible box.
- **`SettingWidget : RadicalMenuOption`** — one class renders **all four** kinds. Drives the value through the non-generic `ConfigEntryBase.BoxedValue` (never sees `T`), casting per `Kind`; CoreLib clamps + auto-saves. `←/→` → `OnSkimLeft/Right`; click/Space → `OnActivated` → `Adjust(+1)`. Per-kind `ValueString`: Toggle on/off term, Stepper int, Choice localized-token, Slider Steps/`Number`/`Percent`. The `Steps` `♦/♢` chain uses `♦`/`♢` escapes (pure-ASCII source; a literal diamond is encoding-unsafe in the Roslyn sandbox) and only renders in the `boldLarge` font atlas, so `Bind` switches a Steps-slider's value font accordingly.

### Shared editor helpers

`../utils/CLIBuildHelper.cs`, `CLIPublishHelper.cs`, `LocalizationGenerator.cs` (namespace `CoreKeeperModUtils`) are **not** vendored: `utils/link.sh` symlinks them into `unity/ModSettingsMenu/Editor/`, so they compile into the editor-only `ModSettingsMenu.Editor` asmdef (a combined runtime+editor asmdef cannot reference editor-only types). `CLIBuildHelper` wraps `ModBuilder.BuildMod`, `CLIPublishHelper` drives the mod.io publish, and `LocalizationGenerator` generates the loc assets — all for `unity -batchmode -executeMethod`. Mod identity comes from `MOD_NAME` in `.envrc`, so one source serves every mod. The `.cs` symlinks and their Unity-generated `.meta` are gitignored (nothing references them by GUID).

Patch targets (`MenuManager`, `RadicalMenu`, `RadicalOptionsMenuOption_PushMenu`, `PugText`, …) were identified by decompiling the SDK's bundled game DLLs with `ilspycmd`.

## Mod-specific gotchas

Adapting a vanilla `UISettings` prefab into a mod AssetBundle surfaced a series of CK-UI traps, each verified in-game. Full detail (with the code paths) lives in `docs/tutorial.md` §20; the load-bearing ones:

- **"Red twin" — `SetText`, never `Render`, on a shared prefab template.** The Options-menu entries live on the **shared** `optionsMenuPrefab` that `MenuManager_PreInit` mutates. `PugText.Render` bakes glyph `SpriteRenderer`s into that prefab; CK's `InstantiateMenu` then clones them as **orphaned** renderers the live `PugText` never tracks or clears — a frozen duplicate label. `SetText` only sets `textString` (0 glyphs), leaving a clean template the live instance renders fresh.
- **Clone parentless, THEN `SetParent`.** `Instantiate(go, parent)` activates the clone mid-clone and fires `OnEnable`/`ResetEffect` before the inner `PugText` is fully cloned → NRE. A parentless clone finishes first; parenting then activates cleanly.
- **Build ≠ render — split them.** `LinearLayout` skips children while the hierarchy is inactive (heights = 0). Build structure before `base.Activate` (so options exist + are navigable), render layouts after (so boxes size to real text heights), innermost-first.
- **`RequiresRestart` prompt must defer off the `Deactivate` call stack.** `StartNewDisplaySequence` → `ShowPopUpMenu` → `PushMenu(POP_UP)` re-enters the menu stack mid-pop and orphans the Cancel/Yes buttons across every later menu. `Deactivate` sets a frame countdown (`ModSettingsMenuMod.RequestRestartPrompt`); `Update` shows CK's own `Menu/RestartToApplyModChanges` popup a few frames later — mirroring CK's `Invoke("RestartToApplyModChanges", 0.1f)`. Reusing CK's shipped term + `Manager.platform.Restart()` gives a localized dialog for free.
- **PreWarm, not pre-build.** The first menu open froze ~1 s (worse under Wine); ~98% of it is the instance's first `SetActive(true)` `OnEnable` cascade (first AssetBundle asset load / shader-variant compile), instance-specific (not shared with vanilla menus). Pre-building the structure was measured useless (~1.3 ms). `PreWarm` pays the enable cost at load with a same-frame enable/disable — 1039 ms → 15.7 ms on first real open.
- **The UI camera z-sorts transparents (not by `sortingOrder`); `SpriteMask` needs the built-in Sprites-Mask material and its scale is its size; a custom shader ignores the SpriteRenderer tint in a bundle (use built-in Sprites-Default); `VisibleInsideMask` glyphs are invisible with no active mask.** See tutorial §20.
- **The Editor reserializes prefabs on save**, overwriting hand-authored prefab YAML (resets background active/z, deletes objects). Per the project rule (`feedback_corekeeper_prefab_edits_in_editor` memory), make prefab edits with the Editor **closed**, and never mutate prefab files while the user is in the Editor.

`docs/roadmap.md` tracks the next widget batch (Button/Action-Row, Info, Separator/Label) and out-of-scope items.

## macOS / CrossOver

The mod is deployed through the fake-mod.io workaround (see parent `../CLAUDE.md`). This mod's fake mod.io ID is **`9999991`**; the siblings use distinct IDs (`disable-durability` `9999999`, `faster-talents` `9999998`, `item-checklist` `9999997`, `caveling-divining-rod` `9999996`, `simple-crafting-pool-extender` `9999995`, `faster-pet-talents` `9999994`, `reusable-cattle-box` `9999993`, `rebalance-key-crafting` `9999992` — they must differ). Do not open the in-game Mods menu while installed; re-run `../utils/build.sh` to restore if the cache is wiped.

## Publishing to mod.io

`../utils/upload.sh` publishes this mod. It runs the shared Editor class `CoreKeeperModUtils.CLIPublishHelper.Publish` (symlinked in from `../utils/`, alongside `CLIBuildHelper`) via Unity batchmode. The publish reads `MOD_REPO_ROOT` (set in `.envrc`) to locate `CHANGELOG.md`.

- `Editor/ModSettingsMenu.Editor.asmdef` references the mod.io plugin DLL via `overrideReferences: true` + `precompiledReferences: ["modio.UnityPlugin.dll"]`.
- The published version comes from the topmost `## [x.y.z]` entry of `CHANGELOG.md` (currently **1.1.0**); bump it before publishing.
- The profile logo is `unity/ModSettingsMenu/Editor/logo.png` (readable, uncompressed; min 512×288).
- The real mod ID is **`6211950`**, in `unity/ModSettingsMenu/Editor/ModSettingsMenu_modio.asset`.
- The mod.io listing lists **CoreLib** as a dependency (synced from the `.asset` `dependencies:` by `CLIPublishHelper`).
- One-time: log in via the SDK window's "Log in" tab before the first publish.

## Conventions

- Commit messages: [Conventional Commits](https://www.conventionalcommits.org/) — `type(scope): subject`, imperative, no emoji.
- Documentation files (`CLAUDE.md`, `README.md`, `docs/`) are English; chat answers are German.
- The user prefers `git commit --amend` / `git reset --soft` over fix-up commits, and `git rebase` over `git merge`. "Push" means all remotes (`origin` GitHub + `backup` bragi).
- Each mod is an independent git repo with its own `CLAUDE.md` for mod-specific detail.
