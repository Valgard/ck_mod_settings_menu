# Design — Locked settings render as withheld (MSM-10)

Roadmap point MSM-10, with MSM-11 merged into it in full. MSM-11 is not
deferred and not split: two thirds of it fall out of this design as a
by-product, and the remaining third has no separate point worth keeping.

Builds on [ADR-012](../adrs/012-permission-and-restart-cascade-from-the-nearest-declaration.md),
which shipped the vocabulary this design renders. Before it, no consumer could
state that a setting belongs to the server, and `SettingDef.ReadOnly` conflated
two unrelated claims. Both are now settled, which is what makes this point
buildable.

## 0 · Three states that look alike

The whole design turns on keeping these apart, and the first two were one
field until ADR-012 split them.

| State | Source | Changes during a session? |
|---|---|---|
| **contextually locked** | `Server`/`Admin` scope with no player, no rights, or guest mode | **yes** — the same row is editable in a session with rights |
| **view-only** | the author declared `ConfigAccessLevel.ViewOnly` | no — never editable through the menu, for anyone |
| **shapeless** | `Kind == Info`: no editable widget exists for this value's form | no — a property of the value, not of permission |

`SettingDef.Locked` answers the first two together; `IsEditable` excludes all
three. This design introduces a question that answers **only the first**,
because only the first is something being withheld.

## 1 · Goal

A setting that exists but cannot be changed right now renders in Core Keeper's
own shipped convention, says why, and keeps saying it while the screen is
open — including when the answer changes mid-session.

Three things follow, and the third is the merged MSM-11:

1. The row renders as withheld: dull red, skipped by navigation, unclickable,
   still visible and still occupying its place.
2. The reason is on screen. A red row whose cause is invisible is worse than no
   lock: the player sees a refusal with no way to resolve it.
3. Nothing is frozen at open. A role change, a guest-mode switch or loading a
   world reaches a row that is already on screen.

## 2 · What is already there, and what is missing

ADR-012 left this design very little to invent:

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

What is missing is only the **rendering**, plus the one thing CK does not do
for free: repainting on a state change.

Both widgets currently rule the third state out explicitly:

```csharp
public override OptionActiveState GetActiveStateInCurrentScene() =>
    _def != null ? OptionActiveState.ACTIVE : OptionActiveState.INACTIVE;
```

## 3 · Decisions (locked)

| # | Decision | Reason |
|---|---|---|
| 1 | Only the **contextual** lock renders red. `ViewOnly` keeps today's appearance. | `GRAYED_OUT` means "normally editable, just not right now". Nothing is withheld from a view-only row, and greying it out would also take it out of navigation — the handbook's own rule is to keep permanently read-only rows `ACTIVE` so they stay navigable. |
| 2 | **`GRAYED_OUT` everywhere, except where a row hides content behind an interaction.** Today that is exactly the list row. | A locked list row would otherwise make its contents unreachable: the row shows a preview, the full list lives behind the drill-in. Every other row keeps label and value on screen and loses nothing by being unreachable. |
| 3 | The list row stays `ACTIVE` **and takes the colour**, through `IsSelectionEnabled(visualOnly: true)`. | CK separates optics from control itself, and the method is `virtual`. The lock stays visible while reading stays possible — no hand-rolled tinting. |
| 4 | While selected, the locked list row renders a **lightened** red, derived from CK's own selected/unselected ratio. | It is the one locked row a player can focus. Switching it to selection blue would hide the lock at the exact moment they try to change something. |
| 5 | One **note per section**, between the hint and the widgets box, carrying one of two texts. | A locked section would otherwise repeat the reason once per row. CK words its own note in the plural for the same reason. |
| 6 | The drill-in of a locked list gets **no note of its own**. | The player just came from the red row whose section note gave the reason, and the drill-in shows the lock by what is absent: no add row, no row buttons, no text field. |

### 3.1 · Why the two notes, and only two

The reasons a row can be contextually locked collapse into two the player can
act on:

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

