# Foreign list-widget editing (v2)

- Status: draft
- Date: 2026-07-28

## 1 · Context and problem statement

ADR-002 (`docs/adrs/002-list-widget-drill-in.md`) replaced the inline list
widget with a compact row + pushed `ListDetailScreen` drill-in, deliberately
**read-only in v1** (YAGNI — editing was out of scope for that redesign). Its
§7 sketched a full future editing vision: per-token add/remove/edit for
`SettingKind.List` strings, a free-text field for `SettingKind.Info` strings,
and a format-override toggle between the two (for when the discovery
heuristic misclassifies a value). Its §6 recorded verified but unused input
findings for a custom controller/keyboard action (`MenuSecondaryActivate`,
Rewired action 221, plus a CoreLib-bound keyboard key) and a self-rolled hint
object, since CK's menu hint bar is a closed 7-value enum that a mod cannot
extend.

This spec picks up the editing half of that deferred work: making a
`SettingKind.List` row's tokens actually editable. It intentionally does not
address the `Info`/free-text half or the format-override toggle — see
§5 Non-goals.

## 2 · Decision drivers

- Core Keeper is controller-first; every interaction must be D-pad/controller
  operable, not just mouse/keyboard.
- Reuse CK's own UI machinery over building bespoke input handling, matching
  the project's established pattern (`ModSettingsScreen` adapts the vanilla
  `UISettings` prefab; `ListWidget`/`ListDetailScreen` already reuse
  `RadicalMenu`/`IScrollable`).
- Stay sandbox-clean (no `System.IO`, no reflection-emit).
- YAGNI: only build what per-token list editing needs. The format-override
  toggle and its `ListOverrideStore`/custom-hint-object/action-221 plumbing
  are not needed for this slice and stay deferred.
- `SettingKind.List` is produced only for foreign CoreLib config entries
  `ForeignConfigDiscovery` has already classified as non-read-only,
  non-server-locked (its first-match cascade routes those to `Info` before a
  string ever reaches the list heuristic) — so every row this screen shows is
  already known to be writable.

## 3 · Key finding: CK already ships a menu-native text-input option

The decompile (`Pug.Other.decompiled.cs:343312`) has
`RadicalMenuOptionTextInput : RadicalMenuOption, InputManager.TextInputInterface`
— the exact base class CK itself uses for in-menu text entry, e.g.
`CharacterCustomizationOption_NameInput` for the character-name field inside
`CharacterCustomizationMenu` (a `RadicalMenu`). It:

- Exposes `MaxCharactersForOnScreenKeyboard`, so a controller-only session
  gets an on-screen keyboard automatically — this directly answers
  `docs/roadmap.md`'s "Free-text string input — controller-hostile" concern,
  which was written without checking for this class.
- Registers itself with the input system only on `OnActivated`
  (`Manager.input.SetActiveInputField(this)`) — so a row looks like a normal
  static-text option while merely navigated onto (`OnSelected`), and only
  enters true edit mode (blinking `characterMarkBlinker`, live
  `AppendString`/`RemoveCharAtMarker` calls from the input system) once
  confirmed. This read-vs-edit visual distinction is inherent to the base
  class — no custom state machine needed on top.
- Implements the full `InputManager.TextInputInterface` contract
  (`GetInputText`, `SetInputText`, `AppendString`, `RemoveCharAtMarker`,
  `RemoveCharBehindMarker`, `MoveCharMarker`, `Deactivate(bool commit)`,
  `GetHintString`, `IsHidden`) — everything a token row needs, for free.

This finding is what makes the chosen interaction model (§4) cheap: **one**
new row component covers add, edit, and remove, with **no** new
controller/keyboard input plumbing (no custom hint object, no action 221
binding) — deferring all of ADR-002 §6's findings to the still-unbuilt
format-override toggle.

## 4 · Decision outcome

**Interaction model: a uniform editable text row.** Every token in the
drill-in is a `ListDetailItem` rebuilt on `RadicalMenuOptionTextInput`
instead of a bare `RadicalMenuOption` + static `PugText`. The list carries one
permanent trailing blank row (same component, hint text "+ Add" via loc) at
the end of the scrollable column.

