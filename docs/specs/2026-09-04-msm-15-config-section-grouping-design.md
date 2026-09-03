# Design — Grouping a mod's rows by config sections (MSM-15)

- **Date:** 2026-09-04
- **Status:** Approved (design); implementation pending
- **Builds on:** ADR-010 (a heading is a plain MonoBehaviour that never enters
  `menuOptions`). That point shipped the widget; this one gives both paths a
  reason to emit one.

## 1 · Goal

A mod with twenty settings renders as one flat list. This point groups those
rows — for a **registered consumer**, which declares its groups, and for a
**discovered mod**, whose `.cfg` already carries sections that nothing reads.

Neither half needs a new widget: a group heading is a `SettingDef` with
`Kind == SettingKind.Label`, which `ModSettingsScreen` already renders as a
`LabelRow` and navigation already steps over. There is no prefab work and no
Editor step in this point at all.

## 2 · Decisions (locked)

1. **Both paths, not only discovery.** The roadmap left open whether grouping
   applies to a registered mod. It does. A consumer can already group the
   *screen* with `.Label()`; what it cannot do is give its `.cfg` the same
   structure, because `SectionBuilder` binds every key into the literal section
   `"Settings"` (one call site, `SectionBuilder.BindGuarded`).
2. **A method of its own — `.Group(key)`** — rather than a parameter on
   `.Label()`. Rejected alternatives and why, in §9.
3. **`.Group()` does two things and says so:** it emits the heading row, and it
   sets the CoreLib section every later declaration binds into.
4. **Nothing changes before the first `.Group()`.** A consumer that never groups
   keeps binding into `[Settings]`, so every existing consumer is unaffected
   without editing a line.
5. **A moved value is recovered, not lost.** Changing the section a key binds
   under would otherwise strand the stored value: CoreLib keeps it, but reads it
   under a definition nobody asks for any more. MSM adopts it — automatically
   from `[Settings]`, and on declaration from any other group (§5).
6. **Discovery groups only from two sections up.** One section — the common case
   — would produce a single heading directly under the box heading that names the
   same mod.

## 3 · Consumer API

```csharp
ModSettings.Section(this)
    .Group("combat")                        // heading + [combat] in the .cfg
        .Toggle(out _, "friendlyFire", false)
        .Slider(out _, "damage", 1f, 0f, 2f)
    .Group("world")
        .Label("advanced")                  // heading only, still in [world]
        .Stepper(out _, "seed", 0, 0, 999)
    .Build();
```

- `SectionBuilder` holds a current section, initially `"Settings"`, and
  `BindGuarded` passes it to `ConfigFile.Bind` in place of today's literal.
  That is the whole of the bind-side change.
- The `SettingDef` `.Group()` appends is the same shape `.Label()` appends, so
  it inherits every behaviour that already exists for a heading: it is a segment
  boundary under `SortOptions(ByKey/ByLabel)`, `RequiresRestart()` refuses to
  attach to it, and `Build()`'s duplicate-key report sees it — a group key and a
  setting key share one term space (§6) and would otherwise render as two rows
  with the same text.
- **The group key is validated at declaration, not at the first bind.**
  `ConfigDefinition` rejects `= \n \t \ " ' [ ]` and leading or trailing
  whitespace. Left to the bind, an invalid name would fail once per setting in
  the group, and `BindGuarded` would drop each of them from the menu with a
  separate error — a broken group name costing every row it contains.
  `.Group()` therefore checks first and, on failure, **declares nothing**:
  no heading, no section change, one message naming the offending character.
  Declarations after it keep binding into whichever section was in force before —
  `"Settings"` for the first group, the previous group otherwise — so a rejected
  group costs its heading and nothing else. Emitting the heading anyway would put
  a group on screen that the file does not have.

## 4 · Discovery path

`ForeignConfigDiscovery.BuildSection` already reads `definition.Section` — for
the per-entry id and for the GMCM terms — and then discards it. It now groups by
it:

- **From two sections up.** A single-section file gets today's flat list. This
  rule is deliberately absent from the consumer path: there, one `.Group()` is a
  declaration, and a declaration is honoured. The same split runs through this
  mod everywhere — discovery infers, a consumer states.
- **Groups in alphabetical order; entries within a group keep `ByKey`.** Two
  reasons, and either would do. `ConfigFile.Save` writes sections
  `OrderBy(x => x.Key)`, so the `.cfg` a player opens is alphabetical and the
  screen now agrees with it. And the alternative — the order of `cf.Entries` —
  is a `Dictionary`'s enumeration order, which .NET does not guarantee.
- **No sorting code is added.** `SortWithinSegments` already treats every
  `Kind == Label` as a boundary and leaves it in place, so emitting each group's
  heading followed by that group's defs is enough for the existing `ByKey` sort
  to order within groups and across none.

## 5 · Migration

Before binding a key into a group, MSM looks for the value under its previous
definition, in this order:

1. **The declared source**, when the consumer passed
   `.Group("fight", movedFrom: "combat")`. The author's statement wins.
2. **`[Settings]`** — both when nothing was declared and when the declared source
   held nothing. The fallback is not a courtesy: a player who skipped the version
   that introduced `[combat]` still has the value under `[Settings]`, and only
   this step reaches it. It is not a guess either — `ConfigStore` creates one file
   per consumer (`<ModId>/config.cfg`) and MSM has only ever bound into
   `[Settings]`, so an orphan there in a registered consumer's file can only be
   MSM's own.

