# Per-row buttons in the list drill-in: delete and reorder

- Date: 2026-08-28
- Scope: `ListDetailScreen`, `ListDetailItem`, the drill-in prefab, one new sprite

Decompile references are against game 1.2.1.5. Class and member names are
stable; **line numbers are not** — they are given as a starting point, not as
an address.

## The problem

**There is no way to delete a list entry, and no way to reorder one.** The
drill-in offers edit and add; removal and ordering have no control at all.

Removal was promised as a later step when the row model was reworked
(2026-08-22) and deliberately left out of that slice; ADR-005 records it as not
adopted. Reordering was never available: insertion order is fixed, which is
fine for an exclude-set and wrong for a priority list — and `auto-rail-bridges`
shipped 1.0.0 with a hardcoded bridge order waiting on exactly this.

**What happens today instead of deleting is not a substitute, and it used to
look like one.** Clearing a row's text takes the token out of the stored value,
because `OnRowTextCommitted` derives the value through `ListTokenizer.Join`,
which skips empties. But since ADR-005 the row itself stays on screen for the
rest of the session — the screen owns its rows, and an empty row is a
legitimate working state there. So the gesture that used to double as "delete"
(the row vanished at the next rebuild) no longer gives that feedback: the entry
is gone from the file while the row still sits there, and the list only looks
right again after closing and reopening. Whichever way a user reads that, one
half of it is wrong.

## Decisions

### Delete and reorder ship together

The row width has to carry all three buttons or none, and the prefab cannot be
edited in two halves without redoing the same geometry twice. Building delete
alone would mean a second Editor session and a second in-game verification pass
on the same row — frame, mask, collider fallback and viewport width all move
with it. Preparing the prefab for three and wiring only one was rejected on the
opposite ground: the intermediate state is visibly broken either way, showing
two dead arrows or 50 px of unexplained empty row.

The code may still land in more than one commit. The prefab work may not.

### The buttons are visible controls, not hint-bar actions

ADR-004's shape — a footer hint acting on the selected row — costs no geometry,
but it is invisible to a mouse user, and deletion is precisely the operation the
mouse has no other way to reach. Controller reachability came first and is
already done: the drill-in moved to CK's UIElement navigation path on
2026-08-24.

### Reorder is per-row arrows, not a grab mode

Decided 2026-08-24 and unchanged here. A grab mode's only workable mouse form is
drag-and-drop, and Core Keeper's menus have none — dragging exists in the
inventory, a different system entirely. A control the mouse cannot reach is the
wrong default for a screen mouse users also open.

Worth keeping in mind rather than re-deriving: a grab mode would need no prefab
change at all if built on `SelectNextIndex`/`SelectPrevIndex` overrides, which
remains the cheapest way to reinterpret up/down on the index path should a later
feature want one.

### Deleting a non-empty row asks; deleting an empty one does not

The difference is not arbitrary — it is exactly the difference between "there is
something to lose here" and "there is not". An empty row never reached the
owning mod's config file, so removing it is inconsequential. A filled one holds
text the user typed, and the write path leads straight into a **third-party**
mod's config; the only recovery is the section reset, which restores the whole
section.

The confirmation reuses the pattern `ModSettingsScreen.ConfirmReset` already
runs: `Manager.menu.centerPopUpText.StartNewDisplaySequence` with two options
and a `PopupResponse.IsConfirm` callback.

**No `holdToConfirm`.** The flag exists (`StartNewDisplaySequence`'s parameter,
~342074, forwarded to `popUpYesOption.SetHoldToConfirm`, ~342120) and turns the
yes-option's activation into a one-second hold with a progress bar rather than
an immediate trigger. CK reserves it for two places, both unrecoverable losses
of playtime: deleting a character (`SaveSlotDeleteOption`, ~343874) and deleting
a world. The nearer comparison is `Menu/ResetToDefaultsDialog`, CK's own
settings reset, which passes `holdToConfirm: false` — as does this mod's own
section reset. A single list entry must not weigh more than resetting an entire
mod's section.

