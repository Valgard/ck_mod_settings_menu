# Design — Locked settings render as withheld (MSM-10)

Roadmap point MSM-10, with MSM-11 merged into it in full. MSM-11 is not
deferred and not split: its permission half falls out of this design as a
by-product, its colour half was already part of MSM-10, and its value-refresh
half is § 4.8 here.

Builds on [ADR-012](../adrs/012-permission-and-restart-cascade-from-the-nearest-declaration.md),
which shipped the vocabulary this design renders. Before it, no consumer could
state that a setting belongs to the server, and `SettingDef.ReadOnly` conflated
two unrelated claims.

**Citations.** Game-code line numbers are from the
`CoreKeeperDecompile-1.3.0.2-182b` checkout. The canonical `CoreKeeperDecompile`
symlink resolves to 1.3.0.4, whose bodies are identical but whose lines sit
elsewhere — `IsSelectionEnabled` is 358545 here and 359560 there. A review of an
earlier draft found one number taken from the symlink, so this is a rule with a
violation history.

## 0 · What locks a row, exactly

Three states look alike and behave differently. The first two were one field
until ADR-012 split them.

| State | Source |
|---|---|
| **contextually locked** | a `Server` or `Admin` scope whose condition is currently unmet — see the table below |
| **view-only** | the author declared `ConfigAccessLevel.ViewOnly`; never editable through the menu |
| **shapeless** | `Kind == Info`: no editable widget exists for this value's form |

**The two scope levels do not lock under the same conditions**, and two earlier
drafts of this document got it wrong in opposite ways. The reason both failed is
that `PlayerController.guestMode` is not the world's flag: it returns the world
flag **and** `adminPrivileges < 1` (`Pug.Other:308097-308103`). An admin in a
guest-mode world is not a guest. Feeding that into
`ConfigScope.Changeable()`:

| Scope | Locked when |
|---|---|
| `Client` | never |
| `Server` | the world's guest-mode flag is on **and** `adminPrivileges < 1` |
| `Admin` | `adminPrivileges < 1` — the world flag never enters this answer, because `!guestMode` is already implied by having rights |
| any non-`Client` | there is no player at all — `AccessLock` answers conservatively before `Changeable()` can dereference one |

So a non-admin in a world without the guest-mode flag can change a
`Server`-scoped setting, and an admin can change one in a guest-mode world. Any
check that expects red in either case will not see it.

Two places in this repository already said so and were not consulted: the
`testServerScoped` fixture's comment (`ModSettingsMenuMod.cs:810-811`) and
`docs/ck/multiplayer-and-server.md`. Reading them first would have been cheaper
than deriving it twice.

**CoreLib's scope default depends on which `Bind` overload a mod called**, which
an earlier draft also got wrong, and which decides how much of this feature a
player ever meets:

| Overload | Default |
|---|---|
| `Bind<T>(ConfigDefinition, T, ConfigDescription, ConfigScope = null)` | `null` → `ConfigScope.Empty` → **`Server`** |
| `Bind<T>(string, string, T, ConfigDescription, ConfigScope = null)` | same → **`Server`** |
| `Bind<T>(string, string, T, string description, ConfigAccessLevel = Client, …)` (`ConfigFile.cs:490-491`) | **`Client`** |

**A row can be in more than one state at once.** A discovered entry of an
unhandled type gets `Kind = Info` while keeping its `ConfigEntry` and that
entry's scope (`ForeignConfigDiscovery.cs`, routes 4, 6 and 7, each marked
"regardless of scope"). If that scope came from one of the `Server`-defaulting
overloads, the row is shapeless **and** contextually locked at the title screen.
Only the shapeless half may show.

Throughout this document, **"locked"** on its own means *contextually* locked.
Where `SettingDef.Locked` is meant — the wider property that also covers
`ViewOnly` — it is written as code.

## 1 · Goal

A setting that exists but cannot be changed right now renders in Core Keeper's
own shipped convention, says why, and keeps saying it while the screen is open.

Three things follow, and the third is the merged MSM-11:

1. The row renders as withheld: dull red, still visible, still in its place. A
   settings row is additionally skipped by navigation and unclickable; the list
   row keeps both, for the reason in decision 2.
2. The reason is on screen. A red row whose cause is invisible is worse than no
   lock: the player sees a refusal with no way to resolve it.
