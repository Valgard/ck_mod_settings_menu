# Design — Locked settings render as withheld (MSM-10)

Roadmap point MSM-10, with MSM-11 merged into it in full. MSM-11 is not
deferred and not split: its permission half falls out of this design as a
by-product, its colour half was already part of MSM-10, and its value-refresh
half is § 4.8 here.

Builds on [ADR-012](../adrs/012-permission-and-restart-cascade-from-the-nearest-declaration.md),
which shipped the vocabulary this design renders. Before it, no consumer could
state that a setting belongs to the server, and `SettingDef.ReadOnly` conflated
two unrelated claims. Both are now settled, which is what makes this point
buildable.

**Citations.** Game-code line numbers are from the
`CoreKeeperDecompile-1.3.0.2-182b` checkout. The canonical `CoreKeeperDecompile`
symlink resolves to 1.3.0.4, whose bodies are identical but whose lines sit
elsewhere — `IsSelectionEnabled` is 358545 here and 359560 there. Every number
below was read in the 1.3.0.2 directory; a review of the previous draft found
one taken from the symlink, so this is a rule with a known violation history.

## 0 · Three states that look alike

The whole design turns on keeping these apart, and the first two were one
field until ADR-012 split them.

| State | Source | Changes during a session? |
|---|---|---|
| **contextually locked** | `Server`/`Admin` scope, and either no player, no rights, or guest mode | **yes** — the same row is editable in a session with rights |
| **view-only** | the author declared `ConfigAccessLevel.ViewOnly` | no — never editable through the menu, for anyone |
| **shapeless** | `Kind == Info`: no editable widget exists for this value's form | no — a property of the value, not of permission |

`SettingDef.Locked` answers the first two together; `IsEditable` excludes all
three. This design introduces a question narrower than either, defined in § 4.1.

