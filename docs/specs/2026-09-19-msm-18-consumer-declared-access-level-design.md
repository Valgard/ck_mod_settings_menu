# Design — A consumer-declared access level, for every widget (MSM-18)

- **Date:** 2026-09-19
- **Status:** Design approved in chat, then revised after a three-lane spec
  review. Not yet implemented.
- **Roadmap point:** MSM-18. It ships together with MSM-19 in 2.0.0 and is that
  point's stated prerequisite, but it is built and verified on its own.
- **Builds on:** ADR-010 (heading rows are not menu options — the
  `RequiresRestart()` guard ordering this touches), ADR-011 (a group is declared,
  not inferred — the `Group()` this extends)

> **Line citations into this repository's own files describe the state before
> the implementation**, written against `387caee`. The branch that implements
> this design moves most of them. To find a construct as it stands now, search
> for its name rather than its line; `git show 387caee:<path>` gives the files
> the numbers were taken from. Citations into CoreLib refer to
> `CoreLib-source-4.0.5`, and those into General Mod Config Menu to mod.io
> modfile `7840263`; neither is edited here. Claims about the game's own
> permission model cite `docs/ck/multiplayer-and-server.md` rather than a
> decompile line, because that chapter already carries them and outlives any
> line number.

## 0 · Words that mean more than one thing

**`requireReload`** (CoreLib) and **`RequiresRestart`** (MSM) mean the same
thing. CoreLib's own parameter documentation settles it — *"Indicates whether
restarting the game is required for the changes to take effect"*
(`ConfigFile.cs:489`). The name is nonetheless misleading, because CoreLib also
has `ConfigFile.Reload()`, which re-reads the file and is unrelated. This spec
says **restart** for the concept and uses each field's own name when naming the
field.

**"Read-only" names three unrelated things** in and around this code, and this
point renames exactly one of them:

| Name | Means | This point |
|---|---|---|
| `SettingDef.ReadOnly` | two things at once — see §2.3 | **renamed** to `Locked`, narrowed to the contextual lock |
| `ListEditing.ReadOnly` | a consumer's declaration that a list is display-only by design | **unchanged**, name included — it is not a permission and never was |
| `ConfigFile.AllConfigFilesReadOnly` | CoreLib's immutable view over its own registry | untouched, unrelated |

The middle one keeps its name because renaming it would be a second, unrelated
break for an unrelated meaning. That the two now differ in name is the point:
they always differed in meaning.

## 1 · Goal

Let a consumer state who may change a setting and whether it travels, on the
same path it declares everything else. `SettingDef.ReadOnly` already exists and
works, and only discovery can set it: `SectionBuilder` passes no `ConfigScope`
to `_file.Bind`, so every declared setting is scope-less.

The mechanism is not the work. CoreLib carries it (`ConfigScope`,
`ConfigAccessLevel`, `Changeable()`), and `ForeignConfigDiscovery.IsReadOnly`
already reads it correctly for foreign mods. What is missing is that the
standard path can reach the rule that exists — plus three clean-ups that only
become expressible once it does, and which are the reason this is a point of its
own rather than a parameter.

### 1.1 · What the four levels actually do

This table is the spec's own correction of a sentence an earlier draft opened
with. It claimed a consumer would want to say *"the host may change this, a
joining player may only look"* and that `Server` was that statement. It is not.

| Level | `Changeable()` | `ShouldSync` | What a player experiences |
|---|---|---|---|
| `ViewOnly` (-1) | always false | false | locked everywhere, never travels |
| `Client` (0) | always true | false | never locked, never travels |
| `Server` (1) | `!player.guestMode` | **true** | locked **only** in a guest-mode world, and there only for non-admins; travels |
| `Admin` (2) | `!player.guestMode && adminPrivileges > 0` | **true** | locked for every non-admin; travels |

The decisive detail is that `PlayerController.guestMode` is true only when the
**world's** guest-mode flag is set *and* `adminPrivileges < 1`
(`docs/ck/multiplayer-and-server.md` § "Who is allowed to change things"). Guest
mode is off by default, so on an ordinary world a joined, non-admin player
changes a `Server`-scoped setting exactly as the host does.

