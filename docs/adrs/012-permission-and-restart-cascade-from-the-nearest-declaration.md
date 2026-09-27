# Permission and the restart flag cascade from the nearest declaration

- Status: accepted
- Date: 2026-09-27
- Builds on [ADR-011](011-a-group-is-declared-not-inferred.md), whose declared group is the middle level this
  cascade needs to exist at all
- Implements roadmap point MSM-18, which shipped with this decision and took its
  id with it

## Context and Problem Statement

CoreLib entries carry a `ConfigScope` saying who may change them and whether a
change needs a restart. This mod bound every setting without one. That is not
the same as binding them as unrestricted: `ConfigEntryBase` replaces a missing
scope with `ConfigScope.Empty`, whose constructor defaults to
`ConfigAccessLevel.Server`.

So every setting any consumer ever declared here was server-scoped, chosen by
nobody. The cost was invisible in this menu — `Server` locks a player only in a
guest-mode world — and visible elsewhere: `ShouldSync` is true for `Server`, so
General Mod Config Menu had been synchronising every setting of every consumer,
HUD positions included, across the network.

A framework cannot leave that to a default. It declares settings on behalf of
mods it never sees, and a permission it invents for them is one their author
cannot discover from anything they wrote.

## Decision Drivers

- A consumer who states nothing must get the answer that withholds nothing and
  travels nowhere. Silence has to be the safe value, not the far-reaching one.
- Permission granularity does not match declaration granularity. A mod may want
  one level throughout, or one for a group of related settings, or one for a
  single row — and the same mod may want all three at once.
- Whatever is decided is readable by other mods through CoreLib, so it has to be
  written where they already look rather than kept beside it.
- The public consumer surface serves mods outside this family from 2.0.0. What
  it accepts cannot be withdrawn.

## Considered Options

1. **Per widget only.** Every declaration states its own level.
2. **Per section only.** One level for the whole mod, set where the section is
   opened.
3. **Three levels, innermost wins.** Section, group and widget may each state a
   level; a declaration that states none inherits outward.

## Decision Outcome

**Option 3**, with the fallback expressed as nullable parameters rather than
defaulted ones.

Options 1 and 2 are each other's failure case. Per widget alone makes the
common shape — a mod whose settings all share one level — repeat that level on
every line, where one forgotten row is a silent permission hole. Per section
alone cannot express the shape that motivated this: a mod with mostly local
settings and two that genuinely belong to the server.

The fallback is nullable because "unstated" and "stated as the default" must be
distinguishable. A group that says nothing means *back to the section's level*,
not *keep the previous group's* — otherwise a group's meaning would depend on
the group before it, and moving a block of declarations would change what they
permit. With a defaulted parameter that distinction cannot be made at all.

**The section's own default is `Client`, not CoreLib's `Server`.** This is the
decision the rest follows from: it is where "the consumer said nothing" is
resolved, once, in `ModSettings.Section`'s own parameters.

**Each entry gets a fresh `ConfigScope`.** `ConfigScope` is a class with public
mutable fields, and `ConfigScope.Empty` is one instance shared by every
scope-less entry in the process. Passing it, or reusing one instance across a
group, turns any later write into a change to strangers' settings — and the
`.RequiresRestart()` modifier is exactly such a write.

**The restart flag lives in `ConfigScope.requireReload`, not beside it.** It
used to be a field on this mod's own descriptor, which no other mod could see.
Writing CoreLib's field means General Mod Config Menu renders the reload marker
for a setting declared here without knowing this mod exists.

### Consequences

- **Settings stop syncing that used to.** A consumer that states nothing is now
  `Client`, so `ShouldSync` is false and GMCM no longer carries its values
  across the network. This is a behaviour change for players running both mods,
  and the changelog says so rather than treating it as a fix.
- **Whether a row is locked cannot be stored.** `Changeable()` reads the current
  player, and the same setting answers differently at the title screen and in a
  session. `SettingDef.Locked` is therefore computed per read; a snapshot taken
  when the section was built would freeze the title screen's answer for the
  whole run.
- **Two levels are observable without a world and two are not.** `ViewOnly` and
  `Client` are answered before any player is consulted — which `AccessLock`
  must do in that order, because `Changeable()` dereferences the player above
  its own switch. `Server` and `Admin` need a live session and a player holding
  no rights, which singleplayer cannot produce.
- **A duplicate key silently keeps the first declaration's level.** CoreLib
  returns the cached entry and drops the second call's scope. For a permission
  that is the fail-open direction, so the duplicate-key warning names it.
- **The cascade is now the shape a fourth thing would join.** Two pieces of
  metadata resolve through the same `??` chain; a third would be a third copy,
  and that is the point at which it should become one mechanism instead.

## More Information

The design document this replaces — including the options weighed against
CoreLib's own API and the acceptance criteria the implementation was measured
against — is recoverable from the commit that deleted it:

~~~
git show "$(git rev-list -1 HEAD -- docs/specs/2026-09-19-msm-18-consumer-declared-access-level-design.md)^:docs/specs/2026-09-19-msm-18-consumer-declared-access-level-design.md"
~~~

What the four access levels do to a player, and why a permission feature cannot
be verified in singleplayer, is in the handbook's `docs/ck/multiplayer-and-server.md`.
`docs/manual-tests.md` carries the fourteen checks, including the three rounds
against a dedicated server that separate `Server` from `Admin`.
