# List widget editing: uniform editable text rows via RadicalMenuOptionTextInput

- Status: accepted, partially superseded by
  [ADR-005](005-drill-in-row-model.md) (2026-08-23)
- Date: 2026-07-28

> **What ADR-005 changed.** The editing mechanism below stands unaltered: token
> rows are still `RadicalMenuOptionTextInput`, and a row still commits on the
> `activeInputField` transition. What it reversed is the **"plus one permanent
> trailing blank '+ Add' row"** half of the chosen option — adding an entry no
> longer happens by typing into a row that pretends to already exist. It is a
> button (`ListAddRow`), which is close to Option 2's "separate non-text '+ Add'
> row" below, rejected here at the time.
>
> The reason is not that the argument below was wrong, but that its premise
> expired: rows were a projection of the stored value, so typing into a
> projection was the only way to make the source grow. Once the screen owns its
> rows (ADR-005), an add action needs no text path at all — and a button costs
> nothing that the shared-component version did not also cost.

## Context and Problem Statement

ADR-002 (`docs/adrs/002-list-widget-drill-in.md`) replaced the inline list
widget with a compact row + pushed `ListDetailScreen` drill-in, deliberately
**read-only in v1** (YAGNI). Its §7 sketched a full future editing vision —
per-token add/remove/edit for `SettingKind.List` strings, a free-text field
for `SettingKind.Info` strings, and a format-override toggle between the two
— and its §6 recorded verified-but-unused controller/keyboard input findings
(a custom `MenuSecondaryActivate` action, a self-rolled hint object) for
building that editing without CK's closed 7-value hint-bar enum.

How should MSM make a `SettingKind.List` row's tokens actually editable,
reusing CK's own UI machinery rather than building bespoke input handling?

## Decision Drivers

- Core Keeper is controller-first; every interaction must be D-pad/controller
  operable.
- Reuse CK's own UI machinery over bespoke input handling (`ModSettingsScreen`
  / `ListWidget` / `ListDetailScreen` already reuse `RadicalMenu`/`IScrollable`).
- Stay sandbox-clean (no `System.IO`, no reflection-emit).
- YAGNI: only build what per-token list editing needs; the format-override
  toggle and its `ListOverrideStore`/hint-object/action-221 plumbing stay
  deferred.
- `SettingKind.List` is produced only for entries `ForeignConfigDiscovery` has
  already classified as non-read-only, non-server-locked — every row this
  screen shows is already known writable.

## Considered Options

1. **Uniform editable text row** — every token row rebuilt on CK's own
   `RadicalMenuOptionTextInput`, plus one permanent trailing blank "+ Add"
   row; edit/add/remove are all the same text-commit path.
2. **Row + explicit remove/add actions** — a read-only look with a per-row
   edit toggle, a separate remove affordance on a new controller action, a
   separate non-text "+ Add" row.
3. **Confirm-on-close batching** — buffer edits locally, write once when the
   drill-in closes.

## Decision Outcome

