# A heading is a plain MonoBehaviour that never enters `menuOptions`

- Status: accepted
- Date: 2026-09-03
- Implements roadmap point MSM-03, which shipped with this decision and took its
  id with it

## Context and Problem Statement

A consumer with many settings gets one flat list. Nothing lets it say "these
belong together", and the framework had no row that holds no value: every
`SettingKind` until now described a configurable value, and every row was a
`RadicalMenuOption` and therefore navigable.

A heading must be visible, must span the full width rather than the two-column
label/value raster every other row uses, and must be **unreachable** — arrow
keys and the controller have to step over it, and the mouse must not select it.
Core Keeper is controller-first here, so "unreachable" is not a detail: a row
the stick can land on but that does nothing is worse than no heading at all.

## Decision Drivers

- The exclusion has to survive future edits by people who do not know it
  matters. A rule that lives only in a reviewer's head is not a rule.
- The framework's public surface (`SettingKind`, `SectionBuilder`) is consumed by
  every sibling mod in this family, so anything added to it is effectively
  permanent.
- Divergent geometry means a divergent prefab — the rule this repository already
  followed when `Info` and `Label` were split in the first place.

## Considered Options

1. **A `RadicalMenuOption` that reports `INACTIVE` or `GRAYED_OUT`.**
2. **A plain `MonoBehaviour`, never registered in `menuOptions`.**
3. **Reuse the existing two-column row prefab**, leaving the value column empty.

## Decision Outcome

**Option 2**, and it is chosen for a reason stronger than taste: a plain
`MonoBehaviour` *cannot* become navigable. `UIelement.isMenuOption` is virtual
with a default of `false` (`Pug.Other:357841`) and is overridden in exactly one
place in the entire game — `RadicalMenuOption` (`Pug.Other:343070`). So the
exclusion is a property of the type rather than a flag somebody must remember to
set. Core Keeper reaches the same result the same way for the category headings
in its key-rebinding screen (`ControlMapping_CategoryLabel`,
`Pug.ControlMapping:1803`), which its own menu adds to a list nothing navigates.

Option 1 was rejected because both states are wrong: `INACTIVE` removes the row
from the layout entirely, and `GRAYED_OUT` means "normally editable, just not
right now" — a red row that signals something is being withheld, which is a lie
about a heading. Option 3 was rejected on the geometry rule above.

Three further decisions came with it:

- **`.Label(string key)` only.** A bare `.Separator()` was cut rather than built:
  no consumer wanted one, and an unused entry point cannot be withdrawn from a
  published API. A divider can be added later without changing `.Label`.
- **The argument is a loc key, with no literal-text fallback.** A readable
  fallback would hide a missing translation; a visible raw key is the same
  diagnosis every other row already gives.
- **A heading is a segment boundary for sorting.** Under `OptionSort.ByKey` /
  `ByLabel`, the settings between two headings are sorted among themselves and
  the headings keep their declared positions. A heading already states an order,
  so a sort that crossed it would answer the same question twice and contradict
  its own first answer.

### Consequences

- `SettingKind.Label` is the first kind whose `SettingDef` carries **no**
  `ConfigEntry`. Every reader of `ModSection.Settings` must gate on `Kind`, or on
  `Entry` being non-null, before reaching for a value. That held without change:
  `SectionReset.IsInScope` already tested exactly that, and discovery never
  produces a label.
- **The whole guarantee rests on one branch** in `ModSettingsScreen.Populate`
  that routes a label to `LabelRow` instead of to `SettingWidget`. A label
  reaching the widget path would not throw — `ValueString` has no `Label` case
  and returns `""` without touching the null `Entry` — it would silently become a
  selectable, empty-valued row, which is the one outcome this decision exists to
  prevent. `SettingWidget.Bind` therefore refuses a label loudly and leaves
  itself unbound, so the row reports `INACTIVE` rather than becoming that row.
- The consumer API gained its first declaration that **cannot fail**: nothing is
  bound, so there is no `BindGuarded` path and no detached handle. In exchange,
  `RequiresRestart()` had to learn to refuse after a heading, and the ordering of
  its two guards turned out to be load-bearing — they are not mutually exclusive,
  because a declaration that fails *after* a heading leaves that heading as the
  last entry.
- A heading and a setting share one loc-key space, and CoreLib cannot see a
  collision because a heading never binds. `SectionBuilder.Build` now reports a
  duplicated key once, for both directions at once.
- MSM-15 (grouping a discovered mod's rows by its `.cfg` sections) can now be
  built on this widget, but is not: a heading is *declared* by a consumer, and a
  discovered mod declares nothing, so something still has to emit one per config
  section on the discovery path.

## More Information

The full design record, including the alternatives weighed before this was
settled and the measurements behind the prefab values, is the raw spec this ADR
was distilled from. It is no longer in the tree; read it from history with:

~~~bash
git show "$(git rev-list -1 HEAD -- docs/specs/2026-09-03-msm-03-label-widget-design.md)^:docs/specs/2026-09-03-msm-03-label-widget-design.md"
~~~

One finding from building it outlived the spec and lives in [`../../CLAUDE.md`](../../CLAUDE.md)
instead, because it will bite the next person authoring a full-width element:
inside a settings row, `x = 0` is the boundary between the two columns, not the
left edge. An element authored fresh in the Editor arrives centred at `x: 0` —
between the columns — and the symptom is a row that occupies its correct height
while showing nothing at all.
