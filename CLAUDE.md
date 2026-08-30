# CLAUDE.md

This file provides guidance to Claude Code (claude.ai/code) when working with code in this repository.

## What this repo is

A **framework** Core Keeper mod. Other mods register their settings into a shared Options-menu screen: a consumer calls `ModSettings.Section(this)` in its `IMod.Init` (or `EarlyInit` for bake-time settings), chains a few widget declarations, and Mod Settings Menu renders them as a labelled box under **Options → Mod settings** and persists every value through a CoreLib `ConfigFile`. The consumer writes no UI, prefab, or `System.IO` code.

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

No automated tests — verification is a manual in-game check: with the reference consumer (Faster Talents, or another migrated sibling) installed, open **Options → Mod settings**, confirm the section box renders, edit a widget, and confirm the value persists across a relaunch.

Localization is generated at build: `LocalizationGenerator` (shared editor helper) templates `localization/localization.yaml` (EN/DE for the framework's own UI terms) into native `TextDataBlock` assets under `unity/ModSettingsMenu/Localization/Generated/`, driven by `LOC_YAML`/`LOC_OUT`/`LOC_TABLE` in `.envrc`. `LOC_YAML` lives outside `unity/` so the ModBuilder doesn't pack the source yaml.

## Architecture

Harmony patch classes are **auto-discovered** by the loader — there is no `PatchAll()` call.

**Class-by-class reference — `docs/architecture.md`.** Deliberately not inlined here: it covers
every bootstrap/patch class, the consumer API, and the rendered screen's widgets and rows, in the
detail that only matters once you are actually touching one of them. Open it before working on a
specific class — a widget, a screen, a patch, the consumer API — never as a prerequisite for a
build or a publish, neither of which needs any of it.

## Mod-specific gotchas

Adapting a vanilla `UISettings` prefab into a mod AssetBundle surfaced a series of CK-UI traps, each verified in-game. Some carry fuller detail (with the code paths) in `docs/tutorial.md` §20; all of the following are load-bearing:

- **"Red twin" — `SetText`, never `Render`, on a shared prefab template.** The
  Options-menu entries live on the **shared** `optionsMenuPrefab` that
  `MenuManager_PreInit` mutates. `PugText.Render` bakes glyph `SpriteRenderer`s into
  that prefab; CK's `InstantiateMenu` then clones them as **orphaned** renderers the
  live `PugText` never tracks or clears — a frozen duplicate label. `SetText` only sets
  `textString` (0 glyphs), leaving a clean template the live instance renders fresh.
- **Clone parentless, THEN `SetParent`.** `Instantiate(go, parent)` activates the clone
  mid-clone and fires `OnEnable`/`ResetEffect` before the inner `PugText` is fully
  cloned → NRE. A parentless clone finishes first; parenting then activates cleanly.
- **Build ≠ render — split them.** `LinearLayout` skips children while the hierarchy is
  inactive (heights = 0). Build structure before `base.Activate` (so options exist + are
  navigable), render layouts after (so boxes size to real text heights),
  innermost-first.
- **`RequiresRestart` prompt must defer off the `Deactivate` call stack.**
  `StartNewDisplaySequence` → `ShowPopUpMenu` → `PushMenu(POP_UP)` re-enters the menu
  stack mid-pop and orphans the Cancel/Yes buttons across every later menu. `Deactivate`
  sets a frame countdown (`ModSettingsMenuMod.RequestRestartPrompt`); `Update` shows
  CK's own `Menu/RestartToApplyModChanges` popup a few frames later — mirroring CK's
  `Invoke("RestartToApplyModChanges", 0.1f)`. Reusing CK's shipped term +
  `Manager.platform.Restart()` gives a localized dialog for free.
- **PreWarm, not pre-build.** The first menu open froze ~1 s (worse under Wine); ~98% of
  it is the instance's first `SetActive(true)` `OnEnable` cascade (first AssetBundle
  asset load / shader-variant compile), instance-specific (not shared with vanilla
  menus). Pre-building the structure was measured useless (~1.3 ms). `PreWarm` pays the
  enable cost at load with a same-frame enable/disable — 1039 ms → 15.7 ms on first real
  open.
- **The UI camera z-sorts transparents (not by `sortingOrder`); `SpriteMask` needs the
  built-in Sprites-Mask material and its scale is its size; a custom shader ignores the
  SpriteRenderer tint in a bundle (use built-in Sprites-Default); `VisibleInsideMask`
  glyphs are invisible with no active mask.** See tutorial §20.
- **The Editor reserializes prefabs on save**, overwriting hand-authored prefab YAML
  (resets background active/z, deletes objects). Per the project rule
  (`feedback_corekeeper_prefab_edits_in_editor` memory), make prefab edits with the
  Editor **closed**, and never mutate prefab files while the user is in the Editor.
- **The base `RadicalMenu.GetHelpButtonsToShow()` returns
  `Manager.menu.defaultHelpButtons` — Core Keeper's shared list instance.** Appending to
  it instead of to a copy would permanently add the prompt to every vanilla menu in the
  game, and only after this screen has been opened once.
- **`StartNewDisplaySequence` defaults `localizePlaceholders` to `true`,** which looks
  every format field up as a localization term. A literal like a mod's display name then
  renders as `<missing>`; Core Keeper passes `false` in all of its own popups that carry
  a literal.
