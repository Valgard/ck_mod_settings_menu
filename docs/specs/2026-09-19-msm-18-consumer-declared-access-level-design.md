# Design — A consumer-declared access level, for every widget (MSM-18)

- **Date:** 2026-09-19
- **Status:** Design approved in chat. Not yet implemented.
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
> modfile `7840263`; neither is edited here.

## 0 · Two names for one thing

**`requireReload`** (CoreLib) and **`RequiresRestart`** (MSM) mean the same
thing. CoreLib's own parameter documentation settles it — *"Indicates whether
restarting the game is required for the changes to take effect"*
(`ConfigFile.cs:489`). The name is nonetheless misleading, because CoreLib also
has `ConfigFile.Reload()`, which re-reads the file and is unrelated. This spec
says **restart** for the concept and uses each field's own name when naming the
field.

## 1 · Goal

Let a consumer state who may change a setting, on the same path it declares
everything else. `SettingDef.ReadOnly` already exists and works, and only
discovery can set it: `SectionBuilder` passes no `ConfigScope` to `_file.Bind`,
so every declared setting is scope-less. A consumer who wants *"the host may
change this, a joining player may only look"* has no way to say so.

The mechanism is not the work. CoreLib carries it (`ConfigScope`,
`ConfigAccessLevel`, `Changeable()`), and `ForeignConfigDiscovery.IsReadOnly`
already reads it correctly for foreign mods. What is missing is that the
standard path can reach the rule that exists — plus three clean-ups that only
become expressible once it does, and which are the reason this is a point of its
own rather than a parameter.

## 2 · The four defects, as measured

Each was verified against the code rather than taken from the roadmap; two
roadmap claims did not survive that and are corrected in §7.

### 2.1 · Every declared setting is formally `Server`, and nobody chose it

`SectionBuilder.BindGuarded` calls `_file.Bind(_currentSection, key, def,
description)` — one call site, no scope. CoreLib then applies
`Scope = scope ?? ConfigScope.Empty` (`ConfigEntryBase.cs:78`), and
`ConfigScope.Empty` is `new()`, whose constructor defaults to
`ConfigAccessLevel.Server`.

Every setting declared through MSM is therefore server-scoped, MSM's own
"show detected mod settings" toggle included. It is harmless only because
nothing on the declared path reads the level today. MSM-19 makes something read
it.

### 2.2 · The restart flag never reaches the place others read

`SettingDef.RequiresRestart` is one field with two writers:
`SectionBuilder.RequiresRestart()` on the declared path, and a copy of
`e.Scope.requireReload` on the discovered one (`ForeignConfigDiscovery`,
in the `BuildDef` object initialiser). So MSM has already decided the two mean
the same thing — it aligns them.

The duplication is therefore not in the model but in the **input paths**, and it
is asymmetric: the discovered path reads where outsiders read; the declared one
writes where only MSM looks. This is observable, not theoretical. General Mod
Config Menu renders a marker per entry straight out of the scope:

| GMCM | Reads | Renders |
|---|---|---|
| `UIConfigEntry.cs:73-79` | `Scope.accessLevel` | permission icon, one template each for Admin / Server / Client / ViewOnly |
| `UIConfigEntry.cs:87-89` | `Scope.requireReload` | a reload marker beside the row |
| `UIConfigEntry.cs:166-177` | `Scope.accessLevel` | Server/Admin get a second value column; ViewOnly is not editable |

A player with both mods installed therefore sees every MSM consumer setting
carrying the **Server** icon and a second, server-side value column — including
`player-coordinates-hud.position` — while the four settings that call
`.RequiresRestart()` carry **no** reload marker.

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
   section rather than inherited.
3. **`requiresRestart` travels the same three levels, and `.RequiresRestart()`
   stays** as a shorthand for the last-declared row. Both write the same place.
4. **`SettingDef.ReadOnly` becomes `Locked`, is computed, and means only the
   contextual lock.** The structural case is `Kind == SettingKind.Info` and gets
   no field.

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

**The nullability is load-bearing, not a style choice.** `ConfigAccessLevel` is
an ordinary enum whose zero value is `Client` (because `ViewOnly = -1` opens the
range downwards). With a non-nullable parameter, "not stated" would be
indistinguishable from "stated as Client", so a group could never pass its own
value down to a row that says nothing — every row would silently pull the group
back to Client. The middle level would be inert, and nothing would report it.

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

**The `ConfigAccessLevel` overload cannot be used for this.**
`ConfigFile.cs:490` takes a `string description` and builds
`new ConfigDescription(description)` internally, discarding any
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
declaration or a heading.

### 4.4 · `Locked` is computed, never stored

```csharp
public bool Locked => Entry != null && AccessLock.IsLocked(Entry.Scope);
```

