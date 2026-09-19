# Design — Escape cancels a drill-in row edit (MSM-12)

- **Date:** 2026-09-19
- **Status:** Design approved; revised after a three-lane spec review; implementation pending
- **Roadmap point:** MSM-12 (`docs/roadmap.md`)
- **Builds on:** ADR-003 (list widget editing — the commit-on-transition model),
  ADR-005 (drill-in row model), ADR-007 (horizontal text scrolling — the
  `HandleTypingInput` patch pair this reuses)

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

**This is a keyboard feature, and deliberately so.** On a controller, text entry
runs through the on-screen keyboard — `HandleTypingInput` takes that branch and
returns before the back-key branch is ever reached (`Pug.Other:269610-269624`),
so vanilla's `Deactivate(!IsMenuBackButtonDown())` at `:269652` is unreachable
there. Cancelling the on-screen keyboard already keeps the seeded value, so the
controller half of this feature needs nothing and gets nothing. §4.3 places the
new branch accordingly.

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

**No priority argument is needed for the back-key read**, and the earlier draft's
claim that `Priority.First` "puts it ahead of any foreign prefix" was wrong on
its own terms — this file already records that equal-priority prefixes fall back
to load order and that only `[HarmonyBefore]` pins it (`MenuPatch.cs:507-509`).
The attribute stays for the reasons already documented there (the caret sample
and the `IsKeyDown` clear); the back-key read simply does not depend on it,
because an input edge is frame-stable and reads the same wherever in the frame
it is sampled.

### 4.3 · The postfix applies

`MenuPatch.MenuManager_HandleTypingInput` (`MenuPatch.cs:602`) gains a branch:
if the remembered row no longer holds `activeInputField` and the back key was
down, call `CancelEdit()` on it.

Its placement is not "before all the guards" — it differs per guard:

| Existing guard | New branch sits | Why |
|---|---|---|
| the `__runOriginal` early return | **before** it | with BetterTextInput installed `__runOriginal` is false on exactly the frame this feature must act (§7) |
| `activeInputField is not ListDetailItem` (`MenuPatch.cs:632`) | **before** it | `Deactivate` has already nulled the field, so the row is reachable only through the remembered reference |
| `!SystemPrefersKeyboardAndMouse()` (`MenuPatch.cs:636`) | **behind** it, or carrying the same test itself | §1: the branch has nothing to do on a controller, and acting there could only overwrite a confirmed on-screen-keyboard result |

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
7. On a controller, **both** halves are unchanged from today: cancelling the
   on-screen keyboard keeps the seeded token, **and** confirming it still writes
   the entered value.
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

## 8 · Open question

**That Escape is bound to Rewired action 6 is inferred, not read.**
`IsMenuBackButtonDown()` returns `system.GetButtonDown(6)`
(`Pug.Other:267169`), and vanilla's typing branch offers no other route out of
a text field by keyboard except Return/KeypadEnter (`:269636`) — yet Escape
demonstrably ends an edit in the shipped game. By elimination the binding exists.
The Rewired binding table is asset data and is not in the decompile, so this is
an argument from the code's shape rather than a reading of the binding.

It is stated as open because the elimination rests on §2.1 being a complete list
of the ways a field can be deactivated, which is a property of the decompile as
read, not a guarantee. **It carries criteria 1, 3, 5 and 8** — everything the
back-key trigger touches. One in-game keypress settles it, and it is the first
check of the verification walk rather than a separate exercise.

## 9 · Verification

This mod has no automated tests; `docs/manual-tests.md` is the verification
record. The criteria in §5 become checks there, grouped with the existing
drill-in typing walk, with §8's keypress first — if the binding is not action 6,
criterion 1 fails immediately and the rest of the walk is moot. Criterion 9 is
checked against vanilla screens, not this mod's own, and is the one that would
catch an accidental widening of scope. Criterion 10 is checked by reading the
diff, not in game.