3. Nothing is frozen at open.

**The change the third point is for comes from someone else.** A player can
revoke their own stage-1 admin entry — `docs/manual-tests.md` walks it — but not
while looking at this screen: the player list is a different menu, and
`ModSettingsScreen.Update()` returns early unless this screen is the top menu
(`ModSettingsScreen.cs:495`). Nor can they undo it: after self-revocation the
admin check before the whole command switch (`Pug.Other:136440`, warning at
`:136442`, `SetGuestMode`'s case at `:136538` inside it) rejects every admin
command. Loading a world closes the menu outright.

So the case this feature answers is an **admin elsewhere in the session**
changing this player's rights or guest-mode flag while this player stands in the
menu. That is what the roadmap described for MSM-11, and it decides § 9: a
feature that answers someone else's action cannot be observed by one actor.

## 2 · What is already there, and what is missing

ADR-012 left the vocabulary in place:

- **`SettingDef.Locked`** is computed per read, deliberately — a snapshot taken
  when the section was built would freeze the title screen's answer for the
  whole run.
- **`AccessLock.IsLocked(scope)`** is the answer both paths reach, with its
  branch order load-bearing: `ViewOnly` and `Client` are answered before any
  player is consulted, because `Changeable()` dereferences the player above its
  own switch.
- **`IsEditable`** already gates every write: `SettingWidget.Adjust` returns
  early, `SectionReset.IsInScope` skips the row, the value text renders static.

What this design adds is the rendering, plus five things that are not rendering:
a narrower question (4.1), the suppressed activation sound and footer hint
(4.5), the reason text and its carrier (4.6), the repaint on a state change
(4.7), and the value refresh (4.8).

Both widgets currently rule `GRAYED_OUT` out explicitly:

```csharp
public override OptionActiveState GetActiveStateInCurrentScene() =>
    _def != null ? OptionActiveState.ACTIVE : OptionActiveState.INACTIVE;
```

## 3 · Decisions (locked)

| # | Decision | Reason |
|---|---|---|
| 1 | Only a **contextual** lock renders red. `ViewOnly` and `Info` keep today's appearance. | `GRAYED_OUT` means "normally editable, just not right now". Nothing is withheld from a row that is never editable, and greying it out would take it out of navigation — the handbook's rule is to keep permanently read-only rows `ACTIVE` so they stay navigable. |
| 2 | **`GRAYED_OUT` everywhere, except where a row hides content behind an interaction.** Today that is the list row. | A locked list row would otherwise make its contents unreachable: the row shows a preview, the full list lives behind the drill-in. Other rows keep label and value on screen and lose nothing by being unreachable. |
| 3 | The list row stays `ACTIVE`, and **MSM paints it** — as it paints every locked row (4.4). | An earlier draft routed this through an `IsSelectionEnabled(visualOnly: true)` override. That cannot work: 4.4 has to silence the effects that would read it. One painter, not two. |
| 4 | A locked row renders a **lightened red while selected** — the value in 4.4 — list row and settings row alike. | Both can be selected while locked (4.5), and on the settings row the lock can arrive *under* the player's cursor. Leaving selection blue would hide the lock at the moment the player tries to act. |
| 5 | One **note per section**, between hint and widgets box, carrying one of two texts. | A locked section would otherwise repeat the reason once per row. CK words its own note in the plural for the same reason. |
| 6 | The drill-in of a locked list gets **no note of its own**. | The player just came from the red row whose section note gave the reason, and the drill-in shows the lock by what is absent: no add row, no row buttons, no text field. |

### 3.1 · Why the two notes, and only two

| Situation | Note |
|---|---|
| no player — the screen was opened from the main menu | *Some settings can only be changed in a world* |
| in a session, the scope's condition is unmet | *Some settings require permissions you do not have here* |

The first mirrors CK's own `Menu/SettingsNotAvailableNote` — vanilla says "can
only be changed in main menu" for the opposite case, so the player meets this
phrasing in the vanilla options screen. MSM reflects it rather than inventing a
second wording for the same idea.

`Server` and `Admin` are not told apart. Their conditions differ (§ 0), but the
player's options do not: in both cases something about this session withholds
the setting, and neither guest mode nor an admin level is theirs to change from
here.

**The two cannot appear at once.** "No player" is a global condition
(`Manager.main.player == null`), not a property of a row. Either there is no
player — then every non-`Client` row is locked for that one reason — or there is
one, and `Changeable()` decides per scope. A section therefore never needs two
notes at the same time, which is what makes decision 5 conflict-free.

## 4 · Design

### 4.1 · One question, in one place

`SettingDef` gains a computed property for **"withheld right now, and would
otherwise be operable"**:

```csharp
Entry != null
  && Kind != SettingKind.Info
  && AccessLock.Reason(Entry.Scope) is LockReason.NoWorld or LockReason.ConditionUnmet
```

- `Kind != Info` is what § 0's multi-state paragraph warns about. Without it, a
  discovered entry of an unhandled type whose author used a `Server`-defaulting
  overload goes red at the title screen, which is not what criterion 4 asks for.
- `ViewOnly` is excluded by naming only the two contextual reasons — decision 1.
- Computed per read, for the same reason `Locked` is.

This is the seam MSM-16 attaches to: a dependency lock ("only while X is on") is
withheld-right-now in the same sense, so it joins here rather than in the
widgets. The widgets ask one question and do not learn where the answer came
from.

**`AccessLock` returns a reason, and the enum needs four values.** `IsLocked`
answers `bool` today, which cannot say which note applies nor whether the lock
came from `ViewOnly`. It becomes `Reason(ConfigScope)` over
`LockReason { None, NoWorld, ConditionUnmet, ViewOnly }` — `ConditionUnmet`
rather than `NoRights`, because § 0's table shows the two scopes fail their
conditions differently and only one of them is about rights alone; `None` is the
value an
earlier draft forgot, and without it every row would read as locked. `Locked`
stays available as `Reason(...) != None`. The branch order stays: it is what
keeps `ViewOnly` and `Client` from reaching a player dereference at the title
screen.

### 4.2 · The settings row reports `GRAYED_OUT`

`SettingWidget.GetActiveStateInCurrentScene()` returns `GRAYED_OUT` when 4.1's
question is true. Three effects follow: navigation skips the row, the click
collider stays off, and the row keeps its place in the layout. The colour does
**not** follow, because 4.4 silences the effects that would apply it.

**Navigation and the click block are live by themselves.** CK calls
`GetActiveStateInCurrentScene()` every frame from `RadicalMenuOption.Update()`
→ `UpdateClickCollider()`. Because 4.1's question is computed rather than
stored, both follow a state change with no prompting. That is the permission
half of the merged MSM-11, and it costs nothing.

One limit worth stating where the claim is made: this skip exists on the
index-based navigation path. The drill-in screen navigates by `UIelement` links,
where a `GRAYED_OUT` neighbour stalls instead of being stepped over — which is
why § 6 keeps that screen out of scope.

### 4.3 · The list row keeps navigation

`ListWidget` keeps returning `ACTIVE`, so the row stays reachable and its
drill-in openable; `CanBeActivated()` keeps returning true here, unlike in 4.5.
Its colour comes from 4.4, like every locked row's.

**What it does not do is override `IsSelectionEnabled`.** That was an earlier
draft's route and is incompatible with 4.4: the base implementation ignores the
`visualOnly` parameter (`Pug.Other:358545`, whose body returns
`enabled && activeInHierarchy && !ShouldBeGrayedOut()`), so the parameter
matters only to the callers that pass it — and those live inside the effects 4.4
silences. Vanilla ships such an override in the opposite direction (popup
buttons input-dead during the anti-misclick timer while looking normal), but
vanilla leaves its effects running.

### 4.4 · MSM paints every locked row

CK's own red arrives through `PugTextEffectMenuOption`, which picks it by
`IsSelectionEnabled(visualOnly: true)` in the paths that run while a row is
**deselected** — `OnDeselected`, the `colorCooloff` blend, and the snap after
it; `ResetEffect` reaches one through `EndEffectImmediate` rather than reading it
itself. That route is unusable here, for the reason that also explains why MSM
already owns colour code:

**Disabling an effect does not silence it.** `OnSelected` and `OnDeselected`
recolour every entry of `menuOptionEffects` *directly*, ignoring
`MonoBehaviour.enabled` — which is why `SuppressValueSelectionEffect` filters
the value's effect out of that array rather than disabling it, and why
`OnSelected` writes `SELECTED_VALUE_COLOR` / `SELECTED_TEXT_COLOR` without
consulting `IsSelectionEnabled` at all.

**And filtering the value's effect is not enough**, which an earlier draft
missed: the filter is `!fx.isValueText`, so the **label**'s effect survives, and
both row templates carry one (`isValueText: 0` twice in
`Prefabs/ModSettingsMenu.prefab`). Left in place it defeats both colour
decisions, in opposite ways — on a `GRAYED_OUT` settings row `OnSelected` writes
`SELECTED_TEXT_COLOR` over the label (decision 4 lost), and on the `ACTIVE` list
row `OnDeselected` writes the ordinary grey, because `IsSelectionEnabled(true)`
answers `true` there (decision 3 lost). Both happen at the first selection
change.

**MSM therefore writes every colour a locked row shows**, at these points — the
two overrides already exist in both widgets, `ListWidget` for `TintDrill`,
`SettingWidget` for `SuppressValueSelectionEffect`:

| What | Where it is written |
|---|---|
| label, value column, and on the list row the drill arrow | the row's own render path — `SettingWidget.Refresh` / `ListWidget.Render`, which already run on bind and on every refresh |
| the selected tone | the `OnSelected` override, after `base.OnSelected()` |
| back to the deselected tone | the `OnDeselected` override, after `base.OnDeselected()` |

**What silences CK's own writes is an open implementation question, and
emptying `menuOptionEffects` is not the whole answer.** Three facts bound it,
and the combination that satisfies all three has to be established at the
keyboard rather than predicted here:

- `menuOptionEffects` (on `RadicalMenuOption`) drives `OnSelected` /
  `OnDeselected`; `PugText.effects` is a **different array** and drives
  `Render` → `ResetEffects`, which runs at the end of every render. The label
  carries no `dontResetEffectsOnRender`, so that path is live on a locked row
  even with `menuOptionEffects` empty.
- `OnParentMenuActivation` (`Pug.Other:358643-358645`) refills
  `menuOptionEffects` when it is empty, so emptying it is not a stable state.
- Disabling an effect stops its `LateUpdate` but not the direct recolouring
  above, which is the asymmetry `SuppressValueSelectionEffect` exists for.

What is settled is the goal (MSM owns the colours of a locked row) and the
write points (the table above). What is not settled is which combination of
disabling, filtering and re-applying gets there.

**The selected tone, computed once here.** Unity's `Color.grayscale`
(0.299 R + 0.587 G + 0.114 B) is the formula, chosen because engine code can
re-derive it:

| | value |
|---|---|
| `UNSELECTED_TEXT_COLOR` grayscale | 0.5 |
| `SELECTED_TEXT_COLOR` grayscale | 0.7558270 |
| CK's selection factor | 0.7558270 / 0.5 = **1.5116540** |
| `UNSELECTABLE_TEXT_COLOR`, exact | 0.4245283 / 0.1742168 / 0.1835400 |
| × factor, clamped, rounded | **0.642 / 0.263 / 0.277**, alpha 1 |

The intermediate values are given unrounded on purpose: an earlier draft printed
0.75583 and 1.51165, and re-deriving the factor from those rounded figures gives
1.51166 instead. Rec. 709 would give 1.53 and a different triple, so the formula
is named rather than assumed.

Channel-wise scaling rather than interpolation toward white, because
interpolating to white desaturates the hue and weakens the red as a signal.

### 4.5 · Input on a locked row

**`GRAYED_OUT` does not block activation.** `CanActivateCurrentOption()` asks
`GetSelectedMenuOption().CanBeActivated()`, not the state.

And the selection can land on a locked row. `RadicalMenu.Activate()`
(`Pug.Other:358102`) sets `num` to the first option whose state is `ACTIVE`
**or** `GRAYED_OUT` (`:358115`), and three of its terminal paths select `num`
unconditionally; mouse input deselects instead, and `rememberSelectedIndex`
keeps a remembered index only while that option is `ACTIVE`, falling back to
`num` otherwise. A row can also be locked *while* it is selected, which is the
case decision 4 exists for.

`SettingWidget.CanBeActivated()` therefore returns false when 4.1's question is
true. CK gates both the menu-select sound and the footer's select hint on it, so
one override removes a confirmation the player can hear but not see the effect
of. `ListWidget` already overrides the same method for a different reason (an
empty drill-in), which is the precedent for the shape, not for the condition.

The write guards in `Adjust` stay as they are. They ask `IsEditable`, the wider
question and the right one for a write.

### 4.6 · The section note

`SectionBox` gains a second `PugText` beside `hint`, and the section's vertical
stack becomes `[Header, Hint, LockNote, Widgets]`. It is switched with
`SetActive` exactly as the hint is, and the layout skips inactive children — so
a section with nothing locked collapses it away at no cost.

It shows while at least one row of that section satisfies 4.1's question, and
carries the note for that row's reason. `Info` and `ViewOnly` rows do not put it
on screen, because 4.1's question excludes both.

The consumer's own `Hint` is left alone. Folding the lock reason into it would
mix two senders in one line and displace one of them in a section that has
both — the objection MSM-17 already records against repurposing that line.

### 4.7 · One poll, and six mutations to reverse

A state change does not repaint a row by itself, and `ResetEffects()` is not the
remedy: `PugText.ResetEffects` (`Pug.Other:367402`) calls `ResetEffect` on every
effect regardless of `enabled`, and `dontResetEffectsOnRender` gates only the
`Render`-driven call (`:367394`). On a locked row a direct call repaints — blue,
if the row is selected. It is not a no-op; it is the wrong tool. MSM painted
these colours, so MSM re-applies them.

**Six mutations make a row locked, and each needs a reversal.** Earlier drafts
named one, then four, then five; the sixth is 4.4's label colour:

| Mutation | Where | Reversal |
|---|---|---|
| `fx.enabled = false` on both value effects | `MakeValueReadOnly` | restore each effect's **captured** prior state — not a blanket re-enable: the prefab ships `JuicyAppear` disabled, so switching everything on would turn on an effect the row never had |
| `valueText.dontResetEffectsOnRender = true` | `MakeValueReadOnly` | clear |
| `valueText.style.color` | `MakeValueReadOnly` | restore the captured prefab value. `PugTextStyle` is `[Serializable]`, so the instance is per row and this write reaches nothing else |
| `labelText`'s colour, newly written by 4.4 | new | restore the captured prefab value, as for the value column |
| `menuOptionEffects` loses the **value** effect | `SuppressValueSelectionEffect` | destructive — capture the array **where the filtering happens**, not in `Bind`: `base.Awake` fills it *after* `Bind` (`SettingWidget.cs:76` and `:87` both say so), so a capture in `Bind` would save an empty array |
| `menuOptionEffects` loses the **label** effect | new, per 4.4 | same capture |

The last two collapse into one capture of the original array, restored whole —
noting that `OnParentMenuActivation` refills it when empty, so the restore may
have less to do than the table suggests.

**The poll goes before the reset guard, not after it.** `Update()` today reads:
top-menu check (`ModSettingsScreen.cs:495`), then
`if (!SectionReset.CanReset(section)) return;` (`:498`), then the reset keys.
`CanReset` is false when no row of the section is `IsEditable`, which a fully
locked section satisfies — and that is the state criterion 12 starts from. A
poll added "to the reset poll" would be silent there. It belongs
immediately after the top-menu check.

**The poll compares inputs, not a verdict.** It reads the three values every
lock decision rests on: whether a player exists, that player's `guestMode`, and
their `adminPrivileges`. A single derived answer would miss that `Server` and
`Admin` lock under different conditions (§ 0). Three values, global, one
comparison per frame rather than one per row. On a change, every row re-applies
its colours and every section note is re-evaluated.

**A poll is what the base game does here, though not for this question.** There
is no event and there cannot be one: `adminPrivileges` and `guestMode` are
`[GhostField]` ECS fields whose changes arrive as a NetCode snapshot, so no code
path raises an event on a change — the RPC handler that writes `guestMode` is
the only writer, and it notifies nobody. CK's comparable cases are polls —
`RadicalMenuOption.Update()` re-evaluates its own gate every frame, and
`SettingsNotAvailableNote` is a ten-line `Update()` component switching a note on
from another option's state. The second is a precedent for the shape; what it
polls is a local option, not a networked permission.

A note appearing or disappearing changes the section's height, so the change
also triggers the re-layout path `RenderContent` already uses.

### 4.8 · The value refresh

A row is redrawn at four places today — on bind, on `OnParentMenuActivation`,
after the player's own change, and after a section reset — and all four
originate with the player or the screen. A value changing from outside never
reaches an open row.

**The event is on `ConfigFile`, and a row can reach its file.**
`SettingChanged` is declared inside `ConfigEntry<T>` (`ConfigEntryBase.cs:54`,
within the generic class that opens at `:13` and closes at `:55`;
`ConfigEntryBase` begins at `:59`), so `_def.Entry.SettingChanged` does not
compile over the base type MSM holds. But `ConfigEntryBase.ConfigFile` is
**public** (`:87`), so the file is one hop away:

```csharp
_def.Entry.ConfigFile.SettingChanged += OnAnySettingChanged;   // on bind
// in the handler: if (args.ChangedSetting == _def.Entry) Refresh();
```

An earlier draft believed a row could not reach its file and moved the
subscription to the screen, with a map from entry to row. That was unnecessary,
and it reintroduced the mapper the roadmap explicitly avoids for MSM-19.

**Per row is also what makes the duplicate-key case work.** When a consumer
declares one key twice **with the same type**, CoreLib's `Bind` returns the
cached entry, so two rows hold one entry — and with a per-row subscription each
of them hears its own event and refreshes itself, including the row that did not
cause the change. A single screen-level map to "the row holding that entry"
could not express that. The fixture is `testDupKeyAccess` (two `Toggle`
declarations); `testDupKey` is type-different and yields one row, so it does not
exercise this.

Unsubscribing happens in `OnDestroy()`, which is the lifecycle that matches:
`Populate()` destroys every row on every open, and it runs from `Activate()` —
including the `Activate()` that resumes this screen after a drill-in visit. The
screen's own `Deactivate(pop: false)` during such a visit therefore needs no
handling, which is worth stating because the same screen deliberately *does*
distinguish `pop` for its restart flag. A subscription outliving its row would
be the same class of mistake as the pooled glyphs `Populate()` already has to
release — the comment there records what happened when it did not: after several
open/edit/reopen cycles, every row in every section rendered text-less.

What this is *for* is MSM-19, which names this subscription as its delivery path
to an open row.

`docs/roadmap.md:649` carries the `ConfigEntryBase.SettingChanged` error this
design inherited from it, and `docs/architecture.md` still says a server-locked
entry becomes `Info`, which `ForeignConfigDiscovery` contradicts. Both are their
own corrections, not part of this change.

## 5 · Acceptance criteria

Each criterion names the scope it observes, because § 0's two levels lock under
different conditions.

1. A `Server`-scoped **toggle** opened from the main menu renders label and
   value in `UNSELECTABLE_TEXT_COLOR` while deselected, is skipped by up/down
   navigation, and does not respond to a mouse click.
2. The same setting, in singleplayer, renders and behaves normally.
3. A `ViewOnly` setting renders as it does today: static grey value, navigable,
   not red.
4. A **`Server`-scoped `Info` row** is not red and does not raise the section
   note — at the title screen, and in a guest-mode session.
5. A **`Server`-scoped list row** renders label, preview and drill arrow red,
   stays reachable by navigation, and opens its drill-in read-only.
6. The locked list row, while selected, renders the lightened red rather than
   selection blue; so does a settings row locked while it holds the selection.
   **Measured across two screenshots**, because `RadicalMenu` has one selection
   at a time: the same row selected-and-editable, then selected-and-locked. It
   must read as red rather than blue, and differ from the deselected locked rows
   beside it. The exact triple is not readable off a screen and is not what this
   checks. The settings-row half needs the live transition in § 9's step 3 or 4,
   since navigation cannot move onto a `GRAYED_OUT` row.
7. A section with at least one contextually locked row shows the note between
   hint and box; a section with none shows no note and no gap.
8. The note names the world when no player exists, and rights when a player
   exists whose scope condition is unmet.
9. A **registered consumer's** section whose builder calls name no access level
   shows no note and no red row, at the title screen and in a session —
   ADR-012's `Client` default holds, and MSM passes it explicitly, so CoreLib's
   own defaults never apply here. A discovered foreign entry bound through a
   `Server`-defaulting overload (§ 0) does go red where its kind is editable;
   where the kind is `Info`, criterion 4 governs.
10. Pressing confirm on a locked toggle **that holds the selection** produces
    neither a sound nor a footer select hint — reachable only through § 9's live
    transition, for the same reason as criterion 6. The locked list row keeps
    both: activating it opens the read-only drill-in, and that half is
    observable without a server.
11. An admin elsewhere in the session revoking this player's level turns the
    `Admin`-scoped rows red and shows the note **without this player leaving
    the screen**; switching guest mode on afterwards does the same for the
    `Server`-scoped rows. That order is required, not incidental — § 0.
12. Reversing either change restores the rows: editable again, their values
    selectable, their label and value colours back to the prefab's, and the
    `JuicyAppear` effect still disabled. The locked **list** row returns to its
    unlocked appearance too, drill arrow included.
13. A section reset offers and resets the rows `IsEditable` admits, which is the
    set `SectionReset` already uses — including a `ListEditing.ReadOnly` list,
    whose rows nobody can edit but whose value a reset does restore. A section
    with nothing editable offers no reset hint.
14. Changing a value from outside while its row is on screen updates the
    rendered text — exercised with `testDupKeyAccess`, where **both** rows must
    update, including the one that did not cause the change.

New fixtures this requires: a `Server`-scoped **list** (criteria 5, 6) and a
`Server`-scoped **`Info`** row (criterion 4). No new toggle is needed —
`testServerScoped` (`ModSettingsMenuMod.cs:812`) and `testAdminScoped` (`:825`)
declare both levels explicitly.

**One side effect on the existing fixture set, to settle before building.** Six
group and section fixtures bind through the `ConfigDescription` overload with no
scope (`ModSettingsMenuMod.cs:439`, `:440`, `:441`, `:446`, `:455`, `:461`), so
they are `Server` and would go red and unnavigable at the title screen —
obstructing the heading and sort-order checks they exist for. Either they get an
explicit `clientScope`, or their checks move into a session. The comment at
`:68-71` currently claims the opposite and would need correcting either way.

## 6 · Edge cases the design covers

| Case | Behaviour |
|---|---|
| Selection strands on a locked row | navigation away still works (the skip tests the target); 4.5 removes sound and hint, and 4.4 keeps the lock visible while it holds focus |
| `RadicalMenu.Activate()` lands on a locked row at open | an ordinary path, not an anomaly. Same handling as above |
| Every settings row of every section is locked | navigation reaches no toggle and none responds. Accepted deliberately: there is nothing to operate, the notes say why, and the back key is not bound to menu options. A locked **list** row stays reachable, so a screen containing one keeps a target |
| Drill-in screen | untouched. Its prefab sets `useUIElementsForNavigation: 1`, where a `GRAYED_OUT` neighbour makes navigation **stall** instead of stepping over; a read-only list's rows stay `ACTIVE` as today |
| Section reset | already correct — `IsInScope` asks `IsEditable`, and `CanReset` returns false when nothing is editable, so the hint disappears on its own |
| `PreWarm` | runs at load, where there is no player, so it builds every scoped row as locked. Harmless: the pre-warmed instance is disabled in the same frame and `Populate()` rebuilds from scratch on a real open |
| Discovered foreign sections | treated like registered ones, same notes. Whether they are locked depends on the overload their author used (§ 0), not on their being discovered |

## 7 · What this is not

- **MSM-16** — the dependency lock ("only while X is on", CK's V-Sync case). It
  is withheld-right-now too and will attach at 4.1's question, but its API is a
  separate point that also depends on MSM-15 for indentation.
- **MSM-19** — server sync. It consumes 4.8's subscription; nothing here sends
  or receives anything.
- **MSM-36** — that MSM's writes may not reach disk with GMCM installed. A
  save-path defect, unrelated to rendering.
- **A reason per row.** Decided against in 3.1; the note is per section.
- **Correcting `docs/roadmap.md:649` and `docs/architecture.md`.** Both named in
  4.8, both done separately.

## 8 · Open questions

- **The three loc term names** (two notes, and whether the `ViewOnly` reason
  ever needs one). They follow MSM's own schema; no decision rests on them.
- **What exactly silences CK's own colour writes** (§ 4.4). Three constraints
  are known and the combination that satisfies them is not; it is cheaper to
  establish at the keyboard than to predict.

## 9 · Verification

**Every fixture named here is behind `DevFlags.Is("TestFixtures")`**
(`ModSettingsMenuMod.cs:66`, `:598`), so the build is
`MOD_DEV_FLAGS=TestFixtures ../utils/build.sh`. A normal build contains none of
these rows and does not say so. `docs/manual-tests.md` opens with this and is
where these checks are written down.

**Criteria 1, 2, 3, 5, 7, 13 and 14, the list-row halves of 6 and 10, and the
first note of 8, need no
server.** The title screen produces the no-player lock on its own — `AccessLock`
treats a non-`Client` scope as locked while `Manager.main.player` is null — and
singleplayer produces the unlocked case, since `GetAdminPrivileges`
short-circuits to `int.MaxValue` offline.

**Criteria 4, 9 and the second note of 8 need a guest-mode session**, not merely
a session: per § 0 a `Server`-scoped row is free for a non-admin as long as
guest mode is off. They ride along with the server round below.

**Criteria 11 and 12, and the settings-row halves of 6 and 10, need two
accounts.** A single
account can reach the lock — `docs/manual-tests.md` round 3 walks it with one
(round 2 is the control, where nothing locks),
and `docs/ck/multiplayer-and-server.md` explains why self-revocation works at
stage 1 — but it cannot *observe* the live repaint (the player list is another
menu, so the poll is dormant, and returning rebuilds every row) and cannot
reverse it (after self-revocation the admin check rejects every command). So:

**The order matters, because the two halves of criterion 11 need different
starting points.** Per § 0, guest mode does not lock a `Server` row for a player
who holds rights — so the observer has to lose their level *before* guest mode
can show anything:

1. `Admins.json` lists the **admin** at `privileges: 2` and the **observer** at
   `privileges: 1`. The bootstrap rule `adminList.adminList.Count == 0 ||
   isLocalPlayer` (`Pug.Other:293683`) hands out stage 2 only while the list is
   empty, so both entries have to be written rather than earned.
2. The observer joins, opens Options → Mod Settings, and stays there for the
   rest of the sequence, touching nothing.
3. The admin **revokes the observer's level** from the player list. The
   `Admin`-scoped rows go red — criterion 11's second half. `Server`-scoped rows
   do not move: guest mode is still off.
4. The admin **switches guest mode on**. Now that the observer is at stage 0,
   the `Server`-scoped rows go red too — criterion 11's first half.
5. The admin switches guest mode off and restores the level. Both reverse —
   criterion 12.

Guest mode does not survive a server restart (its only writer is the RPC
handler), so both changes have to happen inside one session.

**The foreign-config census, and what it does not show.** Three earlier counts
in this document were wrong — once from a path bug, once from counting scope
*mentions* as entries, once from counting family repos as foreign — so this one
states its method per column. Entry counts come from the live `.cfg` files where
one exists; scope assignments come from reading every `Bind` call in the mod's
installed source, including **which overload** it uses (§ 0).

| Mod | Entries | Scopes |
|---|---|---|
| PlacementPlus | 6 live entries (its `.cfg` also holds 2 CoreLib orphans) | **all `Client`** — two state it explicitly, four use the `Client`-defaulting overload, and the mod's own comment says every entry is Client on purpose |
| CoreLib | 16 | mixed |
| GeneralConfigMenu | 1 | `Admin`, declared (`ModConfig.cs:13`) |
| CK-QOL | ~21 from 11 `Bind` call sites, one of which runs per feature — no `.cfg`, because the mod is switched off | **all `Server`** — no `ConfigAccessLevel` in its source, and its call sites use the first overload, which defaults that way |

`SignLabels` and `AutoRailBridges` are family repos and registered MSM
consumers, not foreign mods — an earlier draft counted the first as foreign.

What this establishes is that a foreign mod's scope cannot be guessed from the
absence of a `ConfigScope` argument, because one overload defaults the other
way. What it does **not** establish is how much red a player sees. The spread
here runs from PlacementPlus, which produces none at all, to CK-QOL, whose
roughly twenty-one entries would all be locked — and four mods on one machine
are not a sample of anything.
