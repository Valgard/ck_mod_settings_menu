# Design — MSM-03: a full-width label row (`SectionBuilder.Label`)

- **Date:** 2026-09-03
- **Status:** Approved (design); implementation pending
- **Roadmap point:** MSM-03 (see [`../roadmap.md`](../roadmap.md))
- **Builds on:** the section/widget rendering in `ModSettingsScreen`, and Core
  Keeper's own `ControlMapping_CategoryLabel`, which turns out to be a directly
  copyable precedent.

## 1 · Goal

A consumer can put a **heading between its own settings**, so a long section
reads as groups rather than as one flat list. The row is display-only, spans the
full width of the box, and is skipped by navigation.

```csharp
ModSettings.Section(this)
    .Label("display")
    .Toggle(out _, "showHud", true)
    .Toggle(out _, "showCoords", false)
    .Label("behaviour")
    .Slider(out _, "delay", 0f, 5f, 1f, 0.5f)
    .Build();
```

It is the cheapest widget in the planned batch in code and the only one that
needs Editor work, because it is the only one whose geometry leaves the
two-column label/value raster every other row uses.

## 2 · Decisions (locked)

1. **`.Label(key)` only — no `.Separator()`.** The roadmap listed both. A bare
   divider has no waiting consumer, and this repository's rule is that a
   framework API is not designed from a hypothetical one. A rule or a divider can
   be added later as a modifier without changing `.Label`; removing an unused
   entry point from a published API cannot be done without a break.
2. **One argument, and it is a loc key.** `.Label(string key)` resolves
   `<ModId>-Config/<key>` through the same chain every other row uses, falling
   back to the raw key. Deliberately **no** literal-text parameter: a pretty
   fallback would hide a missing term, and a visible raw key is the same
   diagnosis every other row already gives.
3. **A label is a segment boundary for sorting.** Under `OptionSort.ByKey` /
   `ByLabel` the settings between two labels are sorted among themselves; the
   labels keep their declared positions. A label states an order; a sort that
   reorders across it answers the same question twice, contradictorily.
4. **Not a menu option at all.** The row is a plain `MonoBehaviour`, never
   registered in `menuOptions`. This is Core Keeper's own solution, and it is
   structural rather than a setting — see §3.
5. **No divider sprite.** Grouping is typographic: a heavier font face plus extra
   space above. Again CK's own answer, and it means the template carries no
   sprite, material, mask-interaction or sorting-order decisions.

## 3 · What Core Keeper already does

CK has exactly one precedent, and it answers most of the design.
`ControlMapping_CategoryLabel` (`Pug.ControlMapping:1803`) is the category
heading in the key-rebinding screen:

```csharp
public class ControlMapping_CategoryLabel : MonoBehaviour
{
    [SerializeField] private PugText _nameText;
    [SerializeField] private PugText _descriptionText;

    public void Setup(string nameKey, string descriptionKey, int startPadding)
    {
        LinearLayoutUIComponent component = GetComponent<LinearLayoutUIComponent>();
        if ((bool)component)
        {
            component.paddingStart = startPadding;
            component.RenderUIComponent();
        }
        …
    }
}
```

Four properties transfer directly:

- **It is a `MonoBehaviour`, not a `UIelement` and not a `RadicalMenuOption`.**
  That is what makes it unreachable by navigation, and it is a stronger guarantee
  than any flag: `isMenuOption` is virtual on `UIelement` with a default of
  `false` (`Pug.Other:357841`) and is overridden in exactly one place in the whole
  game — `RadicalMenuOption` (`Pug.Other:343070`). A plain `MonoBehaviour`
  therefore *cannot* enter `menuOptions`; nothing has to remember to keep it out.
- **Its owner keeps it out of `menuOptions` deliberately.** `ControlMappingMenu`
  adds its real rows with `menuOptions.AddRange(...)` and its category labels with
  neither an add nor a remove — `CleanupCategoryLabels()` frees the pool while the
  sibling `CleanupActionMappings()` beside it calls `menuOptions.RemoveAll(...)`.
