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
| `Overlong` | `Before, AncientGuardianStatueFragmentPolishedObsidianVariantLarge, After` | a 57-character token, far wider than the field, between two short neighbours |
| `ProseNotAList` | `This is a long sentence, and another one` | prose that must NOT be taken for a list |
| `WithSpaces` | `Item One, Item Two, Big Chest` | tokens containing spaces |

The same flag also declares five lists through the **public consumer API**, in
this mod's own section (`AddDeclaredListFixtures`). They are the other path
entirely: discovery can only ever declare a `FreeText` list, because a heuristic
cannot know an entry set is closed. So `OrderOnly` exists nowhere else, while
`ReadOnly` is reachable both ways — and the two are not interchangeable, since
only the declared one reconciles its defaults and only the scoped one is skipped
by a section reset.

| Fixture | `ListEditing` | What it is for |
|---|---|---|
| `testListFreeText` | `FreeText` | the declared path reaching the same behaviour discovery produces |
| `testListOrderOnly` | `OrderOnly` | six entries: reordering with no add row, and the arrow columns |
| `testListOrderOnlySingle` | `OrderOnly` | one entry — the row with no neighbour to wrap to |
| `testListReadOnly` | `ReadOnly` | declared inert, as opposed to inert through a scope |
| `testListOrderOnlyEmpty` | `OrderOnly` | no entries and no way to add one — the drill-in must refuse to open |
| `testListReadOnlyDuplicate` | `ReadOnly` | a repeated default — both reconciliation branches must keep only the first |

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
- [ ] **Hovering a row button plays the selection sound**, like every other
      control in this menu. A control that stays silent while its neighbours
      answer reads as broken even when it works.
- [ ] **Clicking a row button plays the activation sound** — the lower of the
      two pitches, and the same one Enter on that button produces. Compare the
      two back to back: they must match.
- [ ] **A greyed-out edge arrow stays silent** (first row's ↑, last row's ↓).
      It does nothing, so it acknowledges nothing.
- [ ] With a button focused, hover back onto that row's field: the button gives
      up its focus marker and the field takes it.

### Keyboard and controller

- [ ] Up/down walks the rows and wraps at both ends, including onto and off the
      trailing **Add entry** button.
- [ ] **After a wrap, the very next press moves.** Walk off the end onto the
      Add entry button and straight on into the list, then keep going in the
      same direction — every press must advance by one. A press that does
      nothing is easy to dismiss as a missed key, which is exactly why it went
      unnoticed once already: hold the direction and count the rows.
- [ ] Right from the text field enters the buttons; left walks back to the
      field.
- [ ] Right on the **last** button does nothing, and does not jump back to the
      first. The horizontal chain is deliberately open at both ends — CK's own
      convention for a short button row, unlike the rows themselves, which wrap.
- [ ] Up/down **while a button is focused** changes rows, and reaches the Add
      button from the last row.
- [ ] Up/down **while a button is focused** scrolls the list once the selection
      passes the visible edge — exactly as it does from a text field. A column
      that navigates but does not scroll walks the selection off-screen and
      leaves the player pressing keys blind.
- [ ] Wrapping across the **Add entry** button does not carry the column: coming
      back into the list lands on whichever control is nearest, not on the one
      you left from. Known and accepted — the add button is a single control, so
      there is no column for it to hand on. Note it if it ever gets worse than
      "lands somewhere sensible".
- [ ] **Enter/Space on a focused button triggers that button** — moves or
      deletes — and does not open the row's text field. Getting this wrong is
      not subtle: you press Enter meaning to delete and end up typing in the
      entry instead.

### The focused column

Moving between rows must not move the player sideways. Every failure here has
looked the same from the outside: the selection quietly ends up on a different
control than the one it was on, so the next keypress does something other than
what was intended. It never crashes and it is easy to miss when testing one
press at a time — walk several rows in each case.

- [ ] Standing on a row's ✕, press down twice: the selection lands on the ✕ of
      the next two rows, never in their text fields.
- [ ] Move a button-focused selection down, arrow **left** back into that row's
      field, then move down again: the next row selects its **field**. Once you
      have deliberately left the buttons, nothing may pull you back into them.
