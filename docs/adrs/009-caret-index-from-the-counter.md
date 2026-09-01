# Read the caret's index from the row's own counter, not from the blinker

- Status: accepted
- Date: 2026-09-01
- Supersedes [ADR-007](007-horizontal-text-scrolling.md) **in part** — only its "the caret is read from the blinker,
  not from an index" consequence; that ADR's masking, `maxWidth: 0` and
  scroll-offset decisions stand unchanged, and the blinker is still what the
  offset follows

## Context and Problem Statement

An editable drill-in row needs the caret's **character index** in three places:
to insert a keystroke where the caret is, to compute a word-jump target, and to
move the caret to a mouse click. `RadicalMenuOptionTextInput` keeps that index in
a private `currentCharIndex` (`Pug.Other:343320`), so ADR-007 derived it instead,
from the caret's on-screen position — which the game writes into a public blinker
component every frame anyway, for the scroll offset.

Deriving costs two things, both of them known and both of them paid:

- **It is exact only while `PugText`'s glyph list holds one entry per
  character.** Four documented paths break that (a character the font has no
  glyph for, a pause sign, a colour tag, an exhausted glyph pool), and the row's
  font can take a TMP path that leaves the list empty while the text is not.
  `TextFieldViewport.IndexSpaceIsSound` exists for no other reason than to catch
  it, and where it fires, all three features degrade at once.
- **It is a frame behind.** The blinker is repositioned once per frame in
  `Update` (`Pug.Other:343386-343388`), so code running inside another
  component's update reads the index from before that frame's movement. The
  word-jump postfix on `MenuManager.HandleTypingInput` therefore carried a
  compensation term, and that term had a case it could not see: a Backspace
  auto-repeating in the same frame sends vanilla down its Backspace branch, no
  arrow shift happens, and the correction then moves the caret one character too
  far.

ADR-007 recorded the private field as reachable but not worth reaching for. Only
that second half changed: the SDK's own reflection surface gets at a private
member legally inside the Roslyn sandbox, and a sibling mod in this family
(`item-checklist`) was already doing exactly that for a private method.

## Decision Drivers

- The insertion point is written into **another mod's config file**. An index
  that is merely usually right is the wrong kind of input for that.
- A compensation term whose premise is "the value I read is one frame stale" is
  load-bearing and invisible. Removing the staleness removes the term; keeping
  both sources would keep the term and make it conditional.
- The private read must be legal in the sandbox without `skipSafetyChecks:
  true`, which would also flip the mod's derived mod.io `Access Type` tag.
- It runs on every keystroke, so its cost has to be known rather than assumed.

## Considered Options

1. **Keep deriving from the blinker** — the status quo.
2. **Read the counter, keep the derivation as a fallback** for the case where
   the field cannot be resolved.
3. **Read the counter, with no fallback** for the caret; keep the derivation
   only where nothing else can answer.

## Decision Outcome

**Option 3.** `TextFieldViewport.TryCaretIndex` reads `currentCharIndex` through
`API.Reflection`; a mouse click still crosses from a *position* into an index
through the glyph list, because a pointer has no index and nothing else can
answer that question.

**Two different gates have to be cleared, and they are easy to confuse.** The
Roslyn sandbox permits the *source*, for the plain reason that `PugMod.MemberInfo`
and `API.Reflection` appear on none of its deny lists (`docs/ck/sandbox.md`
§ "Reaching a private member"). `InvokeChecker.CheckType` (`PugMod.Loader:552`)
is a **runtime** gate inside `API.Reflection.GetValue`: it inspects the member's
declaring type and never the calling mod, so it can only make a sandbox-legal
read *throw* — it can never make an illegal one compile. It refuses
`[DisallowPatching]` types (five in the whole game, four filesystem and one
networking) and `PugMod.Loader` itself, and admits by assembly-name prefix —
`Pug`, `Unity`, `SpriteInstancing`, `I2`, `Rewired`. `RadicalMenuOptionTextInput`
is in `Pug.Other`, so it passes.

The member lookup has to name the declaring type, not the subclass:
`GetMembersChecked` calls `type.GetMembers(Instance|Static|Public|NonPublic)`
(`PugMod.SDK.Runtime:644`), and a private field is only ever reported by the type
that declares it.

Option 2 was rejected for the reason the compensation term illustrates. The two
sources answer as of different moments within a frame, so a caller that has to
account for that lag cannot account for "whichever source happened to reply". The
robustness it buys is also thinner than it looks: an update that renames
`currentCharIndex` is one this mod does not survive elsewhere either, since
`MenuManager.HandleTypingInput` is patched by string name and would stop binding.
(The stale decompile citations such an update leaves behind are a documentation
cost, not a failure mode — a mod whose every comment has rotted still runs, and
this argument does not rest on them.)

### Consequences

- **The soundness check stays, and its one remaining caller is the mouse.**
  ADR-007's `IndexSpaceIsSound` guards the click alone, and its blast radius
  shrinks with it: where an empty glyph list would have stored a typed word
  reversed in a foreign config file, it now costs a click that leaves the caret
  where it was. It needed a trigger of its own to keep working, though — reached
  only from the click, a keyboard-only session would never learn that the glyph
  list had gone empty, and that fault is not confined to clicking, since vanilla
  places the blinker from the same list (`Pug.Other:343387`). `Tick` therefore
  calls it for the warning while a row is being edited, discarding the verdict.
