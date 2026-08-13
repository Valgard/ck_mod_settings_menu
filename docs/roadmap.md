# ModSettingsMenu — roadmap

Planned, not-yet-built work. Shipped widgets (Toggle, Slider, Stepper,
Choice) live in `SettingKind` and the fluent `SectionBuilder` API; this file
tracks the **next** batch.

## Planned widgets

Three new widget kinds, ordered by cost (cheapest first). All three stay
entries in `ModSection.Settings` (the ordered per-section list) so a consumer
places them inline in the builder chain — the `SettingKind` drives which
prefab + behaviour the renderer picks. See `docs/superpowers/plans/` history
for how the existing widgets were built.

### 1. Button / Action-Row

A row that holds **no value** and fires a callback on activate.

- **API:** `.Button(string key, Action onClick)` — no `out` handle (nothing to
  read/write), no `ConfigEntry`.
- **Behaviour:** `SettingWidget.OnActivated` invokes `onClick` instead of
  `Adjust`; no skim (`OnSkimLeft/Right` no-op). `ValueString()` returns empty
  or a `»` chevron affordance.
- **Prefab:** **reuses** the existing option prefab (label left; value column
  empty). No Editor work.
- **Why first:** highest value, lowest cost. Unlocks a framework-built-in
  **"Reset to defaults"** (per-section and/or global) — every `ConfigEntryBase`
  exposes its default, so the reset itself is nearly free. Also serves consumer
  actions ("apply now" for bake-time mods, "clear checklist", "open ledger").
- **Open design question:** reset scope — per-section button vs. one global
  button vs. both. Decide during brainstorming before coding.

### 2. Info (read-only)

A row showing a **computed, non-editable** value in the normal option layout
(label left, value right).

- **API:** `.Info(string key, Func<string> value)` — no `out` handle.
- **Behaviour:** `Adjust` is a no-op; `ValueString()` calls the `Func<string>`
  each `Refresh()` so the display stays live.
- **Prefab:** **reuses** the existing `SettingWidget` prefab (its two-column
  label/value geometry is exactly right). No Editor work.
- **Use cases:** diagnostics / status — "Tracked items: 117", mod version, a
  slider's raw value as plain text.
- **Open design question:** focusable or skipped in navigation? A flag on the
  same widget, not a second kind. Default: focusable (consistent with sibling
  rows), revisit if it feels wrong.

### 3. Separator / Label

A **display-only** row rendered **full-width** (a heading or a divider),
**not** the two-column option layout — for structuring long sections.

- **API:** `.Label(string key)` (heading) / `.Separator()` (bare divider).
- **Behaviour:** never interactive, **skipped in navigation** (not focusable) —
  likely **not** a `RadicalMenuOption` at all, just a `PugText` / divider
  `SpriteRenderer` placed into the section box's layout.
- **Prefab:** needs a **new, full-width prefab** — the one genuinely expensive
  item here. Per the project rule (`feedback_corekeeper_prefab_edits_in_editor`
  memory), new/structural prefab objects **must be authored in the Unity
  Editor**: a `-batchmode` build reserializes and drops hand-authored objects /
  nulls refs. So this is real Editor work, not a code-only change.

## Why 2 and 3 are separate widgets

Logically both are "silent" (non-interactive) rows, but the split is driven by
**layout topology at the prefab layer**, not by intent:

- **Info** keeps the two-column geometry (label left / value right) → reuses the
  existing option prefab → code-only.
- **Separator/Label** spans the full width with no value column → needs its own
  prefab → Editor work.

The moment the geometry diverges, the prefab diverges, and that is precisely
what a distinct `SettingKind` value is for (it selects prefab + behaviour). They
only *share* membership in the ordered `ModSection.Settings` list (so a
separator can sit between option 3 and 4).

## Explicitly out of scope

- **Keybind capture** — attractive for action mods, but real input-capture
  breaks the skim-row model and Core Keeper already owns a rebinding system.
  Only if a consumer actually needs it.
- **Colour picker** — model as a `Choice<T>` over preset swatches instead.
- **Multi-select / flags** — N separate toggles already cover it and read
  clearer.
- **Dual-range (min–max) slider** — too niche for the single-row raster.

> **Correction (2026-08-13):** this list used to also carry "Free-text string
> input — controller-hostile; CK has scarce text-entry surfaces." ADR-003
> (`docs/adrs/003-list-widget-editing.md`) disproved that outright: CK ships
> `RadicalMenuOptionTextInput` (the same base class the character-name field
> uses), which gives on-screen-keyboard support and focus/blink handling for
> free — not controller-hostile at all. See the next section for what is
> actually still missing (a *consumer-facing* way to declare one).

## Consumer-facing List API + token reorder