**So `Server` is a sync marker, not a lock**, and this spec says so wherever it
names the level. A consumer who wants a genuine "not by you" reaches for
`Admin`, which locks every non-admin regardless of the world flag. `ViewOnly`
locks everyone including the host and is therefore for values a mod computes
rather than accepts.

MSM adds one rule of its own on top, and only one: with no player at all — the
title screen — `Server` and `Admin` are treated as locked, because
`Changeable()` would dereference a null player. That is a safety answer, not a
permission answer.

## 2 · The four defects, as measured

Each was verified against the code rather than taken from the roadmap; two
roadmap claims did not survive that and are corrected in §7.

### 2.1 · Every declared setting is formally `Server`, and nobody chose it

`SectionBuilder.BindGuarded` calls `_file.Bind(_currentSection, key, def,
description)` — one call site, no scope. CoreLib then applies `Scope = scope ??
ConfigScope.Empty` (`ConfigEntryBase.cs:78`), and `ConfigScope.Empty` is
`new()`, whose constructor defaults to `ConfigAccessLevel.Server`.

Every setting declared through MSM is therefore server-scoped, MSM's own
settings included. **This is not harmless today, and an earlier draft said it
was.** MSM itself does not read the level on the declared path, but MSM is not
the only reader of a CoreLib entry — §2.2 names three places in another shipped
mod that read it right now, and one of them syncs on it.

### 2.2 · The restart flag never reaches the place others read

`SettingDef.RequiresRestart` is one field with two writers:
`SectionBuilder.RequiresRestart()` on the declared path, and a copy of
`e.Scope.requireReload` on the discovered one (`ForeignConfigDiscovery`,
in the `BuildDef` object initialiser). So MSM has already decided the two mean
the same thing — it aligns them.

The duplication is therefore not in the model but in the **input paths**, and it
is asymmetric: the discovered path reads where outsiders read; the declared one
writes where only MSM looks. General Mod Config Menu is such an outsider, and it
reads both halves of the scope:

| GMCM | Reads | Does |
|---|---|---|
| `UIConfigEntry.cs:73-79` | `Scope.accessLevel` | renders a permission icon, one template each for Admin / Server / Client / ViewOnly |
| `UIConfigEntry.cs:88-89` | `Scope.requireReload` | renders a reload marker beside the row |
| `UIConfigEntry.cs:166-177` | `Scope.accessLevel` | gives Server/Admin a second, server-side value column; makes ViewOnly non-editable |
| `ConfigSyncSystem.cs:42-52` | `Scope.ShouldSync` | **collects the entry into its sync set** |

A player with both mods installed therefore sees every MSM consumer setting
carrying the **Server** icon and a second value column — including
`player-coordinates-hud.position` — and GMCM syncs all of them, while the
settings that call `.RequiresRestart()` carry **no** reload marker.

### 2.3 · `SettingDef.ReadOnly` carries two unrelated claims

The field's own comment says so. The two are:

- **contextual** — view-only, or server/admin-scoped and not changeable in this
  session. Set once per `BuildDef` from `IsReadOnly(e.Scope)`.
- **structural** — no editable widget exists for this value's shape at all. Set
  at three places in `ForeignConfigDiscovery.BuildDef`: an unreadable
  constraint, a non-list string, an unhandled type. All three set
  `Kind = SettingKind.Info` in the same breath, and there is no fourth.

Only the first is a lock worth showing a player, and MSM-10's feedback has to
tell them apart. Today a server-locked Info collapses both into one `true`.

### 2.4 · `IsReadOnly` is private to the discovery path

It is a `private static` in `ForeignConfigDiscovery`. Once the declared path has
a scope, both paths need the same answer, and MSM-19's send façade would be its
third caller.

## 3 · Decisions (locked)

1. **Three levels, cascading.** `Section()`, `Group()` and each widget method
   take the metadata; the innermost statement wins.
2. **The root default is `Client`.** Chosen deliberately, and stated by MSM's own
   sections rather than inherited.
3. **`requiresRestart` travels the same three levels, and `.RequiresRestart()`
   stays** as a shorthand for the last-declared row. Both write the same place.
