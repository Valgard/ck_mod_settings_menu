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

**Citations.** Game-code line numbers in this document are from the
`CoreKeeperDecompile-1.3.0.2-182b` checkout. The canonical `CoreKeeperDecompile`
symlink currently resolves to 1.3.0.4, whose bodies are identical but whose line
numbers differ by roughly a thousand — cite against the directory, not the
symlink.

## 0 · Three states that look alike

The whole design turns on keeping these apart, and the first two were one
field until ADR-012 split them.

| State | Source | Changes during a session? |
|---|---|---|
| **contextually locked** | `Server`/`Admin` scope, and either no player, no rights, or guest mode | **yes** — the same row is editable in a session with rights |
| **view-only** | the author declared `ConfigAccessLevel.ViewOnly` | no — never editable through the menu, for anyone |
| **shapeless** | `Kind == Info`: no editable widget exists for this value's form | no — a property of the value, not of permission |

`SettingDef.Locked` answers the first two together; `IsEditable` excludes all
three. This design introduces a question that is narrower than either, defined
in § 4.1.

**A row can be more than one of these at once**, and that is the trap this
document walked into once already: a discovered entry of an unhandled type gets
`Kind = Info` while keeping its `ConfigEntry` and that entry's scope
(`ForeignConfigDiscovery.cs`, routes 4, 6 and 7, each marked "regardless of
scope"). With no scope stated, CoreLib's default is `Server`, so at the title
screen such a row is shapeless **and** contextually locked. Only the shapeless
half may show.

Throughout this document, **"locked"** on its own always means *contextually*
locked. Where `SettingDef.Locked` is meant — the wider property that includes
`ViewOnly` — it is written as code.

## 1 · Goal

A setting that exists but cannot be changed right now renders in Core Keeper's
own shipped convention, says why, and keeps saying it while the screen is
open — including when the answer changes while the player is looking at it.

Three things follow, and the third is the merged MSM-11:

1. The row renders as withheld: dull red, skipped by navigation, unclickable,
   still visible and still occupying its place.
2. The reason is on screen. A red row whose cause is invisible is worse than no
   lock: the player sees a refusal with no way to resolve it.
3. Nothing is frozen at open.

**The change this third point is for comes from someone else.** Every way the
player could flip their own permission state is unreachable while this screen
is open: `AddOrUpdateAdmin`, `RemoveAdmin` and `SetGuestMode` all sit behind one
admin check placed before the whole command switch
(`NetworkCommandServerSystem`, `Pug.Other:136440`, with `SetGuestMode` at
`:136538` inside it), *and* behind another menu — and
`ModSettingsScreen.Update()` returns early unless this screen is the top menu
(`ModSettingsScreen.cs:495`). Loading a world closes the menu outright.

So the live case is an **admin elsewhere in the session** changing this player's
rights, or switching guest mode, while this player stands in the menu. That is
what the roadmap described for MSM-11, and it is the only shape this feature
reacts to. It also decides § 9: a feature that answers someone else's action
cannot be verified by one actor.

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

Missing is the rendering, and four things that are not rendering: a narrower
question (4.1), the reason text and its carrier (4.6), the repaint on a state
change (4.7), and the value refresh (4.8).

Both widgets currently rule `GRAYED_OUT` out explicitly:

```csharp
public override OptionActiveState GetActiveStateInCurrentScene() =>
    _def != null ? OptionActiveState.ACTIVE : OptionActiveState.INACTIVE;
```

## 3 · Decisions (locked)

| # | Decision | Reason |
|---|---|---|
| 1 | Only a **contextual** lock renders red. `ViewOnly` and `Info` keep today's appearance. | `GRAYED_OUT` means "normally editable, just not right now". Nothing is withheld from a row that is never editable, and greying it out would also take it out of navigation — the handbook's rule is to keep permanently read-only rows `ACTIVE` so they stay navigable. |
| 2 | **`GRAYED_OUT` everywhere, except where a row hides content behind an interaction.** Today that is exactly the list row. | A locked list row would otherwise make its contents unreachable: the row shows a preview, the full list lives behind the drill-in. Every other row keeps label and value on screen and loses nothing by being unreachable. |
| 3 | The list row stays `ACTIVE` and overrides `IsSelectionEnabled(visualOnly: true)` to `false`. | That is the input CK's *deselected* colour paths consult for this choice, so the row is red while unselected without being removed from navigation. It is not a free ride — see 4.4 for the three things it does not cover. |
| 4 | A locked row renders a **lightened red while selected**, by a fixed value named in 4.4 — the list row and the settings row alike. | Both can be selected while locked (4.5), and in the settings row's case the lock can arrive *under* the player's cursor. Leaving selection blue would hide the lock at the exact moment the player tries to act on the row. |
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

**The two can never appear together.** "No player" is a global condition
(`Manager.main.player == null`), not a property of a row. Either there is no
player — and then every `Server` and `Admin` row is locked for that one
reason — or there is one, and `Changeable()` decides on rights. A section can
therefore never need two notes at once, which is what makes decision 5
conflict-free.

## 4 · Design

### 4.1 · One question, in one place

`SettingDef` gains a computed property for **"withheld right now, and would
otherwise be operable"**. All three conjuncts carry weight:

```csharp
Entry != null && Kind != SettingKind.Info && AccessLock.IsLocked(Entry.Scope) is not ViewOnly-locked
```

- `Kind != Info` is what § 0 warns about. Without it, every discovered entry of
  an unhandled type goes red at the title screen, which is the ordinary case for
  a foreign mod, not an exotic one.
- The `ViewOnly` exclusion is decision 1.
- It is computed per read, for the same reason `Locked` is.

This is the seam MSM-16 attaches to later: a dependency lock ("only while X is
on") is withheld-right-now in exactly the same sense, so it joins here rather
than in the widgets. The widgets ask one question and never learn where the
answer came from.

`AccessLock.IsLocked` returns a `bool` today, which cannot say *which* of the
two notes applies, nor whether the lock came from `ViewOnly`. It gains a
return value naming the reason — one value per note plus one for `ViewOnly` —
with `Locked` derived from it. The branch order stays as it is: it is what
keeps `ViewOnly` and `Client` from reaching a player dereference at the title
screen.

### 4.2 · The settings row reports `GRAYED_OUT`

`SettingWidget.GetActiveStateInCurrentScene()` returns `GRAYED_OUT` when 4.1's
question is true. Three effects follow from that one return value: navigation
skips the row, the click collider stays off, and the row keeps its place in the
layout. A fourth — the colour — follows only partly; 4.4 says which part.

**Navigation and the click block are live by themselves.** CK calls
`GetActiveStateInCurrentScene()` every frame from `RadicalMenuOption.Update()`
→ `UpdateClickCollider()`. Because 4.1's question is computed rather than
stored, both follow a state change with no prompting. That is the permission
half of the merged MSM-11, and it costs nothing.

### 4.3 · The list row keeps navigation and goes red while unselected

`ListWidget` keeps returning `ACTIVE` and overrides
`IsSelectionEnabled(bool visualOnly)`: when 4.1's question is true, the
`visualOnly: true` answer is `false` while the plain answer stays `true`.

**CK does not separate optics from control by itself** — the base
implementation ignores the parameter entirely (`Pug.Other:359560`, whose body
returns `enabled && activeInHierarchy && !ShouldBeGrayedOut()` without reading
`visualOnly`). What CK provides is the *parameter* and the fact that different
callers pass different values, so an override can answer them differently.
Vanilla ships such an override and uses the gap in the opposite direction:
popup buttons are input-dead during the anti-misclick timer while looking
normal.

`CanBeActivated()` must keep returning true here, unlike in 4.5 — opening the
drill-in is what this row is for.

### 4.4 · What the colour needs by hand, and the exact values

CK's red arrives through `PugTextEffectMenuOption`, and reading
`IsSelectionEnabled(visualOnly: true)` happens in exactly the paths that run
while a row is **deselected** (`OnDeselected`, `ResetEffect`, and the
transition blend). Three gaps follow, and each needs MSM to set a colour
itself:

1. **The value column.** It deliberately sits outside those paths —
   `MakeValueReadOnly` disables both value effects and sets
   `dontResetEffectsOnRender`. Without an explicit colour there, a locked row
   renders half red: label dull, value in its normal tone.
2. **The list row's drill arrow**, which MSM tints itself (`TintDrill`). A red
   row with a grey arrow is the same defect one element smaller.
3. **Selection.** `OnSelected()` sets `SELECTED_VALUE_COLOR` /
   `SELECTED_TEXT_COLOR` **unconditionally** — it does not consult
   `IsSelectionEnabled` at all. Decision 4 therefore cannot be had from CK; it
   needs an override.

**The selected tone is a fixed value, not a method.** Deriving it "by the same
ratio" is ambiguous: `SELECTED_TEXT_COLOR` is (0.647 / 0.792 / 0.855), a
blue-tinted colour whose channel average is not a grey value, so "the same
ratio" could mean scaling channels or interpolating toward white, and the two
give different reds. What this design fixes:

- Luminance of `UNSELECTED_TEXT_COLOR` (0.5 grey) is 0.5; of
  `SELECTED_TEXT_COLOR`, ≈0.766. CK's selection brightens by a factor of ≈1.53.
- Applied channel-wise to `UNSELECTABLE_TEXT_COLOR` (0.425 / 0.174 / 0.184),
  clamped: **(0.650 / 0.266 / 0.281)**, alpha 1.
- Channel-wise rather than toward white, because interpolating to white
  desaturates the hue and weakens the red as a signal.

**The override holds, and the animation has to be stopped anyway.** The blend
path writes nothing unless a transition is running — its body ends in
`if (!colorCooloff.isRunning) return;` — so a colour set after
`base.OnSelected()` is not overwritten frame by frame. But the blend's *start*
colour is hardcoded to `SELECTED_VALUE_COLOR` / `SELECTED_TEXT_COLOR` rather
than read from the text, so deselecting a row that was red while selected
blends from **blue**: a brief blue flash. Both widgets therefore stop the
effects on a locked row the way `MakeValueReadOnly` already does for the value
column — disable them and set `dontResetEffectsOnRender` — and set all colours
themselves. A locked row loses its colour animation, which it has no use for.

### 4.5 · Input on a locked row

**`GRAYED_OUT` does not block activation.** `CanActivateCurrentOption()` asks
`GetSelectedMenuOption().CanBeActivated()`, not the state.

It is not true that vanilla's navigation never rests on a grey row:
`RadicalMenu.Activate()` selects the first `ACTIVE`-or-`GRAYED_OUT` option with
no filter (`Pug.Other:358115`, `:358146`) — the same base method § 6 relies on
for MSM. And a row can be locked *while* it is selected, which is the case
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
and carries the note for that row's reason. `Info` and `ViewOnly` rows never
put it on screen — not because of decision 1 alone, but because 4.1's question
excludes both.

The consumer's own `Hint` is left alone. Folding the lock reason into it would
mix two senders in one line, and would displace one of them in a section that
has both — the same objection MSM-17 already records against repurposing that
line.

### 4.7 · One poll, screen-wide

A state change does not repaint a row by itself, and `ResetEffects()` is not
the remedy here: on a locked row the effects are disabled and
`dontResetEffectsOnRender` is set (4.4), which is exactly what makes
`ResetEffects()` a no-op there. MSM owns these colours, so MSM re-applies them.

`ModSettingsScreen.Update()` already exists and polls the reset key. It gains a
comparison of one screen-wide state: whether a player exists, and that player's
rights answer. Both are global, so this is one comparison per frame rather than
one per row. On a change, every row of every section re-applies its colours and
every section note is re-evaluated.

**Re-applying must work in both directions**, which today's code does not do:
`MakeValueReadOnly` runs once, from `Bind`. The colour work moves into a method
both `Bind` and the poll can call, and it must be able to restore an editable
row's normal appearance — re-enabling the effects it disabled — or a row that
is unlocked mid-session stays grey for the rest of the visit.

**A poll is not a compromise; it is what the base game does.** There is no
event and there cannot be one: `adminPrivileges` and `guestMode` are
`[GhostField]` ECS fields whose changes arrive as a NetCode snapshot, so no code
path exists that could raise anything. CK polls in comparable places —
`SettingsNotAvailableNote` is a ten-line `Update()` component solving this exact
problem, and `RadicalMenuOption.Update()` re-evaluates its own gate every frame.

A note appearing or disappearing changes the section's height, so the change
also triggers the re-layout path `RenderContent` already uses.

### 4.8 · The value refresh

A row is redrawn at four places today — on bind, on `OnParentMenuActivation`,
after the player's own change, and after a section reset — and all four
originate with the player or the screen. A value changing from outside never
reaches an open row.

**The event is on `ConfigFile`, not on the entry base type.** CoreLib declares
`public event EventHandler SettingChanged` inside `ConfigEntry<T>`
(`ConfigEntryBase.cs:54`, within the generic class that closes at `:55`;
`ConfigEntryBase` only begins at `:59`). `SettingDef.Entry` is typed
`ConfigEntryBase` and MSM deliberately never sees `T`, so
`_def.Entry.SettingChanged` does not compile. The implementable route is
`ConfigFile.SettingChanged` (`ConfigFile.cs:502`) with MSM's own filter on
`SettingChangedEventArgs.ChangedSetting == _def.Entry`.

`docs/roadmap.md` carries the same error in its MSM-11 text and is corrected in
the same change as this spec.

Both widgets subscribe on bind and unsubscribe in `OnDestroy()`. Unsubscribing
is not a formality: `Populate()` destroys every row on every open and already
has to release each `PugText`'s pooled glyphs first — the comment there records
what happened when it did not, namely that after several open/edit/reopen cycles
every row in every section rendered text-less. A subscription outliving its row
is the same class of mistake, and the file-level event makes it worse: every
stale subscriber sees every entry's change.

**What this repairs today** is the duplicate-key case: when a consumer declares
one key twice **with the same type**, CoreLib's `Bind` returns the cached entry,
so two rows show one entry and changing one leaves the other stale. The fixture
for it is `testDupKeyAccess` (two `Toggle` declarations); `testDupKey` is
type-different and yields one row, so it does not exercise this at all.

What it is *for* is MSM-19, which names this subscription as its delivery path
to an open row rather than building an id space for it.

## 5 · Acceptance criteria

1. A `Server`-scoped **toggle** opened from the main menu renders label and
   value in `UNSELECTABLE_TEXT_COLOR`, is skipped by up/down navigation, and
   does not respond to a mouse click.
2. The same setting, in singleplayer, renders and behaves normally.
3. A `ViewOnly` setting renders exactly as it does today: static grey value,
   navigable, not red.
4. A **`Server`-scoped `Info` row** — the discovered-foreign default — is not
   red and does not raise the section note, in either situation.
5. A **`Server`-scoped list row** renders label, preview and drill arrow red,
   remains reachable by navigation, and opens its drill-in read-only.
6. That row, while selected, renders (0.650 / 0.266 / 0.281) rather than
   selection blue; so does a locked toggle that is selected.
7. A section with at least one **contextually** locked row shows the note
   between hint and box; a section with none shows no note and no gap.
8. The note names the world when no player exists, and rights when a player
   exists without them.
9. A **registered consumer's** section whose settings state no scope shows no
   note and no red row, in either situation — the `Client` default from ADR-012
   holds. A discovered foreign section whose entries state no scope is the
   opposite case and does go red, because CoreLib's default is `Server`.
10. Pressing confirm on a locked toggle produces neither a sound nor a footer
    select hint. The locked list row keeps both: activating it opens the
    read-only drill-in.
11. An admin elsewhere in the session revoking this player's rights turns the
    affected rows red and shows the note **without this player leaving the
    screen**.
12. The same admin restoring them reverses both the same way, and the restored
    rows are editable again.
13. A section reset offers and resets only the rows that are editable; a
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
| `RadicalMenu.Activate()` lands on a locked row at open | the ordinary path, not an anomaly — it selects the first non-`INACTIVE` option unfiltered. Same handling as above |
| Every settings row of every section is locked | navigation reaches no toggle and none responds. Accepted deliberately: there is nothing to operate, the notes say why, and the back key is not bound to menu options. A locked **list** row remains reachable, so a screen containing one is never fully inert |
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

## 8 · Open questions

- **The three loc term names** (two notes, and whether the `ViewOnly` reason
  ever needs one). They follow MSM's own schema; no decision rests on them, so
  they are settled while implementing.

## 9 · Verification

**Every fixture named here is behind `DevFlags.Is("TestFixtures")`**
(`ModSettingsMenuMod.cs:66`, `:598`), so the build is
`MOD_DEV_FLAGS=TestFixtures ../utils/build.sh`. A normal build contains none of
these rows and does not say so. `docs/manual-tests.md` opens with this and is
where these checks are written down.

**Criteria 1, 2, 3, 4, 5, 6, 7, 8 (first note), 9, 10, 13 and 14 need no
server.** The title screen produces the no-player lock on its own — `AccessLock`
treats a non-`Client` scope as locked while `Manager.main.player` is null — and
singleplayer produces the unlocked case, since `GetAdminPrivileges`
short-circuits to `int.MaxValue` offline. Both halves are reachable by opening
the menu from the main menu and then from a singleplayer pause menu.

**Criteria 11 and 12 need a second account, and no single-account route
exists.** This is a property of the feature, not a gap in the procedure: § 1
establishes that the live case is someone else's action. Three findings close
the alternatives:

- The admin check sits before the whole command switch
  (`Pug.Other:136440`), so a player at `adminPrivileges == 0` can send none of
  `AddOrUpdateAdmin`, `RemoveAdmin`, `SetGuestMode` or `PlayerBan`. Having
  revoked one's own stage 1, there is no way back in that session — the
  guest-mode detour is closed by the same guard.
- Reaching the manage-players UI makes another menu the top menu, and
  `ModSettingsScreen.Update()` returns early then (`:495`), so the poll is
  dormant exactly while the change happens.
- Returning re-`Activate()`s this screen, which calls `Populate()` and rebuilds
  every row. The only configuration in which one account could observe
  criterion 11 is the opposite of what the game ships.

The second account connects to a dedicated server; the first holds stage 2 by
the `adminList.Count == 0 || isLocalPlayer` bootstrap rule, and grants the
second stage 1 — which is revocable, so 11 and 12 are repeatable. The
`Admins.json` placeholder trick from `docs/ck/multiplayer-and-server.md` is what
puts the observing account at stage 0 to begin with.

A dev-only switch forcing the lock was considered and rejected: it would test
the poll against a simulated change and never touch the real path, which is a
NetCode snapshot.

**The foreign-config census, corrected.** An earlier count in this document was
wrong three times over and the conclusions drawn from it do not survive:

| | |
|---|---|
| Foreign mods writing a CoreLib config | **three**, not four — `SignLabels` and `AutoRailBridges` are family repos and registered MSM consumers, so `Client` by the ADR-012 default |
| PlacementPlus | 2 of its 6 `Bind` calls pass `ConfigScope(Client)`; the other four state nothing and are therefore `Server`, including one whose description reads "Client-side only" |
| CoreLib (16 entries), GeneralConfigMenu (1) | mixed scopes; entry counts verified against the live `.cfg` files |
| CK-QOL | 11 entries, no `ConfigAccessLevel` anywhere in its source, bound individually — all `Server`. Currently switched off in the mod menu, so it writes no config |

How much red a given player sees therefore depends on which mods they run, and
this installation is not a sample to generalise from. What the census does
establish: a foreign mod binding without a scope is the ordinary case, not the
exception, so the title-screen lock is a state most players will meet.
