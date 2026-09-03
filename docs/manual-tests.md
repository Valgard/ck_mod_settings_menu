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
own. There are four files. Two hold the ordinary fixtures and are the ones worth
inspecting after a write; the other two hold one throwing fixture each and can
never be written at all (below), so only three `(detected)` boxes appear — the
fourth loses its only row and with it its box, which is the expected outcome
there:

```
<bottle>/drive_c/users/crossover/AppData/LocalLow/Pugstorm/Core Keeper/Steam/<user-id>/mods/TestListFixtures/config.cfg
<bottle>/drive_c/users/crossover/AppData/LocalLow/Pugstorm/Core Keeper/Steam/<user-id>/mods/TestChoiceFixtures/config.cfg
<bottle>/…/mods/TestThrowingConstraint/config.cfg      (header only, never written)
<bottle>/…/mods/TestExactNoDescription/config.cfg       (header only, never written)
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
| `ChoiceInts` | `4` of 1/2/4/8 | a non-string type read exactly, then written back through the converter |
| `ChoiceEnum` | `Second` of three | the member-name round trip, always ON a member so never the guard |
| `ChoiceFlags` | `Alpha, Beta` | a `[Flags]` combination — the value the guard exists to leave alone |
| `ChoiceSingle` | `Only` | one option: the wrap arithmetic with nowhere to go |
| `ChoiceReadOnly` | `Medium`, view-only | a locked Choice must still display the right token |
| `ChoiceFloats` | `1.5` of 0.5/1.5/2.5 | the culture-proof read and write: same result on every machine |
| `ChoiceReconstructed` | `1.5` of 0.5/1.5/2.5 | the parse path's only SUCCESS case — tokens are the values re-rendered, not the spellings given |
| `RangeDouble` | `1.5`, range 0–10 | negative control: an unhandled constraint stays a read-only Info row |
| `RefuseEmptyToken` | `Alpha` | a blank entry in the value list |
| `RefuseUnconvertible` | `1` | a token that is not an `int` |
| `RefuseInvalid` | `Alpha` | a constraint that rejects the values it prints |
| `RefuseSplitValue` | `0.5` | a split that ate the held value — the held-value check |
| `RefuseSplitDuplicate` | `5.0` | the same split with the held value intact — only the duplicate check |
| `RefuseBlankInSet` | `Alpha` | a blank entry in a **real** `AcceptableValueList` |

Two more fixtures have an unreadable description, and they are a pair on purpose
— the same fault, opposite outcomes. **Each is alone in its own file**, and that
is a hard rule rather than tidiness (below):

| File / box | Fixture | Expected |
|---|---|---|
| `TestThrowingConstraint` | `ThrowingConstraint` | the parse asks for a description, so this row is **lost** and its box never appears |
| `TestExactNoDescription` | `ChoiceExactNoDescription` | the exact read does not ask, so this is a **working Choice** over 0.5/1.5/2.5 |

The second is the only check of the exact read that does not depend on the host:
the culture trap it removes is invisible where decimals render with a dot, so on
such a machine the parse would have produced identical tokens. A throwing
description replaces that dependency on the machine with a certainty.

**Why one per file, and why the others are separate from both.** CoreLib saves a
whole file on every value change and asks each entry in it for a description
while doing so — including from inside `ConfigEntryBase`'s constructor, which
assigns the default value before `Bind` has registered the entry. Two throwing
fixtures in one file therefore cost the *second* one its registration entirely:
no row, no log line, and a box with no rows is dropped whole, so the symptom is a
missing box rather than a missing row. Keeping either beside the ordinary
fixtures has a milder version of the same effect — every save in that file throws,
so a cycle can be checked on screen but never in the `.cfg`.

Most of these need a dev-only `AcceptableValueBase` subclass
(`DescriptionOnlyValues`), because CoreLib's own constraints cannot produce the
states they test: its constructor refuses an empty set, no supported type renders
an unparseable token, and its `Clamp` corrects an off-set value at bind. They are
also the only exercise of a third-party subclass, which is a case the code
reasons about and nothing else reaches.

There is a second dev subclass, `ThrowingDescriptionValues`, and it differs in
kind: it derives from a *real* `AcceptableValueList<float>` and overrides only
`ToDescriptionString()`. Its inheritance is the point — the values stay genuinely
readable while the description does not, which is what lets one fixture separate
the two paths.

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
| `testChoiceFloat` | — | the only declared **Choice**: SectionBuilder's own token rendering |
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
- [ ] `ChoiceInts` shows `4` and cycles `1 / 2 / 4 / 8`, and the `.cfg` holds the
      value shown after each step. What this checks is the non-string **write**:
      unlike the string fixtures, the chosen token goes back through
      `TomlTypeConverter.ConvertToValue`, and this is the only ordinary fixture
      on that branch. It does **not** discriminate between token sources — an int
      renders identically whichever way it is produced, so a regression there
      would pass this step. `ChoiceFloats` and `ChoiceReconstructed` are where
      that shows.
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

- [ ] `ChoiceFloats` is a working three-option Choice reading `0.5 / 1.5 / 2.5`,
      on every machine. There is no longer a second correct outcome: the values
      are read off the constraint, so no separator is involved and the culture
      cannot reach them. An Info row here, or a cycle over `0`, `5`, `1`, `2`, is
      now a failure rather than a machine-dependent result.
- [ ] Cycle it once and check the `.cfg`: the stored value is the one shown. This
      is the half the exact read does *not* fix by itself — a token is written
      back invariantly, so a token rendered in the machine's own culture would
      store `5` where the row shows `0,5`. It is the only fixture where that
      mismatch is visible.
- [ ] It provokes **no** `[ModSettingsMenu]` warning any more, on any machine.
- [ ] `ChoiceReconstructed` shows `1.5` and cycles `0.5 / 1.5 / 2.5` — **not**
      `0.50 / 1.5 / 2.50`, which is how its description spells them. This is the
      parse path's only success case; every other `Refuse*` fixture below ends in
      a rejection, so without this step nothing checks that the reconstruction
      still produces a usable Choice at all. A row that shows the trailing zeros
      is the regression: cycling then lands on a token the widget cannot find,
      and every other press snaps back to the first option.
- [ ] Two other rows still render a `double` through the machine's own culture and
      make the contrast visible: on a comma-decimal host `RangeDouble` and
      `RefuseSplitValue` read `1,5` and `0,5` while this one reads `1.5`. They are
      Info rows, which display the raw value; a Choice displays a token. Their
      `.cfg` values stay invariant regardless, so the file checks below are
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
### Refused, or not, by the same fault

The two fixtures below share an unreadable description and are checked as a pair.
Neither matches the preamble above — one loses its whole box, the other is a
working Choice, and one of them logs a line per keypress by design.

- [ ] There is **no** `TestThrowingConstraint` box: its only row was lost, and a
      section with no rows is dropped whole. The log holds exactly one
      `[ModSettingsMenu]` error naming `ThrowingConstraint`.
- [ ] There **is** a `TestExactNoDescription` box, holding
      `ChoiceExactNoDescription` as a working Choice reading `1.5` and cycling
      `0.5 / 1.5 / 2.5`. Same unreadable description as the row above, opposite
      outcome — that contrast is the check, not either box alone.
- [ ] Cycling it logs `changing 'ChoiceExactNoDescription' failed`, once per
      press. **That line is the pass, not a fault** — and the strongest evidence
      in this walk: the widget could only try to write because the row was built
      as a Choice with real tokens, from a constraint whose description throws.
      Saving it cannot work, since the save asks for that same description.
- [ ] Two ways this one fails, and they mean different things. A read-only Info
      row means the values came from a description again. A **missing box** means
      the entry was never registered — the constructor's own save threw before
      `Bind` could record it, which is what happens if this fixture ever shares a
      file with another throwing one.
- [ ] Cycle any `TestChoiceFixtures` row and confirm the log holds **no**
      `changing '…' failed` line. Those entries used to share a file with these
      two, and CoreLib's whole-file rewrite made every save in it throw — the
      value changed in memory, the row looked right, and nothing reached disk.

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

### The declared Choice

- [ ] `testChoiceFloat` reads `1.5` and cycles `0.5 / 1.5 / 2.5`, and the stored
      value in `ModSettingsMenu.cfg` is the token shown. It is the only fixture
      on the declared Choice path, and a float on purpose: string and enum
      render the same however the token is produced, so this is the one place
      where SectionBuilder going through `ChoiceToken` is observable at all.
- [ ] On a comma-decimal host the old behaviour is visible as `1,5` — in the row
      **and** in the `.cfg`. That is the failure: a token is also the
      localization leaf key a consumer writes into its yaml, so a key that
      changes with the machine cannot be translated once and stay right.

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

## A detected mod's names

Two terms are tried per row — this mod's own schema, then GMCM's — before the
raw key is shown as a last resort; a heading gets the same, minus the first
stage. Nothing installed here ships GMCM terms, so stage 2 has to be provoked:
put the terms below into this mod's own `localization/localization.yaml`,
rebuild, check, then take them out and rebuild again. That leaves nothing
behind, because the install step deletes the game-wide `Localization.csv` on
every run of a loc-shipping mod.

**Pick the target mod from what LOADS, not from what is installed.** A `.cfg`
under `mods/` only proves the mod ran once — CoreLib writes it on first launch
and nothing removes it, so files outlive uninstalls (GMCM's own sits there
still). A mod that does not load renders no box, and a check against it passes
by rendering nothing. `Player.log`'s `Loading mod with ID` is the list that
counts; the ids resolve through the cache directory names, `<modId>_<fileId>`:

```bash
grep "Loading mod with ID" "<bottle>/.../Core Keeper/Player.log"
```

Not loading is often a **choice**, though, and one worth reversing for a test:
a mod's id in `existingUsers[<user>].disabledMods` in
`…/Public/mod.io/5289/state.json` makes the loader skip it silently. Removing
the id switches the mod back on without the in-game Mods menu — which must stay
shut here, since it wipes the fake-id dev install. Edit that file only while the
game is closed; it is rewritten on exit. Put the id back afterwards: what is
switched off is switched off on purpose.

**A mod of this family is not a target, however loaded it is** — for one of two
reasons, and it is worth knowing which. Most ship no CoreLib config at all, so
there is nothing to discover; the rest are registered consumers, and
`ConfigStore.IsOwn` keeps those out of discovery. Either way their rows never
reach the chain. Worse, a term aimed at one *appears* to work:
MSM's own stage-1 schema is `<ModId>-Config/<key>`, which is exactly what
`SectionBuilder` already builds for a consumer, so the term lands — on the old
path, proving nothing. The tell is on screen: a consumer's box carries a hint
line under its heading and **no `(detected)` marker**. Enumerate the family
rather than recalling it; no list of them is kept anywhere, on purpose:

```bash
cd ../..   # the family lives beside this repo, not inside it
find . -maxdepth 2 -name .git -not -path "./.git*" \
  | sed 's|^\./||; s|/\.git$||' | grep -v "^CoreKeeperModSDK$" | sort