4. **`SettingDef.ReadOnly` becomes `Locked`, is computed, and means only the
   contextual lock.** The structural case is `Kind == SettingKind.Info` and gets
   no field.
5. **`Server` is documented as a sync marker**, `Admin` as the lock. MSM does not
   tighten `Server` beyond what `Changeable()` answers — diverging from CoreLib
   would make MSM and GMCM treat one entry differently.

## 4 · Design

### 4.1 · The cascade

```csharp
ModSettings.Section(this, access: …, requiresRestart: …)
    .Group("bake", access: …, requiresRestart: …)
    .Choice(out var f, "reductionFactor", …, access: …, requiresRestart: …)
```

`Group()` and the widget methods take `ConfigAccessLevel?` and `bool?`, where
`null` means inherit. Only `Section()` takes real values, defaulting to
`ConfigAccessLevel.Client` and `false`.

**The nullability carries the middle level.** `ConfigAccessLevel` is an ordinary
enum whose zero value is `Client` (because `ViewOnly = -1` opens the range
downwards). With a plain non-nullable parameter, "not stated" and "stated as
Client" are the same value, so a group's own setting would be pulled back to
Client by every row that says nothing — the middle level would be inert, and
nothing would report it. Overloads could distinguish the two cases instead, and
that is the reason this is a preference rather than a necessity: two metadata on
six widget methods is four overloads each, against one nullable parameter that
says what it means.

`Group()` keeps the shape ADR-011 chose for it: what a call does is visible in
its own arguments. The access level is a statement about authority, so it is a
named argument at the place it applies, never a mode that is switched on
somewhere above and read by scrolling back.

### 4.2 · One fresh `ConfigScope` per bind

`BindGuarded` builds a scope and passes it. Two rules, both mandatory:

- **Never leave it null.** That yields `ConfigScope.Empty`, a `static readonly`
  singleton shared by every scope-less entry in the process. It is reachable and
  writable — `ConfigScope` is a class whose `requireReload` and `accessLevel`
  fields are public and not readonly — so a write through one entry's `Scope`
  would reach every other mod's scope-less entries. `static readonly` freezes
  the reference, not the object.
- **Never share one instance across entries, even when the values are equal.**
  The cascade invites building one scope per group and handing it to each row;
  a later `.RequiresRestart()` on one row would then change the whole group.

**`BindGuarded` is not the only bind in this mod.**
`ModSettingsMenuMod.BindNamingDiagnostics` (`ModSettingsMenuMod.cs:600-613`)
binds `reportForeignNamingStages` straight onto `ConfigStore.ForMod(...)`,
bypassing `SectionBuilder` entirely — so it lands on the shared
`ConfigScope.Empty` exactly as the first rule forbids, and would keep saying
`Server` while every other MSM setting moved to `Client`. It gets its own
explicit scope. It is the only such bind on the shipped path; every other direct
`ConfigFile.Bind` in this repository sits behind the `TestFixtures` dev flag.

**The `ConfigAccessLevel` overload cannot be used for this.**
`ConfigFile.cs:490` takes a `string description` and builds `new
ConfigDescription(description)` internally, discarding any
`AcceptableValueBase`. Slider and Stepper need `AcceptableValueRange`, Choice
needs `AcceptableValueList`; routing through that overload would silently drop
every constraint. The `ConfigScope` overload (`ConfigFile.cs:473`) is the one to
use.

### 4.3 · Restart: two ways in, one place stored

Both the cascade parameter and `.RequiresRestart()` write
`entry.Scope.requireReload`. The modifier can do this despite running after the
bind, because the scope object is mutable and — per §4.2 — now MSM's own.

`SettingDef.RequiresRestart` stops being a field:

```csharp
public bool RequiresRestart => Entry?.Scope?.requireReload ?? false;
```

The `?? false` carries the `Label` case, whose `Entry` is null: a heading cannot
change and never needs a restart. The discovered path stops copying anything —
it reads the same property as everyone else.

