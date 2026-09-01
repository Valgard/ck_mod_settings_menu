# ModSettingsMenu — roadmap

Planned, not-yet-built work. Shipped widgets (Toggle, Slider, Stepper,
Choice) live in `SettingKind` and the fluent `SectionBuilder` API; this file
tracks the **next** batch.

Every point carries a reference id — `MSM-01`, `MSM-02`, … — so a commit, an ADR
or a conversation can name one without quoting a heading. Quoting one is what
goes wrong: the heading gets reworded, or its point ships and the section is
deleted, and every reference to it stops resolving in silence — as happened to
§ "Horizontal scrolling in a text field", cited from ADR-006 after it was
removed here. No gate catches that, because a prose `§ "…"` is not a link.
Three rules make the id worth more than the title it replaces:

- **Assigned once, never reused.** A point that ships takes its id with it, so an
  older reference can never come to mean a different point.
- **Not derived from the position here.** Adding, removing or moving a point
  changes nothing about the ids around it. Do not renumber.
- **Cited bare.** Write `MSM-08`, not `MSM-08 ("Colour settings")` — repeating
  the title beside the id reintroduces the staleness the id removes.

A section without an id is not work: the out-of-scope list, the rationale for a
widget split, and the HealthBars yardstick.

## Planned widgets

Three new widget kinds, ordered by cost (cheapest first). All three stay
entries in `ModSection.Settings` (the ordered per-section list) so a consumer
places them inline in the builder chain — the `SettingKind` drives which
prefab + behaviour the renderer picks. See `docs/superpowers/plans/` history
for how the existing widgets were built.

### MSM-01 — Button / Action-Row

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

### MSM-02 — Info (read-only)

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

### MSM-03 — Separator / Label

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

## Why MSM-02 and MSM-03 are separate widgets

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
- ~~**Colour picker**~~ — **moved out of this list 2026-08-13**, see MSM-08. The
  entry used to read "model as a `Choice<T>` over preset swatches instead", on
  the unstated assumption that a colour picker needs a 2D area the row raster
  cannot give. The installed foreign mod **HealthBars** disproves it: an HSV
  picker is four scalars, and a scalar with a gradient is a `PugText` with
  per-glyph colours. Not promoted to a planned widget yet — it needs its own
  design pass — but it is no longer out of scope on the layout argument.
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

## MSM-04 — Entries chosen from a catalogue, not typed (`ListEditing.FromPicker`)

`SectionBuilder.List` shipped with three levels — `FreeText`, `OrderOnly`,
`ReadOnly` — which between them cover a list whose entries the player writes and
one whose entries are fixed. The gap they leave is the middle: a list whose
entries come from a **known set too large to declare**, the obvious case being
Core Keeper's own object IDs. Requested 2026-08-30, when the three levels were
being cut.

The player picks an entry from a list instead of typing it. That removes the
failure the typed levels cannot avoid — a mod reading `ObjectID` out of a
`FreeText` list gets whatever was typed, so `Stonebridge` for `StoneBridge`
silently drops an entry, and the mod cannot tell a typo from a deliberate
omission.

**It is also the one level that can afford a translation.** A row that is typed
into must show its raw token, because the text on screen becomes the stored
value on commit — which is why `List` has no label hook today, and why an
earlier draft that gave it one was dropped rather than made non-editable. A
picker row is never typed into: it can show `Glass Bridge` while storing
`GlassBridge`. So the label hook belongs to this level, and only to it — through
a **term**, resolved by the same `Loc.T(term, token)` chain the rest of the
framework uses, so pointing at CK's own `Items/<ObjectID>` yields every language
the game ships rather than the two a mod's own yaml would carry.

`ListEditing` was cut as an ordered scale for exactly this reason: a fourth value
slots in between `FreeText` and `OrderOnly` without a second dimension, and
`ListAccess` is already the single place answering what a level permits —
`CanType`, `CanAdd`, `CanReorder`, `CanDelete`, `ReconcilesDefaults`. Adding the
level means filling in those five, in one screenful, rather than auditing the
call sites.

**Open, and not researched yet:** whether CK has a usable picker or catalogue
control to build on, or whether this needs its own screen. The dropdown section
below is the nearest known candidate (`DropdownUIElement` is generic and brings
its own scroll window), but its obstacles were mapped for a `Choice` widget, not
for a set of this size, and nothing has been measured against a full object
catalogue. Also open: where the *set* comes from — a consumer-supplied array is
the simple answer and probably too big for one; anything smarter is a filter or
a search, which is a screen, not a parameter.

## MSM-05 — Text input for plain string settings (`SettingKind.Text`)

