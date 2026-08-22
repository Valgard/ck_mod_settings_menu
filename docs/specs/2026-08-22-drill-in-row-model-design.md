# Design — Drill-in row model: field frame, persistent empty rows, add button

The list drill-in (`ListDetailScreen`) gains a visual field frame, rows that may
stay empty, and an explicit add button in place of today's typed-into add row.
Together these change what a row *is*, so they are designed as one change rather
than three.

## 1 · Goal

A drill-in row that **looks like what it is**: an editable field reads as a
field, a button reads as a button, and a view-only value reads as text. Today
all three render as bare text, so the only way to discover that a row can be
typed into is to try it.

Two behavioural gaps ride along, because they are the same structure: a row
cannot currently be left empty (it vanishes), and adding an entry means typing
into a row that pretends to already exist.

## 2 · Decisions (locked)

1. **Frame sprites live in the existing `ui_chrome` atlas**, not as new
   textures. `sources/msm_ui_chrome.pixaki` + `.json` →
   `../utils/pixaki_to_sheet.py` already carries `borderOverride` (9-slice edges),
   `pad` and `internalIds` (stable fileIDs, so a repack never breaks a prefab
   reference). Two new sprites: **`field_border`** (resting) and
   **`field_focus`** (focus).
2. **CK supplies the templates, the author reworks them.** Vanilla
   `9sl_black` (16×16, border 4·4·4·4) and `character_customization_ui_dark_2`
   (8×8, border 3·3·3·3) are extracted as starting points and adjusted to this
   mod's palette in Pixaki — not copied verbatim, not drawn from scratch.
3. **Read-only rows get no frame.** The frame promises "you can type here";
   `ListDetailItem.OnActivated` returns before `base.OnActivated()` when
   `readOnly`, so such a row can never become `activeInputField`. This is the
   third place the code makes that same distinction — the first two being no add
   row and no edit mode.
4. **An empty row lives until the drill-in closes.** It survives edits to other
   rows, and is skipped when the value is assembled. It does not survive
   reopening, because it was never written.
5. **The frame keeps today's field width (25).** `maxWidth` is capacity, not a
   viewport (see § 7), so a narrower frame would make long tokens *unenterable*
   rather than merely clipped. A later per-row delete button goes beside the
   frame, as CK places `toggleVisibility` beside its fields rather than inside
   them.
6. **The add button selects the new row without entering edit mode.**
   `SetActiveInputField` would raise the on-screen keyboard on a controller
   unasked; CK's own text fields all require explicit activation.

## 3 · Row model

Three row types replace today's two, where "add row" is a flag on a text row:

| Type | Frame | Editable | Present when |
|---|---|---|---|
| Token row | resting + focus | yes | writable list |
| Add button | none | — | writable list, always last |
| Read-only row | none | no | read-only list (no add button) |

The add button needs **no new sprite**: it is a row carrying the `+ Add` label
and no frame, and its lack of a frame is exactly what distinguishes it from a
field. Activating it inserts an empty token row **before itself** and selects
that row.

Because the button is not a `ListDetailItem`, the value-assembly loop's existing
`opt is ListDetailItem` filter already excludes it — no new guard is needed
there. The same loop's `activeSelf` check (which skips the inactive
`ItemTemplate`) stays as is.

## 4 · Data flow — the inversion

Today the stored value is the truth: `RebuildRows()` reads `Value()` and derives
the rows. An empty row cannot exist in that model, because it is never written
and therefore never rebuilt.

The screen instead holds a row list for the lifetime of the open drill-in:

- **`Populate` (open)** reads `Value()` through `ListTokenizer` and seeds the row
  list. This stays the only place the stored value becomes rows.
- **`RebuildRows()` stays, but rebuilds from the row list**, not from `Value()`.
  Keeping it matters beyond the empty rows: destroying and recreating a row is
  what currently resets `PugTextEffectMenuOption.isValueText`, which
  `OnActivated` flips to `true` and nothing else reverts.
- **Commit assembles the value from the rows**, dropping empty ones.

`ListTokenizer` is untouched and keeps dropping empties. Its contract narrows
rather than changes: it describes how a *stored value* becomes an initial row
list, and is no longer a statement about what is on screen. The four callers it
unified stay unified.

Each row carries the token it was seeded with (null for rows the user added).
This is what § 6 needs, and it exists only because rows now hold state at all.

## 5 · The field frame

**Assets.** `field_border` and `field_focus` enter the atlas with their own
`borderOverride` and `internalIds` entries — the existing ids run to `100008`,
so the two new ones continue at `100009` and `100010`. The sheet is regenerated
with `pixaki_to_sheet.py`; the atlas may grow, which is fine — nothing
references it by pixel coordinate.

**Prefab (Unity Editor).** Under `ItemTemplate`:

- a new child **`Border`** with a `SpriteRenderer` (`field_border`, Draw Mode
  *Sliced*, width `25` — the value `maxWidth` and `UpdateClickCollider`'s
  `RowContentWidth` already agree on — and a height chosen in the Editor to
  enclose the row's text),
- the existing **`SelectedMarker`** — today a bare Transform — gains a
  `SpriteRenderer` (`field_focus`).

This mirrors CK's own `sessionIP`, where `border` and `selectedBorder` are
children of the field. Both renderers need the built-in `Sprites-Default`
material and `VisibleInsideMask`, and both need their own, larger Z than the
text: the UI camera sorts transparents by Z, and a frame at equal Z sorts in
front and dims the text grey.

`dontAllowNewLines` is set to `1` on the same component in the same session —
CK sets it on every single-line field, and without it a pasted newline survives
into a token.

**Code.** `ListDetailItem` gains two serialized `SpriteRenderer` fields and
switches them in `Bind` via `.enabled = !readOnly`. Deliberately `.enabled` and
not `SetActive`: the base class drives `selectedMarker`'s GameObject with
`SetActive` on select/deselect, and fighting it over the same flag would be a
race. Field names must not collide with the base class — the same CS0108
shadowing trap that is why `readOnly` is not redeclared on this class.

The existing hover suppression in `OnDeselected` needs no change; it already
prevents the marker from flickering when the mouse crosses other rows, and only
becomes visible once a renderer exists.

## 6 · Truncation guard

`RadicalMenuOptionTextInput.Update` trims any text wider than `maxWidth` one
trailing character per frame, for every active row, edited or not. Commit
assembles from `GetInputText()` of all rows. A foreign token wider than the
field is therefore shortened on display and can be written back in that shortened
form when any other row is committed.

**The fix:** when assembling, a row that was never edited contributes its seeded
token, not its displayed text. "Never edited" means no commit has fired for that
row this session — the row-level state, not a text comparison, since a text
comparison cannot tell a user's deletion from `Update`'s trim. Rescuing the
value is preferable to blocking the commit: the user's actual edit still lands,
and the untouched neighbour is carried through intact.

Included here rather than deferred because the seeded-token state it needs is
introduced by § 4 anyway. Whether real tokens ever exceed the width is unmeasured
(see § 9).

## 7 · Out of scope

- **Horizontal scrolling in a text field** — now its own roadmap entry.
  `maxWidth` is capacity, not a viewport: `AppendString` reverts an overlong
  insertion outright, and `Update` trims from the end every frame. A viewport
  requires `maxWidth = 0` plus hand-built masking and caret-following.
- **Per-row delete button** — deliberately later. The geometry is prepared:
  the frame spans the field, not the row, so the button attaches beside it.
- **`Shake()` on the comma strip** — the silent discard is real, but the fix is
  a behaviour change (feedback must move from commit to typing, since commit
  destroys the row), not a field flip.

## 8 · Edge cases

- **Every row empty.** The assembled value is empty, exactly as today when all
  tokens are deleted. `ListKindStore` keeps the entry classified as a list, so
  it does not silently revert to a read-only `Info` row on the next open.
- **Add button pressed repeatedly.** Each press appends another empty row. They
  are all skipped on assembly; none reaches the config file.
- **A row emptied by the user.** Identical to a row added empty — it stays
  visible, is skipped on assembly, and is gone after reopening.
- **Read-only list.** No add button, no frames; rows stay navigable for reading,
  as they are today.

## 9 · Verification (manual, in-game)

No automated tests exist in this repo. Against a writable foreign list
(PlacementPlus' item exclusion list is the live case):

1. Every token row shows the resting frame; the selected row additionally shows
   the focus frame; `+ Add` shows neither.
2. Add button inserts an empty row above itself and selects it, without opening
   the on-screen keyboard.
3. An empty row survives editing a different row.
4. Closing and reopening the drill-in shows a compact list — no empty rows.
5. A read-only list shows neither frames nor an add button, and stays navigable.
6. Frames clip correctly against the viewport mask while scrolling, and the text
   stays bright (correct Z).
7. A pasted newline is swallowed.
8. **Measure** the character count at which `Update`'s trim engages, and confirm
   an over-long foreign token is displayed shortened but written back intact.

## 10 · References

- `docs/adrs/002-list-widget-drill-in.md`, `003-list-widget-editing.md` — the
  drill-in and its editing model.
- `docs/roadmap.md` § "General List-widget UX", § "Horizontal scrolling in a
  text field".
- CK vanilla: `Join Game Menu.prefab` (`sessionIP` subtree),
  `RadicalMenuOptionTextInput` in the decompiled `Pug.Other` (game 1.2.1.5 —
  class and member names are stable, line numbers are not).
