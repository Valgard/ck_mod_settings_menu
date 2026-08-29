# Manual test plan

This mod has no automated tests: it is a UI layer over Core Keeper's own menu
framework, and almost everything that has broken here broke in the running game
while the build stayed green. What follows is the walk that catches it.

**Every check below exists because something failed it once.** Where the reason
is not obvious from the check itself, it is written down — a checklist whose
items look arbitrary gets skipped, and the items that get skipped are the ones
that were expensive to learn.

## Running the fixtures

The list drill-in is exercised against dev fixtures rather than a real foreign
mod's config, so a failed check never damages someone else's settings file:

```bash
MOD_DEV_FLAGS=TestFixtures ../utils/build.sh
```

Without that flag the fixtures do not exist and none of the list checks below
can run — a normal build must never ship them.

They are created through a raw CoreLib `ConfigFile` **outside**
`ConfigStore.ForMod`, so `ConfigStore.IsOwn` does not recognise them and
`ForeignConfigDiscovery` treats them exactly as it treats a third-party mod.
They are not an imitation of the foreign path; they are that path with a file we
own. The file to inspect after a write:

```
<bottle>/drive_c/users/crossover/AppData/LocalLow/Pugstorm/Core Keeper/Steam/<user-id>/mods/TestListFixtures/config.cfg
```

| Fixture | Value | What it is for |
|---|---|---|
| `Short` | `Alpha, Beta, Gamma` | three rows: first, middle and last edge cases in one list |
| `Long` | `Item01 … Item20` | scrolling, scroll-follow, movement across the visible edge |
| `LongReadOnly` | same, view-only | the read-only path — no editing affordances at all |
| `ShortRestart` | `Alpha, Beta, Gamma`, restart-flagged | the restart prompt |
| `Overlong` | `Before, <58-char token>, After` | a token wider than the field, and its untouched neighbours |
| `ProseNotAList` | `This is a long sentence, and another one` | prose that must NOT be taken for a list |
| `WithSpaces` | `Item One, Item Two, Big Chest` | tokens containing spaces |

## Before anything else

- [ ] The mod loads: `Player.log` has no `CompileFailed`, no exception from this
      mod, and the Options menu shows **Mod settings**.
- [ ] Do **not** open the in-game Mods menu at any point — it triggers a mod.io
      sync that deletes the fake-ID dev install.

## The list drill-in

### Layout and rendering

- [ ] Every row shows its text field plus three buttons: ↑, ↓, and a trash can.
- [ ] The trash can shows **both** its body and its lid. They are separate
      sprites at different z; if the lid is missing, their z has collided.
- [ ] All three glyphs are visible. An invisible glyph on a visible frame means
      the glyph renderer is on the wrong sorting layer — the frames are usually
      duplicates (which inherit it) and the glyphs newly created (which do not).
- [ ] Nothing overhangs the box; the buttons are clipped by the viewport mask
      like the rows are, and scroll with them.

### Mouse

Run these even when the keyboard checks pass. **The mouse is the only input that
exercises the click colliders**, and those are built by hand here: CK creates a
menu option's collider from its rendered text, an icon-only button has no text,
and the base implementation would dereference null on one. A row with an empty
text field once had a zero-height collider and nobody noticed, because keyboard
and controller reach such a row regardless.

- [ ] Click each of the three buttons on a middle row — each responds.
- [ ] Click a button on the first row and on the last row.
- [ ] Click a greyed-out edge arrow — nothing happens.
- [ ] Hover across rows while a row's text field is being edited — the edit is
      not stolen or ended by the hover.
- [ ] Move the pointer around **over a row's text field**: the menu sound plays
      at most once, not on every movement. A repeating sound means something is
      re-selecting the same row each frame — CK plays it on the *attempt*, not
      on a change, so a no-op selection is audible.
- [ ] A mouse click on a button plays **no** sound of its own, and that is
      correct. There is only one menu sound in the game and nothing plays it on
      a successful activation; `UIMouse`'s click path never reaches the code
      that would. Vanilla menu options are silent on mouse activation too, so a
      "missing click sound" here is not a defect to chase. Any sound you do hear
      around a click came from a *selection* moving, subject to a shared 50 ms
      cooldown that silently drops calls made too close together — which is why
      the same click can sound one time and not the next.
- [ ] With a button focused, hover back onto that row's field: the button gives
      up its focus marker and the field takes it.

### Keyboard and controller

- [ ] Up/down walks the rows and wraps at both ends, including onto and off the
      trailing **Add entry** button.
- [ ] **After a wrap, the very next press moves.** If it takes two presses, the
      row is reading a selection state the framework updated on a different
      path.
- [ ] Right from the text field enters the buttons; left walks back to the
      field.
