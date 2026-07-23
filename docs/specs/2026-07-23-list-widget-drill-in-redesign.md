# Design — List widget drill-in redesign (v1)

- **Date:** 2026-07-23
- **Status:** Approved (design); implementation pending
- **Amends:** `2026-07-20-foreign-config-list-widget-design.md` (the original inline
  list-view + per-row toggle design). This redesign **reverts that spec's §5**
  (routing *every* foreign string through the list widget) and **removes the
  per-row list/plain toggle** in favour of heuristic routing plus a dedicated
  drill-in sub-screen.
- **Builds on:** ADR-001 (generic CoreLib config discovery).

## 1 · Motivation

The shipped inline list widget (comma-split lines rendered into the settings row,
with a far-right list/plain toggle icon) has three UX problems, observed in-game
against PlacementPlus `ExcludeItems`:

1. **The toggle icon feels out of place.** It is a *rare, one-off correction*
   ("this string isn't really a list") wearing the clothes of a *frequent view
   switch* — a permanent per-row icon. Affordance mismatched to
   frequency/importance.
2. **No controller/keyboard operation.** The toggle is mouse-only.
3. **Long lists overflow the screen and are unreachable by controller.** The
   widget is a single `RadicalMenuOption` row that grows with the item count.
   When it is taller than the viewport, CK's per-option navigation (D-pad moves
   *between* options, scroll-follows the selected one) brings only the row's top
   into view; pressing down jumps to the *next setting*, skipping the list body.
   The middle/bottom of a long list is a controller dead zone (mouse-wheel only).

The root cause of #1 is architectural: a *collection* value was forced into CK's
*single-value* two-column row model. This redesign resolves all three by moving
the "is it a list?" decision **up to routing** and giving genuine lists their own
scrollable screen — the pattern CK itself uses for collections (the
Controls/keybinding screen is a pushed, controller-scrollable list of rows).

## 2 · Scope

**In v1 (this design):** heuristic routing, a compact list row, and a drill-in
sub-screen. Read-only, as today.

**Deferred to the editing version (explicitly out of v1 — see §7):** the format
override, list token-editing, plain free-text editing, and everything they need
(a contextual footer prompt, a mod keybind, `ListOverrideStore`).

## 3 · Decisions (locked)

1. **Route by heuristic at discovery — revert §5.** No longer force every foreign
   `string` through the list widget. In `ForeignConfigDiscovery.BuildDef`,
   classify the string:
   - looks like a list → `SettingKind.List` → the list widget (drill-in),
   - otherwise (prose, single value, empty) → `SettingKind.Info` → the plain
     read-only row (the existing ADR-001 Info path in `SettingWidget`).

   The heuristic (`ForeignConfigDiscovery.HeuristicSaysList`: ≥ 2 non-empty
   tokens, each ≤ 32 chars, none containing `.`) **moves from `ListWidget`**
   (where it picked a per-render default view) **to `BuildDef`** (where it now
   picks the widget kind).

2. **Remove the per-row toggle entirely.** Delete the toggle icon, its two
   sprites, `ListToggleButton`, `ListWidget.ToggleView`, and the
   `listIcon`/`plainIcon`/`toggleIcon` fields on `ListWidgetBox`. With no toggle,
   `ListOverrideStore` has no writer → **remove it in v1** (reintroduced by the
   editing version, storing a *format* override rather than a *view* override;
   its stable key `ConfigFilePath|Section|Key` is unchanged). Drop
   `SettingDef.OverrideKey` (it only marked a togglable string).

3. **The list row is compact and single-line.** Like every other setting row:
   `label` + a **preview** + a `▸` affordance. The preview shows the first items
   plus an overflow count, e.g. `Torch, Campfire, +10` (conveys both content and
   scale; truncated to fit the value column). Activating the row (A / Space —
   controller-native) opens the drill-in screen.

