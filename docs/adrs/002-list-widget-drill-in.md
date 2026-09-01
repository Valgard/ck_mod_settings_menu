# List widget presentation: compact row + drill-in detail screen

- Status: accepted; its list-detection rule superseded by [ADR-006](006-list-detection-heuristic.md) (2026-08-23)
- Date: 2026-07-23

## Context and Problem Statement

ADR-001 gave Mod Settings Menu (MSM) generic discovery of foreign CoreLib config,
including a `List` kind for comma-separated string values (the installed example is
PlacementPlus `ExcludeItems`). The first shipped list widget rendered the value
*inline* — comma-split lines inside the settings row — with a far-right list/plain
**toggle icon** to correct a mis-classified string. Played in-game, that design has
three UX problems:

1. **The toggle icon feels out of place.** Correcting a mis-detected list is a
   rare, one-off action, but the icon is a permanent per-row fixture — an affordance
   mismatched to its frequency and importance.
2. **No controller/keyboard operation.** The toggle is mouse-only.
3. **Long lists overflow and are unreachable by controller.** The widget is one
   `RadicalMenuOption` row that grows with the item count. Taller than the viewport,
   CK's per-option navigation brings only the row's top into view and then jumps to
   the *next setting* — the middle/bottom of a long list is a controller dead zone.

The root cause of #1/#3 is architectural: a *collection* value was forced into CK's
*single-value* two-column row model. How should MSM present a foreign list so it is
controller-reachable and its affordances match how often they are used?

## Decision Drivers

- Core Keeper is controller-first; every element must be D-pad/controller operable.
- Affordances should match their frequency (a rare correction ≠ a permanent icon).
- Reuse CK's existing `RadicalMenu`/`IScrollable` machinery and MSM's `MenuPatch`
  push pattern rather than build bespoke scrolling.
- Stay sandbox-clean (no `System.IO`, no reflection-emit; `skipSafetyChecks: 0`).
- Read-only v1 — editing (token rows, free-text, format override) is a later step;
  do not build its scaffolding now (YAGNI).

## Considered Options

1. **Inline rows + per-row list/plain toggle** — the shipped design being replaced.
2. **Compact row + pushed drill-in detail screen**, with the list-vs-plain decision
   moved up to discovery-time routing (no per-row toggle).
3. **Inline, always-expanded rows, no toggle** — drop the toggle but keep rendering
   the list into the growing row.

## Decision Outcome

Chosen option: **"Compact row + drill-in detail screen"**, because it is the only
option that makes a long list fully controller-reachable while shrinking the
per-row affordance to a single drill glyph — and it mirrors the pattern CK itself
uses for collections (the Controls/keybinding screen is a pushed, scrollable list).

Key sub-decisions:

- **Route by heuristic at discovery (reverts the original design's §5).** Instead of
  forcing every foreign `string` through the list widget, `ForeignConfigDiscovery.
  BuildDef` classifies it: `HeuristicSaysList(value)` (≥ 2 non-empty comma tokens,
  each ≤ 32 chars, none containing `.`) → `SettingKind.List`; otherwise →
  `SettingKind.Info` (the plain read-only ADR-001 row). The heuristic **moves from
  `ListWidget`** (where it had picked a per-render default view) **to `BuildDef`**
  (where it now picks the widget kind). The widgets stay dumb.

  > The `≤ 32 chars` half of this rule was replaced on 2026-08-23 — see
  > [ADR-006](006-list-detection-heuristic.md).
- **Remove the per-row toggle entirely** — the toggle icon, its two sprites,
  `ListToggleButton`, `ListWidget.ToggleView`, and `SettingDef.OverrideKey`. With no
  writer, `ListOverrideStore` is removed in v1 (it returns with editing, storing a
  *format* override rather than a *view* override).
- **The list row is compact and single-line:** `label` + a width-budgeted preview
  (`"InventoryChest, +15"` — first items plus an overflow count) + a `▸` drill
  affordance whose sprite tints grey/blue with selection like the row text.
  Activating it (A / Space — controller-native) opens the drill-in.
- **The drill-in is a pushed `RadicalMenu` sub-menu** (`ListDetailScreen`) on a new
  menu id `ListDetailMenuType = 29315`, resolved in the `MenuPatch.TypeToMenu`
  prefix exactly like `SettingsMenuType = 29314`. It shows a title plus one
  navigable read-only `ListDetailItem` per token in a scrollable column, so D-pad /
  keyboard scroll-follow reaches every item — the fix for problem #3. It is the
  first pushed sub-screen in MSM and the future home of per-token editing.
- **Always drill in** (no per-length threshold): one predictable behaviour. Short
  lists are already legible via the row preview; the drill-in is the full view.

### Consequences

- Good: long lists are fully controller/keyboard reachable; the per-row affordance
  is one glyph; large reuse of `RadicalMenu`/`IScrollable` + the `MenuPatch` push;
  sandbox-clean; the drill-in establishes a reusable pushed-sub-screen pattern for
  later widgets.
- Bad: a second menu screen carries its own first-enable cascade (whether it needs
  `PreWarm` like `ModSettingsScreen` is measured/deferred, not assumed); a
  mis-classified string has no user recourse in read-only v1 (acceptable — a
  read-only miss only splits at commas, it never traps the user); the value stays
  read-only until the editing version.

### Confirmation

No automated tests exist in this repo; confirmation is a manual in-game check with
PlacementPlus installed and not MSM-integrated: `ExcludeItems` renders as a compact
row (label + `Torch, Campfire, +N` preview + `▸`), activating it pushes the drill-in
whose full list scrolls and whose bottom item is reachable by D-pad/controller
(scroll-follows the selection), a non-list foreign string renders as a plain `Info`
row (no `▸`, no drill-in), and no list/plain toggle icon appears anywhere.

## Pros and Cons of the Options

### Compact row + drill-in detail screen (chosen)

- Good: controller-reachable long lists; minimal per-row affordance; reuses CK's
  scroll machinery and the collection-screen pattern; classification is one
  discovery-time decision, not a per-render view.
- Bad: adds a second menu screen (its own enable cost); read-only v1 gives a
  mis-classification no in-UI recourse.

### Inline rows + per-row toggle (replaced)

- Good: the whole value is visible without a second screen; the toggle can correct a
  mis-detection in place.
- Bad: the three shipped UX problems — misplaced affordance, mouse-only, and long
  lists overflow into a controller dead zone.

### Inline, always-expanded rows, no toggle

- Good: removes the misplaced toggle; no second screen to author.
- Bad: does not fix the core overflow problem — a long list still grows past the
  viewport and stays unreachable by controller.

## More Information

- **Supersedes** the original inline list-widget design
  (`docs/specs/2026-07-20-foreign-config-list-widget-design.md`): this ADR reverts
  its §5 (route every string through the list widget) and removes its per-row
  list/plain toggle.
- **Builds on** ADR-001 (generic CoreLib config discovery; the `SettingKind.Info`
  read-only path that non-list strings fall back to).
- Input findings for the deferred editing version — the menu button-hint bar
  (`MenuHelperButtons`, a closed 7-value `HelpButtonTypes` enum, so a custom prompt
  needs a self-rolled hint object), controller input `MenuSecondaryActivate`
  (Rewired action 221, free in a settings menu), and the keyboard path (a CoreLib
  rebindable action in a mod-owned `player` category) — are recorded in the raw
  spec's §6, not built here.

The full raw design (edge cases, the deferred-editing plan, and the §6 decompile
evidence) is preserved in the design spec. Retrieve it rebase-safely with:

```bash
git show "$(git rev-list -1 HEAD -- docs/specs/2026-07-23-list-widget-drill-in-redesign.md)^:docs/specs/2026-07-23-list-widget-drill-in-redesign.md"
```
