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

They are created through raw CoreLib `ConfigFile`s **outside**
`ConfigStore.ForMod`, so `ConfigStore.IsOwn` does not recognise them and
`ForeignConfigDiscovery` treats them exactly as it treats a third-party mod.
They are not an imitation of the foreign path; they are that path with files we
own. There are two, rendered as two `(detected)` sections, and either may need
inspecting after a write:

```
<bottle>/drive_c/users/crossover/AppData/LocalLow/Pugstorm/Core Keeper/Steam/<user-id>/mods/TestListFixtures/config.cfg
<bottle>/drive_c/users/crossover/AppData/LocalLow/Pugstorm/Core Keeper/Steam/<user-id>/mods/TestChoiceFixtures/config.cfg
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

`TestChoiceFixtures` covers the other thing discovery can produce: a setting
constrained to a fixed set of values. Nothing on this machine produces one
otherwise — every `AcceptableValueList` here lives in a file MSM created, which
`ConfigStore.IsOwn` excludes from discovery.

| Fixture | Value | What it is for |
|---|---|---|
| `ChoiceStrings` | `Medium` of Low/Medium/High | the exact path: `AcceptableValueList<string>` read directly |
| `ChoiceComma` | `Alpha` of three | tokens holding the `", "` separator and a `"` — the quote is what tells the raw read from the escaped one |
| `ChoiceInts` | `4` of 1/2/4/8 | the reconstruction path: tokens parsed back out of the description line |
| `ChoiceEnum` | `Second` of three | the member-name round trip, always ON a member so never the guard |
| `ChoiceFlags` | `Alpha, Beta` | a `[Flags]` combination — the value the guard exists to leave alone |
| `ChoiceSingle` | `Only` | one option: the wrap arithmetic with nowhere to go |
| `ChoiceReadOnly` | `Medium`, view-only | a locked Choice must still display the right token |
| `ChoiceFloats` | `1.5` of 0.5/1.5/2.5 | how *this machine's* culture renders a decimal — outcome depends on it |
| `RangeDouble` | `1.5`, range 0–10 | negative control: an unhandled constraint stays a read-only Info row |
| `RefuseEmptyToken` | `Alpha` | a blank entry in the value list |
| `RefuseUnconvertible` | `1` | a token that is not an `int` |
| `RefuseInvalid` | `Alpha` | a constraint that rejects the values it prints |
| `RefuseSplitValue` | `0.5` | a split that ate the held value — the held-value check |
| `RefuseSplitDuplicate` | `5.0` | the same split with the held value intact — only the duplicate check |
| `RefuseBlankInSet` | `Alpha` | a blank entry in a **real** `AcceptableValueList` |
| `ThrowingConstraint` | `Alpha` | a constraint that throws where MSM asks it a question |

Most of these need a dev-only `AcceptableValueBase` subclass
(`DescriptionOnlyValues`), because CoreLib's own constraints cannot produce the
states they test: its constructor refuses an empty set, no supported type renders
an unparseable token, and its `Clamp` corrects an off-set value at bind. They are
also the only exercise of a third-party subclass, which is a case the code
reasons about and nothing else reaches.

`RefuseBlankInSet` is the exception and the reason it exists: a blank *element*
needs no subclass at all. `AcceptableValueList`'s constructor rejects only a
zero-length array, so `("Alpha", "  ", "Gamma")` binds cleanly and any mod could
ship it by accident.

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
| `testListReadOnlyDuplicate` | `ReadOnly` | a repeated default on the declared-order branch |
| `testListOrderOnlyDuplicate` | `OrderOnly` | the same repeat on the player-order branch — the one the dedupe rewrite touched |
| `testDupKey` | — | one key bound twice with different types: reaches the guarded-bind path with no filesystem fault |

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

### The caret

Keyboard and mouse, because they answer the question differently: typing and word
jumps read the row's own `currentCharIndex` through `API.Reflection`, while a
click has to cross from a pointer position into a character index through the
glyph list (ADR-009). The controller is deliberately out of scope — its text
arrives whole from the on-screen keyboard, caret already at the end.

The two paths fail asymmetrically, which is why both need checking: a glyph-list
fault costs the click alone, while an unreadable counter costs all three, the
click included, since it needs the caret's own index to aim a relative move.

Two fixtures, and the difference matters. `Overlong` for anything that scrolls —
its 57-character token is far wider than the field. `WithSpaces` for the word
jump: `Overlong`'s rows are each a single word, so a jump inside one finds no
boundary and lands on 0, where an off-by-one would be invisible.

- [ ] Press Home, then type `xyz` — the value begins with `xyz`, in the order
      typed. Three characters rather than one: the caret path and the
      append-at-the-end fallback advance the marker by different amounts, and a
      single keystroke looks identical either way.