- [ ] Right on the **last** button does nothing, and does not jump back to the
      first. The horizontal chain is deliberately open at both ends — CK's own
      convention for a short button row, unlike the rows themselves, which wrap.
- [ ] Up/down **while a button is focused** changes rows, and reaches the Add
      button from the last row.
- [ ] **Enter/Space on a focused button triggers that button** — moves or
      deletes — and does not open the row's text field. Activation runs on the
      menu option, which is the row; the row has to hand it on.

### The focused column

The column the navigation is in is screen state, not row state. These checks are
all about it not leaking.

- [ ] Standing on a row's ✕, press down twice: the selection lands on the ✕ of
      the next two rows, never in their text fields.
- [ ] Move a button-focused selection down, arrow **left** back into that row's
      field, then move down again: the next row selects its **field**, not a
      remembered button.
- [ ] In `Short`, focus a button, leave with Escape, then open `Long`: the first
      navigation there lands in a text field. (The drill-in screen is a
      singleton reused for every list.)
- [ ] Press **Add entry** while a button column is active: the new row is
      selected at its **field**.

### Switching between mouse and keyboard

Three separate defects have lived here, all of the same shape: state that
directional input needs applied while a pointer was driving. A pointer names its
target every frame and needs no memory; carrying one for it makes the two fight.
Run these with **both** devices in reach, alternating deliberately.

- [ ] Navigate to a button with the keyboard, then move the mouse: the pointer
      takes over without the selection snapping back.
- [ ] Reorder a row **with the mouse**: the selection stays where the pointer
      is, rather than following the moved entry. (With the keyboard it must do
      the opposite — see Reordering.)
- [ ] After a mouse-driven reorder, click again without moving the pointer: the
      button under the cursor responds on the first click.
- [ ] Hover a row, then navigate away with the keyboard and back: no remembered
      button hijacks the entry.

### Reordering

- [ ] ↑ on the middle row of `Short` swaps it with the row above; the file
      reflects the new order.
- [ ] Pressing ↑ repeatedly walks the same entry upward without the selection
      leaving the arrow.
- [ ] First row's ↑ and last row's ↓ are visibly dull and do nothing.
- [ ] In `Long`, walk `Item20` upward past the top of the visible area — the
      view follows the selection.
- [ ] In `Overlong`, move the long token. `Before` and `After` come back
      **byte-identical** in the file: an untouched row must contribute what it
      was seeded with, never what fits on screen.

### Deleting

- [ ] ✕ on a filled row opens a confirmation naming that entry.
- [ ] **The confirm button must be held, not tapped** — a progress bar fills
      while it is held and runs back down on release. Vanilla pairs this caption
      with a hold in both places it uses it, and a tapped delete behind a
      "delete" caption reads wrong to anyone who knows the game's own dialogs.
- [ ] Cancel leaves both the list and the file untouched.
- [ ] Release the hold early: nothing is deleted.
- [ ] Confirm removes the entry; the selection lands on the ✕ of the row that
      moved up (on the last row, its predecessor).
- [ ] Press **Add entry**, then ✕ on the new blank row: **no dialog**, the row
      disappears, and the file is untouched — a blank row never reached it.
- [ ] Delete the only remaining entry: no crash, and nothing selected out of
      range.
- [ ] In `WithSpaces`, delete `Item Two`: the dialog shows the token including
      its space, and the remainder reads `Item One, Big Chest` — no token is
      re-split on the space.

### The editing tint

- [ ] Activate a row's field, change nothing, and leave it: the vivid editing
      colour goes away. It marks "being edited", and nothing else reverts it —
      for a long time it survived until the next rebuild, which only happens
      when a value actually changed.
- [ ] Activate a field, change something, commit: the tint is gone afterwards
      here too (this path rebuilds the row anyway).

### Read-only

- [ ] `LongReadOnly` shows no ↑/↓/✕ and no Add button.
- [ ] Its rows are still navigable for reading, and scroll normally.
- [ ] No row ever enters edit mode.

### Restart flag

- [ ] In `ShortRestart`, **reorder** two entries and leave the settings screen —
      the restart prompt appears.
- [ ] Same for a **delete**. Both change the stored value as much as typing
      does, and all three paths must raise the flag.
- [ ] In `Short` (no restart flag), neither operation produces a prompt.

### Classification

- [ ] `ProseNotAList` still renders as a read-only info row with no drill-in.
      This slice must not move the list/prose heuristic.

## After the walk

- [ ] `TestListFixtures/config.cfg` carries, for every fixture touched, exactly
      the order shown on screen — no stray commas, no empty tokens.
- [ ] `Player.log` holds no exception and no warning from this mod.
