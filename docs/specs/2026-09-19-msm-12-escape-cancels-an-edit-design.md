# Design — Escape cancels a drill-in row edit (MSM-12)

- **Date:** 2026-09-19
- **Status:** Design approved; revised after a three-lane spec review; implementation pending
- **Roadmap point:** MSM-12 (`docs/roadmap.md`)
- **Builds on:** ADR-003 (list widget editing — the commit-on-transition model),
  ADR-005 (drill-in row model), ADR-007 (horizontal text scrolling — the
  `HandleTypingInput` patch pair this reuses)

> **Line citations into this repository's own files describe the state before
> the implementation.** They were written against `31b8563`, and the branch that
> implements this design moved most of them — a comment added above a member
> shifts every citation below it. The citations into `Pug.Other` are unaffected
> (that file is not edited here) and remain valid for game build 1.2.1.5-8be0.
> To find a construct as it stands now, search for its name rather than its
> line; `git show 31b8563:<path>` gives the file the numbers were taken from.

## 0 · Two words that mean two things

**"Commit"** is used here for two distinct things, and the spec review found
them conflated. Kept apart from here on:

- **`commit` the parameter** — the `bool` argument of
  `RadicalMenuOptionTextInput.Deactivate(bool commit)`, which the game discards.
- **the commit** — this framework's own act of writing a row's text back into
  `_rows` and from there into the owning mod's `ConfigEntry`
  (`ListDetailScreen.OnRowTextCommitted`).

**"Stored"** likewise. `_rows` is the screen's working list, **not** the
persisted value: an empty row sits in `_rows` and is never part of what is
written, because `ListTokenizer.Join` drops empties. Where this document needs
the persisted value it says **persisted**; where it needs the screen's list it
says **`_rows`**.

## 1 · Goal

A player who types into a drill-in list row and then presses the back key
leaves that row holding the value it was seeded with. Enter still commits.
Today an edit cannot be abandoned once a keystroke has landed: the back key
ends the edit exactly as Enter does.

**It is device-agnostic, and an earlier draft of this spec was wrong about
that.** That draft called vanilla's back-key branch unreachable on a controller,
because `HandleTypingInput` takes the on-screen-keyboard branch first. The
on-screen-keyboard block returns only when the keyboard actually **opens**:
`GetControllerTextInput` answers `false` on the fallback platform always
(`Pug.Other:288045`) and on Steam whenever the overlay cannot show it
(`Pug.Other:286913`), and execution then falls out of that block at
`Pug.Other:269625` into the keyboard chain — reaching `:269652` while
`SystemPrefersKeyboardAndMouse()` is false. A device test in the new branch
would therefore decline exactly the players vanilla is cancelling for, which is
why §4.3 has none.

Where the keyboard does open, the cancel needs no exclusion of its own: that
path answers through `TrySetInputText`, where the back key is not down, so no
row is recorded and the branch cannot fire. Cancelling the on-screen keyboard
already keeps the seeded value and is untouched.

The defect is pre-existing and was found on 2026-08-23 by the
`pr-review-toolkit:silent-failure-hunter` gate while it reviewed the drill-in
row model. Its sibling half — a *world event* deleting a row mid-edit — was
fixed in 2.0.0 and is not revisited here.

## 2 · Why the row cannot see the intent today

`RadicalMenuOptionTextInput.Deactivate(bool commit)` discards its own parameter
(`Pug.Other:343542`): the body sets `activeInputField` to null and hides the
caret, and never reads `commit`. Core Keeper's own callers do state an intent —
the back key arrives as `Deactivate(!Manager.input.IsMenuBackButtonDown())`
(`Pug.Other:269652`) — and it is thrown away on the way in.

`ListDetailItem` commits on the transition "this row held `activeInputField`,
now it does not" (`ListDetailItem.cs:634`), which is the only reliable "the user
is done" signal the game offers. A state change carries no intent, so by the
time the row notices, the reason is gone.

`Deactivate` is a non-virtual member of `InputManager.TextInputInterface` and
the game calls it through the interface, so an override on the subclass is never
dispatched. Any fix has to come from a patch.

### 2.1 · The callers, and why the argument alone is not enough

Six call sites reach a `Deactivate(bool)` on a text input:

| Caller | Argument | Intent | Reaches a drill-in row |
|---|---|---|---|
| `MenuManager.HandleTypingInput`, IME branch (`:269642`) | `true` | Enter while composing | yes |
| `MenuManager.HandleTypingInput`, main branch (`:269652`) | `!IsMenuBackButtonDown()` | Enter commits, back key cancels | yes, keyboard only |
| `MenuManager.TrySetInputText` (`:269689`) | `success` | on-screen keyboard confirmed or cancelled | yes, controller only |
| `UIManager.HideAllInventoryAndCraftingUI` (`:273389`) | `false` | a world event — MSM commits here **deliberately** | already patched |
| `UIMouse`, click while a field is active (`:355869`) | field's own flag | guarded on `is TextInputField` | **no** |
| `UIMouse.TrySelectNewElement` (`:356114`) | `false` | click elsewhere | already patched |

**Four of these can pass `false`, and it means something different in each**:
abandon the edit (main branch), the keyboard was dismissed (`TrySetInputText`),
a world event is clearing the field (`HideAll…`), and the player clicked
something else (`TrySelectNewElement`). A prefix on `Deactivate` sees the value
but not the sender, so it would have to reconstruct the caller anyway. That is
what rules out patching `Deactivate` itself, independently of blast radius.

The fifth call site cannot reach this mod at all: `TextInputField` and
`RadicalMenuOptionTextInput` are disjoint branches under `UIelement`
(`Pug.Other:354502` and `:343312`), sharing only the
`InputManager.TextInputInterface` they both implement.

## 3 · Decisions (locked)

1. **Scope: MSM's own rows only.** No vanilla text field changes behaviour.
   Patching `RadicalMenuOptionTextInput.Deactivate` to honour its parameter would
   also reach the character-name field, the world seed and the server connection
   fields — and would still need the caller reconstruction from §2.1. This mod is
   a settings framework, not an input mod.
2. **Extent: the typed text, nothing else.** The back key restores the token the
   row was seeded with. A row added with `+` and never filled stays as an empty
   row, exactly as it does today when left with Enter. The back key acts on the
   row the caret sits in — the `+` press was a separate action with its own undo,
   the row's delete button. Reordering and deleting are likewise untouched; a
   cancel for the whole drill-in visit would be a different feature and a roadmap
   point of its own.
3. **Mechanism: reset in the postfix, before the commit looks.** The intent is
   captured where it is still readable and applied in the same method call, so
   the existing commit path finds nothing to do rather than gaining a second
   exit.

## 4 · Design

Three small changes. The commit path is not among them.

### 4.1 · The row resets itself

`ListDetailItem` already holds the token the last rebuild seeded it with
(`_seededText`, `ListDetailItem.cs:171`), and `SeedText`
(`ListDetailItem.cs:177`) both restores the text and clears the `_edited` latch,
so `CommittedText` (`ListDetailItem.cs:201`) then yields that token rather than
what was typed. The row therefore needs no help from the screen:

```csharp
internal void CancelEdit() => SeedText(_seededText);
```

**Why restoring the seed is enough to leave the persisted value alone** — the
chain the review found missing, stated in full:

1. `_seededText == _rows[index]`, because `RebuildRows` seeds every row from
   `_rows` and `SeedText` is the only path that writes it.
2. `_rows` is what the persisted value is derived from: `WriteValueFromRows`
   joins it through `ListTokenizer` and compares the result against the
   persisted value tokenized the same way, writing only on a difference.
3. So a row restored to `_rows[index]` produces a join identical to the one the
   persisted value already agrees with — the comparison finds no change and
   nothing is written.

Step 3 holds for an empty row too, and that is the case worth naming: an empty
`_rows` entry is dropped by `Join`, so it contributes nothing either way. It is
also why §0 insists on the distinction — `_rows[index]` for such a row has no
counterpart in the persisted value at all, and a claim phrased as "the row's
stored token" would be false for it while the mechanism is fine.

### 4.2 · The prefix remembers

`MenuPatch.MenuManager_PreHandleTypingInput` (`MenuPatch.cs:522`) gains two
remembered values, cleared and consumed like the arrow verdicts beside them:

- the `ListDetailItem` that held `activeInputField` on entry, and
- whether `Manager.input.IsMenuBackButtonDown()` was true.

**Implementation trap, found in review.** The row is already resolved there, but
inside a compound condition:

```csharp
if (Manager.input.activeInputField is ModSettingsMenu.UI.ListDetailItem row
    && row.Viewport.TryCaretIndex(out int caret))
    _caretBeforeBody = caret;
```

`row` is only definitely assigned inside that body, and the body is reached only
when `TryCaretIndex` also succeeds — which it does not for a row whose
`fieldMask` is unwired, a state this mod already reports rather than assumes
(commit `09647dd`). Recording the row inside that body would tie the cancel to
an unrelated precondition and lose it silently. **Split the condition**: resolve
the row first, remember it, then ask for the caret.