- **The word jump loses its compensation term**, and the Backspace-auto-repeat
  case that term could not handle goes with it. The jump is now the plain
  difference between the boundary and where the caret is.
- **The keystroke path gains a clamp it deliberately did not have, and a warning
  with it.** While the index came from a reconstruction, clamping was the very
  move that turned "no answer" into "the front of the string". A trusted index
  inverts that: vanilla guards the same overrun on its own way in
  (`Pug.Other:343431-343434` — log, then correct), and unclamped it would throw
  out of a Harmony prefix for as long as the state held. Adopting vanilla's
  correction meant adopting its report too: the prefix returns `false`, so
  vanilla's own `LogError` no longer runs for these rows, and a silent clamp
  would leave a recurring caret/text desync looking like a one-off mis-click.
- **The blinker keeps its within-frame nudge, for a smaller reason.** It used to
  protect an index as well; now that nothing reads an index off the blinker, it
  protects only the caret the player sees. `CaretLocalX` has one consumer left,
  the scroll offset, and is private accordingly.
- **Losing the counter costs all three features at once**, with one logged
  warning and no text loss or reordering: typing appends at the row's end, a word
  jump falls back to vanilla's own single-character move, click-to-place does
  nothing. Making that true took more than the null check it first looked like.
  `FirstOrDefault` matches on name alone, and `API.Reflection.GetValue` signals
  every refusal by throwing, so a game update that keeps the name and changes the
  shape — a method, another type, an enum — would have thrown once per keystroke
  out of a Harmony prefix that returns before vanilla's own `AppendString`: the
  keystroke silently dropped, which is the opposite of the promise. The read is
  therefore wrapped, and the resolution too, since a throwing static initialiser
  would take both warning latches with it.
- **The viewport now binds to the row, not to its parts.** `Bind` takes the
  `RadicalMenuOptionTextInput` and reads `pugText` and `characterMarkBlinker` off
  it, because the caret read needs the row itself and the other two are its own
  public fields — passing all three in would let a viewport be bound to one row's
  caret and another's glyphs.
- **`API.Reflection` is now a second load-bearing SDK surface for this mod**,
  alongside Harmony. It costs no dependency: `GetMembersChecked`,
  `GetNameChecked` and `API.Reflection` all live in `PugMod.SDK.Runtime`, which
  the runtime asmdef already references.

### Confirmation

Verified in game against the `Overlong` and `WithSpaces` fixtures: typing lands
at the caret after Home, End and a click; a click places the caret on the
character pointed at, including near the right edge of a scrolled row; and an
edit still round-trips through commit and reopen. None of the latched warnings
fired and the log carries no exception, which is what says the counter was really
read — under the fallback the Home-then-type check would simply have failed, so
the checks are not passing on it silently.

One thing looking cannot settle: the ordinary word jump behaves **identically**
with and without the retired compensation term, at both ends and in the middle —
that term was correct in every case it could see, which is why it shipped. Only
the auto-repeat case differs, and it is not cleanly provokable by hand. So the
removal rests on the arithmetic and on the counter's semantics, not on the
walkthrough; `docs/manual-tests.md` says so at the step rather than implying
otherwise.

**The read cost was measured rather than argued**, because MSM-22 asked for that
before this was committed to: **3.57 µs per read** once resolved, and **0.404 ms
for the first `TryCaretIndex` call** — which is where the member scan lands,
since the class is `beforefieldinit` and initialises on first static access
rather than at load. That first call may also pay `InvokeChecker`'s one-off walk
over every type of every loaded assembly (`PugMod.Loader:541-548`), but only when
this mod is the first thing in the process to touch `API.Reflection`: the checker
belongs to the single shared `ModAPIReflection` (`Pug.Other:392596`), so a
sibling mod calling first pays it instead. A keystroke costs one read — 0.02 % of
a 60 fps frame — and even the first call stays inside a single frame, so the
warm-up this might have needed in `Bind()` is not needed at all. Measured under
Wine, and no non-Wine baseline was taken: it is one host's number, not a ratio.

## Pros and Cons of the Options

### Read the counter, no fallback (chosen)

- Good, because the insertion point is the value vanilla itself inserts at, with
  no assumption between them.
- Good, because one source with one frame semantic retires a compensation term
  rather than making it conditional.
- Bad, because a renamed field takes all three features rather than degrading
  one — accepted, since the mod does not survive such an update elsewhere either.

### Read the counter, keep the derivation as a fallback

- Good, because it survives a renamed field.
- Bad, because the two sources answer as of different moments within a frame, and
  a caller compensating for that lag cannot compensate for "whichever replied".

### Keep deriving from the blinker

- Good, because it needs no reflection and one source serves the offset too.
- Bad, because the insertion point stays exact only while glyph count and
  character count agree, and the word jump keeps a term with a case it cannot
  see.

## More Information

- [ADR-007](007-horizontal-text-scrolling.md) built the viewport this changes and recorded the derivation as a
  choice rather than a necessity — including the pointer to `API.Reflection`
  this decision follows.
- [ADR-003](003-list-widget-editing.md) is where the row became editable, which is what makes a wrong
  insertion point reach a foreign config file.
- `docs/ck/sandbox.md` § "Reaching a private member" in the shared handbook has
  the two halves of the recipe and why they cost no dependency.