`RequiresRestart()`'s two existing guards stay exactly as they are. Their
ordering is load-bearing (ADR-010), and nothing here changes what they protect
against: a modifier that would otherwise attach to the wrong row after a failed
declaration or a heading. Both guards have a fixture
(`ModSettingsMenuMod.cs:699` and `:714`), and those fixtures are the evidence
that the promise was kept — they must still trip after the change.

### 4.4 · `Locked` is computed, never stored

```csharp
public bool Locked => Entry != null && AccessLock.IsLocked(Entry.Scope);
```

**Storing it would be a silent, permanent bug on the declared path.**
`ModSettings.Sections` is a static list that `Build()` fills once, so a declared
`SettingDef` is constructed in `EarlyInit`/`Init` and lives unchanged until the
game exits — unlike a discovered one, which `Discover()` rebuilds on every menu
open. A field computed at construction would be evaluated at the title screen,
where `Manager.main.player` is null and `IsLocked` conservatively answers `true`
for Server and Admin. Every server-scoped setting would then stay locked for the
whole session, including inside a world.

`AccessLock.IsLocked` is today's `ForeignConfigDiscovery.IsReadOnly`, moved
unchanged to a place both paths reach:

```csharp
internal static bool IsLocked(ConfigScope scope)
{
    if (scope == null)
        return false;
    if (scope.accessLevel == ConfigAccessLevel.ViewOnly)
        return true;
    if (scope.accessLevel == ConfigAccessLevel.Client)
        return false;
    if (Manager.main == null || Manager.main.player == null)
        return true;
    return !scope.Changeable();
}
```

Note the order: `ViewOnly` and `Client` are answered before any player is
consulted, which is why both are observable in a single-player session and even
at the title screen. Only `Server` and `Admin` need a world.

The `scope == null` branch is unreachable for a CoreLib entry
(`ConfigEntryBase.cs:78` guarantees a non-null scope) and is kept as a guard for
a caller that is not one.

Cost as a property getter: two field reads through `Manager.main.player` inside
`Changeable()`. That is cheap enough for the render path, which is where the
existing `ReadOnly` readers already sit.

### 4.5 · The structural case keeps no field

The three sites in `BuildDef` set `Kind = SettingKind.Info` and nothing else.
Anything asking "is there an editable widget for this at all?" asks the `Kind`.

This stays correct when MSM-02 lands: `.Info(string key, Func<string> value)`
has no `out` handle, so an Info row is non-editable by construction, declared or
discovered. A second field would be a second home for what `Kind` already
states, and those two drift.

## 5 · What moves with it

| Site | Change |
|---|---|
| `SectionBuilder.BindGuarded` | builds and passes a fresh `ConfigScope` per entry |
| `Section()`, `Group()`, every widget method | gain `access` and `requiresRestart` parameters |
| `ModSettingsMenuMod.BindNamingDiagnostics` | gains an explicit scope; see §4.2 |
| `SettingModel.SettingDef` | `ReadOnly` → computed `Locked`; `RequiresRestart` field → computed property |
| `SettingWidget` | the only UI class that reads the field today; reads `Locked` |
| `SectionReset.IsInScope` | must spell out both reasons — `!Locked && Kind != Info && Entry != null`. Today's `!ReadOnly` covered the structural case by accident; without this, a discovered Info would newly fall into the reset |
| `ForeignConfigDiscovery.BuildDef` | drops `ReadOnly = IsReadOnly(e.Scope)`, the three structural `ReadOnly = true`, and the `RequiresRestart` copy; `IsReadOnly` moves to `AccessLock` |
| `.RequiresRestart()`'s six call sites | unchanged — four in two consumers, two fixtures in this repo |

**`ListDetailItem` and `ListDetailScreen` are not change sites.** Both mention
`SettingDef.ReadOnly` only in comments; they read `EffectiveEditing`, which is
where the demotion happens. An earlier draft listed them, having counted comment
matches as reads.

