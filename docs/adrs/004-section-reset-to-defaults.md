# Section reset to defaults: a footer-hint-bar action scoped to one section

- Status: accepted
- Date: 2026-08-16

## Context and Problem Statement

Every setting MSM renders can be changed but never restored. A user who has
walked a slider away from its default has no way back short of hand-editing
the owning mod's `config.cfg` — impossible for the mods `ForeignConfigDiscovery`
mounts, since those have no settings UI of their own at all.
`ConfigEntryBase.DefaultValue` makes the restore itself nearly free
(`entry.BoxedValue = entry.DefaultValue`); what needs deciding is what a single
activation may touch, how the user asks for it, and what the screen does
afterwards.

## Decision Drivers

- Core Keeper's footer hint bar (`HelpButtonTypes`, a closed 7-value enum)
  already ships a fully-wired, unused `RESET_DEFAULTS` slot — glyph, keyboard
  hint, and localized `Menu/Reset` label all baked into the shared prefab.
- `ForeignConfigDiscovery` (ADR-001) treats a detected mod's settings as
  first-class; a reset design has to take a position on whether that extends
  to restoring them.
- The list-editing write path (ADR-003) already carries a known
  misclassification risk when writing back a foreign `ConfigEntry`; a reset
  design must not compound it.
- No automated tests exist for this mod's UI — verification is manual and
  in-game, so the design should minimize new surface that needs verifying.

## Considered Options

**Scope:**

1. One global "reset everything" row.
2. One reset per section (this mod's box).
3. A per-row "restore this one value" affordance.

**Surface:**

1. CK's dormant `RESET_DEFAULTS` footer-hint slot.
2. A dedicated row inside each section's box.
3. A control on the section header.

**Discovered (foreign) sections:**

1. Include them — reset writes back whatever the owning mod itself declared.
2. Exclude them — only sections a consumer registered through
   `ModSettings.Section`.

## Decision Outcome

Chosen: **per-section scope, surfaced through CK's existing `RESET_DEFAULTS`
hint-bar slot, including discovered sections.**

- **Scope is one section, not global or per-row.** The boundary already
  exists in the data model — one `ModSection` is one `ConfigFile` is one
  owning mod — so a reset is one file, one owner, one confirmable sentence. A
  global row was rejected: MSM renders many mods side by side, so its blast
  radius grows with the user's mod list and its confirmation can only warn
  vaguely ("reset everything?" rather than naming a mod). HealthBars' own
  `MenuOptionResetToDefaults` uses a single global row, but that is not a
  counter-example — HealthBars only ever has its own options, so global and
  per-section were the same size of gesture there. A per-row reset was
  rejected as a different interaction entirely (it would live on the row, not
  in a separate control) and left to a future, narrower design rather than
  folded into this one.
- **The trigger is CK's own footer hint bar, not a new row or header
  control.** `HelpButtonTypes.RESET_DEFAULTS` is a closed enum value that
  already has a wired prefab root, a per-platform glyph (keyboard `R`, the
  same controller face button as `openProfile`), and the shipped, localized
  label `Menu/Reset` — no vanilla code path ever requests it. Using it costs
  no Editor work and no own localization for the hint itself; a dedicated row
  or header control would have needed a new prefab element and a placement
  decision inside an already-dense section box. The hint appears only when
  the selected section's `SectionReset.CanReset` is true (at least one
  non-`ReadOnly` entry) — checked once per selection change, not compared
  value-by-value against defaults, which would make the prompt flicker as the
  user walks the list.
- **Discovered (foreign) sections are included.** A reset writes back exactly
  the value the owning mod itself declared at `Bind()` — it never invents one
  — which makes it categorically safer than the list-editing write path's
  comma-rejoin (that risk does not transfer here). Excluding discovered mods
  would remove the feature from where it is needed most (a mod with no
  settings UI of its own) and would contradict ADR-001's premise that a
  detected mod's settings are first-class citizens of this screen.