- [ ] Press Left a few times, then type `xyz` — it lands where the caret is, in
      order. Landing at the end means the counter was not read at all; landing at
      the front means it came back as 0, which is worth reporting.
- [ ] Repeat the first check with **End**. This one is a control, not a proof:
      appending at the end is exactly what the fallback does, so its verdict
      comes from the log check below rather than from the screen.
- [ ] In `WithSpaces`, **Ctrl+Left / Alt+Left from the end of an entry** lands on
      the word boundary; Ctrl+Right walks forward the same way. What this does
      **not** establish: the ordinary word jump behaves identically with and
      without the compensation term ADR-009 removed — that term was correct in
      every case it could see. Only an auto-repeating key in the same frame
      differed, and that is not cleanly provokable by hand. So this guards the
      jump against regression; it does not verify the removal.
- [ ] Click into the middle of a row's text — the caret lands on the character
      pointed at, and the next keystroke goes there.
- [ ] Activate a row and hold **Right** (or press End) until the text has
      visibly scrolled, *then* click near the **right edge**. Without that the row
      rests at offset 0 while still looking full of text, so the check passes
      without ever exercising the offset. A conversion that ignored it places the
      caret several characters from the pointer, and only here.
- [ ] Commit the Home-then-type edit, leave the screen, and confirm
      `TestListFixtures/config.cfg` carries those characters at the front of that
      entry. What appears on screen and what reaches another mod's file are
      separate claims, and only the second one outlives the menu.
- [ ] The log carries **no `[ModSettingsMenu]` line about the caret** and **no
      exception naming `currentCharIndex`**. Two different warnings can land here
      — one for the counter, one for the glyph list — and neither wording
      contains the other's. Without this, checks above can pass for the wrong
      reason: an unreadable counter still types, just at the end.

The clamp on the insertion index has no step, and cannot have one: every path
that writes a row's text also moves the marker, so no manual walk leaves the
counter past the text. It guards a state vanilla itself guards against
(`Pug.Other:343431-343434`), and its only witness is the catch-all at the end of
this document — no exception and no unexpected warning from this mod in
`Player.log`.

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

## Discovered Choice rows

A detected setting whose mod restricted it to a fixed set of values. These run
against the `TestChoiceFixtures` section. Two of them need the log as well as
the screen — open `Player.log` after the walk and search for
`[ModSettingsMenu]`.

### Cycling

- [ ] `ChoiceStrings` shows `Medium`; `→` steps to `High`, again wraps to `Low`,
      and `←` walks back the same way.
- [ ] `ChoiceInts` shows `4` and cycles `1 / 2 / 4 / 8`. This is the
      reconstruction path — its tokens were parsed out of a description line
      rather than read off the constraint, so the two paths must be walked
      separately even though they look identical on screen.
- [ ] `ChoiceEnum` shows `Second` and cycles `Second / First / Third` — the
      declaration order, not alphabetical. Alphabetical output means something
      is sorting the tokens.
- [ ] `ChoiceSingle` shows `Only` and stays there. Pressing `←`/`→` must not
      throw, must not raise the restart prompt, and must not write the file.
- [ ] Every value above survives a relaunch.

### The quote, and why it is the real check

- [ ] `ChoiceComma` cycles to `Beta, and more` and then to `Say "hi"`, both
      rendered exactly like that — **no backslashes**.
- [ ] The `.cfg` holds `Say \"hi\"` while the row shows `Say "hi"`.

Backslashes on screen, or a row that stops matching its own value, mean the read
has fallen back to the serialized form. That is the whole point of this fixture:
the escape set is small, so every other token here reads identically either way
and would not catch a regression.

### The value the row must not touch

- [ ] `ChoiceFlags` shows `Alpha, Beta`. `←`, `→` and Enter all do nothing, and
      the `.cfg` is unchanged afterwards. A combination has no member name to
      cycle from, and snapping it to one flag would silently discard the other
      in what, for a real mod, is its own file.

### A locked Choice

- [ ] `ChoiceReadOnly` shows `Medium` — the correct token, not a raw or escaped
      one. It does not tint or pop in on selection, and no input changes it.

### Culture

- [ ] `ChoiceFloats` is either a working three-option Choice (`0.5 / 1.5 / 2.5`)
      or a read-only Info row. Both are correct; which one says how this
      machine's `CurrentCulture` renders a decimal. A cycle over `0`, `5`, `1`,
      `2` is the failure — that is the split reaching the row.
- [ ] If it *is* an Info row, there is **one** extra `[ModSettingsMenu]` warning
      for it, naming the fragment the constraint rejected. That is the expected
      outcome on a comma-decimal machine, not a defect — count it in the log
      check at the end.