**`Priority.First` is load-bearing for this capture**, and two earlier drafts of
this paragraph got that wrong in opposite directions. The first claimed the
attribute "puts it ahead of any foreign prefix", which overstates what Harmony
guarantees — equal priorities fall back to load order, and only
`[HarmonyBefore]` truly pins one. The second concluded from that correction that
the priority was irrelevant here, which is worse: it is what keeps this prefix
ahead of BetterTextInput, whose Escape branch calls `Deactivate(false)` itself.
Run after it and `activeInputField` is already null, so the `if` never opens and
no row is recorded — the cancel silently stops working, with that mod installed
and for Escape only.

The *key* alone would not need the priority (an input edge is frame-stable, and
vanilla polls it more than once per frame). The *row* does, and since the row is
recorded only when the key is down, the capture as a whole does.

### 4.3 · The postfix applies

`MenuPatch.MenuManager_HandleTypingInput` gains a branch: if a row was recorded
and no longer holds `activeInputField`, call `CancelEdit()` on it. Because the
prefix records a row only while the back key is down, a recorded row already
means "abandon this edit" and the branch tests one thing, not two.

It sits ahead of **every** existing guard in that method, for three different
reasons — two about what it needs, one about what it must not inherit:

| Existing guard | Why the branch is ahead of it |
|---|---|
| the `__runOriginal` early return | with BetterTextInput installed `__runOriginal` is false on exactly the frame this feature must act (§7) |
| `activeInputField is not ListDetailItem` | `Deactivate` has already nulled the field, so the row is reachable only through the recorded reference |
| `!SystemPrefersKeyboardAndMouse()` | it must **not** adopt this test: vanilla's back-key branch is reachable on a controller whenever the on-screen keyboard fails to open (§1), and a device test would decline exactly those players |

Everything downstream then runs unchanged: `Update` sees the transition,
`CommittedText` hands back the restored token, and `WriteValueFromRows` takes
its no-change return — which also skips the rebuild, so **no other row is
re-seeded, reordered or touched**. The write itself is single-index by
construction: `OnRowTextCommitted` assigns `_rows[index]` for the committing row
alone and reads every other row from `_rows`, never off screen.

### 4.4 · Why this needs no frame-order assumption

Whether `ListDetailItem.Update` runs before or after `MenuManager.Update` is
Unity script order and is not settled by reading. This design does not depend on
it: the reset happens inside the same `HandleTypingInput` call that deactivated
the field, and the transition is observable only afterwards — in that frame if
`Update` runs later, in the next one if it ran earlier. Either way the text has
already been restored.

A design that carried the intent as a flag into `Update` would depend on it,
because the prefix does not run again after the field is gone
(`HandleTypingInput` has a single call site, guarded on
`activeInputField != null`, `Pug.Other:269555`), so the flag would have to
survive an unknown number of frames.

## 5 · Acceptance criteria

1. Typing into a row with a persisted token and pressing the back key leaves the
   row **displaying that token from its start** — a row that no longer holds the
   field rests at the text start rather than following the caret
   (`TextFieldViewport.ApplyOffset`, driven from `ListDetailItem.cs:601`), so a
   long value must not be left scrolled to where the caret was — and leaves the
   owning mod's `.cfg` unchanged.
2. Typing into the same row and pressing Enter still writes the typed value.
3. A row added with `+`, typed into, then cancelled with the back key remains
   present and empty; the `.cfg` gains no entry.
4. A row added with `+` and cancelled without any keystroke behaves the same as
   leaving it with Enter does today — it remains present and empty.
5. Cancelling one row does not alter any other row's text or order.
6. The back key still closes the drill-in screen on the press after the edit
   ends — one press ends the edit, the next leaves the screen.
7. On a controller **where the on-screen keyboard opens**, both halves are
   unchanged from today: cancelling it keeps the seeded token, and confirming it
   still writes the entered value. Where it does **not** open — the fallback
   platform, or Steam with the overlay unavailable — the back key cancels there
   too, exactly as it does on a keyboard; that path is the reason the branch
   carries no device test (§1). It is the hardest criterion to exercise here,
   since it needs a build or a session in which `GetControllerTextInput` fails.
8. With BetterTextInput installed, criteria 1–3 hold unchanged.
9. No vanilla text field changes behaviour: the character-name field, the world
   seed and the server connection fields end an edit on the back key exactly as
   before.
10. The placement reasoning of §4.3 is present as a comment at the branch, so a
    later edit cannot move it past the `__runOriginal` guard without meeting the
    reason.

## 6 · Edge cases the design covers