**The mechanism is a move inside `OrphanedEntries`, not a read-and-write.**
`ConfigFile.Reload` files every line without a bound entry there, and
`ConfigFile.Bind` already adopts one whose `ConfigDefinition` matches exactly.
So MSM re-keys the orphan onto the target section, removes the old key, and lets
CoreLib's own path deserialize and persist it.

What is said, and what is not:

- **Adopted** → one log line per key, naming both sections.
- **Nothing found, `movedFrom` declared** → silence. After the first successful
  launch there is nothing left to migrate, while the declaration stays in the
  consumer's chain forever; a "nothing to migrate" line would appear on every
  start for every player from then on.
- **Nothing found, none declared, but an orphan of the same key sits in some
  other section** → a warning. That is the unexplained move, and it names
  `movedFrom` as the fix rather than only stating the finding. MSM must not
  adopt it: an author may legitimately keep `[combat] damage` and
  `[world] damage` side by side, so a cross-section adoption would be a guess.
- **`movedFrom` equal to the group itself** → reported from `.Group()` itself,
  beside the character validation above, since it is a declaration error and
  needs nothing that happens later to recognise it. The group stays valid; only
  its migration is dropped.

## 6 · Localisation

- **Consumer:** `<Mod>-Config/<groupKey>`, exactly the form a `.Label()` key
  takes (`MsmTerms.Label`). No new schema.
- **Discovery:** MSM's own schema, then GMCM's `<Mod>_<file>/<Section>`, then the
  raw section name — the same three-stage chain a discovered row's label uses,
  one level up. GMCM's per-section heading is the fourth `Compose` shape
  `GmcmTerms` deliberately does not build yet; its own documentation names this
  point as where it becomes due.
- **The settings' term space is NOT segmented by group.** A key stays
  `<Mod>-Config/<key>` whether or not it sits in a group. Segmenting it would
  force every consumer that adopts grouping to rewrite its whole yaml, and the
  screen would fall back to raw keys until it did.

## 7 · What does not change

- **The section reset (ADR-004)** stays file-wide, which is mod-wide. Its
  `IsInScope` filters on `Entry != null`, so headings — group or plain — are
  skipped, as they already are.
- **`ListKindStore` ids** already contain the section, so a grouped discovery
  changes no id and no remembered classification.
- **`ModSection`** stays one box per mod. A group is a row inside it, never a
  second box.

## 8 · Verification

There is no test framework here; verification is a walk through the menu,
recorded in [`../manual-tests.md`](../manual-tests.md). Both paths already have
dev fixtures behind `DevFlags.Is("TestFixtures")` — raw `ConfigFile`s for
discovery, `AddDeclaredFixtures` for the consumer path. This point adds:

- a discovery fixture file with **two** sections, to see grouping and its order;
- one with exactly **one** section, which must render no heading;
- a declared group, and a plain `.Label()` inside it that must not change the
  section;
- an **adoption** fixture: a key bound ungrouped and given a value, then bound
  into a group on the next launch, so the value is observed surviving the move
  and the old line is observed leaving the `.cfg`;
- a declared `movedFrom` whose second launch must produce no log line at all.

## 9 · Rejected alternatives

- **`.Label(key, groupInto: …)`** — the file-layout effect is the more
  consequential of the two and would not be named by the method. ADR-010 rejected
  the same shape for `RequiresRestart()` attaching to a heading, for the same
  reason: a silent second effect that a reader of the call cannot see.
- **A separate `.ConfigSection(key)`** — the two would be declared together
  almost every time, and could drift apart unnoticed (heading "Combat", section
  `misc`).
- **Discovery-only grouping** — rejected by the project owner: the consumer API
  is the standard path and must be able to express this, not merely the inferred
  one.
- **Automatic adoption with no declaration for group-to-group moves** — this was
  the working choice until the audience was named. It rests on the first real
  move teaching MSM the right API shape, and with third-party consumers that move
  happens in someone else's log: the author is stuck, and MSM never learns.
- **Declaration for every move, including out of `[Settings]`** — an author who
  forgets loses their players' values, and MSM would be demanding a statement
  about something it knows for certain itself.

## 10 · Out of scope

- **A renamed key**, as opposed to a renamed group. It is a real problem and an
  older one — it exists today without grouping — so it is not this point's to
  solve.
- **Resetting one group** rather than one mod. The reset's scope is the file.
- **Collapsible groups.** Core Keeper has no expand/collapse idiom in its menus
  at all; GMCM's accordion is its own invention. Recorded in the roadmap and
  unchanged by this point.

## 11 · Deliverables

Code, and with it — not after it — the documentation a third-party author needs,
since 2.0.0 is the release that makes MSM a framework for more than this
family's own mods:

- `README.md`, the consumer API reference: `.Group()`, `movedFrom`, and the
  heading term schema.
- [`../tutorial.md`](../tutorial.md): grouping in the integration walk-through.
- `CHANGELOG.md` under the in-progress 2.0.0 entry.
- [`../roadmap.md`](../roadmap.md): MSM-15 is removed; the id travels into the
  ADR, which is where the decision — public API, and therefore permanent — is
  recorded.