- **The spacing lives in the template, not in a height calculation.** `Setup`
  writes `paddingStart` onto the label's *own* `LinearLayoutUIComponent` (4 for
  the first category, 16 otherwise) and re-renders it.
- **The distinction is font weight, not a line.** The heading renders in
  `boldMedium` while the rows around it are `thinSmall`
  (`Pug.Other:271615`–`271619` defines `thinSmall = 16777232`,
  `thinMedium = 16777264`, `boldMedium = 67108912`). There is no divider sprite
  anywhere in the category-label prefab or in `ControlMappingMenu.prefab`.

**What does not transfer:** the second line. CK's label carries an optional
`_descriptionText` in `thinSmall`. MSM already has that relationship one level
up, as a section's heading plus its hint, and no consumer has asked for it inside
a box. Left out — see §10.

**One limit of the precedent, stated plainly:** it is used in exactly one screen.
The vanilla settings menus (`SettingsMenu`, `AudioSettings`, `GraphicsSettings`,
`GameplaySettings`, `DisplaySettings`) are flat lists with a single screen title
and no in-list headings at all. So this is CK's idiom for the case, not a widely
exercised pattern — it is the best available model, not a guarantee.

## 4 · Data model

`SettingKind` gains **`Label` as its last member**. Appending rather than
inserting is a house rule (`ListEditing` carries the same warning): nothing
persists a `SettingKind` today — `ListKindStore` stores only id strings — but the
enum is public API and a shifted value is a silent break.

`SectionBuilder.Label(string key)` records a `SettingDef` with:

| Field | Value |
|---|---|
| `Kind` | `SettingKind.Label` |
| `Key` | the declared key |
| `Term` | `MsmTerms.Label(modId, key)` |
| `Entry` | **`null`** — no `Bind`, no `ConfigEntry`, no persistence |

There is no `out` handle and no `ConfigDescription`: a label holds no value, so
there is nothing for a consumer to read or write. It does not reach
`BindGuarded` at all, which also means it cannot fail the way a real declaration
can.

**A `SettingDef` with a null `Entry` is safe, and that is measured rather than
assumed.** Five places read `ModSection.Settings`:

- `SectionReset` (`SectionReset.cs:28`, `:45`) — both go through `IsInScope`,
  which already tests `def.Entry != null` (`SectionReset.cs:62`).
- `ForeignConfigDiscovery` (`ForeignConfigDiscovery.cs:30`, `:99`) — builds its
  own sections and never produces a label.
- `SectionBuilder.RequiresRestart` (`SectionBuilder.cs:501`–`503`) — needs a
  guard; see §7.
- `ModSettingsScreen.OrderedSettings` (`ModSettingsScreen.cs:550`) — needs the
  segmentation; see §6.
- The widget classes reach a def only through `Bind`, which a label never enters.

## 5 · Rendering

A fourth template beside `settingTemplate` and `listTemplate`:

- **`labelTemplate`** — a serialized field on `ModSettingsScreen`, wired in the
  prefab, kept inactive under `WidgetTemplates` like its siblings (they are
  force-deactivated by `DeactivateTemplates`).
- **`LabelRow : MonoBehaviour`** — one serialized `PugText text` field, mirroring
  `ListWidgetBox` and `SectionBox`. Not a `RadicalMenuOption`, so it inherits
  none of CK's selection, effect or collider machinery, and needs none of it.

`Populate` gains a third branch, placed before the list branch so the common
path stays last:

1. instantiate `labelTemplate` into the section's `widgetContainer`,
2. `SetActive(true)`, name it `Label <key>` for the hierarchy,
3. render the resolved text with `PugTextExtensions.RenderPlain` (the shared
   helper — `localize: false` plus a forced render, because MSM resolves terms
   itself through `Loc` rather than letting `PugText` do it),
