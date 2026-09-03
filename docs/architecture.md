# Architecture

Class-by-class reference for this mod's code. Harmony patch classes are **auto-discovered** by
the loader — there is no `PatchAll()` call. The code splits into three namespaces, covered here in
turn, plus the shared editor helpers symlinked in from `../utils/`.

## `ModSettingsMenu` — bootstrap and menu mount

### `ModSettingsMenuMod`

`IMod` bootstrap. `EarlyInit` grabs the mod's own `AssetBundle` (`GetModInfo().AssetBundles[0]`);
`ModObjectLoaded` keeps the `GameObject` carrying a `ModSettingsScreen` as `MenuPrefab` and the one
carrying a `ListDetailScreen` as `ListDetailPrefab`; `Update` runs two one-shot/deferred jobs —
**PreWarm** the menu once on the first frame the instance exists and there is ≥1 consumer section,
and a frame-countdown that fires the deferred restart prompt.

Owns two free menu ids outside the vanilla `RadicalMenu.MenuType` enum (distinct from GMCM's 1493 /
HealthBars' 19901): `SettingsMenuType = (RadicalMenu.MenuType)29314` for the settings screen and
`ListDetailMenuType = (RadicalMenu.MenuType)29315` for the list drill-in.

### `MenuPatch`

`[HarmonyPatch]` — mounts the screen into the vanilla Options menu:

- `MenuManager.Init` **prefix** — finds the "Go to UI settings" push-menu entry
  (`menuToPush == UI_OPTIONS`), clones it, repoints the clone at `SettingsMenuType`, inserts it
  right after the original, and sets its label with `SetText("ModSettingsMenu-UI/Title")` (NOT
  `Render` — see the gotchas in `CLAUDE.md`).
- `MenuManager.Init` **postfix** — instantiates `MenuPrefab` under
  `Manager.camera.uiCamera.transform`, kept inactive; stores it as `MenuInstance`. If a
  `ListDetailPrefab` was loaded, instantiates it the same way as `ListDetailInstance`
  (the pushed drill-in screen).
- `RadicalMenu.TypeToMenu` **prefix** — resolves `SettingsMenuType` → `MenuInstance` and
  `ListDetailMenuType` → `ListDetailInstance` (each returns `false` to short-circuit vanilla);
  everything else falls through.
- `MenuManager.SelectOption` **prefix** and `UIMouse.TrySelectNewElement` **prefix** — both block
  CK's own mouse-hover-driven reselection while a `ListDetailItem` row is actively being edited
  (`Manager.input.activeInputField` is that row). Two separate mechanisms need two separate patches:
  `SelectOption` stops the hover recolour/SFX every frame the mouse passes over another row;
  `TrySelectNewElement` stops CK's own hardcoded `activeInputField.Deactivate(commit: false)` call
  that would otherwise end the edit the instant the mouse (not even a click) passes over anything
  else. Blocking only the second would still let selection visually drift; blocking only the first
  would still lose the edit on stray mouse movement.
- `UIManager.HideAllInventoryAndCraftingUI` **prefix** — commits the row being edited *before* CK
  blanks it. That method ends with `SetInputText("")` + `Deactivate(commit: false)`, and its callers
  are world events (a chest, a cattle pen, a sign, the map, `FadeOutAndLockPlayer`), which in
  multiplayer another player or a mob can trigger while you sit in the options menu. Committing
  first also clears `activeInputField`, so CK's own `if (textInputIsActive)` finds nothing and the
  blanking never runs. This cannot be a rule inside `ListDetailItem`: the sequence is byte-for-byte
  the on-screen keyboard's own result handler, which the row's edit detector *must* treat as a
  genuine edit — only the call's source separates the two.

### `Loc`

Resolves a loc term for the active language via `API.Localization.GetLocalizedTerm`; `T(term)` for
framework-own strings (yaml guarantees a value) and `T(term, fallback)` for consumer strings (falls
back to the raw key/token when the consumer ships no term). `TFirstOf(preferred, alternate,
fallback)` tries two terms before the fallback — what a discovered entry needs, where a foreign
schema follows MSM's own. It carries a name of its own rather than a third parameter on `T`,
because `T(term, otherTerm)` compiles and would put the raw second term on screen.

## `ModSettingsMenu.Settings` — consumer API and persistence

### `ModSettings`

The public entry point and section registry. `Section(IMod consumer)` resolves the consumer's
`modId` (`Metadata.name`) + `displayName` from the `IMod` ref, and returns a `SectionBuilder`.
`Register` de-dups by `modId` (first `Build()` wins, warns).

### `SectionBuilder`

Fluent declaration. Each widget method (`Toggle`/`Slider`/`Stepper`/`Choice<T>`/`List`) binds a
CoreLib `ConfigEntry` through `BindGuarded`, hands back a typed `SettingHandle<T>` via `out`,
and records a `SettingDef`.

`BindGuarded` exists because `ConfigFile.Bind` is not a lookup: it ends in `Save()`, so
the first bind of a key writes the file, and that write is unguarded all the way down to
`API.ConfigFilesystem`. A fault there — the Wine filesystem faults this project carries
IL patches for — used to unwind out of the widget method, so the consumer's remaining
chain and its `Build()` never ran and its **whole section** vanished. Now a failed bind
logs which key and why, registers no `SettingDef`, and returns a *detached*
`SettingHandle<T>` holding the declared default. The setting is absent from the menu
rather than broken, and the consumer keeps running on its own value. `Hint`,
`SortOptions`, `RequiresRestart` (marks the last-declared setting), and `Build` complete
the chain. Loc term for a key is `<ModId>-Config/<key>`.

`List` differs in two ways from its neighbours. Its value is one comma-separated string
rather than a typed scalar — `ListTokenizer` defines that format in both directions, and it is
the same format a discovered foreign list arrives in, which is why the drill-in needs no notion
of where a list came from. And it carries a `ListEditing` level saying what the player may do
with the entries.

At the two levels that cannot add one, the stored value is **reconciled** against the declared
defaults at bind, in both directions: entries the consumer no longer declares are dropped,
newly declared ones appended. Order follows the same principle as membership — it stays the
player's where they can reorder (`OrderOnly`) and follows the declaration where nobody can
(`ReadOnly`). `ListAccess` holds each of those questions as a named predicate; nothing outside
it should test a `ListEditing` member directly.

### `SettingHandle<T>`

The typed value façade the consumer holds. Delegate-backed so it can front either a
`ConfigEntry<T>` directly (Toggle/Slider/Stepper) or a token-mapped `ConfigEntry<string>`
(Choice<T>, whose token is `value.ToString()`). `Value` reads live / writes-persist; `OnChanged`
fires on any change.

### `SettingModel.cs`

The non-generic descriptors the UI reads: `ModSection` (per-consumer box) and `SettingDef` (one
setting: `Kind`, numeric bounds, its loc terms, `RequiresRestart`, `Foreign`/`Unbounded`
markers, and the live `ConfigEntryBase Entry`).

Both resolve their own displayed text rather than handing terms out: `SettingDef.Label()` and
`ValueLabel(token)`, `ModSection.Heading()` and `Hint()`. Four places render a setting's name — the
widget, the list row, the drill-in title and the `ByLabel` sort — so a chain assembled at each of
them is a chain three of them can quietly be missing, and the sort would then order by text nobody
sees. A section's name has the same problem in miniature: the box, the alphabetical order of the
boxes, and the reset confirmation, which must name the mod the player is looking at.

The stage-2 fields behind them (`GmcmTerm`, `GmcmValueTermPrefix`, `HeadingTerm`) are `internal`
where their neighbours are public. A consumer can reach any `SettingDef` through the public
`ModSettings.Sections`, so a public field there would be a back door that works — set it and
`Label()` honours it — offered to an audience that cannot see `Label()` at all.

The enums: `SettingKind {Toggle,Slider,Stepper,Choice,Info,List}`, `SliderDisplay
{Steps,Number,Percent}`, `OptionSort {AsDeclared,ByKey,ByLabel}`. The last two kinds — `Info`
(read-only value) and `List` (comma-list with a drill-in) — are produced only by
`ForeignConfigDiscovery` (see below), never by the explicit consumer API.

### `MsmTerms`

This mod's own schema: `<Owner>-Config/<key>`, and the reserved `_hint`. A Choice option
appends `/<token>` to the label, which `SettingDef.ValueLabel` does, since the token is
the caller's. It exists because two callers compose it from opposite directions —
`SectionBuilder` from a consumer's mod id, `ForeignConfigDiscovery` from a discovered
config's folder name — and until they shared this they agreed only by being written the
same way twice.

A per-option term reaches three segments through a two-level generator by putting the first two in
the yaml namespace: `<Owner>-Config/<key>:` with each token as a leaf beneath it.

### `ForeignConfigDiscovery`

Mounts the settings of mods that use CoreLib config but never called `ModSettings.Section`
(ADR-001). It reads each foreign `ConfigFile`'s entries and maps every one to a `SettingDef` marked
`Foreign = true` via a first-match cascade:

- a read-only/server-locked entry → Info
- bool → Toggle
- enum → Choice
- ranged int/float → Stepper/Slider
- a closed set of acceptable values → Choice. The values are read off the constraint itself for
  every type CoreLib can convert — `ReadExactValues` names each one in an `is
  AcceptableValueList<X>` cascade, because reaching a generic property means writing `X` down.
  Reflection would avoid that and is not available: the SDK's own `API.Reflection` compiles and
  then refuses at the call, since its permission check admits no assembly named `CoreLib` (the
  parent repo's `docs/ck/sandbox.md` has the measurement). What the cascade cannot
  name is a type registered through `TomlTypeConverter.AddConverter`; for that alone the set is
  reconstructed by parsing `ToDescriptionString()` and kept only if every token converts back to a
  value the constraint calls valid (`TryTokens`) — otherwise the entry falls through to Info below
- a bare numeric → unbounded Stepper
- a raw string → `List`, when a heuristic judges it a genuine comma-list (≥2 tokens, none
  containing a `.` and none more than two words — ADR-006 replaced an underived 32-character cap
  with the word count, because length let prose through and refused long identifiers)
- otherwise → Info

The routing decision lives here so the widgets stay dumb — only genuine lists reach `ListWidget`.

It also fills in every loc term a discovered entry can be read under, since this is the only place
that knows the config file's path and section: MSM's own schema (`<Owner>-Config/<key>`), so an
author who never took the dependency can still name their rows here, and GMCM's below it.

### `GmcmTerms`

General Mod Config Menu's term schema, ported from its `MiscHelper.GetLocalKey` (GMCM 1.4.0), so
that a mod already carrying GMCM terms is read under them instead of under its raw keys. GMCM is
the other config menu for Core Keeper and reads the same CoreLib `ConfigFile`s, which makes its
convention the one a foreign author most likely already follows.

For PlacementPlus — file `PlacementPlus/PlacementPlus.cfg`, section `General`, key
`MaxBrushSize`:

| Method | Term |
|---|---|
| `File(path)` | `PlacementPlus/PlacementPlus` |
| `Label(path, section, key)` | `PlacementPlus_PlacementPlus_General/MaxBrushSize` |
| `ValueBase(path, section, key)` | `PlacementPlus_PlacementPlus_General_MaxBrushSize/` (+ token) |

The second segment is the **file**, not the mod. A mod whose config is the usual `config.cfg`
therefore gets `<Mod>_config_<Section>/<key>` and a heading term of `<Mod>/config` — odd to read,
and still right: an author writing terms for GMCM wrote them against this.

One rule produces all three: every segment but the last is joined with `_`, and the last follows a
`/`. That is why the key sits *after* the slash in a label and *before* it in a value term — GMCM
appends an empty final part for the latter, pushing the key one place left. MSM's own schema
appends instead (`<term>/<token>`), which is why `SettingDef` keeps a separate field for the value
base rather than deriving one from the other.

The goal is to reproduce GMCM's output, not to improve on it: a term this builds that GMCM would
not have built resolves to nothing, which is the same as having no second stage at all.

### `SectionReset`

Restores one section's settings to the defaults their owning mod declared at `Bind()`.
Section-scoped by design: one `ModSection` is one `ConfigFile` is one owning mod, so a reset is one
file, one owner, one confirmable sentence.

`CanReset(ModSection)` reports whether the section has anything a reset could write (gates both
the hint bar and the input poll); `ApplyAndCheckRestart(ModSection)` writes every in-scope entry's
`ConfigEntryBase.BoxedValue` back to its `DefaultValue` — CoreLib's own setter clamps, auto-saves,
and raises `SettingChanged` (the same path that drives `SettingHandle<T>.OnChanged`), so nothing
here notifies or persists by hand — and returns whether a `RequiresRestart` entry actually changed,
so the caller can raise the restart flag without `Settings` depending on `UI`.

Discovered (foreign) sections are included on purpose: a reset only ever writes back the value that
mod itself declared, so unlike the list-editing write path it can never invent or lose a value.
`ReadOnly` entries are always skipped — view-only/server-locked is not writable at all.

### `ConfigStore`

A `Dictionary<modId, ConfigFile>` cache. Creates one CoreLib
`ConfigFile($"{modId}/config.cfg", saveOnInit: true, info)` per consumer. CoreLib does all
`System.IO` in its own trusted assembly via `API.ConfigFilesystem`, so the framework (and
consumers) stay **sandbox-clean** — no `skipSafetyChecks` (the `.asset` has `skipSafetyChecks: 0`).
Auto-save (`SaveOnConfigSet`) is on, so every write persists immediately.

## `ModSettingsMenu.UI` — the rendered screen

### `ModSettingsScreen : RadicalMenu, IScrollable`

This component *is* the adapted vanilla `UISettings` prefab (swapped in for CK's
`RadicalOptionsMenu`), so it inherits CK's open/close, navigation, and scroll machinery.

Open sequence is three steps for a reason: `Activate` → `Populate` (build structure + fill
`menuOptions`) → `base.Activate` (hierarchy goes active) → `RenderContent` (render layouts *now*,
because `LinearLayout` skips inactive children — heights would compute as 0 before activation).
Rebuilds every open (vanilla `PugText`s free their glyphs on disable). Sections render
**alphabetically by `DisplayName`**; options within a box follow the section's `OptionSort`.

`PreWarm()` pays the one-time first-enable cost at load via a same-frame
`SetActive(true)/SetActive(false)`. `Deactivate` consumes the restart-dirty flag and requests the
deferred prompt.

`UseCustomHelpButtons => true` plus an override of `GetHelpButtonsToShow()` surface CK's
`RESET_DEFAULTS` footer-hint slot whenever the selected row's section has anything resettable
(`SectionReset.CanReset`); a same-frame `Update()` polls the reset input (keyboard `R`, Rewired
action 223) while this screen is CK's top menu and, on press, opens a
`centerPopUpText.StartNewDisplaySequence` confirmation naming the section — on **Yes**,
`SectionReset.ApplyAndCheckRestart(section)` runs and only that section's rows are re-`Refresh()`ed
(never `Populate()`, which would discard the selection).

### `SectionBox`

A tiny `MonoBehaviour` on the section-template prefab exposing `header`, `hint`, and
`widgetContainer` as **serialized references** (the screen wires by reference, not by fragile
`Find()` paths). The `widgetContainer` is a `LinearLayout` with a 9-slice border background that
auto-sizes to its rows — the visible box.

### `SettingWidget : RadicalMenuOption`

One class renders the five **non-list** kinds. Drives the value through the non-generic
`ConfigEntryBase.BoxedValue` (never sees `T`), casting per `Kind`; CoreLib clamps + auto-saves.
`←/→` → `OnSkimLeft/Right`; click/Space → `OnActivated` → `Adjust(+1)`. Per-kind `ValueString`:
Toggle on/off term, Stepper int, Choice localized-token, Slider Steps/`Number`/`Percent`; **Info**
is an inert read-only row (its value shows but `Adjust` is a no-op and it takes no selection
effect).

The `Steps` `♦/♢` chain uses `♦`/`♢` escapes (pure-ASCII source; a literal diamond is
encoding-unsafe in the Roslyn sandbox) and only renders in the `boldLarge` font atlas, so `Bind`
switches a Steps-slider's value font accordingly. Implements `ISectionRow` (`Section` + a
now-public `Refresh()`) — one of the two row classes the section-scoped reset reads a selected
row's section from and redraws after a bulk write.

The `List` kind is the one that does NOT render through `SettingWidget` — it has its own compact
row plus a pushed detail screen:

### `ListWidget : RadicalMenuOption`

Plus its serialized-ref holder **`ListWidgetBox`** — `label`, `preview`, `drillIcon`. The compact
single-line row for a foreign comma-list: label + a width-budgeted preview
(`"InventoryChest, +15"`) + a right-arrow drill affordance. `OnActivated` pushes the detail screen
(`ListDetailScreen.Open`); `OnSelected/OnDeselected` tint the `drillIcon` sprite grey/blue to
follow the row's text colour. Read-only — the classification already happened in
`ForeignConfigDiscovery`, so there is no per-row toggle. Implements `ISectionRow`, same as
`SettingWidget`.

### `ListDetailScreen : RadicalMenu, IScrollable`

Plus **`ListDetailBox`** — `title`, `itemContainer`, `itemTemplate`, `addRow` — and
**`ListDetailItem : RadicalMenuOptionTextInput`** for each row. The drill-in itself, a pushed
sub-menu (`ListDetailMenuType`) showing one comma-list in full: a title plus one navigable,
**editable** row per entry (edit/remove — CK's own text-input base class, the same one the
character-name field uses, so on-screen-keyboard/controller input comes for free), scrollable,
with controller/keyboard scroll-follow that reaches the bottom (the overflow fix that motivated the
redesign).

**Row ownership.** The screen owns its rows (`_rows`) for the lifetime of one open drill-in:
`Populate` seeds the list from the stored value through `ListTokenizer`, `RebuildRows` renders it,
and a commit writes the edited row back at its own `RowIndex` and derives the value from the list,
skipping empty entries. That inversion is what lets a row sit there **blank** while you edit its
neighbours — the stored value never carries an empty token, so a row derived from it could not
exist.

**Redundant safeguards.** Two things together used to keep the base class's per-frame width trim
out of a foreign config file — moot since `ListDetailItem.maxWidth` is `0` (the field mask defines
the visible window now, not a capacity that discards characters; see the drill-in-row-geometry
gotcha in `CLAUDE.md`), kept as redundancy rather than a live safeguard: an untouched row
contributes what it was seeded with rather than what is on screen, and the committing row hands
back `CommittedText`, which is the seeded token unless a keystroke actually changed it (a text
comparison could not tell a trimmed value from a backspaced one; the timing can).

**`RowGeneration` and reuse.** Each open bumps `RowGeneration`, and a row takes that stamp **from
the owner it binds to** — the screen is a singleton reused for every setting, so without it a row
outliving its session could commit its stale index against the next list; doomed rows are
additionally disabled before being detached, so they cannot fire at all.

**The trailing add button** is **`ListAddRow`**, a plain `RadicalMenuOption` and deliberately not a
`ListDetailItem`: `OnRowTextCommitted` therefore cannot receive it, which is what let both of its
guards become loud. It is a live object inside `itemContainer` (there is only ever one), keeps a
resting frame and focus marker like CK's own `joinButton`, and takes its caption straight from the
prefab — a `PugText` holding the loc term with `localize` + `renderOnStart` resolves and renders
itself, so no code sets it.

**Read-only mode.** A genuinely read-only `SettingDef` (`SettingDef.ReadOnly`) still shows every
row navigable for viewing, just without the trailing add button, without frames, and without ever
entering edit mode. Same three-step open as `ModSettingsScreen` (Populate → `base.Activate` →
RenderContent) for the LinearLayout-height reason. `_pending` (the setting to show) is seeded on
the singleton instance by `Open` before `PushMenu` resolves it, and cleared after consume.

**`IListRow` and visibility.** Both row types implement **`IListRow`** (`RowHeightPx`) so the
container's measuring loop asks an interface instead of naming each class — the `ISectionRow`
precedent one screen up, for the same single-inheritance reason: the two sit at different points of
CK's own hierarchy, and a row that goes unmeasured silently collapses to `renderHeightPixels: 0`.
`ListDetailItem.GetActiveStateInCurrentScene` gates on `activeSelf` so the inactive row template
isn't itself navigable.

**Edit commits.** A row's edit commits when it stops being `Manager.input.activeInputField`
(Enter/Escape/click a different row) or when the screen itself closes (`Deactivate`'s own safety
net) — never on mere mouse hover, which CK's own `OnDeselected` also fires on.

**Rebuilds must stay full teardown-and-recreate**: destroying a row is the only thing that resets
`PugTextEffectMenuOption.isValueText`, which `OnActivated` flips to the vivid editing tint and
nothing else reverts.

`ListDetailItem.OfferedButtons()` answers which of a row's buttons exist at its access level,
for the two callers that must agree — `ListDetailScreen.AddItem` (registration as a menu
option) and `RowElements` (the navigation chain). If those disagree, a button is either
invisible but selectable (the selection lands on it and cannot leave) or wired but absent.
`RefreshButtonStates` deliberately does **not** use it: it must walk every button, because
`Refresh` is what switches a hidden one off in the first place.

### `ListRowButton : RadicalMenuOption`

The three per-row controls (↑, ↓, ✕), one class with a serialized `Role` rather than three types.
They are **real menu options**, registered in `menuOptions` beside the rows and navigated on CK's
UIElement path (`useUIElementsForNavigation` on the screen): each control names its counterpart in
the neighbouring row via `topUIElements`/`bottomUIElements`, so "which column am I in" is **wiring,
not remembered state** (ADR-008; an earlier design kept the buttons out of CK's routing and paid
for it seven times over, rebuilding the selection model by hand). `ChainRowsForUIElementNavigation`
rebuilds all four chains on every rebuild — horizontal open at both ends, vertical wrapping through
the add button, whose own lists carry all four controls of the adjacent row so
`GetClosestUIElementInList` picks by position.

An icon-only option has no text, so it builds its own `BoxCollider` (CK derives one from rendered
text and would dereference null here), and `GetActiveStateInCurrentScene` tests
`activeInHierarchy`, not `activeSelf` like the two row types — a button is a CHILD of the template
that gets switched off. A disabled edge arrow still reports **ACTIVE**, because a greyed neighbour
is a dead end on this path rather than a skip and an unreachable arrow would strand the whole
column; it overrides `CanBeActivated()` to false instead, which is also what stops CK sounding an
activation receipt and offering the SELECT hint for a press that does nothing.

The one thing wiring cannot express is where the selection lands after a reorder or delete — the
entry moves rows — so the acting control names its own role through `RowSelection`, a one-shot
target consumed by that rebuild and never read again.

### `ListKindStore`

`Settings`, persisted via `API.ConfigFilesystem` like `ConfigStore`. Sticky "this foreign string
was once a genuine list" memory. `ForeignConfigDiscovery.HeuristicSaysList` needs ≥2 tokens, and
discovery re-runs on every menu open; editing a list down to 0-1 tokens through the drill-in would
otherwise silently reclassify it back to a read-only `Info` row on the next open. Once `BuildDef`
sees a genuine list for an entry, marking it here keeps it a `List` even after an edit drops it
below the heuristic's threshold. The stickiness cuts both ways: `BuildDef` reads
`HeuristicSaysList(value) || WasEverList(id)`, so the store outranks the rule and a false positive
an older build already granted survives every later tightening of the heuristic (ADR-006).

### `PugTextExtensions`

One shared `PugText.RenderPlain(string)` extension (localize=false + forced render, null-tolerant)
used by every screen instead of the render helper that used to be copied into each.

### `PugTextEffectPatch`

`[HarmonyPatch]` on `PugTextEffectMenuOption.ResetEffect` — suppresses harmless log-noise: every
row's `Populate` sets its text before `base.Activate()` runs (the "Build ≠ render" gotcha in
`CLAUDE.md`), so `PugText`'s own self-init guard renders it fine, but the sibling
`PugTextEffectMenuOption` has no such guard and reaches `ResetEffect` with a still-null text
reference, logging a warning every row on every open. The prefix skips the original body only when
the instance is not yet active-in-hierarchy AND sits under one of this mod's own two screens —
narrow enough that a genuinely null text reference anywhere else in the game (a real fault) still
surfaces its warning unchanged.

## Shared editor helpers

`../utils/CLIBuildHelper.cs`, `CLIPublishHelper.cs`, `LocalizationGenerator.cs` (namespace
`CoreKeeperModUtils`) are **not** vendored: `utils/link.sh` symlinks them into
`unity/ModSettingsMenu/Editor/`, so they compile into the editor-only `ModSettingsMenu.Editor`
asmdef (a combined runtime+editor asmdef cannot reference editor-only types). `CLIBuildHelper`
wraps `ModBuilder.BuildMod`, `CLIPublishHelper` drives the mod.io publish, and
`LocalizationGenerator` generates the loc assets — all for `unity -batchmode -executeMethod`. Mod
identity comes from `MOD_NAME` in `.envrc`, so one source serves every mod. The `.cs` symlinks and
their Unity-generated `.meta` are gitignored (nothing references them by GUID).

Patch targets (`MenuManager`, `RadicalMenu`, `RadicalOptionsMenuOption_PushMenu`, `PugText`, …) were
identified by decompiling the SDK's bundled game DLLs with `ilspycmd`.