**Storing it would be a silent, permanent bug on the declared path.**
`ModSettings.Sections` is a static list that `Build()` fills once, so a declared
`SettingDef` is constructed in `EarlyInit`/`Init` and lives unchanged until the
game exits — unlike a discovered one, which `Discover()` rebuilds on every menu
open. A field computed at construction would be evaluated at the title screen,
where `Manager.main.player` is null and `IsReadOnly` conservatively answers
`true` for Server and Admin. Every server-scoped setting would then stay locked
for the whole session, including inside a world.

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
| `SectionReset.IsInScope` | must spell out both reasons — `!Locked && Kind != Info && Entry != null`. Today's `!ReadOnly` covered the structural case by accident; without this, a discovered Info would newly fall into the reset |
| `ForeignConfigDiscovery.BuildDef` | drops `ReadOnly = IsReadOnly(e.Scope)`, the three structural `ReadOnly = true`, and the `RequiresRestart` copy |
| `SettingWidget`, `ListDetailItem`, `ListDetailScreen` | read `Locked` instead of `ReadOnly`; `EffectiveEditing` demotes on `Locked` |
| The nine consumers | compile and behave unchanged. `Client` alters nothing that works today, because nothing reads the level on the declared path yet |
| `.RequiresRestart()`'s four call sites | unchanged |

**A list declared `ListEditing.ReadOnly` stays outside all of this.** It is the
consumer saying "this list is display-only by design", not "not by you, not right
now", and `SectionReset` deliberately *does* reset it — the values are the mod's
to define. `Locked` stays false for it, exactly as `ReadOnly` does today.

## 6 · Acceptance criteria

1. A consumer can state an access level at section, group and widget level, and
   the innermost statement wins.
2. A consumer that states nothing gets `Client` — verifiable in the written
   `.cfg`'s companion state and in GMCM's permission icon.
3. `ViewOnly` declared by a consumer renders the row as locked in MSM, in every
   session type including the title screen.
4. `Server` declared by a consumer is editable for the host and locked for a
   joined guest.
5. `Admin` declared by a consumer is editable for an admin and locked for a
   non-admin guest.
6. A setting marked restart-required — by either route — carries GMCM's reload
   marker and raises MSM's restart prompt when its value actually changes.
7. A group-level `requiresRestart: true` applies to every row in the group and a
   row may still override it.
8. Changing one row's metadata after the bind leaves its neighbours' scopes
   untouched.
9. `ConfigScope.Empty` is never written to: an unrelated mod's scope-less entry
   still reports `Server` and no reload after MSM has bound its own settings.
10. A discovered Info row is still skipped by the section reset.
11. A list declared `ListEditing.ReadOnly` is still restored by the section
    reset.

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

## 8 · Out of scope

- **The rendering of a lock** — the dull red, the navigation skip, the reason
  text — is MSM-10. This point produces only the vocabulary MSM-10 needs to tell
  "locked" from "inert".
- **The transport** is MSM-19. Nothing here sends, receives or awaits anything;
  a `Server`-scoped value still takes effect locally and nowhere else.
- **An operator-side override** — whether a server may claim a setting whose
  author left it at `Client` — is recorded as unresolved in MSM-19's `AdminOnly`
  section and is not decided here.

## 9 · Verification (manual, in-game)

There are no automated tests in this repository; verification is a person
walking the menu. Two properties of this point make that more involved than
usual.

**Permission semantics cannot be tested in a single-player session.** An offline
session reports `int.MaxValue` privileges and a host holds level 2, so `Server`
and `Admin` behave identically there — criteria 4 and 5 need the local dedicated
server (`../utils/server.sh start`) plus a second client joining as a guest. See
`docs/ck/multiplayer-and-server.md`.

**Criterion 9 needs a scope-less foreign entry to observe.** Any discovered mod
that binds without a scope serves; the check is that its GMCM permission icon
and reload marker are unchanged after MSM has bound its own settings in the same
session.

Criteria 1, 2, 6, 7, 10 and 11 are checkable in single-player with the
`TestFixtures` dev flag build (`MOD_DEV_FLAGS=TestFixtures ../utils/build.sh`);
see `docs/manual-tests.md` for the walk. New fixtures are needed for a
group-level declaration and for a row that overrides its group.

## 10 · References

- `docs/roadmap.md` — MSM-18, and MSM-19 for what this is a prerequisite of
- [ADR-010](../adrs/010-heading-rows-are-not-menu-options.md) — the
  `RequiresRestart()` guard ordering
- [ADR-011](../adrs/011-a-group-is-declared-not-inferred.md) — `Group()`, and
  why both of its effects are named at the call site
- CoreLib `4.0.5` — `ConfigScope.cs`, `ConfigFile.cs:432-493`,
  `ConfigEntryBase.cs:78`
- General Mod Config Menu, mod.io modfile `7840263` —
  `Scripts/Scripts/UIConfigEntry.cs`