- **Edit:** activate a row, type, confirm — the row's text is the token.
- **Add:** type into the trailing blank row and confirm — it becomes a real
  token, and a fresh blank trailing row appears after it.
- **Remove:** clear a row's text to empty and confirm (leave it via
  navigation or Back) — an empty non-trailing row is dropped from the list.

Two other options were considered and rejected:

- **Row + explicit remove/add actions** (read-only look with an edit toggle,
  a separate per-row remove affordance on action 221 + a custom hint object,
  a separate non-text "+ Add" row) — closer to ADR-002 §7's original sketch,
  but needs three new pieces (remove action, hint object, add-row class)
  where the uniform model needs one, and reintroduces exactly the
  controller-input plumbing §3's finding lets this slice skip. Considered and
  briefly chosen mid-brainstorm, then reverted in favor of the uniform model
  once its lower surface area was weighed against the value of an explicit
  (accidental-clear-proof) delete action.
- **Confirm-on-close batching** (buffer edits locally, write once when the
  drill-in is closed) — rejected because no other MSM widget batches writes;
  it would need a new "discard unsaved changes?" path that does not exist
  anywhere else in the mod, for a save operation (a small CoreLib config
  write) that is not expensive enough to justify avoiding.

### Commit path

On any row's `Deactivate(commit: true)` (confirmed, not cancelled): the
screen reads `GetInputText()` from every row in order, drops empty strings,
joins the rest with `,`, and writes the result through the setting's
`ConfigEntryBase.BoxedValue` — the same non-generic write path
`SettingWidget` already uses for owned settings. This persists immediately
via CoreLib's existing `SaveOnConfigSet` auto-save; no new persistence
mechanism. Whether the foreign consuming mod picks up the change at runtime
is outside MSM's control — the same already-accepted caveat that applies to
MSM's read side of foreign config.

### Round-trip safety: comma sanitization

Storage is a plain comma-joined string, so a token containing a literal `,`
would silently split into two tokens on the next read.
`RadicalMenuOptionTextInput.characterWhiteList` is an **inclusion** filter
(only listed characters survive) — unsuited to blocking a single character
while allowing everything else, since that would mean enumerating every other
permitted character. Instead, a comma is stripped at commit time: whenever a
row's text is read to recompute the persisted list (§4's commit path), each
row's text is stripped of literal `,` before being rejoined — so a comma can
never make it into the stored value, regardless of what was typed on screen.

### Empty list stays a List

Classification (`HeuristicSaysList`) runs once, at discovery time, and is not
re-evaluated after edits. A list emptied to zero tokens stays
`SettingKind.List` — it does not fall back to `Info`. The compact row's
preview shows a placeholder (e.g. "(empty)") instead of the usual
"first items, +N" text when there are no tokens.

## 5 · Non-goals (explicitly out of scope for this slice)

- **Free-text editing for `SettingKind.Info` strings** and the
  **format-override toggle** between `List` and free-text interpretation
  (ADR-002 §7's other half, `ListOverrideStore`'s eventual return) — a
  separate future feature.
- **Reordering tokens** — insertion order is preserved but not user-editable.
- **Rich item rendering** (icons/names for `ObjectID`-like tokens).
- **Any new controller/keyboard input plumbing** (custom hint object, action
  221, a CoreLib keybind) — ADR-002 §6's findings stay recorded for the
  format-override toggle, unused by this slice.
- **Validation beyond comma-stripping** — no duplicate-detection, no
  per-token length/charset rules beyond what `RadicalMenuOptionTextInput`
  already offers generically.

### Known risk, accepted for this slice: heuristic misclassification now has a write-side consequence