- [ ] In `Short`, focus a button, leave with Escape, then open `Long`: the first
      navigation there lands in a text field — a column chosen in one list must
      not follow you into an unrelated one.
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
- [ ] Pressing ↑ four times moves **one entry four rows up**. If the entry
      stops after the first press and the selection starts travelling instead,
      reordering has become a two-handed operation — press, steer back, press.
- [ ] Walk an entry to the very top, so its ↑ greys out: the selection stays on
      that arrow. From there ↓ and ← must still work — a selection that cannot
      rest on a dulled control leaves everything below it unreachable.
- [ ] First row's ↑ and last row's ↓ are visibly dull and do nothing.
- [ ] In `Long`, walk `Item20` upward past the top of the visible area — the
      view follows the selection.
- [ ] In `Overlong`, move `AncientGuardianStatueFragmentPolishedObsidianVariantLarge`.
      `Before` and `After` come back **byte-identical** in the file: an
      untouched row must contribute what it was seeded with, never what fits on
      screen.

### Deleting

- [ ] ✕ on a filled row opens a confirmation naming that entry.
- [ ] **The confirm button must be held, not tapped** — a progress bar fills
      while it is held and runs back down on release.
- [ ] **The bar starts empty**, and it does so at **every** list length. Check
      at least 4, 5 and 6 entries: this once failed at 4, worked at 5 and failed
      again at 6, so a single list size proves nothing. A bar that is already
      full still requires the full hold, which reads as the dialogue being
      broken rather than as a display fault. Vanilla pairs this caption
      with a hold in both places it uses it, and a tapped delete behind a
      "delete" caption reads wrong to anyone who knows the game's own dialogs.
- [ ] Cancel leaves both the list and the file untouched.
- [ ] Release the hold early: nothing is deleted, **and the dialogue stays
      open** with the bar run back down. Letting go is a pause, not a cancel —
      dismissing the dialogue on release would make an accidental twitch cost
      the whole decision.
- [ ] Confirm removes the entry; the selection lands on the **✕** of the row
      that moved up (on the last row, its predecessor) — so several entries can
      be deleted in succession without walking back to ✕ each time.
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
- [ ] **Open it with the mouse, then press ↑ as the very first input.** Not ↓ —
      the two enter the list from opposite ends, and only this end once landed
      on an invisible control that then swallowed every further keypress until a
      mouse hover rescued it. Nothing looked selected and nothing was logged.
      Then keep navigating: every direction must keep working.
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

## The declared list path

These run against the four fixtures in this mod's **own** section, not the
detected one. They exist because every check above enters through discovery, and
the two paths reach the drill-in with different things known about the value.

### `FreeText` — the same as a detected list

- [ ] `testListFreeText` behaves exactly like `Short` above: rows editable, add
      row present, ↑/↓ and ✕ on every row. Anything that differs here is a
      difference between the two paths, which is what this fixture is for.

### `OrderOnly`

- [ ] No add row at the bottom of the screen at all.
- [ ] Every row shows ↑ and ↓ and **no** ✕.
- [ ] Activating a row does **not** enter edit mode — no caret, no keyboard
      capture, and the on-screen keyboard never appears on a controller.
- [ ] ↑/↓ still reorder, and the value in `config.cfg` follows.
- [ ] **The selection travels with the entry, one row per press.** Press ↓ on a
      middle row: the marker sits on the ↓ of the row one lower, i.e. still on
      the entry that just moved, so pressing again moves the same entry once
      more. Then the same upward. Both directions failed this once, and in
      different ways — ↓ appeared to leave the selection behind, ↑ appeared to
      skip a row — because the add row, hidden at this level, was still sitting
      at child 0 of the container that the landing step indexes by row number.
      Checking only the stored value passes while this is broken.
- [ ] Down from the bottom row wraps to the top row **in the same column**, and
      up from the top row wraps to the bottom — there is no add row to pass
      through, so the column cycles onto itself.
- [ ] Walk down through every row with a button focused: the selection never
      lands on an invisible element and never gets stuck. (A ✕ left in the
      navigation chain while switched off would show up exactly here.)
- [ ] `testListOrderOnlySingle`: up and down from its single row do nothing at
      all — no movement, and no selection sound.

### `ReadOnly`