- **A drill-in row's geometry has one source of truth — the frame — and one hand-kept
  literal.** The frame sprites (`Border` / `SelectedMarker` on `ItemTemplate`) are
  22×1.5; the click collider *and* the row's layout height are both derived from that
  renderer at runtime (`UpdateClickCollider`, and `RowHeightPx` via the shared
  `ModSettingsScreen.FrameHeightPx`), so an Editor resize needs no code change and a
  copied literal cannot go stale — one already did. `ListDetailItem.maxWidth` is now
  `0`: the visible window is defined by the row's own `FieldMask` (21 units from
  row-local 0), not by a capacity that discards characters. The frame is 22 units
  centred at 10.5 and therefore spans `[-0.5, 21.5]`, so a mask sized from the frame
  would let text run past it — the mask keeps the half unit of air the old `maxWidth`
  used to provide. Deriving the height from the *text* instead is doubly wrong: the
  frame is taller than its text-measured slot (which used to overhang into the viewport
  mask at the first and last row, where `gapBetweenItems: 5` cannot absorb it — hence
  `paddingStart`/`paddingEnd` are now `0`, the padding that compensated for it being
  obsolete), and `PugText.Render` reports `Rect.zero` for an empty string, so a blank
  row would collapse to nothing and become unreachable by mouse. The row's own
  `PugText`s must keep `maxWidth: 0` — a non-zero value there makes `PugText.Render`
  wrap, which silently disables the text input's own capacity check entirely (mechanism
  and the general rule in `docs/ck/ui-framework.md` § "A text row in a menu").
- **Core Keeper ships the `RESET_DEFAULTS` hint slot fully wired** (glyph plus the
  localized `Menu/Reset` label) **and requests it in exactly one place** — the
  control-mapping menu, through a `[SerializeField]` list rather than from code
  (`ControlMappingMenu.prefab:2456-2457` → `Pug.ControlMapping:916-924`). No C# file
  names the value, which is why this bullet used to call the slot unused; a grep over
  the decompile cannot see a serialized list. Reusing it here is still correct — the
  two screens are separate and `GetHelpButtonsToShow()` is per-menu — and the label
  means what this menu does with it.
- **The reset poll binds Rewired action 223 (`OpenProfile`)** rather than the more
  obviously-named `MenuSecondaryActivate` (221): 223 is the action actually reported
  by the button whose glyph the slot depicts. **Not** vanilla's own `ResetDefaults`
  (300) — that one belongs to the `ControlMapperUI` category, whose maps serve the
  Controls screen alone, whereas 223 is in `Menu`, the category that applies while any
  menu is open (`docs/ck/ui-framework.md` § "Which input actions you can use inside a
  menu").

`docs/roadmap.md` tracks the next widget batch (Button/Action-Row, Info, Separator/Label) and out-of-scope items.

## macOS / CrossOver

The mod is deployed through the fake-mod.io workaround (see parent `../CLAUDE.md`). This mod's fake mod.io ID is **`9999991`**; the siblings use distinct IDs (`disable-durability` `9999999`, `faster-talents` `9999998`, `item-checklist` `9999997`, `caveling-divining-rod` `9999996`, `simple-crafting-pool-extender` `9999995`, `faster-pet-talents` `9999994`, `reusable-cattle-box` `9999993`, `rebalance-key-crafting` `9999992` — they must differ). Do not open the in-game Mods menu while installed; re-run `../utils/build.sh` to restore if the cache is wiped.

## Publishing to mod.io

`../utils/upload.sh` publishes this mod. It runs the shared Editor class `CoreKeeperModUtils.CLIPublishHelper.Publish` (symlinked in from `../utils/`, alongside `CLIBuildHelper`) via Unity batchmode. The publish reads `MOD_REPO_ROOT` (set in `.envrc`) to locate `CHANGELOG.md`.

- `Editor/ModSettingsMenu.Editor.asmdef` references the mod.io plugin DLL via
  `overrideReferences: true` + `precompiledReferences: ["modio.UnityPlugin.dll"]`.
- The published version comes from the topmost `## [x.y.z]` entry of `CHANGELOG.md`;
  bump it before publishing.
- The profile logo is `unity/ModSettingsMenu/Editor/logo.png` (readable, uncompressed; min 512×288).
- The real mod ID is **`6211950`**, in `unity/ModSettingsMenu/Editor/ModSettingsMenu_modio.asset`.
- The mod.io listing lists **CoreLib** as a dependency (synced from the `.asset`
  `dependencies:` by `CLIPublishHelper`).
- One-time: log in via the SDK window's "Log in" tab before the first publish.

## Conventions

- Commit messages: [Conventional Commits](https://www.conventionalcommits.org/) — `type(scope): subject`, imperative, no emoji.
- Documentation files (`CLAUDE.md`, `README.md`, `docs/`) are English; chat answers are German.
- The user prefers `git commit --amend` / `git reset --soft` over fix-up commits, and
  `git rebase` over `git merge`. "Push" means all remotes (`origin` GitHub + `backup`
  bragi).
- Each mod is an independent git repo with its own `CLAUDE.md` for mod-specific detail.
