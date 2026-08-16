# Section reset to defaults: a footer-hint-bar action scoped to one section

- Status: accepted
- Date: 2026-08-16

## Context and Problem Statement

MSM lets a mod's settings change, but nothing lets them change back. A
registered mod's only fallback is hand-editing its `config.cfg`; a merely
*detected* mod (`ForeignConfigDiscovery`) has none at all — it ships no
settings UI of its own. Writing a default back is trivial
(`ConfigEntryBase` already stores it); the open questions are how much one
reset touches, where the player triggers it, and whether detected mods
count.

## Decision Drivers

- A reset that touches too much can only confirm with something as vague as
  "reset everything?"
- ADR-001 already treats a detected mod's settings as first-class; a reset
  design either honors that or quietly contradicts it.
- ADR-003's list-editing path already carries a known misclassification
  risk on foreign writes, which a reset must not compound.
- No automated tests cover this mod's UI, favoring reused vanilla surface
  over a new control.

## Considered Options

- **Blast radius:** one global sweep, one per mod section, or one per row.
- **Trigger:** Core Keeper's own dormant footer-hint slot, a new row inside
  the box, or a control on the section header.
- **Detected (foreign) sections:** reset them too, or hold the feature back
  to sections a consumer explicitly registered.

## Decision Outcome

A reset acts on **one section** (one mod's box), is triggered by **Core
Keeper's existing `RESET_DEFAULTS` footer hint** rather than anything new in
the box, and **includes detected sections**.

- **One section, not everything or one value.** A global sweep can only
  confirm "reset everything?" — meaningless once several mods are
  installed; per-value undo is a different feature, deferred. HealthBars'
  single global row works only because it's one mod's own screen; MSM hosts
  many.
- **The existing hint slot, not a new control.** The `RESET_DEFAULTS` hint
  already ships a wired prefab, glyph and label that no vanilla screen
  requests — free UI. Core Keeper queries it itself every frame
  (`MenuManager.LateUpdate → UpdateHelperButtons`, twice once a row is
  selected); the check only asks "can this section be reset at all", never
  "does any value currently differ from its default" — the latter, a
  rejected per-value comparison, is what would have to run every frame and
  would flicker as the player navigates.
- **Detected sections included, locked settings excluded.** A reset only
  ever writes back a value the mod itself supplied, so it carries none of
  the misclassified-list risk; excluding detected mods would strand those
  with no other undo path. `ReadOnly` entries are simply never in scope —
  whether locked for a genuine permission reason (view-only / server-locked)
  or only because no editable widget exists for that value's shape; a reset
  covers exactly what this menu shows as editable, no more.
- **The confirmation names the mod; the hint bar can't.** Highlighting a box
  or rewriting the shared hint per selection don't fit this screen, so the
  existing restart-style popup takes the display name as a plain
  substitution value — a lookup would render `<missing>`.
- **The input matches the glyph, not its label.** The obviously-named
  action wasn't the one bound to the depicted button; the one confirmed
  in-game is what the poll uses.

### Consequences

- Good: no new prefab and no new widget class — the whole feature is an
  unused vanilla slot plus a popup Core Keeper already ships.
- Good: detected mods gain the same safety net as registered ones, with no
  new failure mode introduced for them.
- Bad: no "reset everything" gesture and no per-value undo; both were real
  options and both are deferred.
- Bad: a player who never looks at the bottom of the screen has no other way
  to discover the feature.

### Confirmation

Manual, as always for this mod's UI: reset a registered and a detected
section and confirm every value and its on-disk config match the defaults;
cancel and confirm nothing moved; reset a restart-flagged setting and
confirm the game's restart prompt appears on leaving the screen but stays
silent when nothing changed; confirm the hint only shows when there is
something to restore; and confirm both input methods trigger it, the
selection and scroll survive, and the glyph matches the input that works.

## Pros and Cons of the Options

### Per-section reset via the existing hint slot, detected sections included (chosen)

- Good: reuses dead vanilla surface instead of building new UI.
- Good: a confirmation that can name exactly one mod.
- Good: no new risk class for detected mods.
- Bad: no bulk sweep, no per-value granularity.

### One global reset

- Good: one gesture, one confirmation, matches HealthBars' own precedent.
- Bad: a confirmation this broad can't say what it is about to undo.

### A row or header control per section

- Good: visible without reading the footer hint bar.
- Bad: new prefab and a placement fight inside an already crowded box, for
  something the hint bar already gives away free.

### Holding the feature back from detected sections

- Good: never touches config outside what a consumer explicitly declared.
- Bad: withholds it from the mods that most need it, and contradicts
  ADR-001's own stance on detected settings.

## More Information

- **Builds on** ADR-001 (`docs/adrs/001-generic-corelib-config-discovery.md`)
  — discovered sections are in scope because that ADR already treats them as
  first-class.
- **Distinct from** the planned Button/Action-Row widget (`docs/roadmap.md`
  § "Planned widgets"): that is a declaration API for a consumer's own
  callback rendered as a row; this is framework-owned logic surfaced through
  the footer hint bar, not a row at all.
- A worked precedent from the foreign, source-readable mod HealthBars
  (`MenuOptionResetToDefaults`, one global row) confirmed the restore
  mechanism and the confirmation-popup approach, but its single-mod global
  scope does not transfer to a framework hosting many mods side by side.
- **Refines** ADR-002's input findings (`docs/adrs/002-list-widget-drill-in.md`
  § More Information): that ADR recommended Rewired action 221
  (`MenuSecondaryActivate`) as the controller path for a future feature. An
  in-game probe for this feature found 221 dispatched through
  `InputReceiver.OnAlternate()` on the Square button, while the
  `RESET_DEFAULTS` glyph is the Triangle one shared with `openProfile` — so
  the reset poll binds action 223 (`OpenProfile`) instead, the action that
  button actually reports.

The full raw design (decompile evidence for the hint-bar enum and its prefab
wiring, the Rewired action-id investigation, and the precondition checklist)
is preserved in the design spec. Retrieve it rebase-safely with:

```bash
git show "$(git rev-list -1 HEAD -- docs/specs/2026-08-16-section-reset-to-defaults-design.md)^:docs/specs/2026-08-16-section-reset-to-defaults-design.md"
```