**A row can be more than one of these at once**, and that is the trap an earlier
draft of this document walked into: a discovered entry of an unhandled type gets
`Kind = Info` while keeping its `ConfigEntry` and that entry's scope
(`ForeignConfigDiscovery.cs`, routes 4, 6 and 7, each marked "regardless of
scope"). With no scope stated, CoreLib's default is `Server`, so at the title
screen such a row is shapeless **and** contextually locked. Only the shapeless
half may show.

Throughout this document, **"locked"** on its own means *contextually* locked.
Where `SettingDef.Locked` is meant — the wider property that includes
`ViewOnly` — it is written as code.

## 1 · Goal

A setting that exists but cannot be changed right now renders in Core Keeper's
own shipped convention, says why, and keeps saying it while the screen is
open — including when the answer changes while the player is looking at it.

Three things follow, and the third is the merged MSM-11:

1. The row renders as withheld: dull red, still visible, still occupying its
   place. For a settings row it is also skipped by navigation and unclickable;
   the list row keeps both, for the reason in decision 2.
2. The reason is on screen. A red row whose cause is invisible is worse than no
   lock: the player sees a refusal with no way to resolve it.
3. Nothing is frozen at open.

**The change this third point is for comes from someone else.** Every way the
player could flip their own permission state is unreachable while this screen
is open: `AddOrUpdateAdmin`, `RemoveAdmin` and `SetGuestMode` all sit behind one
admin check placed before the whole command switch (`Pug.Other:136440`, warning
at `:136442`, with `SetGuestMode`'s case at `:136538` inside it), *and* behind
another menu — and `ModSettingsScreen.Update()` returns early unless this screen
is the top menu (`ModSettingsScreen.cs:495`). Loading a world closes the menu.

So the live case is an **admin elsewhere in the session** changing this player's
rights, or switching guest mode, while this player stands in the menu. That is
what the roadmap described for MSM-11. It also decides § 9: a feature that
answers someone else's action cannot be observed by one actor.

## 2 · What is already there, and what is missing

ADR-012 left the vocabulary in place:

- **`SettingDef.Locked`** is computed per read, deliberately — a snapshot taken
  when the section was built would freeze the title screen's answer for the
  whole run.
- **`AccessLock.IsLocked(scope)`** is the single answer both paths reach, with
  its branch order load-bearing: `ViewOnly` and `Client` are answered before
  any player is consulted, because CoreLib's `Changeable()` dereferences the
  player above its own switch.
- **`IsEditable`** already gates every write: `SettingWidget.Adjust` returns
  early, `SectionReset.IsInScope` skips the row, the value text is rendered
  static.

What this design adds is the rendering, plus four things that are not rendering:
a narrower question (4.1), the reason text and its carrier (4.6), the repaint on
a state change (4.7), and the value refresh (4.8).

Both widgets currently rule `GRAYED_OUT` out explicitly:

```csharp
public override OptionActiveState GetActiveStateInCurrentScene() =>
    _def != null ? OptionActiveState.ACTIVE : OptionActiveState.INACTIVE;
```

## 3 · Decisions (locked)

| # | Decision | Reason |
|---|---|---|
| 1 | Only a **contextual** lock renders red. `ViewOnly` and `Info` keep today's appearance. | `GRAYED_OUT` means "normally editable, just not right now". Nothing is withheld from a row that is never editable, and greying it out would also take it out of navigation — the handbook's rule is to keep permanently read-only rows `ACTIVE` so they stay navigable. |
| 2 | **`GRAYED_OUT` everywhere, except where a row hides content behind an interaction.** Today that is the list row. | A locked list row would otherwise make its contents unreachable: the row shows a preview, the full list lives behind the drill-in. Every other row keeps label and value on screen and loses nothing by being unreachable. |
| 3 | The list row stays `ACTIVE`, and **MSM paints it red itself** — the same way it paints every other locked row (4.4). | An earlier draft routed this through an `IsSelectionEnabled(visualOnly: true)` override, which cannot work: 4.4 has to switch off the very effects that would read it. One painter, not two. |
| 4 | A locked row renders a **lightened red while selected** — the value named in 4.4 — list row and settings row alike. | Both can be selected while locked (4.5), and in the settings row's case the lock can arrive *under* the player's cursor. Leaving selection blue would hide the lock at the moment the player tries to act on the row. |
| 5 | One **note per section**, between the hint and the widgets box, carrying one of two texts. | A locked section would otherwise repeat the reason once per row. CK words its own note in the plural for the same reason. |
| 6 | The drill-in of a locked list gets **no note of its own**. | The player just came from the red row whose section note gave the reason, and the drill-in shows the lock by what is absent: no add row, no row buttons, no text field. |

### 3.1 · Why the two notes, and only two

The reasons a row can be locked collapse into two the player can act on:

| Situation | Note |
|---|---|
| no player — the screen was opened from the main menu | *Some settings can only be changed in a world* |
| in a session, rights are missing | *Some settings require permissions you do not have here* |

The first is deliberately CK's own `Menu/SettingsNotAvailableNote` mirrored —
vanilla says "can only be changed in main menu" for the opposite case, and a
player meets that wording in the vanilla options screen. MSM reflects it
instead of inventing a second phrasing for the same idea.

`Server` and `Admin` are not told apart, because the player's answer is the
same either way: rights are missing. Whether that is guest mode or an admin
level changes nothing they can do about it from here.

**The two cannot appear at once.** "No player" is a global condition
(`Manager.main.player == null`), not a property of a row. Either there is no
player — and then every `Server` and `Admin` row is locked for that one
reason — or there is one, and `Changeable()` decides on rights. A section
therefore never needs two notes at the same time, which is what makes decision
5 conflict-free.

## 4 · Design

### 4.1 · One question, in one place

`SettingDef` gains a computed property for **"withheld right now, and would
otherwise be operable"**. All three conjuncts carry weight:

```csharp
Entry != null
  && Kind != SettingKind.Info
  && AccessLock.Reason(Entry.Scope) is LockReason.NoWorld or LockReason.NoRights
```

- `Kind != Info` is what § 0 warns about. Without it, every discovered entry of
  an unhandled type goes red at the title screen, which is the ordinary case for
  a foreign mod, not an exotic one.
- `ViewOnly` is excluded by naming only the two contextual reasons — decision 1.
- It is computed per read, for the same reason `Locked` is.

This is the seam MSM-16 attaches to later: a dependency lock ("only while X is
on") is withheld-right-now in the same sense, so it joins here rather than in
the widgets. The widgets ask one question and do not learn where the answer
came from.

**`AccessLock` returns a reason, and the enum needs four values, not three.**
`IsLocked` answers `bool` today, which cannot say *which* note applies nor
whether the lock came from `ViewOnly`. It becomes `Reason(ConfigScope)` over
`LockReason { None, NoWorld, NoRights, ViewOnly }` — `None` is the value an
earlier draft forgot, and without it every row would read as locked. `Locked`
stays available as `Reason(...) != None`. The branch order stays as it is: it is
what keeps `ViewOnly` and `Client` from reaching a player dereference at the
title screen.

### 4.2 · The settings row reports `GRAYED_OUT`

`SettingWidget.GetActiveStateInCurrentScene()` returns `GRAYED_OUT` when 4.1's
question is true. Three effects follow from that return value: navigation skips
the row, the click collider stays off, and the row keeps its place in the
layout. The colour does **not** follow from it here, because 4.4 switches off
the effects that would apply it.

**Navigation and the click block are live by themselves.** CK calls
`GetActiveStateInCurrentScene()` every frame from `RadicalMenuOption.Update()`
→ `UpdateClickCollider()`. Because 4.1's question is computed rather than
stored, both follow a state change with no prompting. That is the permission
half of the merged MSM-11, and it costs nothing.

### 4.3 · The list row keeps navigation

`ListWidget` keeps returning `ACTIVE`, so the row stays reachable and its
drill-in stays openable; `CanBeActivated()` keeps returning true here, unlike in
4.5. Its red comes from 4.4, like every other locked row's.

**What it does not do is override `IsSelectionEnabled`.** That was the previous
draft's route, and it is incompatible with 4.4: the base implementation ignores
the `visualOnly` parameter entirely (`Pug.Other:358545`, whose body returns
`enabled && activeInHierarchy && !ShouldBeGrayedOut()`), so the parameter only
matters to the callers that pass it — and those are inside the effects that 4.4
disables. Vanilla does ship such an override, in the opposite direction (popup
buttons are input-dead during the anti-misclick timer while looking normal), but
vanilla leaves its effects running.

### 4.4 · MSM paints every locked row, and the exact value

CK's own red arrives through `PugTextEffectMenuOption`, which chooses it by
`IsSelectionEnabled(visualOnly: true)` in the paths that run while a row is
**deselected** — `OnDeselected`, the `colorCooloff` blend, and the snap that
follows it; `ResetEffect` reaches one through `EndEffectImmediate` rather than
reading it directly. That route cannot be used here, for a reason that also
explains why MSM already has colour code of its own:

**Disabling an effect does not stop it.** `OnSelected` and `OnDeselected`
recolour every entry of `menuOptionEffects` *directly* and ignore
`MonoBehaviour.enabled` — which is why `SuppressValueSelectionEffect` filters
the value's effect out of that array instead of disabling it, and why
`OnSelected` sets `SELECTED_VALUE_COLOR` / `SELECTED_TEXT_COLOR` without
consulting `IsSelectionEnabled` at all. So a locked row's appearance cannot be
had by configuring CK; it has to be painted.

Both widgets therefore paint a locked row themselves — label, value column and,
on the list row, the drill arrow (`TintDrill`) — and they stop CK's own colour
work the way `MakeValueReadOnly` already does for the value column. 4.7 lists
every mutation this involves, because each one has to be reversible.

**The selected tone is a fixed value, computed once here.** Unity's own
`Color.grayscale` (0.299 R + 0.587 G + 0.114 B) is the formula, chosen because
it is reachable from engine code if anyone wants to re-derive it:

| | value |
|---|---|
| `UNSELECTED_TEXT_COLOR` grayscale | 0.50000 |
| `SELECTED_TEXT_COLOR` grayscale | 0.75583 |
| CK's selection factor | **1.51165** |
| `UNSELECTABLE_TEXT_COLOR`, exact | 0.4245283 / 0.1742168 / 0.1835400 |
| × factor, clamped, rounded to three places | **0.642 / 0.263 / 0.277**, alpha 1 |

Two notes on that arithmetic, because the previous draft got it wrong in both
places. The factor depends on the luminance formula — Rec. 709 gives 1.53 and a
different triple — so the formula is named rather than assumed. And the
multiplication uses the **exact** constants: rounding the inputs first yields a
triple whose channels disagree with this one.

Channel-wise scaling rather than interpolation toward white, because
interpolating to white desaturates the hue and weakens the red as a signal.

### 4.5 · Input on a locked row

**`GRAYED_OUT` does not block activation.** `CanActivateCurrentOption()` asks
`GetSelectedMenuOption().CanBeActivated()`, not the state.

And the selection can land on a locked row. `RadicalMenu.Activate()`
(`Pug.Other:358102`) sets `num` to the first option whose state is `ACTIVE`
**or** `GRAYED_OUT` (`:358115`) and then selects it on three of its four paths:
mouse input deselects instead, and `rememberSelectedIndex` keeps a remembered
index only while that option is `ACTIVE` — otherwise it, too, falls back to
`num`. A row can also be locked *while* it is selected, which is the case
decision 4 exists for.

`SettingWidget.CanBeActivated()` therefore returns false when 4.1's question is
true. CK gates both the menu-select sound and the footer's select hint on it,
so one override removes a confirmation the player can hear but not see the
effect of. `ListWidget` already overrides the same method for a different
reason (an empty drill-in), which is the precedent for the shape, not for the
condition.

The write guards in `Adjust` stay as they are. They ask `IsEditable`, which is
the wider question and the right one for a write.

### 4.6 · The section note

`SectionBox` gains a second `PugText` beside `hint`, and the section's
vertical stack becomes `[Header, Hint, LockNote, Widgets]`. It is switched with
`SetActive` exactly as the hint is, and the layout skips inactive children — so
a section with nothing locked collapses it away at no cost.

It is shown while at least one row of that section satisfies 4.1's question,
and carries the note for that row's reason. `Info` and `ViewOnly` rows do not
put it on screen, because 4.1's question excludes both.

The consumer's own `Hint` is left alone. Folding the lock reason into it would
mix two senders in one line, and would displace one of them in a section that
has both — the same objection MSM-17 already records against repurposing that
line.

### 4.7 · One poll, and four mutations to reverse

A state change does not repaint a row by itself, and `ResetEffects()` is not the
remedy: `PugText.ResetEffects` (`Pug.Other:367402`) calls `ResetEffect` on every
effect regardless of `enabled`, and `dontResetEffectsOnRender` only gates the
`Render`-driven call (`:367394`). On a locked row a direct call therefore
repaints — blue, if the row is selected. It is not a no-op; it is the wrong
tool. MSM painted these colours, so MSM re-applies them.

**Four mutations make a row locked, and all four must be undoable.** An earlier
draft named one, and missed the one that cannot be undone by re-enabling
anything:

| Mutation | Where | Reversal |
|---|---|---|
| `fx.enabled = false` on both value effects | `MakeValueReadOnly` | re-enable |
| `valueText.dontResetEffectsOnRender = true` | `MakeValueReadOnly` | clear |
| `valueText.style.color = UNSELECTED_TEXT_COLOR` | `MakeValueReadOnly` | restore the prefab value, which has to be captured before the first overwrite |
| `menuOptionEffects = Array.FindAll(…, !isValueText)` | `SuppressValueSelectionEffect` | **destructive** — the filtered-out effect is gone and `base.Awake` fills the array once, before `Bind`. The original array has to be kept to restore it |

**To check while implementing:** whether `PugText.style` is a shared object. If
several rows reference one style instance, writing `style.color` reaches them
all, and the third row of that table is a bug that predates this design rather
than a reversal problem.

**The poll compares inputs, not a verdict.** `ModSettingsScreen.Update()`
already exists and polls the reset key; it gains a comparison of the three
values every lock decision rests on — whether a player exists, that player's
`guestMode`, and their `adminPrivileges`. Comparing a single derived answer
would miss that `Server` and `Admin` diverge: `Changeable()` answers
`!guestMode` for one and `!guestMode && adminPrivileges > 0` for the other, so a
non-admin in a world without guest mode has `Server` free and `Admin` locked.
Three values, global, one comparison per frame rather than one per row. On a
change, every row re-applies its colours and every section note is
re-evaluated.

**A poll is what the base game does here, though not for this exact question.**
There is no event and there cannot be one: `adminPrivileges` and `guestMode` are
`[GhostField]` ECS fields whose changes arrive as a NetCode snapshot, so no code
path exists that could raise anything. CK's comparable cases are polls —
`RadicalMenuOption.Update()` re-evaluates its own gate every frame, and
`SettingsNotAvailableNote` is a ten-line `Update()` component that switches a
note on from another option's state. The second is a precedent for the shape;
what it polls is a local option, not a networked permission.

A note appearing or disappearing changes the section's height, so the change
also triggers the re-layout path `RenderContent` already uses.

### 4.8 · The value refresh

A row is redrawn at four places today — on bind, on `OnParentMenuActivation`,
after the player's own change, and after a section reset — and all four
originate with the player or the screen. A value changing from outside never
reaches an open row.

**The event is on `ConfigFile`, not on the entry base type.** CoreLib declares
`public event EventHandler SettingChanged` inside `ConfigEntry<T>`
(`ConfigEntryBase.cs:54`, within the generic class that opens at `:13` and
closes at `:55`; `ConfigEntryBase` begins at `:59`). `SettingDef.Entry` is typed
`ConfigEntryBase` and MSM deliberately never sees `T`, so
`_def.Entry.SettingChanged` does not compile. What exists is
`ConfigFile.SettingChanged` (`ConfigFile.cs:502`), whose
`SettingChangedEventArgs.ChangedSetting` (`SettingChangedEventArgs.cs:18`) is a
`ConfigEntryBase`.

**The screen subscribes, not the row** — and that follows from the previous
paragraph rather than being a preference. A row cannot reach its file: a
`SettingDef` does not carry one, and `ConfigStore.ForMod` resolves a file from
an `IMod`, which a discovered foreign config does not have. The screen can reach
every file, because the two sets are enumerable from where it stands: its own
consumers' files through `ConfigStore`, and the discovered ones the same way
`ForeignConfigDiscovery` finds them, through `ConfigFile.AllConfigFilesReadOnly`.

So `ModSettingsScreen` subscribes once per file in `Activate()` and unsubscribes
in `Deactivate()`, and maps an incoming `ChangedSetting` to the row holding that
entry. One subscription per file instead of one per row also removes the leak
this was going to have: `Populate()` destroys every row on every open, and a
subscription outliving its row is the same class of mistake as the pooled glyphs
it already has to release — the comment there records what happened when it did
not, namely that after several open/edit/reopen cycles every row in every
section rendered text-less.

**What this repairs today** is the duplicate-key case: when a consumer declares
one key twice **with the same type**, CoreLib's `Bind` returns the cached entry,
so two rows show one entry and changing one leaves the other stale. The fixture
is `testDupKeyAccess` (two `Toggle` declarations); `testDupKey` is type-different
and yields one row, so it does not exercise this.

What it is *for* is MSM-19, which names this subscription as its delivery path
to an open row rather than building an id space for it.

`docs/roadmap.md:649` carries the same `ConfigEntryBase.SettingChanged` error
this design inherited from it. Correcting it is its own change, not part of this
one.

## 5 · Acceptance criteria

1. A `Server`-scoped **toggle** opened from the main menu renders label and
   value in `UNSELECTABLE_TEXT_COLOR`, is skipped by up/down navigation, and
   does not respond to a mouse click.
2. The same setting, in singleplayer, renders and behaves normally.
3. A `ViewOnly` setting renders exactly as it does today: static grey value,
   navigable, not red.
4. A **`Server`-scoped `Info` row** is not red and does not raise the section
   note — at the title screen, and in a session without rights.
5. A **`Server`-scoped list row** renders label, preview and drill arrow red,
   remains reachable by navigation, and opens its drill-in read-only.
6. That row, while selected, renders (0.642 / 0.263 / 0.277) rather than
   selection blue; so does a locked toggle that is selected.
7. A section with at least one contextually locked row shows the note between
   hint and box; a section with none shows no note and no gap.
8. The note names the world when no player exists, and rights when a player
   exists without them.
9. A **registered consumer's** section whose settings state no scope shows no
   note and no red row, at the title screen and in a session — the `Client`
   default from ADR-012 holds. A discovered foreign entry that states no scope
   gets CoreLib's `Server` default instead, so it does go red **where its kind
   is editable**; where the kind is `Info`, criterion 4 governs.
10. Pressing confirm on a locked toggle produces neither a sound nor a footer
    select hint. The locked list row keeps both: activating it opens the
    read-only drill-in.
11. An admin elsewhere in the session revoking this player's rights turns the
    affected rows red and shows the note **without this player leaving the
    screen**.
12. The same admin restoring them reverses every one of 4.7's four mutations:
    the rows are editable, their values selectable, and their colours back to
    the prefab's.
13. A section reset offers and resets the rows `IsEditable` admits, which is
    the set `SectionReset` already uses — including a `ListEditing.ReadOnly`
    list, whose rows nobody can edit but whose value a reset does restore. A
    section with nothing editable offers no reset hint.
14. Changing a value from outside while its row is on screen updates the
    rendered text — exercised with `testDupKeyAccess`, whose two rows share one
    entry.

New fixtures this requires: a `Server`-scoped **list** (criteria 5, 6) and a
`Server`-scoped **`Info`** row (criterion 4). Neither exists; every current list
fixture is `ViewOnly` or inherits the section's `Client` default.

## 6 · Edge cases the design covers

| Case | Behaviour |
|---|---|
| Selection strands on a locked row | navigation away still works (the skip tests the target); 4.5 removes sound and hint, and 4.4 keeps the lock visible while it holds focus |
| `RadicalMenu.Activate()` lands on a locked row at open | the ordinary path on three of its four branches, not an anomaly. Same handling as above |
| Every settings row of every section is locked | navigation reaches no toggle and none responds. Accepted deliberately: there is nothing to operate, the notes say why, and the back key is not bound to menu options. A locked **list** row stays reachable, so a screen containing one keeps a target |
| Drill-in screen | untouched. Its prefab sets `useUIElementsForNavigation: 1`, where a `GRAYED_OUT` neighbour makes navigation **stall** instead of stepping over; a read-only list's rows stay `ACTIVE` as today |
| Section reset | already correct — `IsInScope` asks `IsEditable`, and `CanReset` returns false when nothing is editable, so the hint disappears on its own |
| `PreWarm` | runs at load, where there is no player, so it builds every scoped row as locked. Harmless: the pre-warmed instance is disabled in the same frame and `Populate()` rebuilds from scratch on a real open |
| Discovered foreign sections | treated like registered ones, same notes. They carry CoreLib's `Server` default where their author stated nothing; a registered consumer can also state `Server` explicitly, so this is a difference of defaults, not of capability |

## 7 · What this is not

- **MSM-16** — the dependency lock ("only while X is on", CK's V-Sync case).
  It is withheld-right-now too and will attach at 4.1's question, but its API is
  a separate point that also depends on MSM-15 for indentation.
- **MSM-19** — server sync. It consumes 4.8's subscription; nothing here sends
  or receives anything.
- **MSM-36** — that MSM's writes may not reach disk with GMCM installed. A
  save-path defect, unrelated to rendering, deliberately left alone.
- **A reason per row.** Decided against in 3.1; the note is per section.
- **Correcting `docs/roadmap.md`.** Named in 4.8, done separately.

## 8 · Open questions

- **The three loc term names** (two notes, and whether the `ViewOnly` reason
  ever needs one). They follow MSM's own schema; no decision rests on them.
- **Whether `PugText.style` is shared between rows** (4.7). It decides whether
  the value column's colour can be restored per row or is a pre-existing bug.

## 9 · Verification

**Every fixture named here is behind `DevFlags.Is("TestFixtures")`**
(`ModSettingsMenuMod.cs:66`, `:598`), so the build is
`MOD_DEV_FLAGS=TestFixtures ../utils/build.sh`. A normal build contains none of
these rows and does not say so. `docs/manual-tests.md` opens with this and is
where these checks are written down.

**Criteria 1, 2, 3, 5, 6, 7, 10, 13 and 14, and the first note of 8, need no
server.** The title screen produces the no-player lock on its own — `AccessLock`
treats a non-`Client` scope as locked while `Manager.main.player` is null — and
singleplayer produces the unlocked case, since `GetAdminPrivileges`
short-circuits to `int.MaxValue` offline.

**Criteria 4, 9 and the second note of 8 need a session without rights**, which
singleplayer cannot produce: each names a behaviour "in a session" as well as at
the title screen. They ride along with the server round below rather than
needing one of their own.

**Criteria 11 and 12 need a second account.** Not because no single-account
route to the *lock* exists — `docs/manual-tests.md` walks one, and
`docs/ck/multiplayer-and-server.md` documents why it works: at stage 1 a player
can remove themselves, since `RemoveAdminInternal` matches `privileges <= 1` and
the `UNASSIGN_ADMIN` list does not filter the local player. Three things make
that route unable to show criteria 11 and 12 specifically:

- It cannot be **observed**. Reaching the player list makes another menu the top
  menu, and `ModSettingsScreen.Update()` returns early then
  (`ModSettingsScreen.cs:495`), so the poll is dormant exactly while the change
  happens. Returning re-`Activate()`s this screen, which calls `Populate()` and
  rebuilds every row — so a working poll and a broken one look identical.
- It cannot be **reversed**. After self-revocation the player is at stage 0, and
  the admin check before the command switch (`Pug.Other:136440`) rejects
  `AddOrUpdateAdmin`, `RemoveAdmin` and `SetGuestMode` alike. Criterion 12 is
  unreachable this way.
- The bootstrap does not grant stage 2 alongside a placeholder.
  `AddAdminInternal` is reached when `adminList.adminList.Count == 0 ||
  isLocalPlayer` (`Pug.Other:293683`), and a placeholder entry makes that count
  non-zero. The observing account's stage 0 comes *from* the file being
  non-empty; the admin account has to be **in** the file at `privileges: 2`,
  which is what `docs/manual-tests.md` round 2 does.

So: both accounts are listed in `Admins.json` — the admin at `privileges: 2`,
the observer absent or at 0 — the admin changes the observer's rights from the
player list, and the observer watches MSM's screen without touching it.

**The foreign-config census.** An earlier count in this document was wrong
three times over, so this one states its method: entry counts come from the live
`.cfg` files, scope assignments from reading each `Bind` call in the mod's
installed source.

| | |
|---|---|
| Foreign mods writing a CoreLib config | **three** — an earlier draft said four, counting `SignLabels`, which is a family repo and a registered MSM consumer (`AutoRailBridges` likewise, and was never in the count) |
| PlacementPlus | 8 entries; 2 of its 6 `Bind` calls pass `ConfigScope(Client)`, the other four state nothing and are therefore `Server` — including one whose description reads "Client-side only" |
| CoreLib | 16 entries, mixed scopes |
| GeneralConfigMenu | 1 entry |
| CK-QOL | ~21 entries from 11 `Bind` call sites, one of which runs once per feature. No `ConfigAccessLevel` anywhere in its source, so all `Server`. Switched off in the mod menu at the time of counting, so it writes no config |

What this establishes is narrow and worth keeping separate from what it does
not. It establishes that a foreign mod binding without a scope happens, and in
the largest of these mods happens more often than not. It does not establish how
much red an arbitrary player sees: that depends on their mod set, and five mods
on one machine are not a sample.