A **genuinely editable** single-line string row in the main settings screen —
the widget a consumer needs for a plain `string` value (a name, a prefix, a
format token, an address). Requested 2026-08-13, alongside the drill-in row
dressing above and sharing its prefab building block.

Today there is no path to it at all: `SectionBuilder` exposes
`Toggle`/`Slider`/`Stepper`/`Choice` and nothing string-shaped, and a foreign
`string` that fails `HeuristicSaysList` is routed to `SettingKind.Info` — a
**read-only** row. So a plain string is currently either unreachable (own
consumers) or displayed-but-not-editable (foreign config).

**The expensive half is already built (2026-08-23).** The missing piece for an
editable field was never the mechanism — `RadicalMenuOptionTextInput` delivers
focus/blink, on-screen keyboard, width budgeting and commit handling, and ADR-003
has run it in production in the drill-in since — but the *visual frame*. That
frame now exists: `field_border` and `field_focus` in the `ui_chrome` atlas, wired
on `ItemTemplate` as a `Border` child plus a renderer on `SelectedMarker`, switched
per row through `ListDetailItem`. This widget is therefore a **second consumer of
the same two sprites and the same field wiring** — see ADR-005 for the row model
they hang in, and `docs/ck/ui-framework.md` for the `PugText.maxWidth` trap that
disables the capacity check if the text is allowed to wrap.

An earlier plan for this was a `.Text(...)` rendering through the `Info` path as
a *read-only placeholder*, to be replaced once a real field existed. With the
frame available that shortcut has no reason left: a real editable field is barely
more work, and the placeholder would ship a row that looks editable-ish and
isn't. `SectionBuilder.List` has since closed the list half of the same gap; a
scalar string still has no declaration of its own.

### What it needs beyond the drill-in row

The geometry differs, and by this file's own rule (§ "Why MSM-02 and MSM-03 are
separate widgets") divergent geometry means a divergent prefab:

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

### MSM-06 — Masked values (a secret setting)

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

### Open design questions — the text widget

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

## MSM-07 — Dropdown lists — CK's `DropdownUIElement`

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

### What CK gives for free — dropdowns

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
   - `entryPrefab` is an **external** cross-prefab reference (`guid: 74fbf6b0…,
     type: 2` → `DropdownEntry.prefab`), the shape the
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

### Open design questions — dropdowns

- **New kind or presentation flag?** A `SettingKind.Dropdown` duplicates
  `Choice`'s value handling; a flag on `Choice` (`.AsDropdown()`, or automatic
  above N options) keeps one value path and one loc convention. The flag looks
  right, but it means one `SettingKind` maps to two prefabs — which is exactly
  the coupling § "Why MSM-02 and MSM-03 are separate widgets" argues against. Resolve
  before coding.
- **Does it belong in the drill-in, the main screen, or both?** Obstacle 1 has
  to be solved per screen.
- **Mouse-only or full controller parity?** The vanilla wrappers show the
  controller path (`NavigateInternally` + `dropdown.button.Select()`), so parity
  is achievable — but it is the part most easily left half-done, and this
  framework's own driver is "Core Keeper is controller-first".

## MSM-08 — Colour settings — HealthBars' gradient-glyph slider

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

### Open design questions — colour

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

## MSM-09 — Slider interaction & write amplification

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
`docs/adrs/004-section-reset-to-defaults.md`): there is no colour kind (MSM-08
is the plan, not an implementation), and the slider interaction its colour rows
depend on is MSM-09. Asking now would mean asking a working mod to regress.

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

## MSM-10 — Locked settings — CK's `GRAYED_OUT` convention

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

### What CK gives for free — locked settings

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
question (see MSM-02) stays their own. The read-only-list precedent already goes
the other way on purpose — `ListDetailItem` keeps a read-only list's rows
`ACTIVE` so they remain navigable for *reading*.

### Open design questions — locked settings

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

### MSM-11 — Nothing reaches a row that the player did not touch

The lock has to appear **while the screen is open**, and today nothing can make
that happen. A row is refreshed at exactly four places — on bind
(`SettingWidget.cs`), on `OnParentMenuActivation`, after the player's *own*
change, and after a section reset (`ModSettingsScreen.RefreshSection`) — and
nothing polls: `ModSettingsScreen.Update` tests the reset key and returns. All
four have one thing in common: they originate with the player or with the
screen. Anything arriving from outside never reaches the row.

**The load-bearing case is a permission change mid-session, not a value change.**
Core Keeper's admin system runs independently of this mod — `NetworkCommand`
carries `AddOrUpdateAdmin`, `RemoveAdmin` and `SetGuestMode` — so a player can be
made an admin, or guest mode can be switched, while the options screen is open.
`IsReadOnly` is evaluated once per `Discover()`, i.e. once per menu open, so rows
stay locked that no longer are until the screen is closed and reopened. GMCM
polls `adminPrivileges` and `guestMode` every frame for exactly this reason.