What the dialog does bring for free is `accidentalInputBlockDuration`, one
second by default, during which the yes-option reports `CanBeActivated() ==
false`. That covers the momentum of the click that opened it.

### The add button is unchanged

It keeps appending an empty row on every press. The reason to restrict it was
that empty rows could not be removed until the drill-in closed; the delete
button removes that reason. A button that does nothing when pressed would be a
silent failure — the same defect `ListAddRow` was split into its own type to
avoid.

### No wrap when moving

The first row's up arrow and the last row's down arrow render greyed out and do
nothing. Navigation wraps on this screen and will continue to; moving does not.
Sending the first entry to the end is almost never what was meant, and on a
priority list it is the most expensive possible misfire.

**The greyed-out state copies CK's look but not its mechanism.**
`OptionActiveState.GRAYED_OUT` bundles four effects: red tint, click blocking,
staying in the layout, and being skipped by navigation. The fourth one is broken
on this screen: `SelectIndexInDirection` asks `GetAdjacentUIElement` *before*
filtering, so a locked neighbour yields no match and navigation stalls instead
of stepping over. That applies only on the UIElement path, which is the path
this screen has used since 2026-08-24.

So the edge buttons stay navigable and merely look and behave as disabled. That
is also the better answer on its own terms: a button that cannot be reached
cannot explain why it does nothing, while a visibly disabled one turns an
otherwise silent no-op into a statement.

## Geometry

Everything derives from `spritePixelsToUnits: 16` in the atlas — 1 unit = 16 px,
`filterMode: 0`, so every position must land on a whole pixel.