`SettingDef` gains a computed property beside `Locked` for "withheld right
now, would otherwise be operable" — `Locked` minus the `ViewOnly` case. It is
computed per read for the same reason `Locked` is.

This is the seam MSM-16 attaches to later: a dependency lock ("only while X is
on") is contextual in exactly the same sense, so it joins here rather than in
the widgets. The widgets ask one question and never learn where the answer
came from.

`AccessLock.IsLocked` returns a `bool` today, which cannot say *which* of the
two notes applies. It gains a return value naming the reason, with `Locked`
derived from it. The branch order stays as it is — it is what keeps `ViewOnly`
and `Client` from reaching a player dereference at the title screen.

### 4.2 · The settings row reports the third state

`SettingWidget.GetActiveStateInCurrentScene()` returns `GRAYED_OUT` when the
new question is true. Four effects follow from that one return value without
further work: navigation skips the row, the click collider stays off, the row
keeps its place in the layout, and CK tints label and effect sprites.

**Navigation and the click block are live by themselves.** CK calls
`GetActiveStateInCurrentScene()` every frame from `RadicalMenuOption.Update()`
→ `UpdateClickCollider()`. Because the question is computed rather than
stored, both effects follow a state change with no prompting. This is the
larger half of the merged MSM-11, and it costs nothing.

### 4.3 · The list row keeps navigation and takes the colour

`ListWidget` keeps returning `ACTIVE` and overrides
`IsSelectionEnabled(bool visualOnly)` instead: when the row is contextually
locked, the `visualOnly: true` answer is `false` while the plain answer stays
`true`. CK's own colour paths read the first, its navigation the second.

Vanilla exploits the same gap in the opposite direction — popup buttons are
input-dead during the anti-misclick timer while looking normal — and ships an
override of this method, so this is a documented seam rather than a trick.

`CanBeActivated()` must keep returning true here, unlike in 4.5: the drill-in
is what this row is for.

### 4.4 · The value column and the drill arrow need tinting by hand

CK's red arrives solely through `PugTextEffectMenuOption`, and MSM's value
column deliberately sits outside those paths — `MakeValueReadOnly` disables
both value effects and sets `dontResetEffectsOnRender`. Without an explicit
`UNSELECTABLE_TEXT_COLOR` on the value text, a locked row renders half red:
label correctly dull, value in its normal tone.

The same applies to the list row's drill arrow, which MSM tints itself
(`TintDrill`). A red row with a grey arrow is the same defect one element
smaller.

**The lightened selected tone is derived, not chosen.** CK's constants give
the ratio: `UNSELECTED_TEXT_COLOR` is 0.5 grey at alpha 0.725,
`SELECTED_TEXT_COLOR` is roughly 0.76 at alpha 1.0 — selection brightens by
about half and goes fully opaque. Applying that same ratio to
`UNSELECTABLE_TEXT_COLOR` (0.425 / 0.174 / 0.184) yields the selected variant,
clamped. No third colour is invented; one relationship is reused.

### 4.5 · Input on a locked row

**`GRAYED_OUT` does not block activation.** `CanActivateCurrentOption()` asks
`GetSelectedMenuOption().CanBeActivated()`, not the state. Vanilla survives
this because its navigation never rests on a grey row; MSM cannot assume that,
because a row can be locked *while* it is selected.

`SettingWidget.CanBeActivated()` therefore returns false when the row is
contextually locked. CK gates both the menu-select sound and the footer's
select hint on it, so one override removes a confirmation the player can hear
but not see the effect of. The precedent is `ListWidget.CanBeActivated()`,
which exists for exactly this reason.

The write guards in `Adjust` stay as they are. They ask `IsEditable`, which is
the wider question and the right one for a write.

### 4.6 · The section note

`SectionBox` gains a second `PugText` beside `hint`, and the section's
vertical stack becomes `[Header, Hint, LockNote, Widgets]`. It is switched with
`SetActive` exactly as the hint is, and the layout skips inactive children — so
a section with nothing locked collapses it away at no cost.

It is shown while at least one row of that section is contextually locked, and
carries the note for that reason. `Info` rows and `ViewOnly` rows never put it
on screen, which follows from decision 1.

The consumer's own `Hint` is left alone. Folding the lock reason into it would
mix two senders in one line, and would displace one of them in a section that
has both — the same objection MSM-17 already records against repurposing that
line.

### 4.7 · One poll, screen-wide

The colour is the one effect CK does not refresh by itself: a state change
repaints the row only at the next selection change, which is why vanilla's
V-Sync row calls `ResetEffects()` on its neighbour by hand.

`ModSettingsScreen.Update()` already exists and polls the reset key. It gains a
comparison of one screen-wide state: whether a player exists, and that
player's rights answer. Both are global, so this is one comparison per frame
rather than one per row. On a change, the affected rows get their
`ResetEffects()` and every section note is re-evaluated.

**A poll is not a compromise here; it is what the base game does.** There is no
event and there cannot be one: `adminPrivileges` and `guestMode` are
`[GhostField]` ECS fields whose changes arrive as a NetCode snapshot, so no
code path exists that could raise anything. CK itself polls in at least two
comparable places — `SettingsNotAvailableNote` is a ten-line `Update()`
component solving this exact problem, and `RadicalMenuOption.Update()`
re-evaluates its own gate every frame.

A note appearing or disappearing changes the section's height, so the change
also triggers the re-layout path `RenderContent` already uses.

### 4.8 · The value refresh

The remaining third of MSM-11. A row is redrawn at four places today — on
bind, on `OnParentMenuActivation`, after the player's own change, and after a
section reset — and all four originate with the player or the screen. A value
changing from outside never reaches an open row.

Both widgets subscribe to `_def.Entry.SettingChanged` on bind and call
`Refresh()`; they unsubscribe in `OnDestroy()`. `ConfigEntryBase` exposes it as
a `public event EventHandler` and filters to its own entry, so a row listens to
exactly its own value.

Unsubscribing is not a formality. `Populate()` destroys every row on every open
and already has to release each `PugText`'s pooled glyphs first — the comment
there records what happened when it did not: after several open/edit/reopen
cycles, every row in every section rendered text-less. A subscription
outliving its row is the same class of mistake.

**What this repairs today** is the duplicate-key case: when a consumer declares
one key twice, CoreLib's `Bind` returns the cached entry, so two rows show one
entry and changing one leaves the other stale. MSM warns about duplicate keys
but does not drop the second declaration; the `testDupKeyAccess` fixture covers
the access-level half of the same situation.

What it is *for* is MSM-19, which names this subscription as its delivery path
to an open row rather than building an id space for it.

## 5 · Acceptance criteria

1. A `Server`-scoped setting opened from the main menu renders label and value
   in `UNSELECTABLE_TEXT_COLOR`, is skipped by up/down navigation, and does not
   respond to a mouse click.
2. The same setting opened in a session with rights renders and behaves
   normally.
3. A `ViewOnly` setting renders exactly as it does today: static grey value,
   navigable, not red.
4. An `Info` row is unaffected in both situations.
5. A locked list row renders label, preview and drill arrow red, remains
   reachable by navigation, and opens its drill-in read-only.
6. That row, while selected, renders the lightened red rather than selection
   blue.
7. A section with at least one locked row shows the note between hint and box;
   a section with none shows no note and no gap.
8. The note's text names the world when no player exists, and rights when a
   player exists without them.
9. A consumer section whose settings state no scope shows no note and no red
   row, in either situation — the `Client` default from ADR-012 holds.
10. Pressing confirm on a locked **settings** row produces neither a sound nor
    a footer select hint. The locked **list** row is the exception and keeps
    both: activating it is what opens the read-only drill-in (4.3).
11. Revoking the player's admin level while the screen is open turns the
    affected rows red and shows the note, without closing and reopening.
12. Restoring it reverses both in the same way.
13. A section reset offers and resets only the rows that are editable; a
    section with nothing editable offers no reset hint.
14. Changing a value from outside while its row is on screen updates the
    rendered text. Testable without a second mod through the duplicate-key
    case: a fixture declaring one key twice yields two rows over one entry, so
    changing one must update the other.

Criteria 1, 2, 8, 11 and 12 need a dedicated server and an `Admins.json`
placeholder — see § 9.

## 6 · Edge cases the design covers

| Case | Behaviour |
|---|---|
| Selection strands on a row that becomes locked | navigation away still works (the skip tests the target); 4.5 removes sound and hint, so the row is inert without being misleading |
| `RadicalMenu.Activate()` lands on a locked row at open | same as above — inert, and the section note explains it |
| Every row of every section is locked | navigation finds no target and nothing responds. Accepted deliberately: there is genuinely nothing to operate, the notes say why, and the back key is not bound to menu options |
| Drill-in screen | untouched. Its prefab sets `useUIElementsForNavigation: 1`, where a `GRAYED_OUT` neighbour makes navigation **stall** instead of stepping over; a read-only list's rows stay `ACTIVE` as today |
| Section reset | already correct — `IsInScope` asks `IsEditable`, and `CanReset` returns false when nothing is editable, so the hint disappears on its own |
| `PreWarm` | runs at load, where there is never a player, so it builds every scoped row as locked. Harmless: the pre-warmed instance is disabled in the same frame and `Populate()` rebuilds from scratch on a real open |
| Discovered foreign sections | treated exactly like registered ones, same notes. They are the only sections that can carry CoreLib's `Server` default |

## 7 · What this is not

- **MSM-16** — the dependency lock ("only while X is on", CK's V-Sync case).
  It is contextual too and will attach at 4.1's question, but its API is a
  separate point that also depends on MSM-15 for indentation.
- **MSM-19** — server sync. It consumes 4.8's subscription; nothing here sends
  or receives anything.
- **MSM-36** — that MSM's writes may not reach disk with GMCM installed. A
  save-path defect, unrelated to rendering, deliberately left alone.
- **A reason per row.** Decided against in 3.1; the note is per section.

## 8 · Open questions

- **The two loc terms' names.** They follow MSM's own schema; no decision rests
  on them, so they are settled while implementing.
- **Whether `SignLabels` carries a scope.** The survey of installed foreign
  configs missed it while it does write a `.cfg`, so the census in § 9 is a
  measurement and not a complete count. It changes nothing in this design —
  one more or less locked foreign section is the case the design already
  handles.

## 9 · Verification

Criteria 3, 4, 5, 6, 7, 9, 10, 13 and 14 are observable in singleplayer, with
the `ViewOnly` fixtures (`LongReadOnly`, `ChoiceReadOnly`) and the list
fixtures.

Criteria 1, 2, 8, 11 and 12 need a live session with a player holding no
rights, which singleplayer cannot produce — everyone reports `int.MaxValue`
there. The handbook's `docs/ck/multiplayer-and-server.md` records the route
that needs no second account: a placeholder entry carrying a foreign `steamId`
in `Admins.json` ends the bootstrap rule, so one's own connection lands on
stage 0. Writing stage 1 rather than 2 additionally buys a revocable role,
which is what criteria 11 and 12 need — stage 2 cannot be taken away, and
guest mode does not survive a server restart.

`docs/manual-tests.md` already carries the three dedicated-server rounds
MSM-18 introduced; these criteria extend that section rather than starting a
new procedure.

**Measured before this design, and worth recording.** Of 53 installed mods,
only four foreign ones write a CoreLib config at all: PlacementPlus (8
entries, `Client` only — never locked), CoreLib itself (16, mixed),
GeneralConfigMenu (1, mixed) and SignLabels (1, unmeasured). CK-QOL would be
the interesting case — 11 entries, no `ConfigAccessLevel` anywhere in its
source, so all of them `Server` — but it is switched off in the mod menu and
writes no config. The red is therefore moderate in practice, and the note
realistically appears once, not once per section.