**A value changing underneath is the same defect and the cheaper half.** A mod
writing its own entry from gameplay code is possible — `SettingHandle.Value` has
a setter — though no example is at hand in the installed set. It is cheap to fix
because `SettingDef.Entry` is a live handle: the data is already right, only the
rendered text is stale, so a `Refresh()` is enough and CoreLib supplies the
trigger ready-made in `ConfigEntryBase.SettingChanged`, subscribable per entry
for as long as the row lives.

**The two halves are not equally cheap.** There is no `SettingChanged` for a lock:
`adminPrivileges` is a property over a component and `guestMode` a field in a
singleton, and nothing announces a write to either — which is why GMCM polls.
Whether MSM must poll as well, or whether CK offers an event nobody has looked
for, is unverified. *To check:* search `PugMod.SDK.Runtime` and `Pug.Other` for
connect/permission events before assuming a poll is the only option.

## MSM-12 — Escape does not cancel an edit

Found 2026-08-23 by the `pr-review-toolkit:silent-failure-hunter` gate while
reviewing the drill-in row model. **Pre-existing** — not introduced by that work.

CK carries the intent in `Deactivate(bool commit)` and
**`RadicalMenuOptionTextInput.Deactivate` discards that parameter**: it only clears
`activeInputField` and hides the caret. The drill-in commits on the
`activeInputField` transition, which is the only reliable "the user is done" signal
CK offers, and a transition cannot see what a dropped parameter said. `Pug.Other`
calls `Deactivate(!IsMenuBackButtonDown())` for Escape, so Escape means "commit what
is typed" exactly like Enter — there is no way to abandon an edit.

Possible answers, none tried: patching `Deactivate` to honour its own parameter,
which is the honest fix and the widest blast radius (every text input in the game
runs through it, the character-name field included); or a narrower prefix that
records the argument for the current field so the commit path can read it.