```

It lists `CoreKeeperModDocs` too, which is Pugstorm's docs clone rather than a
mod of this family.

That leaves **PlacementPlus** and the **fixtures**. The fixtures reach discovery
through the very same path (see § Running the fixtures), so they are not a
stand-in for a foreign mod but an instance of one — but they sit in a
`[Settings]` section of a file called `config.cfg`, and **both of those values
go into the term**. PlacementPlus differs in each: section `[General]`, and a
file named after its mod. Without it, a hardcoded section or a mis-derived file
name passes every other check here.

```yaml
PlacementPlus-Config:                   # stage 1, MSM's own schema - must WIN
  MaxBrushSize:
    en: "Brush size [stage 1]"
    de: "Pinselgroesse [Stufe 1]"
PlacementPlus:                          # heading where FILE == mod name
  PlacementPlus:
    en: "Placement Plus [heading]"
    de: "Platzierung Plus [Ueberschrift]"
PlacementPlus_PlacementPlus_General:    # section General, not Settings
  MaxBrushSize:                         # stage 2 for the SAME key - must lose
    en: "Brush size [stage 2 - WRONG if visible]"
    de: "Pinselgroesse [Stufe 2 - FALSCH wenn sichtbar]"
  ExcludeItems:                         # a LIST row, and its drill-in title
    en: "Excluded items [list row]"
    de: "Ausgeschlossene Gegenstaende [Listenzeile]"