**Accepted consequence: a section entirely locked loses the reset affordance
too, not just its rows.** `SectionReset.CanReset` gates the footer hint and
the reset key on whether `IsInScope` finds at least one row, and `IsInScope`
now asks `IsEditable` — so a section whose every declared row is `Server`-
or `Admin`-scoped reports none at the title screen, where §1.1's own safety
rule treats both levels as locked. The hint and the key both disappear
entirely there, not merely refuse to act, and both return the moment a world
exists in the same session, exactly as the rows they gate on do. Accepted
rather than special-cased: a wholly-`Info` discovered section already lost
the reset this same way before this point existed, and there is nothing
honest to offer resetting when every row it would touch cannot be changed.

### 5.1 · What consumers actually experience

An earlier draft claimed the consumers "compile and behave unchanged". They
compile unchanged, and their behaviour changes in one observable way — which is
the intended effect of the point, not a side effect:

- **Without GMCM installed:** nothing changes. MSM reads the level nowhere on
  the declared path until MSM-19.
- **With GMCM installed:** every MSM consumer setting moves from the Server icon
  to the Client icon, loses its second server-side value column, and — the part
  that matters — **drops out of GMCM's sync set**, because that set is filtered
  on `ShouldSync` (`ConfigSyncSystem.cs:49`) and `Client` is `0`. Until now GMCM
  has been syncing every MSM consumer setting, including HUD positions, on the
  strength of a default nobody chose.

**A list declared `ListEditing.ReadOnly` and no access level stays outside all
of this** — that declaration is not a permission, so `Locked` stays false and
the section reset still restores it, which is right: the values are the mod's to
define. A list that declares *both* is locked like any other row and is skipped,
and that is also right. The two statements are independent, which is exactly why
they are separate fields.

## 6 · Acceptance criteria

1. A consumer can state an access level at section, group and widget level, and
   the innermost statement wins.
2. A consumer that states nothing gets `Client` — observable as GMCM's Client
   permission icon on that row. (Not in the `.cfg`: CoreLib's
   `WriteDescription` emits description, type, default and acceptable values,
   and no scope.)
3. `ViewOnly` declared by a consumer renders the row as locked in MSM in every
   session type, the title screen included.
4. `Server` declared by a consumer is editable for any player in an ordinary
   world, and locked for a non-admin in a world with **guest mode enabled**.
5. `Admin` declared by a consumer is editable for an admin and locked for every
   non-admin, with or without guest mode.
6. A setting marked restart-required — by either route — carries GMCM's reload
   marker and raises MSM's restart prompt when its value actually changes.
7. A group-level `requiresRestart: true` applies to every row in the group and a
   row may still override it.
8. Changing one row's metadata after the bind leaves its neighbours' scopes
   untouched.
9. `ConfigScope.Empty` is never written to: an unrelated mod's scope-less entry
   still reports `Server` and no reload after MSM has bound its own settings.
10. A discovered Info row is still skipped by the section reset.
11. A list declaring `ListEditing.ReadOnly` and no access level is still restored
    by the section reset; one that also declares a locking level is skipped.
12. **Every constraint survives the bind.** A Slider still clamps to its
    declared range and a Choice still cycles exactly its declared tokens, after
    the scope is introduced.
13. **A declared `Server` row seen at the title screen is editable in a world in
    the same session**, without reopening the game.
14. MSM's own settings each carry a deliberately chosen level —
    `reportForeignNamingStages` included.

## 7 · Two roadmap claims that did not survive verification

Both are corrected in the roadmap text as part of this work.

- *"The overload that takes a `ConfigAccessLevel` directly already defaults to
  `Client`, so switching to it may be the whole fix."* It is not a fix: that
  overload takes a `string description` and drops every `AcceptableValueBase`
  (§4.2).
- *"`Client` is the honest one for a framework whose consumers are HUD and UI
  mods."* The conclusion holds, the premise does not. Across the consumers in
  this workspace, roughly half of the declared settings change game rules rather
  than presentation — the XP multipliers, the recipe scaling, and the `enabled`
  toggles of the gameplay mods. The reason to choose `Client` is that it is the
  only value under which a consumer's silence stays silence — it neither syncs
  (`ShouldSync` is `(int)accessLevel > 0`) nor locks in any session type.

A third roadmap sentence is left alone because it is right: MSM-18's own closing
paragraph already says `Server` and `Admin` are indistinguishable in singleplayer
and while hosting. What it does not say — and what §1.1 adds — is that they stay
indistinguishable for a *joined* player too, as long as guest mode is off.