ADR-002's "Bad" column judged a `HeuristicSaysList` misclassification
harmless in the read-only v1: "a mis-classified string has no user recourse
... a read-only miss only splits at commas, it never traps the user." That
reasoning assumed a read-only drill-in. This slice makes the drill-in
editable, so the same misclassification now has a materially different
consequence: `ListDetailScreen.OnRowTextCommitted` persists a lossy,
comma-rejoined `BoxedValue` straight into whatever `ConfigEntry` the
heuristic pointed at — a third-party mod's config, not MSM's own — with no
confirmation step and no path back to the original formatting. Flagged
during this branch's `pr-review-toolkit:review-pr` gate (2026-08-12) as a
Critical finding; deliberately **not** fixed in this slice. `HeuristicSaysList`
itself is out of scope to tighten (it is pre-existing, shared with the
`Info` routing path, and tightening it risks misrouting genuine lists the
other way); the two real mitigations — a confirmation step before the first
write to an unconfirmed entry, or the format-override toggle that lets a
user correct a misclassification directly — are both new UX surface that
belongs with the deferred format-override work above, not bolted onto this
slice. Accepted as a known, tracked risk until that follow-up lands.

## 6 · Edge cases

- **Clearing every row at once:** each row's own `Deactivate` commits
  independently; there is no multi-row transaction. Clearing rows one at a
  time in sequence converges on the same end state as clearing them in any
  other order.
- **Reopening the drill-in after an edit:** the screen re-splits from the
  live `ConfigEntryBase` value on every open (unchanged from ADR-002) — the
  freshly written value is what renders.
- **A row cancelled (Back/ESC) mid-edit, not confirmed:** the commit trigger is
  a row's `OnDeselected` (see §4), not the `TextInputInterface.Deactivate(bool
  commit)` parameter — `RadicalMenuOptionTextInput`'s own `Deactivate` does not
  actually revert text on cancel (verified against the decompile: its body only
  releases input capture). Practical effect: leaving the whole drill-in screen
  before ever navigating to another row abandons an in-progress edit (no
  `OnDeselected` fires for that row, so it's never read/persisted); pressing
  ESC and then navigating to another row still commits, same as confirming.

## 7 · Verification (manual, in-game)

With PlacementPlus installed and not MSM-integrated, in a world, open
Options → Mod Settings → PlacementPlus → `ExcludeItems`:

- Editing an existing token's text and confirming persists it — reopening the
  drill-in shows the new text.
- Clearing a token to empty and confirming removes it from the list.
- Typing into the trailing blank row and confirming adds a new token; a fresh
  blank trailing row appears afterward.
- Typing a comma into a token's text does not split it into two tokens after
  a commit + reopen round-trip.
- Clearing every token down to zero leaves the compact row showing an empty
  placeholder (not falling back to a plain `Info` row).
- With only a controller connected, activating a row shows the on-screen
  keyboard (`RadicalMenuOptionTextInput.MaxCharactersForOnScreenKeyboard`).
- Numeric/toggle/choice widgets elsewhere in the same and other sections are
  unaffected.

## 8 · Sequencing note (not part of this feature)

ADR-002's read-only drill-in redesign is merged to `main` but not yet pushed
to either remote or published to mod.io. Per project decision, that ships as
its own release (push + `upload.sh` publish) before this editing work begins,
so the two do not mix in one branch/release.

## 9 · References

- `docs/adrs/002-list-widget-drill-in.md` — the read-only drill-in this
  builds on; §6/§7 are this spec's starting point.
- Raw ADR-002 predecessor spec (retrieve rebase-safely):
  ```bash
  git show "$(git rev-list -1 HEAD -- docs/specs/2026-07-23-list-widget-drill-in-redesign.md)^:docs/specs/2026-07-23-list-widget-drill-in-redesign.md"
  ```
- Decompile evidence (2026-07-28):
  `RadicalMenuOptionTextInput`, `TextInputField`, `InputManager.TextInputInterface`
  (`Pug.Other.decompiled.cs:343312`, `:354502`, `:266926`),
  `CharacterCustomizationOption_NameInput` / `CharacterCustomizationMenu`
  (`Pug.Other.decompiled.cs:335091`, `:335386`) — the character-name-input
  usage pattern this design mirrors.
