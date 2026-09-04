# A group is declared, and only MSM's own history is inferred

- Status: accepted
- Date: 2026-09-04
- Builds on [ADR-010](010-heading-rows-are-not-menu-options.md), which shipped the heading row and left both paths without a
  reason to emit one
- Implements roadmap point MSM-15, which shipped with this decision and took its
  id with it

## Context and Problem Statement

A mod with twenty settings renders as one flat list. A registered consumer could
already break up the *screen* with `.Label()`, but not its file: `SectionBuilder`
bound every key into the literal section `"Settings"`. A discovered mod is the
mirror image — its `.cfg` often already carries sections, and
`ForeignConfigDiscovery` read `definition.Section` for the per-entry id and for
the GMCM terms, then threw the structure away.

Giving a consumer the file half is what makes this more than a rendering
question. A key that binds into a different CoreLib section leaves its stored
value behind — CoreLib keeps the line but reads it under a definition nothing
asks for again — so a mod tidying up its own file would silently reset every
player's setting.

## Decision Drivers

- The consumer surface is public and, from 2.0.0, serves mods outside this
  family. Nothing added to it can be withdrawn, and anything got wrong surfaces
  in a log nobody here will ever read.
- Discovery infers, a consumer declares. That split already runs through this
  mod everywhere, and a decision that contradicts it costs more than it saves.
- A player's stored value must not be the price of a mod reorganising its file.

## Considered Options

1. **A method of its own, `.Group(key)`**, which emits the heading *and* rebinds
   every later declaration.
2. **A parameter on the existing heading**, `.Label(key, groupInto: …)`.
3. **A separate `.ConfigSection(key)`** declared beside `.Label()`.
4. **Discovery-only grouping**, with no consumer API at all.

## Decision Outcome

**Option 1.** `.Group(key)` does two things — it renders a heading, and it binds
every declaration after it into the CoreLib section `key` — and the method name
is where a reader of the call site sees both.

Option 2 was rejected because the file-layout effect is the more consequential
of the two and would not have been named at the call site; ADR-010 rejected the
same shape for the same reason. Option 3 was rejected because the two would be
declared together nearly every time and could then drift apart unnoticed — a
heading reading "Combat" over a section named `misc`. Option 4 was rejected
because the consumer API is the standard path, not the inferred one.

Four further decisions came with it:

- **Moving out of `[Settings]` is adopted automatically; moving between groups
  must be declared** with `.Group(key, movedFrom: …)`. The asymmetry is not a
  compromise but a matter of what MSM can know. `ConfigStore` gives each
  consumer a file of its own and MSM has only ever bound into `[Settings]`, so
  an orphan there is its own history and adopting it states a fact rather than
  making a guess. A same-named orphan in any other section is genuinely
  ambiguous — an author may keep `[combat] damage` and `[world] damage` side by
  side, and adopting one of them would be picking. A declared `movedFrom` is
  tried first and still falls through to `[Settings]`, for a player who skipped
  the version that introduced the old group.
- **Discovery groups only from two sections up, while a consumer's single group
  is honoured.** A discovered file with one section would get a heading directly
  under the box heading that names the same mod, repeating it. One `.Group()`
  call is a declaration, and a declaration is followed.
- **A discovered section with an empty name gets no heading, and still counts
  toward that threshold.** CoreLib files every line before a file's first `[...]`
  header under the empty section name, so this is reachable rather than
  theoretical. A heading built for it resolves through all three localisation
  stages to nothing and lands on `SettingDef.Label()`'s `(unnamed)` placeholder
  — a heading that names nothing, sitting above rows that have names. It still
  counts, because a reader does see two areas whether or not the first one is
  captioned.
- **The settings' term space is not segmented by group.** A key resolves as
  `<Mod>-Config/<key>` inside a group exactly as it does outside one. Segmenting
  it would have forced every consumer that adopts grouping to rewrite its whole
  yaml, and their screens would fall back to raw keys until they did. The
  heading's own term is `<Mod>-Config/<groupKey>` — the schema a `.Label()` key
  already uses, so grouping introduces no term shape at all.

### Consequences

- **No sorting code was needed**, which is the kind of thing a later reader
  re-derives painfully. `SortWithinSegments` already treats every
  `Kind == SettingKind.Label` as a segment boundary, so emitting a heading
  followed by its own group's rows is enough for the existing sort to order rows
  within groups and across none.
- **Nothing is ever destroyed by getting a move wrong.** `ConfigFile.Save`
  writes orphans back out, so an undeclared move costs a value its visibility —
  the setting reverts to its default while the line stays in the file — and not
  the value itself.
- **The failure is reported with its remedy**: the warning names the exact
  `.Group(…, movedFrom: …)` call that would fix it, rather than only stating
  that a value is sitting somewhere unread. From 2.0.0 the person reading that
  line is a third-party author this repository will never hear from, and a
  warning that says only that something is wrong teaches them nothing.
- **A satisfied `movedFrom` says nothing at all.** After the first successful
  launch there is nothing left to migrate, while the declaration stays in the
  consumer's chain forever — so a "nothing to migrate" line would appear on
  every start for every player from then on.
- **A group name is validated at declaration and refused whole.** CoreLib
  rejects a section name containing `= \n \t \ " ' [ ]` or padded with
  whitespace; left to the first bind, one such name would fail once per setting
  in the group and `BindGuarded` would drop each of those rows from the menu
  separately. A refused group therefore emits no heading and changes no section
  — and it is the first declaration that can fail without a bind having been
  attempted, which is what makes `RequiresRestart()`'s two guards
  non-exclusive and their order load-bearing.

## More Information

The full design record — the migration mechanism inside `OrphanedEntries`, the
alternatives weighed before this was settled, and what was deliberately left out
— is the raw spec this ADR was distilled from. It is no longer in the tree; read
it from history with:

~~~bash
git show "$(git rev-list -1 HEAD -- docs/specs/2026-09-04-msm-15-config-section-grouping-design.md)^:docs/specs/2026-09-04-msm-15-config-section-grouping-design.md"
~~~
