# The list drill-in owns its rows; the stored value is derived from them

- Status: accepted
- Date: 2026-08-23
- Supersedes [ADR-003](003-list-widget-editing.md) **in part** — see
  § "Relationship to ADR-003"

## Context and Problem Statement

The drill-in's rows were derived from the stored value: `RebuildRows` read the
`ConfigEntry`, tokenized it, and produced one row per token. That made the value
the single source of truth and the rows a pure projection of it — simple, and
correct for as long as every row corresponded to a token.

It cannot represent a row that is *empty*. An empty token is never written (the
value would carry consecutive commas into another mod's config file), so it is
never read back, so the row vanishes at the next rebuild — and a rebuild follows
every commit. A user who clears an entry to retype it, or wants a blank line to
fill in next, loses it the moment they touch a neighbouring row.

The same projection also made "add an entry" a strange act: the trailing `+ Add`
row was a text field that pretended to be an entry, and typing into it was how a
list grew.

## Decision Drivers

- An entry must be allowed to be blank **while editing**, without that blankness
  reaching the owning mod's config file.
- Whatever holds the rows must not weaken `ListTokenizer`'s role as the one
  agreed tokenization rule — four call sites were unified there precisely because
  their divergence had already caused a real bug.
- Adding an entry should be an action, not a disguised text field.
- The framework writes into **third-party** config files. Any change to the write
  path is judged first on what it can destroy.

## Considered Options

1. **Keep deriving rows from the stored value** and accept that empty rows cannot
   exist.
2. **Persist empty entries** so they survive a rebuild.
3. **Give the screen its own row list** for the lifetime of one open drill-in and
   derive the value from it.

## Decision Outcome

**Option 3.** `ListDetailScreen` holds `_rows` while the drill-in is open.
`Populate` seeds it from the stored value through `ListTokenizer`; `RebuildRows`
renders it; a commit writes the editing row back at its own `RowIndex` and
assembles the value from the list, skipping empty entries. An empty row therefore
exists on screen and nowhere else, and disappears on reopen because it was never
written — which is exactly what "blank entries are not saved" should mean.

`ListTokenizer` is untouched. Its contract narrows rather than changes: it
describes how a *stored value* becomes an initial row list, and is no longer a
statement about what is on screen.

With rows no longer projected from the value, the trailing row no longer needs to
be an input at all. It became **`ListAddRow`**, a plain `RadicalMenuOption` that
appends an empty row on activation — a live object inside the container rather
than a template, because there is only ever one of it and cloning it per rebuild
would destroy and re-render an unchanging object on every commit.

**A first attempt kept it on `ListDetailItem`**, separated only by a row-kind
field, on the argument that a second prefab object costs more than carrying the
inherited text-input machinery as inert ballast. That was wrong, and the review
showed why: the ballast had consequences the machinery itself did not. The commit
path had to accept a row index of `-1` as a normal case, which forced its guard to
stay silent — and a silent guard cannot distinguish "this is the button" from
"this row lost its index", the second being a user's edit vanishing without a
word. Three fields had to agree (`kind`, `rowIndex`, `readOnly`) with nothing
enforcing it. And the button inherited `Update()`, hence the ability to fire a
commit while being torn down.

Splitting the type makes all three **unrepresentable** rather than guarded:
`OnRowTextCommitted` takes a `ListDetailItem`, and a `ListAddRow` is not one. The
teardown likewise stopped needing an exemption for the button and now says what it
means — it removes the rows it created.

The button keeps a resting frame and a focus marker. CK's own `joinButton` carries
both exactly as its text fields do, so a frame there means "interactive element",
not "type here"; and the focus marker is the only thing telling a controller or
keyboard user where they are. `selectedMarker` belongs to
`RadicalMenuOptionTextInput`, so `ListAddRow` re-declares it and mirrors the
show/hide in `OnSelected`/`OnDeselected`.

### Consequences

- **`RebuildRows` must stay a full teardown-and-recreate.** It looks like an
  obvious candidate for an in-place update now that the row list is stable, but
  destroying a row is the only thing that resets
  `PugTextEffectMenuOption.isValueText`, which `OnActivated` flips to the vivid
  editing tint and nothing else reverts. A reused row would stay tinted forever.
- **A write-back defect needed closing in two places, not one.**
  `RadicalMenuOptionTextInput.Update` trims any text wider than `maxWidth` — a
  `while` loop that cuts it down to fit within a single frame — on every active
  row, edited or not. A foreign token too wide for the field is therefore shortened
  *on display*, and writing that shortening back would truncate someone else's
  config value.

  Assembling from `_rows` closes the half that concerns **untouched neighbours**:
  they contribute the token they were seeded with, whatever the screen shows. It
  does nothing for the row being committed, which is trimmed like any other — so
  merely activating an over-long row and pressing Enter would still have persisted
  the shortened form. Escape is no escape either: `Deactivate(bool commit)` ignores
  its parameter.

  The second half is `ListDetailItem.CommittedText`, which returns the seeded token
  unless a keystroke actually changed the text. A text comparison could not have
  decided this — a trimmed value and a backspaced one look identical — but the
  timing can: while a row holds `activeInputField`, a change is the user's; outside
  that window only the trim runs. Neither half suffices alone.

  Never observed in the wild, and it could not have been: the row's `PugText`
  carried a `maxWidth` of its own until this same pass, so it wrapped instead of
  overflowing and the trim never fired at all.
- **A guard retired by construction.** That walk also saw the inactive
  `ItemTemplate`, whose `pugText` carries a prefab placeholder forever, and needed
  an `activeSelf` check to keep it from being committed as a phantom token.
  Nothing walks `menuOptions` any more.
- **Row state is per open session.** Closing the drill-in discards it. That is the
  intended lifetime, and it is why an empty row does not survive a reopen.

### Confirmation

Verified in game against the fixture list and a real foreign config: a cleared row
stays visible while another row is edited; reopening shows a compact list; the add
button appends a blank row and selects it without raising the on-screen keyboard;
pressing it repeatedly leaves several blank rows, none of which reaches the config
file; emptying every row keeps the setting classified as a list (`ListKindStore`)
rather than collapsing to a read-only `Info` row; a read-only list shows neither
button nor frames.

## Pros and Cons of the Options

### The screen owns its rows (chosen)

- Good, because an empty row is expressible without inventing a stored
  representation for it.
- Good, because the write path stops reading rendered text **for every row but the
  one being committed**, which removes most of the surface through which
  display-side truncation could reach a foreign config file. The remainder needed
  its own answer (`CommittedText`); the row list alone would not have sufficed.
- Good, because it makes the add button possible: the button needs no path from
  screen text into the value.
- Bad, because there are now two representations of the same list during a
  session, and `RebuildRows` must be the only thing that reconciles them.

### Deriving rows from the stored value

- Good, because there is exactly one representation and no reconciliation.
- Bad, because it cannot express a blank row at all — the defect that prompted
  this ADR.
- Bad, because it forced the add row to be a text field, since typing into a
  projection was the only way to grow the source.

### Persisting empty entries

- Good, because blank rows would survive a reopen, which is arguably tidier.
- Bad, because it writes consecutive commas into a **third-party** mod's config
  file to represent something that only matters to this menu's UI. The framework
  does not get to spend another mod's data on its own convenience.
- Bad, because every reader of that value — including the owning mod — would have
  to tolerate empty tokens.

## More Information

- **Builds on** ADR-002 (`002-list-widget-drill-in.md`): the drill-in itself.

### Relationship to ADR-003

ADR-003 chose "uniform editable text row" — every token row on
`RadicalMenuOptionTextInput`, **plus one permanent trailing blank '+ Add' row**,
with edit, add and remove all travelling the same text-commit path. This ADR keeps
the first half and replaces the second, so read the two together as follows:

| Aspect | Still ADR-003 | Now this ADR |
|---|---|---|
| What a token row is | `RadicalMenuOptionTextInput` | unchanged |
| When an edit commits | the `activeInputField` transition, never `OnDeselected` | unchanged |
| Removing an entry | clear the text, confirm | unchanged |
| **Adding an entry** | type into a trailing blank row | press `ListAddRow`, a plain `RadicalMenuOption` |
| Where rows come from | derived from the stored value | the screen's own row list |

ADR-003 rejected exactly this shape as its Option 2 ("a separate non-text '+ Add'
row"), and that rejection was sound **on its premise**: while rows were a
projection of the stored value, typing into one was the only way to make the
source grow, so a non-text add row would have needed a parallel path into the
value for no gain. The premise expired with the inversion above, and the
cost/benefit flipped with it — see § Decision Outcome for what the shared
component was actually costing.

Not adopted from ADR-003's Option 2: the explicit per-row delete affordance. It
remains on the roadmap, and it is the reason the frame spans the field rather than
the whole row — the button it needs goes beside it.
- The field frame shipped in the same pass — `field_border` / `field_focus` in the
  `ui_chrome` atlas — and is what makes the *absence* of a frame a usable signal
  for the add button. `docs/ck/ui-framework.md` records the CK-level trap that
  came with it: a prefab `PugText.maxWidth` makes the text wrap and thereby
  disables the text input's own capacity check entirely.
- Still open in `docs/roadmap.md`: token reorder in the drill-in, a
  consumer-facing `SectionBuilder.List`, `Shake()` feedback for silently discarded
  input, and horizontal scrolling in a text field.
- The raw design spec this distils, with the alternatives as they were weighed at
  the time:

~~~
git show "$(git rev-list -1 HEAD -- docs/specs/2026-08-22-drill-in-row-model-design.md)^:docs/specs/2026-08-22-drill-in-row-model-design.md"
~~~