Chosen option: **"Uniform editable text row,"** because CK already ships the
exact base class this needs — `RadicalMenuOptionTextInput` (the same one
`CharacterCustomizationOption_NameInput` uses for the character-name field) —
giving on-screen-keyboard support, focus/blink handling, and the read-vs-edit
visual split for free. One new row component covers add, edit, and remove,
with **no** new controller/keyboard input plumbing (deferring all of
ADR-002 §6's findings to the still-unbuilt format-override toggle).

Key sub-decisions:

- **Commit path:** on a row's confirmed `Deactivate`, the screen reads every
  row's `GetInputText()`, drops empties, joins with `,`, and writes through
  `ConfigEntryBase.BoxedValue` — the same non-generic write path
  `SettingWidget` already uses. No new persistence mechanism; CoreLib's
  `SaveOnConfigSet` auto-save applies unchanged.
- **Comma sanitization:** a token's text is stripped of literal `,` at commit
  time (not via `characterWhiteList`, an inclusion filter unsuited to
  blocking one character) — a typed comma can never desync the stored
  split/join.
- **Empty list stays a `List`:** classification runs once at discovery time
  and is not re-evaluated after edits; a list emptied to zero tokens does not
  fall back to `Info`. `ListKindStore` (a small, sticky,
  `API.ConfigFilesystem`-persisted memory) additionally keeps an entry
  classified as `List` even after an edit shrinks it below
  `HeuristicSaysList`'s own ≥2-token threshold on a later reopen — the
  instability ADR-002 anticipated returning "with editing."
- **The commit trigger is the `activeInputField` transition, not
  `OnDeselected`:** CK's own `UIMouse` fires `OnDeselected` on mere mouse
  hover, so committing there would end an edit the instant the mouse passed
  over another row. Two Harmony prefixes (`MenuManager.SelectOption`,
  `UIMouse.TrySelectNewElement`) additionally suppress CK's own hover-driven
  reselection and focus-stealing while a row is being edited — two separate
  mechanisms CK's own hover system uses, needing two separate patches.

### Consequences

- Good: the widest editing surface (add/edit/remove) for the smallest new
  surface area — one row class, no new input plumbing, no new persistence;
  reuses CK's own controller-first text-input idiom exactly as CK itself
  uses it.
- Bad: **`HeuristicSaysList` misclassification now has a write-side
  consequence.** ADR-002 judged a misclassification harmless in the
  read-only v1 ("a read-only miss only splits at commas, it never traps the
  user"); editing means `OnRowTextCommitted` now persists a lossy,
  comma-rejoined value straight into whatever `ConfigEntry` the heuristic
  pointed at — a third-party mod's real config, not MSM's own — with no
  confirmation step and no path back to the original formatting. Accepted as
  a known, tracked risk (flagged by this branch's
  `pr-review-toolkit:review-pr` gate) rather than fixed here:
  `HeuristicSaysList` itself is out of scope to tighten (pre-existing, shared
  with the `Info` routing path, and tightening it risks misrouting genuine
  lists the other way), and the real mitigations — a one-time confirmation
  before the first write to an unconfirmed entry, or the still-deferred
  format-override toggle — are new UX surface that belongs with that
  already-deferred work, not bolted onto this slice.
- Bad: reordering tokens is still not user-editable (insertion order is
  preserved but fixed) — deferred, not attempted here.

### Confirmation

No automated tests exist for this mod's UI (a manual in-game check is the
standing verification method — see `CLAUDE.md`). With a foreign mod's genuine
comma-list installed (PlacementPlus's `ExcludeItems` is the reference case)
and not MSM-integrated: editing an existing token and confirming persists it
across a reopen; clearing a token to empty removes it; typing into the
trailing blank row adds a new token and a fresh blank row follows; a typed
comma never splits a token after a commit+reopen round-trip; emptying every
token leaves the compact row's placeholder rather than falling back to
`Info`; and, controller-only, activating a row raises the on-screen keyboard.

## Pros and Cons of the Options

### Uniform editable text row (chosen)

- Good: CK already ships the base class this needs
  (`RadicalMenuOptionTextInput`), so on-screen-keyboard support, focus/blink
  handling, and the read-vs-edit split come for free; one row component for
  add/edit/remove; no new controller/keyboard input plumbing.
- Bad: an accidental clear-and-confirm removes a token with no dedicated "are
  you sure" step (mitigated only by the fact that navigating away without
  confirming abandons an edit — see the raw spec's edge cases).

### Row + explicit remove/add actions

> **Half of this was adopted after all (2026-08-23, ADR-005):** the "separate
> non-text '+ Add' row" is now what ships. The explicit per-row delete
> affordance is still not built and remains on the roadmap.

- Good: an explicit per-row delete affordance is harder to trigger by
  accident than clearing text to empty.
- Bad: needs three new pieces (a remove action bound to a new controller
  input, a self-rolled hint object, a separate non-text add-row class) where
  the uniform model needs one — reintroducing exactly the controller-input
  plumbing this design's key finding (§3) lets the slice skip.

### Confirm-on-close batching

- Good: a single write per drill-in session instead of one per row-commit.
- Bad: no other MSM widget batches writes this way; would need a new
  "discard unsaved changes?" path that exists nowhere else in the mod, for a
  write (a small CoreLib config set) far too cheap to justify the added
  state.

## More Information

- **Builds on** ADR-002 (`docs/adrs/002-list-widget-drill-in.md`) — the
  read-only drill-in this makes editable; its §6/§7 were this design's
  starting point.
- **Companion mechanism:** the branch that implemented this also added
  `MOD_DEV_FLAGS` (`core_keeper/utils/CLIBuildHelper.cs`) — an env-gated,
  self-healing build flag so this mod's own dev-only test fixtures (four
  disposable foreign-config rows exercising the drill-in without a real
  third-party mod installed) never ship active in a normal build.
  Mod-agnostic infrastructure, not part of this ADR's own decision.
- Nothing from ADR-002 is reverted — this ADR only adds capability to its
  read-only rows.

The full raw design (decision-driver detail, the rejected-option rationale,
and the §9 decompile evidence) is preserved in the design spec. Retrieve it
rebase-safely with:

```bash
git show "$(git rev-list -1 HEAD -- docs/specs/2026-07-28-list-widget-editing-design.md)^:docs/specs/2026-07-28-list-widget-editing-design.md"
```
