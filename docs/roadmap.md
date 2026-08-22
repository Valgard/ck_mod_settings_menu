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

A row that holds **no value** and fires a **consumer-supplied** callback on
activate — the declaration-side counterpart to the value widgets, for actions a
mod author wants reachable from the settings screen.

- **API:** `.Button(string key, Action onClick)` — no `out` handle (nothing to
  read/write), no `ConfigEntry`.
- **Behaviour:** `SettingWidget.OnActivated` invokes `onClick` instead of
  `Adjust`; no skim (`OnSkimLeft/Right` no-op). `ValueString()` returns empty
  or a `»` chevron affordance.
- **State-dependent label — CK's own pattern, found 2026-08-13.**
  `RadicalEnterTextMenu_EnterButtonOption` (the *Join Game* menu's join button)
  holds **two** `LocalizedString`s — `joinTerm` / `stopJoinTerm` — and swaps them
  in `Update` based on the menu's `IsConnecting`. So a button label that reflects
  state ("Apply" → "Applying…", "Reset" → "Reset!") is an established CK idiom,
  implemented by plain polling rather than an event. Worth taking for a
  bake-time consumer's "apply now": the action's effect is invisible otherwise.
- **Prefab:** **reuses** the existing option prefab (label left; value column
  empty). No Editor work.
- **Why first:** highest value, lowest cost — the only planned widget that needs
  no Editor work at all. Serves consumer actions like "apply now" for a
  bake-time mod (whose change is otherwise invisible until a restart), "clear
  checklist", or "open ledger".
- **Not to be confused with "Reset to defaults"**, which shipped 2026-08-16 as
  a footer-hint-bar action rather than a row (see `CHANGELOG.md` and
  `docs/adrs/004-section-reset-to-defaults.md`) — this is a declaration API for
  a consumer's own callback, unrelated to that framework-owned logic.

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
- ~~**Colour picker**~~ — **moved out of this list 2026-08-13**, see
  § "Colour settings — HealthBars' gradient-glyph slider". The entry used to
  read "model as a `Choice<T>` over preset swatches instead", on the unstated
  assumption that a colour picker needs a 2D area the row raster cannot give.
  The installed foreign mod **HealthBars** disproves it: an HSV picker is four
  scalars, and a scalar with a gradient is a `PugText` with per-glyph colours.
  Not promoted to a planned widget yet — it needs its own design pass — but it
  is no longer out of scope on the layout argument.
- **Multi-select / flags** — N separate toggles already cover it and read
  clearer.
- **Dual-range (min–max) slider** — too niche to build for. **Reason narrowed
  2026-08-13:** this used to read "too niche for the single-row raster," and the
  raster half of that is simply not true. CK's *Join Game* menu puts **four**
  controls in one row — `sessionIP`, `sessionPort`, a `ServerDropdown` and a
  visibility toggle as siblings under one `SessionIP` parent (screenshots plus
  the prefab tree). A row can carry two controls; MSM's rows come from a
  vertically-stacking `LinearLayout`, so it would mean a container row with
  hand-positioned children — Editor work, not an impossibility. Staying out of
  scope on the *niche* judgement alone, not on a layout limit. (The same
  multi-control-row pattern is the prerequisite for a field with a trailing
  affordance — see the dropdown and visibility-toggle items below.)
- **Sprite-based toggle switch** — i.e. swapping the `Toggle` kind's `on`/`off`
  text for CK's `RadicalMenuOption_Toggle` (`toggleOnSR`/`toggleOffSR`
  checkbox sprites). **Checked and rejected 2026-08-13**, because it looks like
  an obvious upgrade and is not: that class has **zero** subclasses, and all
  **three** of its field uses in `Pug.Other` are companions *to a text field*
  (`visibilityToggle`, `saveCodeToggle`,
  `RadicalMenuOptionTextInput.radicalMenuOptionToggleVisibility`) — it is CK's
  idiom for a small affordance beside an input, not for a boolean setting. For
  options-menu booleans CK has a separate base class,
  `RadicalOptionsMenuOption_TextToggle` (Touchpad, Vibration, …), and all 52
  `RadicalOptionsMenuOption_*` classes use the text-value row. MSM's text
  toggle is therefore already the options-menu-consistent idiom; changing it
  would move *away* from vanilla.