| | |
|---|---|
| Row today | 22 × 1.5 units = 352 × 24 px |
| Button | 24 × 24 px, the full row height, flush with the field frame |
| Glyph inside it | 16 × 16 px (`field_border` is 16×16 with `border: [4,4,4,4]`) |
| Three buttons + 1 px gaps | 74 px |
| Gap to the field frame | 12 px (CK's own spacing in `SaveSlot.prefab`) |
| Taken from the row | 86 px = 5.375 units |
| Field becomes | **16.625 units** |

CK's own delete button is 16 × 16 inside a 32 px row, i.e. deliberately smaller
than the row — the one vanilla argument for a smaller button here. It was
weighed against a flush 24 px one and lost on legibility: at 16 × 16 the frame
leaves an 8 × 8 glyph, half of what `ToggleListView` / `TogglePlainView` already
use in this very atlas.

**`ListDetailItem.maxWidth` needs no adjustment.** The roadmap's table said
`21 → ~15.5`; that entry predates ADR-007. The prefab now carries `maxWidth: 0`
throughout, because the field mask defines the visible window. Nothing derives
from it any more.

**Two values follow the Editor change on their own.**
`TextFieldViewport.Bind` reads `_fieldWidth` from `fieldMask.transform.localScale.x`
and `_fieldOriginX` from its position; `ListDetailItem.UpdateClickCollider`
derives the collider through `FitColliderToFrame`. Both were built as
derivations after an earlier hardcoded copy went stale the moment the frame
moved. Narrowing the field is therefore an Editor change, not a code change.

## Prefab

Authored in the Unity Editor, not in YAML: a `-batchmode` build reserializes and
drops hand-authored objects or nulls their references.

- `ItemTemplate`: `Border` and `FieldMask` narrowed to 16.625 units, both shifted
  left by half the difference so the left edge stays put.
- Three new child objects per row, each with a frame `SpriteRenderer`, a glyph
  `SpriteRenderer` and a `SelectedMarker`. Material `Sprites-Default` (10754) and
  `m_MaskInteraction: VisibleInsideMask` — the defaults are wrong on both counts
  and produce a button that is either invisible in the AssetBundle or overscrolls
  the box.
- `handleNavigationInternally: 1` on the row (currently `0`).
- The in-row chain `left`/`rightUIElements` between field and buttons, wired in
  the prefab because siblings of one template keep their references through
  `Instantiate`.

Chrome comes from sprites already in `ui_chrome`: `field_border` as the resting
frame, `field_focus` as the selection marker — the same pair the row itself
uses, and the same resting/selected split CK's save-slot button has.

**One new sprite is needed and blocks the slice.** The arrows reuse the existing
`Arrow` rotated by ±90°, which is lossless under point filtering. The delete
glyph is new, must be drawn into `sources/msm_ui_chrome.pixaki`, and the
`utils/pixaki_*` tools are read-only — there is no write path into the master.
It then needs its `pad` entry and a pinned `internalIds` number in
`sources/msm_ui_chrome.json`; the next free one is `100011`. Without the pin the
id is re-derived at the next cut and the prefab reference is orphaned, which
shows up as an empty patch of UI rather than as an error.

## The click collider

**`clickCollider` cannot live in the prefab.** It is declared
`protected UnityEngine.BoxCollider clickCollider;` (~343064) with no
`[SerializeField]`, so Unity does not serialize it — it appears in neither this
mod's prefab nor CK's own `SaveSlot.prefab`. It exists only at runtime, created
by `InitClickCollider` from `Awake` (~343153).

Two consequences, opposite for the two row kinds:

- **The row keeps deriving its collider.** `RadicalMenuOptionTextInput.Update`
  (~343376) does not call `base.Update()`, so `UpdateClickCollider` never runs on
  its own; `ListDetailItem` calls it explicitly and overwrites width, height and
  centre from the frame. That stays exactly as it is — there is no prefab
  alternative to compare it against.
- **Each button needs two overrides.** `InitClickCollider` only creates a
  collider when `labelText` or `valueText` is set (~343161), and a glyph button
  has neither, so it must create its own. And `UpdateClickCollider` (~343174)
  must not call `base`: the base branch picks `valueText` when `labelText` is
  null, so with both null it dereferences null. It also sets
  `clickCollider.enabled` from the active state, which the override then has to
  do itself.

The underlying reason is worth stating once: in CK's menu framework the click
collider is not layout data but a **derivation from rendered text** — created
from it, measured from it, enabled from the activation state, every frame. A
menu option is at heart a piece of text. A button that is only a picture falls
out of that model and has to supply both ends itself.

## Navigation and focus

`handleNavigationInternally: 1` gives the row first refusal via
`NavigateInternally(Direction)`: left/right it handles itself, walking between
field, ↑, ↓ and ✕; up/down it declines, and the existing cyclic row chain
applies unchanged. The pattern is CK's own player list (~331681) — ask
`GetAdjacentUIElement` on the selected child, then `Select()`.

**CK also supplies the hook for remembering which child was focused.** The same
player list overrides `GetInternalOption()` (~331672) to return its
`lastSelectedPlayerButton`, falling back to the base answer when there is none —
so "which child does this row hand focus to when entered" is a question the
framework already asks, not one to invent. The slot restored after a rebuild is
therefore delivered through that override rather than by selecting a child by
hand. The row-level field CK caches in cannot survive here, because the row is
destroyed on every rebuild; the screen's `_pendingSelect` holds it instead and
seeds the fresh row.

**The focus has to survive the rebuild, and that is the real intervention.**
`_pendingSelect` currently remembers an index into `menuOptions`, i.e. *which
row*. Reorder and delete both trigger a rebuild, and `RebuildRows` is a full
teardown-and-recreate — ADR-005 requires it, because destroying a row is the
only thing that resets `PugTextEffectMenuOption.isValueText`. So the remembered
selection needs a second component, *which slot within the row*.
`_pendingSelect` becomes a small value type carrying both, so "row without slot"
cannot exist as an intermediate state. Its existing special case is unchanged:
`-1` still means "same numeric slot, clamped".

Two behaviours follow from it:

- **After a reorder the selection follows the row and stays on the same arrow**,
  so a further press continues the movement. Otherwise moving an entry four
  places would cost eight inputs instead of four.
- **Changing rows keeps the slot.** Standing on a ✕ and pressing down lands on
  the ✕ below, not in the text field — the same expectation any table sets, and
  "clear out several entries" is the case the button exists for.

**Reorder goes through the rebuild, not through a text swap.** Re-seeding only
the two affected rows would be cheaper and would leave the focus physically in
place, but every other write path on this screen defers through
`_rebuildPending`, and a second, shortcutting path would have to swap both rows'
`RowIndex` bindings by hand — the kind of special case ADR-005 split the types to
be rid of.

## Code

**One button type, not three.** `ListRowButton : RadicalMenuOption, IListRow`
with a serialized `Role` enum (`MoveUp`, `MoveDown`, `Delete`). The three share
frame, focus marker, collider creation, collider measurement and height
reporting, and differ in a single branch in `OnActivated`.

This is deliberately the opposite call to ADR-005, and the distinction holds
there: that split separated two types because three fields (`kind`, `rowIndex`,
`readOnly`) had to agree with nothing enforcing it — reconcilable state that can
drift. Here one field selects a constant and is reconciled with nothing. Three
classes would be three copies of the same collider overrides, and keeping those
in step is precisely the failure ADR-005 set out to make unrepresentable.

**The logic lives in the screen, not in the button**, exactly as `ListAddRow`
already calls `_owner?.AddEmptyRow()`. `ListDetailScreen` gains:

- `MoveRow(int index, int delta)` — swaps in `_rows`, sets `_rebuildPending`,
  aims `_pendingSelect` at the new position and the same slot.
- `RequestDelete(int index)` — empty row: remove immediately. Filled row: open
  the dialog and act on the response.

**The dialog makes deletion two-phase, which is the delicate part.** Between
"✕ pressed" and "confirmed" sits a pushed popup menu and a callback. The
remembered index must therefore be validated against the same guard this screen
already applies to rows: the `RowGeneration` stamp. A callback arriving one
drill-in session later must not delete into the list that is open by then — the
very hazard the generation was introduced for, reached through a new route. And
the dialog must not be raised synchronously from a `Deactivate` chain.

**Delete and reorder must set the restart flag.** Today `OnRowTextCommitted`
raises it, which is the text-entry path only. Both new operations change the
value just as much and do not pass through there; without this, reordering a
restart-relevant setting would change it with no prompt. The `ShortRestart`
fixture exists to catch exactly this.

**Not built:** no undo, no drag-and-drop, no multi-select. The section reset
remains the coarse recovery path it already is.

## Verification

In game, against the `TestFixtures` dev fixtures rather than a real foreign
mod's config — they are created through a raw `ConfigFile` outside
`ConfigStore.ForMod`, so `ForeignConfigDiscovery` treats them exactly as it
treats a third-party mod. They are not an imitation of the foreign path; they
are that path with a file we own. Build with `MOD_DEV_FLAGS=TestFixtures`.

| Fixture | What it checks here |
|---|---|
| `Short` | Row 1 has a greyed up arrow, row 3 a greyed down arrow, row 2 both live. Deleting a filled row asks; cancelling changes neither list nor file |
| `Long` | Moving across the visible edge — Item20 walking up, with scroll-follow keeping pace, and the slot surviving each row change |
| `LongReadOnly` | None of the three buttons appears, no add button, navigation still unbroken |
| `Overlong` | Moving a token wider than the field, with the untouched neighbours not rewritten — the `CommittedText` guarantee from the other side |
| `WithSpaces` | Delete and reorder on tokens containing spaces; `ListTokenizer.Join` must leave them intact |
| `ShortRestart` | Delete and reorder each raise the restart prompt |
| `ProseNotAList` | Still no drill-in — this slice must not touch the heuristic |

Beyond the table:

- **All three buttons are reachable with the mouse.** The one property keyboard
  and controller do not exercise, and where the collider overrides prove
  themselves. ADR-005 documents this blind spot from the last time it bit: an
  empty row got a zero-height collider and nobody noticed who did not
  deliberately click it.
- After a delete the selection sits on the ✕ of the row that moved up; on the
  last row, on its predecessor.
- `TestListFixtures/config.cfg` carries the order shown on screen after every
  step.

## Open

- The delete glyph has to be drawn before the prefab work can finish.
- Whether the arrows should also be reachable from the field by a single
  left/right press, or only after entering the button group, is a question the
  in-row chain answers in the prefab; it is cheap to change and should be judged
  in game rather than decided here.