Two related gaps in the `List` kind `list-widget-editing` shipped (ADR-003),
both surfaced by real external pressure rather than internal planning —
tracked together since a Consumer List API would naturally carry reorder as
part of its own design, not as a later bolt-on.

### No consumer-facing way to declare a `List` (or even plain free text)

`SettingKind.List` is currently produced **only** by `ForeignConfigDiscovery`'s
auto-detection heuristic — `SectionBuilder` (the explicit consumer API:
`Toggle`/`Slider`/`Stepper`/`Choice`/`Hint`/`SortOptions`/`RequiresRestart`)
has no `.List(...)` or even a plain `.Text(...)` method. A mod author who
*wants* a user-editable ordered/list (or just a free-text) value has no clean
path today — only the indirect one of shipping a raw `ConfigFile` outside
`ModSettings.Section` and letting `ForeignConfigDiscovery` auto-detect it as
foreign, which is the mechanism built for mods that *don't* integrate with
MSM, not ones that do.

Surfaced 2026-08-08 while designing the sibling mod **auto-rail-bridges**:
its author wanted the mod's bridge-type build order configurable, hit this
exact gap (`SectionBuilder.cs` has no list/string declaration), and the mod
shipped 1.0.0 with a **fixed** default order, deferring configurability to
"v1.1, sobald Mod Settings Menu Listen- oder String-Werte kann." That mod is
now a concrete, already-shipped consumer waiting on this.

A cheap intermediate step was designed at the time but not built — deferred
to avoid a cross-repo merge conflict with the then-in-progress
`list-widget-editing` worktree, since both touch `SettingModel`/
`SectionBuilder` (no longer a concern now that branch is merged): a
`.Text(out SettingHandle<string> h, key, default)` on `SectionBuilder`,
rendering through the existing `SettingKind.Info` path as a read-only
placeholder until the full Consumer List API lands — at which point a
consumer declaring it as a proper `List` would need no migration on their
side, just a call-site change.

### Token reorder

ADR-003 deliberately left tokens' insertion order fixed — add/edit/remove
only, no drag/up-down/keyboard reorder. Fine for an exclude-set (order is
irrelevant), but not for a genuine priority list: auto-rail-bridges' own
bridge-order use case is exactly this shape, and add/edit/remove alone means
reordering by retyping every token. Any Consumer List API design should
treat reorder as a first-class requirement from the start, given this
already-known consumer need.

### Also open: general List-widget UX

Raised 2026-08-12 without a specific complaint attached — flagged as "mit
der UI/UX bin ich nicht zufrieden" while deciding whether to merge
`list-widget-editing`, deferred in favor of running the
`pr-review-toolkit:review-pr` gate first and never revisited since. No
concrete pain points recorded yet; needs its own conversation to pin down
what specifically feels wrong before it can become an actionable item.

## Locked settings — CK's `GRAYED_OUT` convention

Core Keeper has a **shipped convention for a setting that exists but cannot be
changed right now**: the whole row (label *and* value) renders in a dull red,
navigation skips it, and the mouse cannot click it — while the row stays
visible and in the layout. Vanilla uses it for "Frame rate target" while V-Sync
is on, and for the title-menu-only settings ("Season override",
"Multiplayer connectivity") when opened from an in-game pause menu. The
framework currently has **no way for a consumer to express this** — every
declared setting is always editable.

Surfaced 2026-08-13 from the user's own in-game observation; the mechanism was
then verified against the decompiled `Pug.Other` (game 1.2.1.5 — class and
member names are stable, the line numbers are not).

### What CK gives for free

`OptionActiveState { INACTIVE, ACTIVE, GRAYED_OUT }`, returned per row by the
**virtual** `RadicalMenuOption.GetActiveStateInCurrentScene()`. From that single
return value four independent effects follow:

- **Navigation skips it** — `RadicalMenu.SelectNextIndex`/`SelectPrevIndex` walk
  on while `!IsSelectionEnabled()`, which is `!ShouldBeGrayedOut()`.
- **The mouse cannot hit it** — `UpdateClickCollider` enables the collider only
  for `ACTIVE`; `GRAYED_OUT` does not count.
- **It stays visible and laid out** — `GetAllCurrentlyActiveMenuOptions` and
  `Activate` both accept `ACTIVE || GRAYED_OUT`; only `INACTIVE` is
  `SetActive(false)`-ed out of the auto-layout.
- **The red** — `PugTextEffectMenuOption.UNSELECTABLE_TEXT_COLOR` (`#6C2C2F`),
  selected by `IsSelectionEnabled(visualOnly: true)` for the text *and* the
  effect's `spriteRenderers`.