4. set the row height through the same `SetRowHeight(go, RowHeightPx(text))` path
   every other row uses,
5. **do not** add it to `menuOptions`.

An unwired `labelTemplate` logs a warning and skips the row, exactly as an
unwired `listTemplate` does today (`ModSettingsScreen.cs:311`–`314`). That is
what lets the code land before the Editor work.

**Why nothing else needs touching.** `ModSettingsScreen` navigates by index —
`useUIElementsForNavigation: 0` in `ModSettingsMenu.prefab:1304` — so a row absent
from `menuOptions` simply does not exist for navigation. The stall that the
handbook records for the `UIelement` path (`SelectIndexInDirection` asks
`GetAdjacentUIElement` before the state filter runs, so a skipped neighbour ends
navigation instead of stepping over) cannot occur here. `SelectedSection()` reads
`GetSelectedMenuOption() as ISectionRow`, so the footer hint bar is unaffected;
`LabelRow` does not implement `ISectionRow` and has no reason to.

**Scroll-follow has one consequence worth stating.** `ScrollSelectedIntoView`
centres the selected row with a half-height padding. Moving onto the first row of
a group therefore does not guarantee that the group's heading is on screen. This
is accepted rather than solved: the alternative is teaching the scroll logic
about a row that is not a menu option, which would put knowledge of labels into
a method that otherwise only knows menu options.

## 6 · Sorting — a label is a segment boundary

`OrderedSettings` (`ModSettingsScreen.cs:550`) currently copies
`section.Settings` and sorts the copy. `AsDeclared` stays untouched. For `ByKey`
and `ByLabel` the list is walked once, splitting at every `Kind == Label`; each
run of non-label defs is sorted on its own and appended after the label that
opened it.

```
declared              ByLabel, segmented
  Label "display"       Label "display"
  zebra                   alpha
  alpha                   zebra
  Label "behaviour"     Label "behaviour"
  yak                     beta
  beta                    yak
```

Degenerate cases need no special handling: settings before the first label are
one leading run, two adjacent labels produce an empty run between them, and a
section with no labels reduces to exactly today's behaviour.

## 7 · Two guards

**`RequiresRestart()` after a label is refused.** It addresses the
most-recently-declared setting positionally as `Settings[Count - 1]`
(`SectionBuilder.cs:501`–`503`). `.Label("x").RequiresRestart()` would mark the
label — harmless in effect today, since a label never reaches `Adjust`, but it is
the same failure `_lastDeclarationFailed` was written for: a modifier silently
attaching to the wrong thing. It logs and returns, in the same wording as that
guard.

**A key that collides with a setting's key is warned about.** `.Label("x")` and
`.Toggle(out _, "x", …)` in one section resolve to the *same* loc term, because
`MsmTerms.Label` is the single schema for both. CoreLib cannot catch it — a label
never binds — so the check belongs here: when a label's key already appears in
`_section.Settings`, or a later setting reuses a label's key, log it once naming
both. Cheap, and it turns "my heading is named like my setting" into a line the
author can act on.

## 8 · The Editor step

New prefab objects must be authored in the Unity Editor: a `-batchmode` build
reserializes the prefab to canonical form and silently drops hand-written
objects, which this mod has already paid for once (a section-box `SpriteRenderer`
plus its `background` wiring, both appended by script, gone after the next
build). What follows is therefore a description for a person, not a task for the
assistant.

Under `WidgetTemplates`, beside `SettingTemplate` and `ListTemplate`:

- **`LabelTemplate`** — a GameObject carrying
  - a `WrapperUIComponent`, so the box's `LinearLayout` measures the row (every
    widget row has one; the section's own header and hint deliberately do not),
  - a `LabelRow` component,
  - one child `PugText`, assigned to `LabelRow.text`, with
    `fontFace: boldMedium` (`67108912`), `maskInteraction: 1` — the value both
    widget-row texts carry, so it clips inside the box instead of overscrolling
    it; note the section header beside them uses `0`, because it sits outside
    the box — and `maxWidth: 22` as a starting value, which is what the two
    columns add up to (`11` each) and therefore the full inner width. A non-zero
    `maxWidth` means a long heading wraps rather than running out of the box,
    and the row grows with it, since the height is measured from the rendered
    text. The prefab's `localize` value is immaterial: `RenderPlain` sets it to
    `false` on every render, because MSM resolves terms itself through `Loc`.