> **Correction (2026-08-13):** this list used to also carry "Free-text string
> input — controller-hostile; CK has scarce text-entry surfaces." ADR-003
> (`docs/adrs/003-list-widget-editing.md`) disproved that outright: CK ships
> `RadicalMenuOptionTextInput` (the same base class the character-name field
> uses), which gives on-screen-keyboard support and focus/blink handling for
> free — not controller-hostile at all. See the next section for what is
> actually still missing (a *consumer-facing* way to declare one).

## Consumer-facing List declaration (`SectionBuilder.List`)

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
placeholder until the full declaration API lands — at which point a
consumer declaring it as a proper `List` would need no migration on their
side, just a call-site change.

> **Superseded 2026-08-13** as far as the free-text half goes: see
> § "Text input for plain string settings" below. The read-only-placeholder
> shortcut is no longer the cheapest route — the frame sprites that make a real
> editable field possible are the same ones the drill-in rows need anyway. The
> `List`-declaration half of this gap stands unchanged.

**One constraint comes from elsewhere:** the consumer driving this item wants an
*ordered* list, so the API has to carry reorder as part of its own design rather
than as a later bolt-on — see § "Token reorder in the List drill-in", which
ships independently of this one.

## Token reorder in the List drill-in

The drill-in that `list-widget-editing` shipped (ADR-003) lets a user add, edit
and remove tokens, but their **insertion order is fixed** — no drag, no up/down,
no keyboard reorder. Fine for an exclude-set, where order carries no meaning;
wrong for a genuine priority list, where add/edit/remove alone means reordering
by retyping every token.

**Buildable on its own.** The editable drill-in already exists and already runs
against discovered foreign configs, so reorder is a change to `ListDetailScreen`
and needs no consumer-facing declaration API to be useful. It sits beside
§ "Consumer-facing List declaration" for two reasons only: the same
already-shipped consumer motivates both — auto-rail-bridges' bridge build order
is exactly a priority list — and a declaration API designed without reorder in
mind would have to be revisited.

## General List-widget UX: the drill-in rows don't look like input fields

Raised 2026-08-12 without a specific complaint attached — flagged as "mit
der UI/UX bin ich nicht zufrieden" while deciding whether to merge
`list-widget-editing`, deferred in favor of running the
`pr-review-toolkit:review-pr` gate first. **Pinned down 2026-08-13** from the
user's own in-game screenshots of CK's *Join Game* menu: the drill-in rows are
editable but are not *dressed* as editable. ADR-003 adopted CK's text-input
base class and got its whole mechanism; what it did not adopt is the visual
field CK builds around that class in its own prefab.

The rows and CK's own text inputs are the same component
(`RadicalMenuOptionTextInput`), so the gap is a straight field-by-field
comparison — MSM's `ItemTemplate` (`Prefabs/ListDetailScreen.prefab`) against
CK's `sessionId` / `sessionIP` / `sessionPort` / `password`
(`Resources/Assets/GameObject/Join Game Menu.prefab`, four identical instances
in the AssetRipper export):

| | CK's text inputs | MSM's `ItemTemplate` |
|---|---|---|
| `pugText` / `hintText` / `characterMarkBlinker` | set | set |
| **`selectedMarker`** | → `selectedBorder` (a `SpriteRenderer`) | → `SelectedMarker`, **a GameObject with no renderer** |
| **resting-state frame** | child `border`, 9-slice `9sl_black` | **no such child** |
| `dontAllowNewLines` | `1` | `0` |

The `selectedMarker` row is the mechanical core of it.
`RadicalMenuOptionTextInput.OnSelected()` does exactly one thing —
`selectedMarker.SetActive(true)` — so the focus affordance *is* wired, it just
terminates on an empty GameObject. Nothing is missing in the code, which is why
reading the code never surfaces this. CK points the same field at a
`SpriteRenderer` (`character_customization_ui_dark_2`) and gets the blue focus
frame; the resting frame is a second, separate child (`9sl_black`) that MSM has
no equivalent of at all. Net effect: a row that can be typed into looks
identical to static text, in both the resting and the focused state.

Deliberately **not** part of this — verified before proposing, so it does not
get re-raised:

- `hintString` is already used, set at runtime rather than in the prefab
  (`ListDetailScreen.cs:210`, the `+ Add` row's placeholder).
- `maxWidth: 25` is deliberate and code-referenced (`ListDetailItem.cs:142`).
- `characterWhiteList` staying empty is an **ADR-003 decision**, not an
  oversight: it is an inclusion filter, unsuited to blocking the single
  problematic character, so commas are stripped at commit instead.

Two smaller items that do belong here:

- **`dontAllowNewLines: 0`** — CK sets `1` on every single-line field.
  `AppendString` only filters `\n`/`\r` when the flag is set, so a pasted
  newline currently survives into a token.
- **`Shake()` is inherited and unused.** The base class ships shake feedback
  (0.4 s, 20/s, configured in the template already). ADR-003's commit path
  strips a typed comma *silently* — the user types a comma, it vanishes, and
  nothing explains why. This is the affordance for exactly that.

**Cost: real Editor work, not a code change.** `border` and `selectedBorder`
are new prefab objects, and per the project rule
(`feedback_corekeeper_prefab_edits_in_editor` memory) new/structural prefab
objects must be authored in the Unity Editor — a `-batchmode` build
reserializes and drops hand-authored objects. Four traps apply, each already
documented in this repo:

1. **Both sprites must be imported** (`utils/import_vanilla_prefab.py` handles
   the AssetRipper GUID remap and pulls transitive asset deps), then given the
   ModBuilder sprite-meta treatment (`textureType: 8`, `spriteMode: 1`) or they
   pack as `Texture2D` and `LoadAsset<Sprite>` returns null.
2. **A new `SpriteRenderer` has two wrong defaults** — set the built-in
   `Sprites-Default` material (the project-default custom material renders in
   the Editor and is invisible in the AssetBundle) and `m_MaskInteraction:
   VisibleInsideMask` (or the frame overscrolls the viewport mask).
3. **The uiCamera z-sorts transparents by Z, not `sortingOrder`.** A frame
   behind text at equal absolute Z sorts *in front* and dims the text grey —
   it reads as a colour bug. The frame needs its own, larger Z.
4. **The 9-slice `size` is per-row width**, the same thing `SectionBox` already
   does for its box background.

A **side observation** while comparing: the template YAML still carries
`owner`, `isAddRow` and a second `readOnly` — field names from before the
CS0108 shadowing fix. Harmless (Unity drops them on the next Editor save), but
it does confirm the prefab has not been reserialized since that refactor.

## Text input for plain string settings (`SettingKind.Text`)

A **genuinely editable** single-line string row in the main settings screen —
the widget a consumer needs for a plain `string` value (a name, a prefix, a
format token, an address). Requested 2026-08-13, alongside the drill-in row
dressing above and sharing its prefab building block.

Today there is no path to it at all: `SectionBuilder` exposes
`Toggle`/`Slider`/`Stepper`/`Choice` and nothing string-shaped, and a foreign
`string` that fails `HeuristicSaysList` is routed to `SettingKind.Info` — a
**read-only** row. So a plain string is currently either unreachable (own
consumers) or displayed-but-not-editable (foreign config).

**What changed on 2026-08-13:** the § above establishes that the missing piece
for an editable field is the *visual frame* (`border` + `selectedBorder`), not
the mechanism — `RadicalMenuOptionTextInput` already delivers focus/blink,
on-screen keyboard, width budgeting and commit handling, and ADR-003 has been
running it in production in the drill-in since. That makes this widget mostly
a **second consumer of the same two sprites and the same field wiring**, which
is why the two items should be sequenced together rather than costed
separately.

This also **supersedes the cheap intermediate step** sketched under
§ "Consumer-facing List declaration" — a
`.Text(...)` rendering through the `Info` path as a *read-only placeholder*.
With the frame available, a real editable field is barely more work than the
placeholder, and the placeholder would ship a row that looks editable-ish and
isn't.

### What it needs beyond the drill-in row

The geometry differs, and by this file's own rule (§ "Why 2 and 3 are separate
widgets") divergent geometry means a divergent prefab:

- **Two-column, not full-width.** A drill-in row *is* the value and spans the
  row; a settings row is `label` left / `value` right. The text field has to
  live in the value column, at the value column's width — so its `maxWidth`
  and 9-slice size differ from `ListDetailItem`'s, even though both wrap the
  same component.
- **It sits among skim rows.** Neighbouring `SettingWidget`s answer `←/→`; a
  focused text field must not. `RadicalMenuOptionTextInput` handles capture
  itself via `Manager.input.SetActiveInputField`, but ADR-003 needed **two
  Harmony prefixes** (`MenuManager.SelectOption`, `UIMouse.TrySelectNewElement`)
  to stop CK's own hover-driven reselection from stealing an active edit. Those
  patches are already in `MenuPatch` and currently gate on the active field
  being a `ListDetailItem` — they need to gate on the new row type too, or the
  main screen reproduces the exact bug they were written for.

### Masked values (a secret setting)

The eye icon in the *Join Game* screenshots is not a bespoke control: it is a
`RadicalMenuOption_Toggle` assigned to the text input's own
`radicalMenuOptionToggleVisibility` field, and the base class does the rest —
`Update` mirrors the toggle into `pugText.isHidden` and re-renders, with
`IsHidden()` exposing the state. So a **masked** string setting (an API key, a
server password, a webhook token) costs the toggle sprites and one field
assignment, and nothing in the value/persistence path changes.

Worth recording now because MSM cannot express it at all today, and because it
is the one place CK's sprite toggle **is** the right idiom — see the
"Sprite-based toggle switch" entry under § Explicitly out of scope for why that
same class is the wrong choice for a boolean setting. It presumes the
multi-control row from that same § (field plus a trailing affordance).

Open: whether masking is a declaration flag (`.Text(…, masked: true)`) or
inferred, and whether a masked value should be excluded from any future
config-export/diagnostics output — a secret that a status row prints defeats
the mask.

### Open design questions

- **API shape.** `.Text(out SettingHandle<string> h, string key, string def)`
  is the obvious signature. Whether it also takes validation (a
  `Func<string,bool>`, or a max length) or accepts anything and relies on
  `Shake()` for rejection feedback is undecided — the second is cheaper and
  matches how CK itself handles a bad game ID.
- **Commit trigger.** Reuse ADR-003's `activeInputField`-transition rule
  verbatim (not `OnDeselected`, which CK's `UIMouse` fires on mere hover). This
  is a solved problem; the only question is whether the logic is lifted into a
  shared helper or duplicated in the new widget.
- **Empty string.** Is clearing a text setting "empty" or "reset to default"?
  The drill-in drops empty tokens; a scalar string has no such precedent.
- **`RequiresRestart` interaction.** A text field can be edited character by
  character; marking it restart-dirty on every keystroke would be wrong. The
  dirty flag should be set on commit, not on change.

## Dropdown lists — CK's `DropdownUIElement`

An **expanding dropdown** instead of `←/→` step-through, for a `Choice` whose
option count outgrows the skim model, and as a value-suggestion list on an
editable text row. Requested 2026-08-13 from the same *Join Game* menu
screenshots as the two items above; verified against the decompiled
`Pug.Other` (game 1.2.1.5 — class and member names are stable, line numbers are
not) and the AssetRipper export of `Join Game Menu.prefab`.

The motivating limit: `SettingWidget` drives `Choice` through
`OnSkimLeft/Right` → `Adjust(±1)`, so selecting the 9th of 12 options is eight
key presses with no overview of what else exists. Fine for `Low/Medium/High`,
poor for a language list, item category, or any enum with real breadth.

### What CK gives for free

Three classes, all in `Pug.Other`, **all generic** — they contain no join-menu
logic whatsoever:

- **`DropdownUIElement : UIelement, IScrollable`** — the control: open/close
  (`ToggleDropdownList`/`HideDropdownList`), entry instantiation and vertical
  stacking (`InitList` via `UIManager.PositionElementBeneath`, the same helper
  the section box uses), selection (`OnEntryClicked` → `activeEntry`/`activeId`
  + an `OnActiveEntryChanged` UnityEvent), and its **own `UIScrollWindow`** so a
  long list scrolls inside the popup.
- **`DropdownEntry : UIelement`** — one row, with `text` **and** `subText`.
- **`DropdownEntryData`** — the data record: `id`, `textStringToShow`,
  `subtextStringToShow`, `subStringFormatFields`, `string0` (a free payload
  slot).

Two further pieces that matter for a mod:

- **The close-on-back handling is menu-agnostic.** It lives in
  `MenuManager.UpdateInputAndApplyToCurrentMenu`, keyed on
  `Manager.input.activeDropdown` (set by `ToggleDropdownList`), and takes
  priority over the menu pop — so Escape/B closes the list first, then the
  screen. Nothing about it is bound to the join menu; it works in any
  `RadicalMenu`, including this framework's two screens.
- **Two-line entries come free** — `subtextStringToShow` +
  `subStringFormatFields`, which vanilla uses for the server name plus its last
  join date. That is "option label + explanation", something the single-row
  skim model structurally cannot render. `localizeEntries: 1` is already set in
  the vanilla prefab, so MSM's existing per-option loc terms drop straight in.

**The two vanilla wrappers are the build instructions, not an obstacle:**
`RadicalJoinGameMenu_JoinMethodDropdown` and `_ServerDropdown` are ~90-line
`RadicalMenuOption`s that only supply entry data and forward navigation
(`OnSelected` → `dropdown.button.Select()`, `NavigateInternally` delegating to
the open list's current element, `OnActivated` → forward a `LeftClick`,
`OnParentMenuActivation` → `HideDropdownList`). A MSM widget replaces them
1:1 with its own `SettingDef`-driven equivalent.

### Two places it docks in

1. **`Choice` with many options** — either a new `SettingKind`, or a
   presentation flag on `Choice` chosen at declaration (or automatically above
   an option-count threshold). Undecided; see below.
2. **Value suggestions on an editable text row.** `_ServerDropdown` carries a
   `public RadicalMenuOptionTextInput textInput` and its `onActiveEntryChanged`
   is wired to `RadicalJoinGameMenu.SetSessionData` — i.e. picking from the list
   *writes into the text field*. That is the "type it **or** pick a known value"
   pattern from the first screenshot, and it is exactly what an editable list
   token wants (item names for PlacementPlus' `ExcludeItems`, for instance).
   Same combination, no new mechanism.

**This is not the inline widget ADR-002 rejected.** That one grew the row
itself, so a long value pushed past the viewport into a controller dead zone.
`DropdownUIElement` is an *overlay* with its own scroll window — the row keeps
its height and the list scrolls internally. The rejection reason does not
transfer; the new problems are different ones, below.

### The three real obstacles

1. **Sprite-mask range conflict.** Vanilla's dropdown masks run with
   `m_IsCustomRangeActive: 1`, range sorting order **13–17** on layer 5 — they
   deliberately affect only sprites inside that band. **Both** of this mod's
   prefabs use `m_IsCustomRangeActive: 0`, i.e. an unbounded range that clips
   everything. Dropping a dropdown into either screen puts two masks over the
   same sprites. The fix is the mechanism CK itself uses (custom ranges), but it
   means touching this framework's existing masking, and it must be **verified
   in-game** — the interaction of two `SpriteMask`s is not readable off the YAML.
2. **The popup must escape the scroll hierarchy.** An open list has to overhang
   the viewport edge and must not scroll with the rows behind it. So on open it
   needs reparenting to the screen root with a computed position, and closing
   must restore it. The join menu has no equivalent problem —
   `RadicalJoinGameMenu` is not `IScrollable` — so vanilla offers no pattern to
   copy here.
3. **Three serialized references that an extraction does not carry.** All
   verified in the prefab YAML:
   - `entryPrefab` is an **external** cross-prefab reference
     (`guid: 74fbf6b0…, type: 2` → `DropdownEntry.prefab`), the shape the
     `project_corekeeper_nested_prefab_variant` memory records as breaking on
     extraction → wire at runtime.
   - **`ToggleDropdownList()` has no C# caller at all** — it is reached solely
     through the `button`'s `onLeftClick` UnityEvent in the prefab.
   - `OnActiveEntryChanged`'s persistent call targets
     `m_TargetAssemblyTypeName: RadicalJoinGameMenu_JoinMethodDropdown,
     Pug.Other` — a class this mod never instantiates.

   All three want `AddListener`/assignment in `Bind()` rather than trust in the
   imported YAML. Half of a Unity dropdown's wiring is data, not code.

Two smaller traps worth writing down now: `SelectFirstEntry()` indexes
`entries[0]` unguarded (empty option set → exception, so seed or guard before
calling), and vanilla hides the affordance entirely at one option —
`JoinMethodDropdown.Awake` does `dropdown.button.gameObject.SetActive(false)`
when `GetEntryDatas().Count <= 1`, which is the right precedent for a
single-option `Choice`.

### Cost and sequencing

**The most expensive of the three CK-control items** — obstacles 1 and 2 both
reach into this framework's scroll/mask architecture, whereas the drill-in
frame and `SettingKind.Text` are additive. The drill-in frame is also its
natural prerequisite in the second docking place above (a suggestion list on a
text row presumes the text row looks like a field). Sequence: frame → `Text` →
dropdown.

### Open design questions

- **New kind or presentation flag?** A `SettingKind.Dropdown` duplicates
  `Choice`'s value handling; a flag on `Choice` (`.AsDropdown()`, or automatic
  above N options) keeps one value path and one loc convention. The flag looks
  right, but it means one `SettingKind` maps to two prefabs — which is exactly
  the coupling § "Why 2 and 3 are separate widgets" argues against. Resolve
  before coding.
- **Does it belong in the drill-in, the main screen, or both?** Obstacle 1 has
  to be solved per screen.
- **Mouse-only or full controller parity?** The vanilla wrappers show the
  controller path (`NavigateInternally` + `dropdown.button.Select()`), so parity
  is achievable — but it is the part most easily left half-done, and this
  framework's own driver is "Core Keeper is controller-first".

## Colour settings — HealthBars' gradient-glyph slider

A colour-valued setting, rendered inside the ordinary row raster. Surfaced
2026-08-13 from the installed foreign mod **HealthBars** (mod.io id `4164578`),
which is a **source mod** — its `Scripts/` are readable in the mod.io cache, so
this is real working code, not a decompile.

**The technique.** `MenuOptionColorSlider : RadicalMenuOption` builds the
gradient out of the value text itself: the value is a string of N pipe
characters, drawn with negative letter spacing, and each glyph is coloured
individually.

```csharp
private const char StepChar = '|';
public int numberOfSteps = 90;
// OnValidate:  valueText.SetText(new string(StepChar, numberOfSteps));
//              valueText.style.extraCharSpacing = -2;
// UpdateVisuals: valueText.glyphs[i].color = (CurrentColor with { Hue = i / (float) numberOfSteps }).Rgba
```

No sprite, no texture, no new prefab geometry — `valueText.glyphs[i].color` is
the whole mechanism, and the row stays a normal two-column option. A pointer
transform marks the current step, and a 9-slice `border` is resized to the
text's measured width each update.

**A picker is four rows, not a popup.** `ColorComponent { Hue, Saturation,
Value, Alpha }` selects which component a given row edits; the subclass supplies
nothing but the property (`MenuOptionColorHealth` → `Options.Instance.
ColorHealth`). Four colours × their components are all plain rows. The value
type is a `record HsvColor` whose `Rgba` goes through `Color.HSVToRGB` — and
C# 9 records with `with` expressions evidently pass the Roslyn sandbox.

### Open design questions

- **One setting or four?** A `SettingKind.Color` that internally renders four
  rows conflicts with `ModSection.Settings` being a flat ordered list of rows;
  four coupled slider settings sharing one stored value is the other shape, and
  it makes labelling awkward ("Health colour — hue").
- **Persistence type.** This is the load-bearing unknown. MSM stores through
  CoreLib `ConfigEntry<T>`; whether its TOML layer can round-trip a `Color` /
  four floats grouped as one value needs checking before any API is designed.
  HealthBars sidesteps it entirely by serializing its own JSON (see below), an
  option MSM does not have without giving up the CoreLib contract.
- **Alpha optional?** A bar colour wants alpha; a text colour usually does not.
- **Does the gradient need the `boldLarge` atlas?** The `Steps` slider's `♦/♢`
  already forced a per-widget font switch; `|` may have the same constraint.

## Slider interaction & write amplification

Three findings from the same mod that apply to **MSM's existing sliders**, not
just to a future colour one:

- **Mouse drag.** HealthBars' `LateUpdate` raycasts the UI layer against its own
  `valueCollider` while the left button is held and derives the step from the hit
  position (`RoundToPixelPerfectPosition.RoundPosition`). MSM's slider offers
  only `←/→` and click — workable at a handful of steps, unusable at ninety, and
  a real improvement even at MSM's current granularity.
- **Halving the skim cooldown.** Per step it reaches into
  `InputManager.menuSelectionInputCooldownTimer` and `MenuManager.
  sfxCooldownTimer` via **`API.Reflection`** and fast-forwards them
  (`inputTimer.FastForward(inputTimer.remainingTime / 2f)`), doubling the repeat
  rate so a long range stays tolerable. Note `API.Reflection` is PugMod's
  **sandbox-legal** reflection surface — HealthBars runs with
  `skipSafetyChecks: false`. It also pitch-shifts the step SFX by position, which
  is why its sliders feel responsive rather than merely fast.
- **Write amplification — the one to act on.** MSM persists through CoreLib's
  `SaveOnConfigSet`, so **every** `Value` write hits the file. HealthBars instead
  sets an `_isDirty` flag and writes at most every 10 s (`AutosaveInterval`),
  and its colour setters even compare before marking dirty. Today MSM is fine
  (one key press = one step = one write), but drag or a halved cooldown turns
  that into dozens of writes per gesture. So this is not a present defect — it
  is the precondition under which the two items above create one, and it should
  be solved *with* them, not after.

## HealthBars as MSM's reference target

HealthBars is the natural yardstick for "is the widget palette complete?", and
the idea of asking its author to migrate onto MSM came up 2026-08-13. Recording
the **order of operations**, because it is counter-intuitive:

**MSM cannot host HealthBars' settings today, and a migration would cost it
features.** Its stored options are seven booleans plus four `HsvColor` values
and a reset row. MSM covers the booleans and, since 2026-08-16, resetting too
(though as a per-section footer-hint action, not a row — see
`docs/adrs/004-section-reset-to-defaults.md`): there is no colour kind (this
file's own § above is the plan, not an implementation), and the slider
interaction its colour rows depend on is the § above. Asking now would mean
asking a working mod to regress.

**It is also invisible to ADR-001 discovery.** `ForeignConfigDiscovery` finds
mods that use a CoreLib `ConfigFile`; HealthBars persists its own JSON through
`API.ConfigFilesystem` + `Newtonsoft.Json` (with `accessesExtraAssemblies:
true`). So it appears in MSM neither integrated nor auto-detected — it is
outside the framework's reach in both directions, which is worth knowing
independently of any migration: **CoreLib is not a safe proxy for "uses config"**
when judging discovery coverage.

What that makes it useful for right now is an **acceptance target**: when MSM can
express HealthBars' options screen in full — colours and draggable
sliders — the palette is demonstrably complete, and the conversation with its
author has something to offer instead of something to ask for. Reaching out is
the user's call, not the framework's, and it belongs after that point.

Its own integration details, for reference: it mounts as a **submenu of the UI
options menu** rather than a screen of its own —
`MenuAdder.AddMenu((RadicalOptionsMenu) Manager.menu.uiOptionsMenu, 19901,
"HealthBars-Options/Header")`, with all rows coming from a single prefab via
`AddOptionFromPath`. Menu id `19901` is one of the two ids MSM's own 29314/29315
were deliberately chosen to avoid.

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

  **CK already ships the declarative variant — with the wrong state (found
  2026-08-13).** `RadicalMenuOption_Toggle.relatedOption` is exactly an
  `.EnabledWhen`: a serialized reference to another option, and an override that
  asks `relatedOption.GetActiveStateInCurrentScene()` and adopts its result. Its
  tooltip even says "Will be disabled if related option is disabled". But it
  only propagates **`INACTIVE`** — the dependent option *disappears* rather than
  going grey-red-and-visible, which is the opposite of what this section's whole
  convention is for. So the field validates the API shape (a plain reference
  beats a per-render callback) while its state semantics must **not** be copied:
  a MSM equivalent has to map the dependency to `GRAYED_OUT`. Worth knowing that
  vanilla itself has both behaviours and picks per case.
- **The reason text.** Mirror `SettingsNotAvailableNote` per section, per row,
  or fold it into the existing `Hint`? A locked row needs its own string either
  way, so this is a loc-term question as much as a layout one.
- **Live re-colour.** Vanilla's V-Sync row calls `ResetEffects()` on its
  neighbour's label *and* value by hand so the red appears immediately instead
  of at the next selection change. A dependency-driven lock in this framework
  needs the same nudge on whatever row just became locked.

## Small fixes

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