Two ways vanilla reaches the state, both worth copying: **imperative** — an
override that consults live state (`RadicalOptionsMenuOption_TargetFrameRate`
returns `GRAYED_OUT` when `Manager.prefs.vsync`) — and **declarative**, the
prefab flag `visibleButNotSelectableWhenInactive`, which turns a
scene-mismatched row into `GRAYED_OUT` instead of hiding it.

CK also pairs the red row with an explanation: `SettingsNotAvailableNote` is a
`PugText` that switches itself on exactly while a named option is
`GRAYED_OUT`. The red row alone is only half the convention — a locked setting
whose reason is invisible is a worse UX than no lock at all.

### Where it docks into this framework

`SettingWidget.GetActiveStateInCurrentScene` (`SettingWidget.cs:88`) and
`ListWidget`'s equivalent (`ListWidget.cs:27`) already override the method — but
binary, `_def != null ? ACTIVE : INACTIVE`, so they actively rule the third
state out. Returning `GRAYED_OUT` there buys skip + click-block + layout-stay
outright. Two things it does **not** buy:

- **The value column's colour.** The red is applied solely through
  `PugTextEffectMenuOption`, and those are exactly the paths the widgets already
  handle by hand (own value tinting, `isValueText` flipping, effect filtering —
  see `docs/tutorial.md` §20 and `ListDetailItem`'s comments). Without an
  explicit `UNSELECTABLE_TEXT_COLOR` on the value text, a locked row renders
  half red.
- **Skip on the UIelement navigation path.** The `while (!IsSelectionEnabled())`
  skip only exists on the index-based path. `SelectIndexInDirection`
  (`useUIElementsForNavigation`) asks `GetAdjacentUIElement` *before* filtering,
  so a locked neighbour yields no match and navigation **stalls** instead of
  stepping over. Verify which path both screens take before assuming the skip
  works.

### Not the same thing as `Info`

`GRAYED_OUT` means "normally editable, not right now" — it is **contextual**,
and its red is a signal that something is being withheld. It is *not* the model
for a permanently display-only row: `SettingKind.Info` and the planned
`Separator/Label` are inert by nature, and their "focusable or skipped?"
question (see § Planned widgets #2) stays their own. The read-only-list
precedent already goes the other way on purpose — `ListDetailItem` keeps a
read-only list's rows `ACTIVE` so they remain navigable for *reading*.

### Open design questions

- **API shape.** A static `.Locked()` marker on the last-declared setting (like
  `RequiresRestart`) cannot express V-Sync-style dependencies; a
  `Func<bool>` predicate evaluated per `Refresh()` can, at the cost of a
  consumer-supplied callback running in the render path; a declarative
  `.EnabledWhen(SettingHandle<bool>)` covers the common "B only applies while A
  is on" case with no callback at all. Likely more than one of the three.
- **The reason text.** Mirror `SettingsNotAvailableNote` per section, per row,
  or fold it into the existing `Hint`? A locked row needs its own string either
  way, so this is a loc-term question as much as a layout one.
- **Live re-colour.** Vanilla's V-Sync row calls `ResetEffects()` on its
  neighbour's label *and* value by hand so the red appears immediately instead
  of at the next selection change. A dependency-driven lock in this framework
  needs the same nudge on whatever row just became locked.

## Small fixes

- **English label casing: "Mod Settings" → "Mod settings".** The
  `ModSettingsMenu-UI/Title` term (`localization/localization.yaml:6`, the `en:`
  value) — the Options-menu entry label **and** the screen title — should be
  sentence case, matching the framework's own `On`/`Off` values, which are
  already lowercase (`"on"`/`"off"`). One-line loc edit; the German
  `de: "Mod-Einstellungen"` stays unchanged (German noun capitalization). **Scope:**
  only the loc term is user-facing — the "Mod Settings" mentions in code comments,
  `README.md` and `CHANGELOG.md` are the feature *name* in prose and need no change.
  Requested 2026-07-12.
- **Format-override toggle / misclassification confirmation for editable lists.**
  `ForeignConfigDiscovery`'s `HeuristicSaysList` can misclassify a foreign plain
  string as a list; in the read-only drill-in (ADR-002) that was harmless, but
  the `list-widget-editing` slice makes the drill-in write `BoxedValue` back
  into the foreign `ConfigEntry` on commit — a misclassification now risks a
  lossy, comma-rejoined overwrite of a third-party mod's real config value.
  ADR-002 §7's format-override toggle (or a lighter one-time confirmation
  before the first write to an unconfirmed entry) is the fix; deliberately not
  built in that slice (see ADR-003's "Consequences" section — the risk this
  bullet describes). Flagged by the `pr-review-toolkit:review-pr` gate,
  requested 2026-08-12.
