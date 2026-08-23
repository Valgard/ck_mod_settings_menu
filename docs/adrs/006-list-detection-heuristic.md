# List detection: at most one space per token, instead of a length limit

- Status: accepted
- Date: 2026-08-23
- Supersedes the list-detection rule of
  [ADR-002](002-list-widget-drill-in.md); that ADR's presentation decisions
  stand unchanged

## Context and Problem Statement

A foreign `string` setting is classified at discovery time:
`ForeignConfigDiscovery.HeuristicSaysList` decides whether it becomes an editable
`SettingKind.List` with a drill-in, or a read-only `SettingKind.Info` row. ADR-002
set the rule as **≥ 2 comma tokens, each ≤ 32 characters, none containing `.`**
and did not derive the 32.

Both halves of that threshold turned out wrong in the same session:

- **It lets prose through.** `"This is a long sentence, and another one"` splits
  into tokens of 22 and 15 characters. Both clear the limit, so a sentence is
  offered as an editable list — and committing it rejoins it on commas, quietly
  reformatting the owning mod's text.
- **It refuses legitimate entries.** An ordinary long identifier
  (`AncientGuardianStatueFragmentPolishedObsidianVariantLarge`, 56 characters) is
  a perfectly good list member, but falls through to an `Info` row — which then
  also breaks that row's layout, because the `Info` path was never built for
  values that wide.

Length was never the property worth testing. What separates a list from prose is
how many words a token contains.

## Decision Drivers

- The classification decides whether a third-party value becomes **writable**, so
  a false positive is worse than a false negative: a misclassified entry is
  rejoined on commas and written back (ADR-003's recorded consequence).
- The rule runs at discovery on values this framework has never seen, from mods it
  does not know. It must be cheap and explainable, not clever.
- `ListKindStore` makes any positive classification **sticky**, so a false positive
  is not self-correcting.

## Considered Options

1. **Raise the length limit** (e.g. to 64) and keep everything else.
2. **Replace length with internal word count** — a token may contain at most one
   space.
3. **Drop the heuristic**, let a consumer or the user declare the format.

## Decision Outcome

**Option 2.** A token may contain at most one space; the `.` rule is unchanged.
Identifiers (`InventoryChest`) and two-word names (`Copper Ore`) stay lists, while
anything with more internal spacing reads as prose and stays an `Info` row.

The rule is deliberately not airtight — a three-word item name is now misjudged as
prose. That direction is the safe one: it withholds an editable drill-in rather
than offering one for a value that should not be rejoined on commas.

### Consequences

- **Some existing entries change kind.** A foreign value that was an `Info` row
  because one token exceeded 32 characters becomes an editable list. That is the
  intent, and it is why the change belongs in the same pass as the drill-in's
  write-path hardening (ADR-005) rather than on its own.
- **The false-positive risk moves, it does not vanish.** Prose whose every token
  has at most one space still passes. The mitigation remains the roadmap's
  format-override / one-time confirmation item, which is still unbuilt.
- **A three-word value can no longer be edited in the drill-in.** No data is lost;
  it is shown read-only.
- The change is invisible to consumers using the explicit API — the heuristic only
  ever applies to *discovered* foreign config.

### Confirmation

Two fixtures, behind `DevFlags.Is("TestFixtures")`: `Overlong` carries a 56-character
identifier and must appear as an editable list with a drill-in; `ProseNotAList`
carries `"This is a long sentence, and another one"` and must stay a read-only
`Info` row. The second is the case ADR-002's rule was meant to catch and never did.

## Pros and Cons of the Options

### At most one space per token (chosen)

- Good, because it tests the property that actually distinguishes the two shapes.
- Good, because it is one pass over the string with no configuration.
- Bad, because multi-word names are misjudged — accepted, since the error falls on
  the read-only side.

### Raise the length limit

- Good, because it is a one-character change.
- Bad, because it keeps testing the wrong property: prose still passes, and the new
  threshold would be as underived as the old one.

### Drop the heuristic

- Good, because guessing is replaced by knowing.
- Bad, because a *discovered* mod by definition declares nothing — that is what
  makes it discovered. Deferring to the user needs the format-override UI the
  roadmap still lists as unbuilt, so this cannot be the answer today.

## More Information

- ADR-002 (`002-list-widget-drill-in.md`) placed this heuristic in `BuildDef` and
  is otherwise unaffected.
- ADR-003 (`003-list-widget-editing.md`) records why a false positive matters: the
  drill-in writes back.
- `docs/roadmap.md` § "Small fixes" carries the format-override / confirmation item
  that would let a user overrule the guess.