**The sibling half of this finding is fixed in 2.0.0.** The same dropped
parameter let a *world event* delete a list entry mid-edit:
`UIManager.HideAllInventoryAndCraftingUI` ends with `SetInputText("")` +
`Deactivate(commit: false)` on whatever field is being edited, which is
indistinguishable from pressing Enter on an emptied row — the entry was dropped
and the shortened value written to the owning mod's config. Its callers are
world events, not menu actions (opening a chest, a cattle pen, a vending
machine, a crafting station, a sign, the map, and
`PlayerController.FadeOutAndLockPlayer`), and in multiplayer the simulation
keeps running while a player sits in the options menu, so another player or a
mob can trigger it. `MenuPatch` now prefixes that method and commits the row
first, which also clears `activeInputField` and thereby disarms CK's own `if
(textInputIsActive)` blanking. It had to be a patch rather than a rule in
`ListDetailItem`: that sequence is byte-for-byte the on-screen keyboard's own
result handler, so only the call's *source* can separate "the player just typed
this" from "the world just wiped it" — see that patch's comment.

## MSM-13 — A discovered entry's localisation term

A discovered entry is rendered under its **raw key**:
`ForeignConfigDiscovery` sets `Term = key`, so there is nothing for the lookup to
find. That is a gap in one value, not in the mechanism — the two-stage resolution
has been in place all along, `Loc.T(_def.Term, _def.Key)` for the label and
`Loc.T(_def.Term + "/" + tok, tok)` per choice value.

**MSM's own schema stays the default**; General Mod Config Menu's published
convention becomes a second stage in front of the raw-text fallback:

| Stage | Term | For whom |
|---|---|---|
| 1 | `<ModId>-Config/<key>` (`SectionBuilder`) | an author who targets MSM |
| 2 | `<path>_<file>_<section>/<key>` | an author who already supports GMCM |
| 3 | the raw key | an author who ships neither |

Nobody who is correct today gains a burden, and a mod carrying GMCM terms becomes
readable instead of having its existing translations ignored.

- **It does need a small API change.** `Loc.T(term, fallback)` knows one term;
  the chain needs a variant that tries several in order.
  `API.Localization.GetLocalizedTerm` returns `null` for an unregistered term, so
  the chain is a `??` sequence. (`I2.Loc` has a second `GetTranslation` that
  returns `string.Empty` instead — it is the instance method on `LanguageSource`
  and not the one in this path. Confusing the two breaks the chain silently.)
- **One trap.** GMCM's value schema moves the key **in front of** the slash
  (`…_Section_Key/Value`) where MSM's appends it (`…/key/token`). A second field
  on `SettingDef` — the base for per-option terms, `null` meaning "use `Term`" —
  keeps them apart without touching how registered consumers behave.
- **Not included:** the hover description GMCM reads from `<key>Desc`; see
  MSM-17 below.

## MSM-14 — `AcceptableValueList<string>` should render as a Choice

A discovered entry constrained by anything other than a numeric range is routed
to a read-only `Info` row — while `SectionBuilder` binds every declared `Choice`
as exactly `AcceptableValueList<string>`. MSM therefore produces the shape its own
discovery classifies as unrenderable, and for that shape the type parameter is
known, so no reflection and no parsing is needed:

```csharp
if (av is AcceptableValueList<string> l) { d.Kind = SettingKind.Choice; d.Tokens = l.AcceptableValues; return d; }
```

- **Fallback for a foreign `T`:** parse
  `AcceptableValueBase.ToDescriptionString()`, which every implementation
  formats as `"# Acceptable values: a, b, c"`. It is what GMCM does, and it
  needs no type parameter. It is **not** the only sandbox-legal route, which
  this bullet used to claim on the grounds that `System.Reflection.*` is denied: [ADR-009](adrs/009-caret-index-from-the-counter.md)
  establishes that a member is reachable through `API.Reflection` without naming
  it, and `AcceptableValueList<T>.AcceptableValues` is one — the returned `T[]`
  casts to `System.Array`, so `T` never has to be spelled. Which of the two is
  preferable here is untested; only the reason for excluding one of them was
  wrong.
- **Worth it on its own:** it makes MSM's own declared settings legible to MSM's
  own discovery path.

## MSM-15 — Group a mod's rows by its `.cfg`'s own sections

Everything binds into the CoreLib section `"Settings"` today, so a discovered mod
renders as one flat list. GMCM groups by the file's own sections. For a mod with
twenty values that is the difference between a list and a structure.

- **It rides on the planned Separator/Label widget** (MSM-03): a section header
  *is* that widget's `.Label(key)` — full width, no value column, not
  interactive. So this point carries no Editor work of its own, and it becomes
  the first real consumer of a widget that would otherwise ship unexercised.
  Build the widget first; if a grouped header cannot be built from it, the
  widget's spec is incomplete, and that is better learned here.
- **Deliberately not collapsible.** Core Keeper has no expand/collapse idiom in
  its menu UI at all — a search for `Expand`, `Collapse`, `Foldout`,
  `SwitchExpandState`, `isExpanded` turns up only ECS collider pooling and two
  inspector attributes. Vanilla uses tabs (`WorldSettingsTab`), sub-menus
  swapped by `SetActive`, and scrolling. GMCM's accordion is its own invention.
- **Loc: the same chain as above**, one level up — MSM's own schema, then GMCM's
  `<path>_<file>/<section>`, then raw. No separate scheme for headers.
- **Open:** whether a header applies to a *registered* mod at all. A consumer
  binds everything into `"Settings"`, so there would be exactly one header
  repeating the box heading. Either grouping stays limited to discovered mods, or
  `SectionBuilder` gains a way to name sections.

## MSM-16 — A master switch with sub-values

GMCM's `CombindConfigPage` is a public API a consuming mod registers against: a
`bool` entry plus named sub-values bound underneath it, rendered as an indented
group the switch collapses. MSM has no equivalent.

- **What vanilla offers, and what it does not.**
  `RadicalMenuOption_Toggle.relatedOption` is exactly the API shape — a
  serialized reference rather than a callback — but propagates only `INACTIVE`,
  so the dependent row *disappears*. The shape transfers, the behaviour must not;
  see MSM-10.
- **Open:** is this a `SectionBuilder` declaration (`.EnabledWhen(...)`, already
  an open question there) or a group API like GMCM's? The difference is that
  GMCM's can also *bind* the sub-values, not merely lock them.
- **Depends on** the grouping point above for the indentation.

## MSM-17 — A description per entry — undecided, and deferred

GMCM reads a description from the term `<key>Desc` and shows it as hover text.
MSM shows none. **Neither whether this happens nor in what form is decided**, and
the form is the harder half:

- **Hover is controller-hostile.** This screen is controller-first; a text that
  appears only under a mouse pointer never reaches half the audience. Copying
  GMCM's solution one-for-one would be copying it for one input device.
- **Alternatives, each with a price:** a line under the selected row (costs
  space, changes row height per selection); the section's existing `Hint`,
  repurposed (collides with its current meaning); a dedicated footer area (Editor
  work, competes with the hint bar).
- **Deferred, not rejected.** Worth discussing once the term chain above exists —
  without a resolved term there is no text to show — and once it is clear whether
  the grouping point rebuilds the screen anyway.

## MSM-18 — A consumer-declared access level, for every widget

`SettingDef.ReadOnly` exists and works, and **only the discovery path can set
it**. `SectionBuilder` passes no `ConfigScope` to `_file.Bind`, so every declared
setting is scope-less and always editable; a consumer who wants "the host may
change this, a joining player may only look" has no way to say so.

CoreLib already carries the mechanism — `ConfigFile.Bind` takes an optional
`ConfigScope`, and one overload takes `ConfigAccessLevel` directly — and
`ForeignConfigDiscovery.IsReadOnly` already reads it: `ViewOnly` locks
unconditionally, `Client` never locks, `Server`/`Admin` ask `scope.Changeable()`
and lock conservatively at the title screen where there is no player. So the
work is not inventing a rule but letting the standard path reach the one that
exists.

Deliberately **not** folded into `SectionBuilder.List`, which is where the
question came up (2026-08-30): it applies to all five widget kinds equally, and
giving exactly one of them an access level would be the wrong shape. Note that
`ListEditing.ReadOnly` is a different thing and stays — it says "this list is
display-only by design", where a scope says "not by you, not right now".

Three clean-ups come with it, and are the reason this is its own point rather
than a parameter:

- **`IsReadOnly` sits in the discovery path** as a `private static`. If the
  declared path uses it, it belongs somewhere both can reach.
- **`RequiresRestart` and `requireReload` already say the same thing twice.**
  `ConfigScope` carries `requireReload`, which discovery reads
  (`ForeignConfigDiscovery`) — while a declared setting sets an MSM-owned flag
  through `.RequiresRestart()` instead. Introduce scope on the declared path and
  the two meet: one of them has to win, or a consumer can state both and
  contradict itself.
- **`SettingDef.ReadOnly` carries two unrelated claims** (`SettingModel.cs`):
  "locked by permission right now" and "no editable widget exists for this
  value's shape at all". Only the first is a lock worth showing a player, and
  the MSM-10 feedback needs to tell them apart. Split it here, where the
  vocabulary for the first one arrives.

**The default is `Server`, and nobody chose it.** `SectionBuilder` binds without
a `ConfigScope`, CoreLib falls back to `ConfigScope.Empty`, and that is `new()` —
whose constructor defaults to `ConfigAccessLevel.Server`. Every setting declared
through MSM is therefore formally server-scoped, MSM's own "show detected mod
settings" toggle included. It is harmless only because nothing reads the level on
the declared path today; the moment something does, a server would be entitled to
decide whether a player's menu lists detected mods.

Two consequences. The API needs a **deliberately chosen** default — `Client` is
the honest one for a framework whose consumers are HUD and UI mods — and MSM's
own toggle should state it rather than inherit it. The overload that takes a
`ConfigAccessLevel` directly already defaults to `Client`, so switching to it may
be the whole fix; the two entry points into the same structure disagree, which is
worth knowing before writing either.

**`Admin` belongs in the declaration too, and it pays off before any sync.**
`IsReadOnly` already delegates to `Changeable()`, which for `Admin` asks
`!guestMode && adminPrivileges > 0` — a *discovered* admin entry is already
locked for a non-admin today. What is missing is only that a consumer can say the
same. Without it, a foreign mod can express a level an integrated one cannot.

The distinction only bites with a joined player: offline sessions report
`int.MaxValue` and the host holds level 2, so `Server` and `Admin` behave
identically in singleplayer and while hosting. A permission feature therefore
cannot be tested alone. Details in `docs/ck/multiplayer-and-server.md`.

## MSM-19 — Server sync — one point, not three

MSM reads the server's rules in full already (`ForeignConfigDiscovery.IsReadOnly`
→ CoreLib's `ConfigScope.Changeable()`) and cannot send a change to the server.
A change to a server-scoped value therefore takes effect locally and nowhere
else. **That it should be built is settled** — MSM is meant to be able to replace
General Mod Config Menu, and that is impossible without it. What is open is the
how, below.

**It is one point because its parts do not admit an order in which they make
sense separately.** The call order (send → await → write) is not an addition to
the transport but its shape: build the transport writing locally first, and the
order has to dismantle that afterwards. `AdminOnly` is technically separable —
the sync would run without it — but a server-authoritative config system the
operator cannot tighten is not parity, so it belongs in.

**Two prerequisites before any of it.** The access level above, because without a
deliberately chosen scope default the sync inherits one from a constructor; and
an **ADR**, because this contradicts ADR-001 in as many words (*"no server-sync
reimplementation"*).

**The point is large, but hardly because of the code.** Nearly every mechanism
exists: transport, permission evaluation, change notification, disk access,
serialisation, clamping, even a precedent for chunking a large message. See
`docs/ck/multiplayer-and-server.md` for the RPC hash, the topology test and the
permission substrate, and `docs/ck/persistence.md` for CoreLib's config
semantics. What MSM adds is almost no mechanism and almost only policy — which
key, which direction, when, who wins — and that is where the cost sits: each of
those can be decided wrongly and then stay wrongly decided in silence.

### Two directions, and the second is the one that gets forgotten

| Direction | Trigger | Runs |
|---|---|---|
| Send a change, apply the verdict | the player edits something | only while the menu is open |
| **Fetch everything (initial sync)** | a connection exists | **independent of the menu** |

Without the second, the write rule below contradicts itself: on entering a world
the client has changed nothing, so its memory would still hold its own
preferences and only the entries the player touches would come from the server.

GMCM has an initial sync — `RequestSyncAll()` on an edge of
`Manager.networking.isConnected` — but it lives in `ModConfigMenu.Update`, i.e.
**only while the menu is open**. For a display-only model that suffices: values
are needed when they are looked at. Here they are meant to *apply* whether anyone
is looking or not, so the initial sync has to leave the menu — and with it, MSM
writes to memory outside the menu, which GMCM never does on a client at all.

- **To check before deciding:** how MSM recognises "connected" and "session
  ended". GMCM polls; `API.Client.OnWorldCreated` exists and GMCM uses it
  elsewhere. **Whether a suitable pair of events exists is unverified** — that
  GMCM polls proves nothing. *To check:* search `PugMod.SDK.Runtime` and
  `Pug.Other`. It is the same pair that triggers the `Reload()` below: one
  decision, not two.

### Send first, then write — and one send method for both

For a server-scoped value the order is **send → await → write**, not write
locally and roll back on refusal. Whoever writes first creates a state that
should never exist — a value in effect locally that the server does not know —
and has to catch it again afterwards. Whoever asks first never has it: either the
server accepts and it is written, or it refuses and nothing happened. There is
nothing to roll back because nothing was written.

**The send method is a façade, so call sites do not each decide where a value
goes.** It asks two questions, not one — the scope, and whether anyone else is
the authority. The second is `Manager.ecs.ServerWorld != null`: singleplayer and
hosting both own the world and have nobody to ask, so they are one case, not two
(`docs/ck/multiplayer-and-server.md`).

| Scope | `ServerWorld` | What happens |
|---|---|---|
| `ViewOnly` | either | never written; refused without touching the network |
| `Client` | either | written locally, disk and memory |
| `Server` / `Admin` | ✓ | written locally and directly — this process *is* the server |
| `Server` / `Admin` | ✗ | sent, awaited, and **only memory** is set |

**That last row separates disk from memory on purpose.** The player's own `.cfg`
stays their preference; the in-memory entry carries what is in effect, and while
they are on someone else's server the memory belongs to the server. GMCM makes
the same separation, only visibly, through two value columns; here it stays
invisible and the row shows the memory value. Coming back is cheap:
`ConfigFile.Reload()` re-reads the file, so leaving a session restores the
player's own settings without MSM having buffered anything — the disk was never
touched.

**This is the write-side mirror of a rule that already exists.**
`ForeignConfigDiscovery.IsReadOnly` makes exactly the same three-way distinction
on the read side, title-screen case included. The access-level point pulls
`IsReadOnly` out of the discovery path anyway; the façade would be its second
caller.

- **Open:** what the row shows while an answer is outstanding. GMCM uses a yellow
  icon; going inert for the duration is equally possible. Something must be
  visible — a row that answers a keypress a network round later reads as broken.
  A locally answered value replies in the same frame, so the waiting state must
  not flash there at all.
- **Open:** what happens when no answer arrives. A timeout needs its own
  decision, or the row hangs indefinitely.
- **Open:** a value the server accepts is then in memory but not on the player's
  disk, so it is gone at the next singleplayer start. That is consistent, but it
  should surprise nobody.
- **The refusal itself is MSM-10** — the same feedback, a different trigger.
- **Collision rule: an incoming value beats your pending one.** If another player
  changes the same entry while you wait, the server wins. GMCM decides it the
  same way (`OnReceiveSync` drops the pending change) but does it **silently** —
  the player's input vanishes without a word. Agree with the rule, not with the
  silence.

**Checked and rejected: send client-scoped values too and have the server echo
them back.** It looks like the same unification and costs more. The branch does
not disappear, it moves to the server — which would have to *not* apply such a
value, since applying it would overwrite **its own** copy of that mod's setting.
It spends a network round on a purely local setting, so a HUD toggle would visibly
hang on a poor connection. Chunking comes along, so editing a local exclusion list
would send dozens of RPCs for nothing. And with no connection there is no echo
partner — the menu is reachable from the title screen, so the local path has to
exist regardless, which is exactly where the unification was supposed to pay off.

### The key on the wire must not be a list position

Vanilla identifies everything that crosses the wire **at compile time**, and
differently per direction: a `NetworkCommand` enum member for client → server, a
`[GhostField]` name for server → client. There is no generic key→value channel
anywhere — `guestMode` and `simulationDisabled` are fields, not entries. For a
foreign mod's config entry neither an enum member nor a field can be declared, so
vanilla has no answer here because it never has the problem.

**GMCM's answer is the list position, and it does not hold.** Its id space is
built by enumeration — `AllConfigFilesReadOnly` × `Entries.Values`, filtered on
`ShouldSync` — and the index *is* the id. Only that number travels;
`GetFullName()` is logged, never compared. A **client-only mod** with config
entries — an ordinary thing to have — appears in the client's list and not the
server's and shifts every subsequent id. The server then writes the value into
the wrong entry without complaint, because an admin skips the scope check anyway;
an id past the end throws `IndexOutOfRangeException` inside the server system,
since the lookup is unchecked.

**For dynamic identity CK uses hashes**, just not in RPC payloads —
`StableTypeHash` per type, `TypeHash.CombineFNV1A64` for the RPC collection. A
hash key is therefore the house pattern for this case, and **MSM already has the
string**: `ListKindStore` addresses foreign entries as `"ModId/Section/Key"`.

| | GMCM | proposed |
|---|---|---|
| Key | list position (`int`) | `FNV1A64` over `"ModId/Section/Key"` (`ulong`, 8 bytes) |
| Resolution | unchecked indexing | dictionary lookup |
| Unknown key | wrong entry, or an exception | refuse, and say which |
| Differing mod sets | shifts every id silently | no effect |

The gain is diagnosability more than robustness: "the other side does not know
this entry" becomes an answer that can be named instead of a silent miswrite —
and that case is the ordinary one, not the exotic one.

- **To check before building the key:** "CK hashes *types*, not entries" is an
  argument from absence; nobody searched systematically for a string-keyed hash
  utility. If one exists it is preferable to a hand-rolled `FNV1A64`. *To check:*
  grep for `FNV1A64`, `Hash128`, `StableTypeHash` and their string overloads.

**Delivery to an open row is the subscription from MSM-11, not the id space.**
GMCM needs a detour there because its menu is a singleton whose rows live
permanently: a received value lands in a list, is drained in the menu's own
`Update`, and is mapped to a row through a `Dictionary<ConfigEntryBase, …>`. MSM
rebuilds its rows **per open**, so there is no row to update while the screen is
closed, and on opening the current value is there anyway. Delivery is needed
only for the one case where the screen is open, and a per-entry subscription
covers it. No mailbox, no mapper, no id.

**The ghost route is more expensive despite being vanilla's own.** Replicating
state through a ghost component instead of RPCs would move the ghost collection
hash **on top of** the RPC hash. Closer to vanilla, twice the protocol cost.

### `AdminOnly` — the operator's switch

GMCM binds an `AdminOnly` entry at `ConfigAccessLevel.Admin`, and the **server**
evaluates it when judging a change: with it set, server-scoped values may be
changed by admins only, no longer by every non-guest. That is why it has no place
without the transport — it is not read locally but on the other side.

**It is set through the ordinary path; there is no special mechanism.** GMCM's
own config file appears as a page in its menu like any other, the entry is a row
in it, an admin toggles it, and it travels the same sync as every other value. On
a dedicated server the accepting write is saved, so the switch survives a
restart. Arriving at the clients it is the one entry with a special case in the
receive path: it re-evaluates every row's permission immediately.

**It protects itself, so there is no lock-out.** The setting that governs who may
change server settings is itself an `Admin`-scoped setting — a non-admin cannot
toggle it — and `Admin` entries are not subject to the `adminOnly` branch at all,
which tests `accessLevel == Server`. Even with the switch set, the switch stays
reachable for admins.

- **Effort:** small once the transport stands, and "small" here is not an
  estimate: one `Bind(..., ConfigAccessLevel.Admin)` in MSM's own section plus one
  `&& !adminOnly` in the server's accept check.
- **Open:** whether MSM brings the switch itself or whether it belongs to
  whatever carries the sync — if that turns out to be CoreLib, the switch
  probably belongs there too.
- **To check before building on it:** two pieces above come from a subagent
  report and were not looked up first-hand — `PugSimulationSystemBase`/`isServer`
  and, for serialising values, `Get`/`SetSerializedValue` with
  `TomlTypeConverter`. Both are in use in GMCM and in MSM but were not opened in
  the decompile or in CoreLib's source.

## Small fixes

- **MSM-20 — Format-override toggle / misclassification confirmation for
  editable lists.** `ForeignConfigDiscovery`'s `HeuristicSaysList` can
  misclassify a foreign plain string as a list; in the read-only drill-in
  (ADR-002) that was harmless, but the `list-widget-editing` slice makes the
  drill-in write `BoxedValue` back into the foreign `ConfigEntry` on commit — a
  misclassification now risks a lossy, comma-rejoined overwrite of a third-party
  mod's real config value. ADR-002 §7's format-override toggle (or a lighter
  one-time confirmation before the first write to an unconfirmed entry) is the
  fix; deliberately not built in that slice (see ADR-003's "Consequences"
  section — the risk this bullet describes). Flagged by the
  `pr-review-toolkit:review-pr` gate, requested 2026-08-12.
- **MSM-21 — `Shake()` is inherited and unused.** `RadicalMenuOptionTextInput`
  ships shake feedback (0.4 s, 20/s, already configured on the row template) for
  exactly the case where it silently discards input, and the drill-in has two
  such cases: a typed comma is stripped at commit, and a row whose text already
  fills the 255-character cap refuses every keystroke (`room == 0` in the
  `AppendString` prefix). The second case used to be vanilla's width rejection;
  horizontal scrolling removed that one and moved the same silence to a
  different, much rarer threshold — reachable since ADR-006 let long identifiers
  through as list tokens. Both vanish without a word today. **Not the field flip
  it looks like:** the comma strip happens at commit, and every commit path
  destroys and rebuilds the row, so a shake started there would animate an
  object that disappears in the same frame — the feedback has to move to the
  moment of typing. `ShakeAndClear` despite its name clears only its own
  coroutine handle, not the text. Carried over 2026-08-23 from the
  drill-in-frame work, which shipped the rest of that section.
- **MSM-23 — Holding a word-jump key crawls after the first jump.** Vanilla's
  arrow branch repeats on a cooldown (`MenuManager.IsKeyDown` is `GetKeyDown(k)
  || (GetKey(k) && cooldown elapsed)`), while the word-jump postfix triggers on
  `Input.GetKeyDown` alone — so holding the key jumps one word and then crawls
  character by character at the repeat rate. Not a one-line change, though the
  reason has changed: this used to say the cooldown could not be read at all,
  which [ADR-009](adrs/009-caret-index-from-the-counter.md) disproves — a private field is legally readable through
  `API.Reflection`, and `MenuManager.typingInputCooldown` is one
  (`Pug.Other:269210`). What remains is that reading it is not the same as
  sharing it: the postfix would still have to decide *its own* repeat behaviour
  from a timer vanilla restarts on its own terms. Switching to `GetKey` instead
  is the shape to avoid — it would fire every frame while vanilla still moves on
  its own ticks, racing the caret through the string at frame rate. Cursor
  position only — no text is lost. Found by the review gate 2026-08-26.
- **MSM-24 — The click collider's fallback reads a transform that moves every frame.**
  `UpdateClickCollider` falls back to the field mask's `localScale`/
  `localPosition` when `fieldBorder` is missing, but `FitMaskToViewport` rewrites
  exactly those two values each frame to the mask's intersection with the list
  viewport. The hit area would then shrink with the row instead of being the
  fixed upper bound its own comment promises. `TextFieldViewport` already caches
  the authored geometry (`_fieldWidth`, `_fieldOriginX`) precisely because the
  live transform stops being a witness to it after the first frame — the row
  should ask there. Dead today: the branch runs only on a mis-wired prefab. Found
  by the review gate 2026-08-26.
- **MSM-25 — `maxScroll` and the caret reader disagree about `dimensions.xMin`.**
  `TryCaretIndexFromLocalX` subtracts it, matching vanilla's own blinker formula;
  `ApplyOffset`'s end-of-text clamp does not. The two agree while the row's text
  is left-aligned, which it is — change that and the clamp silently stops short
  of the text end or overshoots it. `_text.dimensions.xMin + _text.dimensions.width
  - _fieldWidth` holds either way. Found by the review gate 2026-08-26.
- **MSM-26 — Prove that the reset poll's action id is the one that works.** The poll binds
  Rewired action 223 (`OpenProfile`) rather than vanilla's own `ResetDefaults`
  (300), and the reasoning is on paper rather than measured: 300 belongs to the
  `ControlMapperUI` category and 223 to `Menu`, the category that applies while
  any menu is open (`docs/ck/ui-framework.md`). What is established is that the
  categories differ — **not** that a poll on 300 stays silent here. The
  counter-test is cheap: poll 300 in this screen once and press the button.
  Worth doing because `Manager.input.GetButtonDown(int)` returns **`false`
  silently** for an action in no active map, so a wrong choice never surfaces as
  an error, only as "the button does nothing" — the same combination that
  already cost a round in ADR-002 → ADR-004. The result belongs in the handbook,
  not in code.
