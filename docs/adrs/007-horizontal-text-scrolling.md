# Clip a drill-in row's text with a second mask, not by shortening it

## Context and Problem Statement

A drill-in row could show only as much of a value as fitted its width, and the
part that did not fit was **discarded**, not hidden. Viewing such a row was safe,
but editing it destroyed data: the user typed against the shortened text on
screen, and the invisible remainder was gone with nothing in the UI hinting at
it. Measured with a 57-character token, appending one character silently dropped
four.

The cause was that `RadicalMenuOptionTextInput.maxWidth` is a **capacity**, not a
viewport. It is enforced twice, and the two enforcements are not symmetric — one
trims the text every frame, the other refuses input outright.

This also blocked the planned per-row delete and reorder buttons: they take a
quarter of the row's width, which would have turned a rare trap into an ordinary
one.

## Decision Drivers

- The row's **chrome** must stay clipped vertically by the list viewport, while
  its **text** is clipped horizontally by the field. Two different rectangles on
  one row.
- Whatever holds the value must remain the single source of truth, because the
  row already distinguishes a user's edit from a machine-made shortening by
  timing, and that distinction protects a foreign mod's config file.
- New prefab objects belong in the Editor: a batchmode build reserialises
  prefabs and drops hand-authored ones.

## Considered Options

1. **Two `SpriteMask`s with disjoint sorting ranges**, the row's one re-fitted per
   frame to its intersection with the list viewport, plus a caret-following
   offset on the text transform.
2. **A substring window** — render only the visible characters and keep the true
   value beside it. No second mask.

## Decision Outcome

**Option 1.** Option 2 would make the rendered text stop being the value, and the
row's edit detector reads exactly that text to tell a keystroke from a machine
shortening — scrolling would change it without a keystroke and defeat the guard
this feature exists to protect. It would also have required replacing three of
the base class's text methods rather than one, and `GetInputText` is a
non-virtual interface member that cannot be redirected at all.

### Consequences

- **`maxWidth` goes to `0`, and `AppendString` must be replaced in the same
  breath.** Its rejection is *not* gated on `maxWidth > 0` the way the per-frame
  trim is, so clearing the limit alone makes the condition true for every
  non-empty string and the field accepts nothing. This asymmetry is the single
  most surprising detail of the change.
- **A length limit has to be reintroduced deliberately.** Vanilla's width check
  was also the only cap on the keyboard path; without a replacement, a paste
  writes unbounded text into another mod's config. The on-screen keyboard's own
  limit serves as the value.
- **The caret is read from the blinker, not from an index — by choice, not by
  necessity.** The offset needs a *position* regardless, and the game writes the
  caret's into a public component every frame, so deriving the character index
  from that same source keeps one source of truth instead of two. It is not the
  only route: `currentCharIndex` is private, but `API.Reflection` reaches private
  members legally inside the sandbox (`docs/ck/sandbox.md` § "Reaching a private
  member"), and reading it would be authoritative where this is derived. The
  trade is paid for in the next consequence — and unpaid, it was a real defect
  rather than a theoretical one.
- **Anything that reads that caret must account for when the game last wrote
  it.** It is refreshed once per frame, so code running inside another
  component's update — a Harmony patch on the game's own input handling — sees
  the value from before that frame's movement and has to compensate.
- The guards that used to keep the per-frame trim out of a config file still
  work but no longer guard anything. They are kept as redundancy, and the prose
  around them says so rather than describing a live mechanism.
- **The viewport has to hold state, and the obvious stateless form is wrong.**
  Deriving the offset from the caret alone is simple, cannot drift, and is
  trivially verifiable — which is why it was built that way first. It also pins
  the caret one character inside the right edge whenever the text is scrolled at
  all, so jumping backwards into a long value reveals nothing of what follows the
  caret: the very case this exists for. The offset is therefore carried across
  frames and moved only when the caret leaves the visible window, at either edge.
  The carried value is read back from the text transform rather than kept in a
  field of its own, so it cannot quietly disagree with what is on screen after a
  rebuild.

### Confirmation

Verified in game against fixtures written for it — a token of the same 57
characters the original hazard was measured with, and a list whose entries
contain spaces, so word jumps have boundaries to find. The whole token is
reachable by moving the caret; an edit round-trips through commit and reopen
without loss; the view holds still while the caret stays inside it and follows
only when it leaves; text ends flush against the field edge rather than short of
it; unfocused rows rest at their text start; and scrolling the list leaves
nothing outside it.

Glyph rendering is the one property looking could not settle, because its failure
is a single pixel row tipping into the neighbouring cell — visible, but not
reliably reproducible on demand. Every offset the code can produce was enumerated
instead, across all five branches, and none falls on the pixel grid; the closest
approach is five thousandths of a unit. The in-game check then confirmed what the
enumeration predicted.

## More Information

The mask mechanics are **not** specific to this mod and live in the shared
handbook — `docs/ck/ui-framework.md` § "Two masks over one renderer combine as
OR, not AND" carries why two masks widen rather than narrow, why their bands
must abut rather than overlap, and the three ways that arrangement fails
silently and identically.

The raw design document that preceded this decision, with the full measurement
record and the rejected option's detail:

~~~
git show "$(git rev-list -1 HEAD -- docs/specs/2026-08-25-horizontal-text-scrolling-design.md)^:docs/specs/2026-08-25-horizontal-text-scrolling-design.md"
~~~

Related: [ADR-003](003-list-widget-editing.md) built the editable row this extends, [ADR-005](005-drill-in-row-model.md) the row model the
masks hang in, and [ADR-006](006-list-detection-heuristic.md) widened which values can reach the drill-in at all —
which is what moved this from a nicety to a mitigation.