```

Both languages, always — and note what goes wrong when you skip one, because it
is not what you would expect. The generator writes one entry per language
whether the yaml carries it or not, so a term with only `en:` ships an empty
German title; I2 then answers the lookup with the first non-empty cell in **any**
language, so the German run shows the **English** text and the check passes
without testing anything. The German strings above therefore differ visibly from
their English counterparts on purpose: the failure signature to watch for is
English text in a German game, not blank text.

The chain's remaining stage is a Choice's per-option **value**, and the choice
fixtures carry one: `ChoiceEnum` is enum-typed, so discovery builds a Choice and
`SettingWidget` asks `ValueLabel()` for the text beside it.

```yaml
TestChoiceFixtures:                              # heading, file named config.cfg
  config:
    en: "Choice Fixtures [heading]"
    de: "Auswahl-Fixtures [Ueberschrift]"
TestChoiceFixtures_config_Settings:              # stage 2, no stage-1 rival
  ChoiceEnum:
    en: "Enum choice [stage 2]"
    de: "Enum-Auswahl [Stufe 2]"
TestChoiceFixtures_config_Settings_ChoiceEnum:   # the VALUE, not the label
  Second:
    en: "Second [value stage 2]"
    de: "Zweiter [Wert Stufe 2]"
```

Only one of the three tokens is translated on purpose: cycling with ←/→ must
make the translation appear and disappear. A value term built per row rather
than per token would leave every option reading the same.

The second segment is the **file**, not the mod. The fixtures name theirs
`config.cfg`, so their heading term is `TestChoiceFixtures/config`; one named
after its mod reads the way you would expect, `PlacementPlus/PlacementPlus`.
Both shapes are in the blocks above on purpose.

- [ ] The MaxBrushSize row reads **"Brush size [stage 1]"**. Seeing the
      `[stage 2 - WRONG if visible]` variant instead means the stages resolve
      in the wrong order — the one failure no other check can show, because it
      needs two terms competing for one key.
- [ ] PlacementPlus is headed **"Placement Plus [heading] (detected)"**. Raw
      text here with the other boxes translated is the signature of a file name
      that is assumed rather than read.
- [ ] Its ExcludeItems row reads **"Excluded items [list row]"**, and opening
      that drill-in shows the same text as the screen's title. Those are two
      further render paths — `ListWidget` and `ListDetailScreen` — that no other
      check on this page reaches. That row is also the section proof: its term
      names `General`, so raw text here while the heading is translated means
      the section was assumed to be `Settings` rather than read.
- [ ] Its MinHoldTime row stays raw. Untranslated rows sitting beside
      translated ones in the same box is what stage 3 looks like.
- [ ] The `(detected)` marker survives the translation. A translated name does
      not make a detected mod curated, and the marker is the only thing that
      still says so once the raw keys are gone.
- [ ] The fixtures' box is headed **"Choice Fixtures [heading] (detected)"** —
      the same heading path reached through a file named `config.cfg` rather
      than after its mod.
- [ ] In that box, the ChoiceEnum row reads **"Enum choice [stage 2]"** and its
      VALUE reads **"Second [value stage 2]"**. The value is the half that would break
      on its own — the two schemas put the key on opposite sides of the slash.
- [ ] Cycle that value with ←/→: the other tokens stay raw, and the translation
      returns on `Second`. A term built per row rather than per token would show
      the same text for every option.
- [ ] `TestListFixtures` keeps its raw heading. Two boxes down the same
      discovery path, one translated and one not, is what ties the heading to
      its term rather than to something applied per box.
- [ ] The boxes are in alphabetical order **by what is on screen**: the fixtures
      box now sorts under **C** ("Choice Fixtures"), where `TestChoiceFixtures`
      sorted under T. PlacementPlus is no test of this — both its names begin
      with P, so a build still ordering by the raw name looks identical.
- [ ] Select a row in a translated box and press reset: the confirmation names
      the mod as the box does. The regression to watch for is the untranslated
      folder name (`TestChoiceFixtures`), not a path — a path is never rendered
      anywhere.
- [ ] Switch the game to German: every translated line follows, and each reads
      as its German string rather than its English one. English text here means
      a `de:` cell was never authored, not that the chain failed.
- [ ] With the terms out and rebuilt, all of it is back to `PlacementPlus`,
      `MaxBrushSize`, `ExcludeItems` and `ChoiceEnum`.

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
duplicate fixtures, `testDupKey`, one line for each `Refuse*` entry, the error
from `ThrowingConstraint`, and one `changing '…' failed` per press on
`ChoiceExactNoDescription`). The count no longer depends on the machine's culture:
`ChoiceFloats` used to add a line here on a comma-decimal host and now never does.