## 8 · Out of scope

- **The lock's appearance** — the dull red, the navigation skip, the reason text
  — is MSM-10. What this point does is make a *declared* lock take effect
  through the rendering MSM already has (`SettingWidget.MakeValueReadOnly` and
  the early return in `Adjust`); what it does not do is change how that looks.
- **The transport** is MSM-19. Nothing here sends, receives or awaits anything;
  a `Server`-scoped value still takes effect locally and nowhere else, and
  `ShouldSync` remains a marker MSM itself does not act on.
- **An operator-side override** — whether a server may claim a setting whose
  author left it at `Client` — is recorded as unresolved in MSM-19's `AdminOnly`
  section and is not decided here.
- **GMCM's `SaveOnConfigSet = false`, now MSM-36.** GMCM clears auto-save on
  every config file whose path does not start with `CoreLib`, in three places,
  and MSM never calls `Save()` itself — so with GMCM installed nothing MSM
  writes reaches the disk, the section reset being only the loudest case. Found
  during this review, unrelated to this point and not fixed by it; the roadmap
  point carries the evidence and what to check first.

## 9 · Verification (manual, in-game)

There are no automated tests in this repository; verification is a person
walking the menu. Every criterion is assigned below, because an unassigned one
is an untested one.

**Single-player, `TestFixtures` build** (`MOD_DEV_FLAGS=TestFixtures
../utils/build.sh`; see `docs/manual-tests.md` for the walk) — criteria **1, 2,
3, 6, 7, 8, 9, 10, 11, 12, 14**. `ViewOnly` (criterion 3) belongs here despite
being a permission: `IsLocked` answers it before consulting a player, so it holds
at the title screen too. New fixtures are needed for a group-level declaration,
for a row overriding its group, for a list that declares both a level and
`ListEditing.ReadOnly`, and for a Slider and a Choice whose constraints are
checked after the bind.

**Single-player, one session, two moments** — criterion **13**. Open the menu at
the title screen, note a declared `Server` row, load a world, open the menu again
and change it. This is the failure §4.4 exists to prevent and it needs no second
player.

**Dedicated server plus a second client** (`../utils/server.sh start`) —
criteria **4 and 5**. Two preconditions that an earlier draft omitted, and
without which the observation proves nothing:

- **Guest mode must be enabled on that world** for criterion 4 to show anything.
  With it off, `Server` is editable for everyone and a correct implementation is
  indistinguishable from a broken one.
- **A dedicated server has no host player.** "Editable for the host" is checked
  on a *hosted* session, not against the dedicated server; on the dedicated
  server the two roles to compare are an admin client and a non-admin client.

Singleplayer cannot substitute for either: everyone there reports
`adminPrivileges == int.MaxValue` (`docs/ck/multiplayer-and-server.md`).

**Criterion 9 needs a scope-less foreign entry to observe.** Any discovered mod
that binds without a scope serves, and this repository's own group fixtures
(`ModSettingsMenuMod.cs:418-440`) bind without one on purpose; the check is that
their GMCM permission icon and reload marker are unchanged after MSM has bound
its own settings in the same session.

## 10 · References

- `docs/roadmap.md` — MSM-18, and MSM-19 for what this is a prerequisite of
- [ADR-010](../adrs/010-heading-rows-are-not-menu-options.md) — the `RequiresRestart()` guard ordering
- [ADR-011](../adrs/011-a-group-is-declared-not-inferred.md) — `Group()`, and why both of its effects are named at the call site
- `docs/ck/multiplayer-and-server.md` § "Who is allowed to change things" —
  `adminPrivileges`, `guestMode`, and why singleplayer has no permission model
- CoreLib `4.0.5` — `ConfigScope.cs`, `ConfigFile.cs:432-493`,
  `ConfigEntryBase.cs:78` and `:137-157`
- General Mod Config Menu, mod.io modfile `7840263` —
  `Scripts/Scripts/UIConfigEntry.cs`, `Scripts/Scripts/ConfigSync/ConfigSyncSystem.cs`