- **`ReadOnly` entries are always skipped.** A view-only or server-locked
  entry is not "resettable but locked" — it is not writable at all, so it is
  simply excluded from `SectionReset.CanReset`/`Apply`.
- **The confirmation names the mod; the hint bar does not.** A footer prompt
  cannot express which box it applies to, and the alternatives are both
  worse: highlighting the selected row's box invents a visual language CK has
  no equivalent for, and rewriting the hint text per selection fights the
  shared singleton's per-frame refresh. The confirmation dialog reuses CK's
  own `centerPopUpText.StartNewDisplaySequence` (the same call the restart
  prompt makes) with the mod's display name passed as a `string.Format`
  field — `localizePlaceholders: false`, since a literal display name would
  otherwise be looked up as a loc term and render as `<missing>`.
- **No new input plumbing beyond the poll itself.** The reset binds Rewired
  action 223 (`OpenProfile`) rather than the more obviously-named
  `MenuSecondaryActivate` (221), because an in-game probe showed the button
  the hint's baked glyph actually depicts reports 223, not 221.

### Consequences

- Good: no new prefab, no new widget class, no own localization for the hint
  bar itself (only the confirmation sentence needed a new term) — the whole
  feature rides an enum value Core Keeper had already built and never used.
- Good: because the write path only ever restores a value the owning mod
  itself declared, discovered sections are exactly as safe to reset as
  registered ones — no new risk class, unlike the list-editing write path.
- Bad: a global "reset everything" gesture (useful right after installing
  many mods at once) is out of scope; a user must reset each section
  individually.
- Bad: a per-row "restore this value" affordance is out of scope; resetting
  one setting still means resetting the whole section.
- Bad: the reset is invisible outside the footer hint bar — a player who
  never reads the bottom of the screen has no other way to discover it.

### Confirmation

MSM has no automated tests — verification is an in-game pass with the
reference consumer installed: reset a registered consumer's section and a
discovered section, confirm every affected row shows its declared default and
the corresponding config file agrees; cancel and confirm nothing changes;
reset a section carrying a `RequiresRestart` setting away from its default
and confirm CK's restart prompt appears on leaving the screen (and does not
appear when the setting was already at its default); confirm the hint bar
shows only for a section with something to reset; confirm both controller and
keyboard trigger it and the glyph shown matches the input that actually
works; confirm the selection and scroll position survive the reset (no full
rebuild).

## Pros and Cons of the Options

### Per-section scope via the footer hint bar, including discovered sections (chosen)

- Good: reuses a Core Keeper enum value, prefab, glyph and label that already
  exist and are otherwise dead code.
- Good: the confirmation can name exactly one mod, keeping the blast radius
  small and legible.
- Good: no new risk for discovered sections — the write path can only
  restore what the mod itself declared.
- Bad: no global sweep and no per-row granularity; resetting many mods means
  repeating the gesture per section.

### One global reset row

- Good: matches HealthBars' own precedent and needs only one confirmation
  ever.
- Bad: MSM's blast radius is every installed mod at once, and a single
  confirmation cannot name what it is about to touch.

### A dedicated row or header control per section

- Good: discoverable without reading the footer hint bar.
- Bad: needs new prefab/Editor work and a placement decision inside an
  already-dense box, for a control CK already gives away for free through the
  hint bar.

### Excluding discovered (foreign) sections

- Good: only ever touches config a consumer explicitly registered through
  this framework's own API.
- Bad: removes the feature from exactly the sections that have no other
  settings UI to fall back on, and contradicts ADR-001's premise that
  discovered settings are first-class.

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

The full raw design (decompile evidence for the hint-bar enum and its prefab
wiring, the Rewired action-id investigation, and the precondition checklist)
is preserved in the design spec. Retrieve it rebase-safely with:

```bash
git show "$(git rev-list -1 HEAD -- docs/specs/2026-08-16-section-reset-to-defaults-design.md)^:docs/specs/2026-08-16-section-reset-to-defaults-design.md"
```