4. **Drill-in sub-screen.** A pushed `RadicalMenu` sub-menu showing the full list:
   a title (the setting's label), a **scrollable column of read-only item rows**,
   and Back (ESC / B). Each item is its own `RadicalMenuOption` (read-only-styled)
   so D-pad navigation walks the items and CK's scroll-follow reaches every one —
   this is what makes a long list fully controller-reachable. It reuses the
   `RadicalMenu` / `IScrollable` machinery and the `MenuPatch` push pattern (a new
   menu-type id resolved in a `TypeToMenu` prefix, analogous to
   `SettingsMenuType = 29314`; the "row pushes a sub-menu" behaviour mirrors CK's
   `RadicalOptionsMenuOption_PushMenu`). The item rows are also the future home of
   per-token editing.

5. **Always drill in** (no per-length branching). One predictable behaviour, no
   "how long is long?" threshold. Short lists are already legible via the row's
   preview; the drill-in is for the full/scrollable view.

## 4 · Architecture

Extends the ADR-001 foreign path; only foreign `string` entries change.

- **`SettingModel`:** `SettingKind.List` stays for genuine lists; non-list strings
  route to the existing `SettingKind.Info`. Remove `SettingDef.OverrideKey`.
- **`ForeignConfigDiscovery.BuildDef`:** replace "every string → List" with the §3
  classification (`HeuristicSaysList(value) ? List : Info`). Move
  `HeuristicSaysList` here.
- **`ListWidget`** (settings-screen row): drop all toggle/override/list-view logic.
  Render a compact row (label + preview + `▸`); on activation, push the detail
  menu. No longer renders item lines into a container itself.
- **`ListWidgetBox`:** reduce to what the compact row needs (label, preview text,
  affordance). Remove `itemContainer`/`itemTemplate`/toggle fields, or repurpose
  `ListWidgetBox` for the *detail screen* — decided in the plan.
- **New detail-screen component + prefab (Editor-authored):** a `RadicalMenu`
  subclass rendering the title + a `LinearLayout` of read-only item rows + Back.
  A small `MonoBehaviour` (à la `SectionBox`) exposes its parts by serialized
  reference. Prefab authored in the Editor (per the prefab rule — a batchmode
  build reserializes and would drop hand-authored objects).
- **`MenuPatch`:** register the new menu-type id and resolve it to the detail
  screen instance in the `TypeToMenu` prefix (mirroring the existing
  `SettingsMenuType` resolution); instantiate the detail prefab once.
- **`ModSettingsScreen`:** a `SettingKind.List` row is the compact list widget;
  activating it pushes the detail menu, seeded with that setting's live value.

Everything else (Toggle/Slider/Stepper/Choice, integrated sections, master toggle,
dedup, `SettingKind.Info`) is untouched.

## 5 · Edge cases

- **Empty / single-token / prose strings:** classified `Info` at discovery →
  plain read-only row, no `▸`, no drill-in.
- **Mis-classification (read-only v1):** cosmetic only — a mis-detected list shows
  as a truncated `Info` line; a mis-detected prose would (if it reached List) wrap
  at commas. No user recourse in v1 (the override returns with editing, §7). This
  is acceptable precisely because read-only misses do not trap the user.
- **Long list in the drill-in:** every item is a navigable read-only row →
  scroll-follow reaches the bottom under controller and keyboard.
- **Value changes underneath (mod rewrites its cfg):** the list re-splits from the
  live value each time the detail screen opens; nothing is cached across opens.

## 6 · Verified input findings (for the editing version — not built in v1)

Recorded here so the editing version need not re-derive them (evidence gathered
2026-07-23 from the decompile at `~/Projects/checkouts/CoreKeeperDecompile/`):

- **The menu button-hint bar** ("Navigate / Select / Back") is `MenuHelperButtons`
  (a singleton on the menu manager, `Pug.Other.decompiled.cs:338817`), fed each
  frame from `RadicalMenu.GetHelpButtonsToShow()` (`:343022`) — evaluated against
  the focused option. It IS per-selection contextual, via the engine hooks
  `UseCustomHelpButtons` + `GetHelpButtonsToShow()` + the empty virtual
  `OnSelectedOptionChanged()` (`:342861`). But its vocabulary is a **closed
  7-value enum** (`HelpButtonTypes`, `:338829`) with baked per-platform glyphs — a
  mod cannot add a new prompt cleanly. A custom prompt = **roll your own hint
  object** (PugText + sprite) parented under the menu, toggled via
  `OnSelectedOptionChanged`.
- **Controller input:** `MenuSecondaryActivate` (Rewired action id **221**,
  category "Menu") is defined in `PugMod.SDK.Runtime` (mod-accessible), bound by
  default to a controller face button, and **free in a normal settings menu**
  (CK polls it only in the mod.io browser, `Pug.Other.decompiled.cs:269987`). Poll
  via `Manager.input.GetButtonDown(221)`.
- **Keyboard input:** action 221 has **no default keyboard binding**, and its
  category "Menu" is `_tag: system`, `_userAssignable: 0` (Rewired asset
  `Resources/Assets/Resources/Rewired Input Manager.prefab:1901`) → it is **not
  exposed in CK's Controls screen** (only `player`-tagged categories are). Setting
  a keyboard default on 221 directly is possible but fragile (invisible,
  non-rebindable, global, persistence-uncertain). The clean path is a **CoreLib
  rebindable keyboard action** in a mod-owned `player` category (the family
  pattern; CoreLib patches `ControlMappingMenu.Initialize` to show it), polled by
  name, with controller still on 221.

## 7 · Deferred to the editing version (out of v1)

- **Format override, re-cast.** The removed toggle's *function* (correct a
  mis-classification) returns only when it becomes *functional* — i.e. when
  editing can trap a user in the wrong editor. Then a **contextual footer prompt**
  (own hint object via §6; input = 221 for controller + a CoreLib keyboard bind)
  flips a string between the two editor shapes. `ListOverrideStore` returns to
  persist that choice.