- [ ] `testListReadOnly` shows every entry, with no add row and no row buttons.
- [ ] It looks and behaves like `LongReadOnly`, which reaches the same state
      through a `ViewOnly` scope instead of a declaration.
- [ ] **The declared order wins here, unlike at `OrderOnly`.** Reorder the
      declared defaults of `testListReadOnly` in `AddDeclaredListFixtures`,
      rebuild, reopen: the new order shows. Nobody can reorder this list in
      game, so a stored order would otherwise outlive every release.

### Duplicates and hand-edited case

- [ ] `testListReadOnlyDuplicate` shows **two** rows (`Alpha`, `Beta`), not three,
      and `Player.log` names the duplicate at declaration time. Both branches of
      the reconciliation collapse duplicates; they did not always agree on that.
- [ ] **A differently-cased entry keeps its place.** In `config.cfg`, change one
      middle entry of `testListOrderOnly` to lower case and relaunch: it must
      still sit where it was, spelled as the mod declares it — not dropped and
      re-appended at the end. This is the only route a player has to these
      entries outside the menu, so losing their position here would undo exactly
      what `OrderOnly` is for.

### A list that cannot work

- [ ] Opening `testListOrderOnlyEmpty` does **nothing** — no screen is pushed,
      and `Player.log` says the list has no entries and no way to add one.
- [ ] Try it with a **controller** as well, and with the keyboard, since the
      three crash routes this guards differ by input device: `base.Activate()`
      fires before any key is pressed, left/right go through `SkimLeft`/
      `SkimRight`, and only up/down reach the guard inside this screen.
- [ ] The row itself still renders in the settings screen and previews
      `(empty)` — refusing the drill-in must not make the setting vanish.
- [ ] **The row does not pretend to be activatable.** Selecting it plays no
      activation sound, and the footer hint bar offers no select prompt. A row
      that answers a press with silence and a log line is worse than one that
      visibly cannot be pressed.
- [ ] **The other half of the guard — a list emptied through a *scope*, not a
      declaration.** This is the route the guard sits in `Open()` for, and no
      declared fixture can reach it. Open `LongReadOnly` once so it is
      classified, quit, edit `TestListFixtures/config.cfg` so `LongReadOnly`
      has no value, relaunch: the row must still be a list row, preview
      `(empty)`, and refuse to open. Here refusing is the only defence — the
      value is read-only through its scope, so the player cannot repair it from
      the screen.

### Defaults reconciled at bind

Needs two builds, and is the only group here that cannot be done in one session.
It runs in **both** directions, and the removal half is the one that was missing
at first: appending alone left an entry the mod had stopped declaring stuck in
the player's file forever, because neither side can delete it at these levels.

- [ ] Edit the order of `testListOrderOnly` in game, quit, and confirm
      `config.cfg` holds the new order.
- [ ] **Added default:** add an entry to that fixture's declared defaults in
      `ModSettingsMenuMod.AddDeclaredListFixtures`, rebuild, and reopen. The new
      entry is present, **at the end**, and the order you set is otherwise
      untouched.
- [ ] **Removed default:** delete one of the middle entries from those same
      declared defaults, rebuild, and reopen. It is **gone** from the list and
      from `config.cfg`, the remaining entries keep the order you gave them, and
      `Player.log` names the dropped entry.
- [ ] Do both again for `testListFreeText`: neither must change anything —
      that list can add and delete entries itself, so reconciling it would
      overrule the player.

### The section reset, which is the only escape hatch

`OrderOnly` has no delete button, and both the README and the code point at the
section reset as the way back. It is worth one pass because the two read-only
routes behave *differently* here, which nothing on screen shows.

- [ ] Reorder `testListOrderOnly`, then reset this mod's section from the footer
      hint: the declared order comes back.
- [ ] The same reset rewrites `testListReadOnly` (declared read-only, so
      `SettingDef.ReadOnly` is false and it is in scope) but leaves
      `LongReadOnly` untouched (read-only through a `ViewOnly` scope, which the
      reset skips).

## After the walk

- [ ] `TestListFixtures/config.cfg` carries, for every fixture touched, exactly
      the order shown on screen — no stray commas, no empty tokens.
- [ ] `Player.log` holds no exception and no warning from this mod.