- Wire the template into `ModSettingsScreen.labelTemplate`.
- Leave the object **inactive**; `DeactivateTemplates` enforces this at runtime
  anyway, but an active template in the prefab is a trap for the next reader.

The `WrapperUIComponent.pivot` and the space above the heading are the two values
to settle by looking at it — see §11.

## 9 · Sequence and verification

1. **Code first**, with `labelTemplate` unwired. It compiles, builds and runs;
   a declared label logs the unwired warning and renders nothing.
2. **Editor session** (user) — author `LabelTemplate` per §8, close the Editor.
3. **Build and check in game.** There are no automated tests in this repo;
   verification is a person walking the menu.
4. **Calibrate** the two open values of §11, in game.

Checks to add to [`../manual-tests.md`](../manual-tests.md), in its existing
style: a label renders full width and is visibly a heading; arrow keys and the
controller step *over* it in both directions; the mouse cannot select it; a
section whose labels and settings are declared under `ByLabel` sorts within
groups only; a label with no term shows its raw key; `RequiresRestart()` after a
label logs and changes nothing; the section reset leaves labels alone and still
resets every real setting of that section.

Documentation to carry along: [`../architecture.md`](../architecture.md) (the
new class and the third branch), `README.md` (the consumer API reference),
`CHANGELOG.md` (the in-progress version's entry), and
[`../roadmap.md`](../roadmap.md) — where MSM-03 is removed on delivery, taking
its id with it. Since `.Separator()` was cut from the point rather than built,
it needs a **new** id if it is ever wanted; ids are never reused, and the first
unused one is MSM-29.

## 10 · Explicitly out of scope

- **`.Separator()`** — see decision 1.
- **A description line under the heading**, which CK's own precedent offers. MSM
  expresses that relationship one level up already (a section's heading plus its
  hint), and no consumer has asked for it inside a box.
- **MSM-15 — grouping a discovered mod's rows by its `.cfg`'s own sections.** Its
  own roadmap point, and the roadmap's instruction is to build the widget first:
  if a grouped header cannot be built out of `.Label`, the widget's spec is
  incomplete, and that is cheaper to learn here. This spec therefore asserts
  only that a discovered section could *render* a label the same way — it does
  not build the path that would produce one.
- **Making a label reachable for any reason** (a tooltip, a fold, a click
  target). Each would turn it back into a menu option and undo §3.

## 11 · Open, to be calibrated in game

Two values cannot be decided on paper, and both are single fields in the
template:

- **The space above a heading.** CK uses `paddingStart: 16` against `4` for the
  first one, set on the label's own `LinearLayoutUIComponent`. MSM's box stacks
  rows with its own gap, so the equivalent may be a `paddingStart` on a layout in
  the template or simply a taller `WrapperUIComponent`. Decide by looking; the
  difference between "grouped" and "crowded" is a few pixels.
- **`WrapperUIComponent.pivot`.** `MiddleLeft` centres a row in its slot and is
  right for the symmetric single-line rows; `TopLeft` top-anchors and is what the
  section header and hint need because their content grows downward. A heading
  with space above it may want either, depending on how the space is produced.

A third question is a judgement rather than a measurement: **`boldMedium` is
also what the section header uses**, one level up and outside the box. CK draws
the same equivalence (its category label and its screen title are both heavier
than the rows), and the position inside the box is what separates them. If it
reads as competing with the section heading in game, the fallback is
`thinMedium` in the header's colour — a one-field change in the template, not a
design revision.