- **Editing.** List strings → per-token rows (add / remove / edit); plain strings
  → a single free-text field. Both edit in a drill-in surface; the format switch
  lives in its header/footer.
- **Rich item rendering** (icons/names for ObjectID-like tokens) — a still-later
  polish; explicitly *not* assumed by this design (the widget stays generic and
  value-based).

## 8 · CK-UI traps to heed (a second screen re-exposes them)

From `docs/tutorial.md §20`, load-bearing for the new detail prefab/screen:

- Author the prefab with the **Editor closed**; never mutate prefab files while the
  user is in the Editor (reserialization drops hand-authored objects / nulls refs).
- **`SetText`, not `Render`,** on any shared template label (orphaned-glyph "red
  twin").
- **Clone parentless, then `SetParent`** (mid-clone `OnEnable` NRE).
- **Build ≠ render:** build structure before `base.Activate`, render layouts after
  (LinearLayout skips inactive children → heights 0).
- **uiCamera z-sorts transparents by Z** (not `sortingOrder`); SpriteMask needs the
  built-in Sprites-Mask material.
- **Measure `PreWarm` cost** — a second screen adds its own first-enable cascade;
  decide whether it needs pre-warming like `ModSettingsScreen`.

## 9 · Verification (manual, in-game)

With PlacementPlus installed, in a world, open Options → Mod Settings →
PlacementPlus:

- `ExcludeItems` renders as a **compact row** — label + preview (`Torch, Campfire,
  +N`) + `▸` — *not* a multi-line block.
- Activating it (Space / controller A) **pushes the drill-in screen**; the full
  list scrolls; **D-pad/controller reaches the bottom item** (scroll-follows the
  selection); ESC / B returns.
- A non-list foreign string renders as a **plain `Info` row** (no `▸`, no drill-in).
- `MaxBrushSize` / `MinHoldTime` (numeric) are unaffected — still editable steppers.
- No list/plain toggle icon appears anywhere.

## 10 · References

- Amended spec: `docs/specs/2026-07-20-foreign-config-list-widget-design.md`.
- `docs/adrs/001-generic-corelib-config-discovery.md` (discovery base; the
  `SettingKind.Info` path).
- Decompile evidence (2026-07-23): `MenuHelperButtons` /
  `RadicalMenu.GetHelpButtonsToShow` (`Pug.Other.decompiled.cs`),
  `ControlMappingMenu` action filter (`Pug.ControlMapping.decompiled.cs:623-648`),
  CoreLib `AddKeyboardBind` (`CoreLib-source-4.0.5/.../ControlMappingModule.cs`),
  Rewired action/category asset (`Rewired Input Manager.prefab:1630,1901`).