| Case | Behaviour | Why |
|---|---|---|
| IME composing and the back key pressed | nothing happens, today and after | the inner `if (!IsMenuBackButtonDown())` (`Pug.Other:269640`) skips the `Deactivate` entirely, so the row keeps the field and the new guard declines. This is also the one case where today's back key does **not** commit |
| Second back press closes the screen | no double commit | after `Deactivate` the method reaches `if (wasAutoActivated && …) return false` (`:269653-269655`) — and `WasAutoActivated` is set only via `LeftClick(…, autoActivated: true)`, which the whole assembly does once, on the character-name field (`:335183`). For every MSM row it is false, so `HandleTypingInput` returns `true` and its caller returns (`:269555`), keeping the menu pop out of that frame. The next press finds no active field and never enters the method |
| Controller on-screen keyboard, either outcome | unchanged | the new branch does not run on the controller path at all (§4.3), so neither confirm nor cancel is affected |
| A world event fires mid-edit (a chest, a cattle pen, the player dying) | the edit **commits**, unchanged from 2.0.0 | the only route by which something is written that the player did not confirm, and deliberately so: the alternative is CK blanking the field and the entry vanishing, which is the defect `MenuPatch`'s `HideAllInventoryAndCraftingUI` prefix exists to prevent. The asymmetry against the back key is intended — the back key discards because the player said so; a world event saves because nobody did |
| Read-only or order-only row | not reachable | such a row never becomes `activeInputField` |
| Screen closed while a row is active | unchanged: the edit **commits** — but the state is barely reachable | `ListDetailScreen.Deactivate`'s safety net still commits, and it is not a back-key path. Three independent mechanisms already prevent the screen from closing under an active row: vanilla swallows menu input while a field is active (`Pug.Other:269555`, so the pop at `:269903` is never reached), this mod's own prefix skips `UIMouse.TrySelectNewElement` entirely so no click is delivered anywhere (`MenuPatch.cs:167-173`), and the one external route, `PopUntil`, has a single caller and it is the join-game menu (`:338042`). The net therefore guards against a future in which one of those lapses — a foreign patch on the mouse path, or a new close route in a game update — not against a route a player can take today |

## 7 · Coexistence with BetterTextInput

BetterTextInput (mod.io id `3425473`) patches the same method, and its prefix
handles Escape itself, in its published `Scripts/Patches/MenuManager_Patch.cs`:

```csharp
if (__instance.__IsKeyDown(KeyCode.Escape, false))
{
    Manager.input.activeInputField.Deactivate(false);
    return true;          // the prefix then returns false
}
```

Two consequences:

- **The field is still deactivated**, by that mod rather than by vanilla, so the
  transition this design keys on happens either way.
- **`__runOriginal` is false in that frame.** The existing postfix guards its
  arrow verdicts on it, which is correct for those; a cancel branch behind that
  guard would work without this mod and silently not work with it. Hence the
  placement in §4.3 and criterion 10.

**The two halves are read from two different signals** — that mod tests
`KeyCode.Escape`, this design reads `IsMenuBackButtonDown()`. They agree only if
Escape is bound to that action, which is §8's question; so the question carries
criterion 8 just as much as criterion 1, and is not narrowed by that mod being
absent.

## 8 · Settled in verification

**Escape is bound to Rewired action 6 — measured, no longer inferred**
(2026-09-19, game 1.2.1.5 under CrossOver). While a drill-in row held the input
field, one Escape press produced exactly one `IsMenuBackButtonDown()` hit, and
the row's text returned to its stored token. The mod under test was the fake-id
dev build (`Loading mod with ID 9999991`), not a subscription, so the reading is
of this code.

It was worth measuring rather than reasoning through. `IsMenuBackButtonDown()`
returns `system.GetButtonDown(6)` (`Pug.Other:267169`), and the Rewired binding
table is asset data absent from the decompile — so the code could only support
an argument by elimination: vanilla's typing branch offers no other keyboard
route out of a field except Return/KeypadEnter (`:269636`), yet Escape
demonstrably ends an edit. That argument rests on §2.1 being a *complete* list
of deactivation routes, which is a property of the decompile as read rather than
a guarantee — and `GetButtonDown` reports a wrong action id by returning `false`
in silence, so a mistaken premise would have surfaced as "the key does nothing"
and been indistinguishable from a broken implementation. The premise carried
criteria 1, 3, 5 and 8; one keypress settled all four.

## 9 · Verification

This mod has no automated tests; `docs/manual-tests.md` is the verification
record. The criteria in §5 become checks there, grouped with the existing
drill-in typing walk, with §8's keypress first — if the binding is not action 6,
criterion 1 fails immediately and the rest of the walk is moot. Criterion 9 is
checked against vanilla screens, not this mod's own, and is the one that would
catch an accidental widening of scope. Criterion 10 is checked by reading the
diff, not in game.