- [ ] Two other rows render a `double` through the same culture: `RangeDouble`
      and `RefuseSplitValue` read `1,5` and `0,5` where this one reads `0,5`.
      Their `.cfg` values stay invariant regardless, so the file checks below are
      unaffected.

### Refused, and said out loud

Each of these must render as a **read-only Info row** and log exactly one
`[ModSettingsMenu]` warning naming the reason. Opening and closing the settings
screen several times must not repeat the line — the report is once per entry,
because discovery re-runs on every open.

- [ ] `RefuseEmptyToken` — the value list has a blank entry.
- [ ] `RefuseUnconvertible` — `two` is not an `int`.
- [ ] `RefuseInvalid` — the constraint rejects the values it prints.
- [ ] `RefuseSplitValue` — the list does not contain the value the setting
      holds. Every fragment there converts *and* validates, so neither per-token
      check sees anything wrong; only the held-value check refuses it.
- [ ] `RefuseSplitDuplicate` — the same split, but the held value survived it as
      one of the fragments, so the held-value check passes too. Only the
      duplicate check is left, and it is what stops the row from offering
      `0 / 5 / 5 / 0` with `0.5` unreachable and `→` frozen.
- [ ] `RefuseBlankInSet` — a blank entry in a **real** `AcceptableValueList`.
      Worth its own line because it needs no dev-only subclass: the constructor
      accepts it, so any mod could ship this by accident.
- [ ] `ThrowingConstraint` — its constraint throws where MSM asks it a question.
      The row is **absent** rather than read-only, the log holds one
      `[ModSettingsMenu]` error naming it, and — the actual point — **every
      other row in both sections is still there**. Losing the screen instead of
      the row is the failure this guards.

### The negative control

- [ ] `RangeDouble` stays a read-only Info row showing `1.5`, and logs
      **nothing**. It is not a degradation — an unhandled range is the designed
      route to an Info row, and a warning here would fire on a healthy config at
      every menu open.

### Nothing else moved

- [ ] The `TestListFixtures` section behaves exactly as the list checks above
      describe. Discovery routes both, and a Choice case placed wrongly in the
      cascade would take entries from the list path.

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

- [ ] **An inert row does not pretend to be activatable.** In `testListReadOnly`
      and in `testListOrderOnly`, selecting a row plays the ordinary selection
      sound, but pressing Enter/Space produces **no** activation sound and the
      footer offers no select prompt — the row cannot enter edit mode, so it
      must not promise that it can. The arrows in `OrderOnly` still respond.
- [ ] `testListReadOnly` shows every entry, with no add row and no row buttons.
- [ ] It looks and behaves like `LongReadOnly`, which reaches the same state
      through a `ViewOnly` scope instead of a declaration.
- [ ] **The declared order wins here, unlike at `OrderOnly`.** Reorder the
      declared defaults of `testListReadOnly` in `AddDeclaredListFixtures`,
      rebuild, reopen: the new order shows. Nobody can reorder this list in
      game, so a stored order would otherwise outlive every release.

### Duplicates and hand-edited case

- [ ] `testListReadOnlyDuplicate` **and** `testListOrderOnlyDuplicate` each show
      **two** rows (`Alpha`, `Beta`), not three, and `Player.log` names the
      duplicate at declaration time. Two fixtures because the two levels take
      different reconciliation branches and did not always agree on this.

### A setting that cannot be bound

- [ ] `testDupKey` appears **once**, as a toggle. The slider sharing its key is
      absent, and `Player.log` carries `Could not bind setting 'testDupKey'`.
- [ ] Everything declared **after** it is still there — that whole guard exists
      so one failed setting does not take the rest of the section with it.
- [ ] `Player.log` also carries `RequiresRestart() ignored`. Without that, the
      modifier would attach to the toggle before it, and changing the HUD toggle
      would ask you to restart the game for no reason.
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
- [ ] `TestChoiceFixtures/config.cfg` carries, for every row cycled, the token
      the row displayed — with `Say \"hi\"` as the one deliberate exception,
      where the file holds the escaped form and the row does not.
- [ ] The four `Refuse*` entries and `ChoiceFlags` are byte-identical to before
      the walk. Nothing may write a value it refused to render, or one it
      declined to cycle.
- [ ] `Player.log` holds no exception from this mod, and no warning **other than**
the ones the checks above deliberately provoke (`testListOrderOnlyEmpty`, the two
duplicate fixtures, `testDupKey`, one line for each `Refuse*` entry, the error from
`ThrowingConstraint`, and — on a comma-decimal machine — one for `ChoiceFloats`).
