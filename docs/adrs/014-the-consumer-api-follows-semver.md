# The consumer API follows SemVer

- Status: accepted
- Date: 2026-10-16

## Context and Problem Statement

A mod that declares its settings through this framework takes a hard
dependency on it, and that dependency is the one part of the consumer's mod its
author does not control. When the framework changes in a way the consumer did
not expect, the consumer breaks — and its players see a broken mod, not a
changed dependency. The author of another settings framework for Core Keeper
named exactly this as a reason for building their own: their mods had broken
more than once because a dependency's author needed to change something.

Nothing in the platform softens this. A loader dependency is `{modName,
required}` and carries no version, so a consumer cannot ask for "1.x" and be
kept there; mod.io and the Steam Workshop deliver the newest release to every
player. Whatever this framework ships is what every consumer runs against the
next time the game starts.

So far nothing was said about what a release may change. An author could not
tell from a version number whether updating was safe, and had no warning before
something they used went away.

## Decision Drivers

- A consumer's author must be able to tell from the version number alone
  whether a release can break their mod.
- Removing or reshaping something a consumer uses must be announced before it
  happens, not discovered by the players.
- The framework is still growing. A promise that freezes its design would
  either be broken or keep it from improving.
- Consumers ship source, not binaries: the loader compiles a mod's scripts
  against whatever version of this framework is installed, on every launch.
  Compatibility therefore means *source* compatibility.

## Considered Options

- Promise that every release stays backwards compatible.
- Keep a separate API version beside the mod's own version.
- Make no promise, and let the changelog speak for each release.
- Version the consumer API by Semantic Versioning, using the mod's own version
  number.

## Decision Outcome

**The mod's version is the consumer API's version, and it follows Semantic
Versioning 2.0.0.**

- **Major** — a change that can make a consumer's source stop compiling, or
  make it behave differently without being changed: a removed or renamed
  member, a changed parameter order or type, a changed default, a reinterpreted
  value.
- **Minor** — something added that existing consumers do not have to notice: a
  new method, a new widget kind, a new optional parameter, a new enum member.
- **Patch** — a fix that changes nothing a consumer declares or relies on.

The rule runs one way only: a breaking change forces a major, but a major does
not imply one. A release that reshapes the mod for its players may take a new
major with the consumer API untouched — 2.0.0 is such a release — and its
changelog then says that nothing changed for consumers.

**What the promise covers** is the surface a consumer is meant to write against:
the `ModSettingsMenu.Settings` types `ModSettings`, `SectionBuilder` and
`SettingHandle<T>`, the enums `SettingKind`, `SliderDisplay`, `OptionSort` and
`ListEditing`, and the localisation term scheme a consumer's own terms are
looked up under.

`ModSection` and `SettingDef` are covered for **reading only**. They are what
`ModSettings.Sections` hands out, and a mod rendering its own surface over the
registered settings is told to read them — `SettingDef.IsEditable` exists for
exactly that caller. Writing to them is not covered: their fields are public
because the screen and the discovery fill them in, and a member may become
read-only in any release. That split costs little, because a field turning into
a read-only property breaks only an assignment, and every reader still compiles
— which is precisely what happened to `SettingDef.RequiresRestart` in 2.0.0.

Not covered at all: everything in `ModSettingsMenu.UI` and anything `internal`.
The README names the same surface, so an author does not have to read this
record to find it.

**Removal is announced first.** A member that is going away is marked
`[Obsolete]` in a minor release, with the replacement named in its message, and
is removed no earlier than the next major. A consumer compiling against it then
sees a warning while there is still time to act. A major release states in its
changelog what changed for consumers and what to do about it.

Backwards compatibility forever was rejected because it is the promise most
likely to be broken: the framework is still finding its shape, and the choice
would come down to breaking the promise or keeping a mistake. A separate API
version was rejected because two numbers that mean nearly the same thing drift
apart, and the mod's version is the one an author already sees on mod.io and
the Workshop. No promise at all is the situation that produced the problem.

### Consequences

Good: an author reads a minor or patch release as safe to take without looking,
and a major one as the release whose changelog to read. Breakage stops being a
surprise, and a removal arrives with a warning period instead of a broken mod.

Bad: the promise is only as good as the discipline behind it. Nothing checks
mechanically whether a release's number matches its changes; an added optional
parameter and a changed default look alike in a diff, and only one of them is
minor. And because the platform cannot pin a version, a major release still
reaches every player at once — the version number warns the author, it does not
protect the player.

Not decided here: a runtime version constant a consumer could query to adapt
when an older framework is installed. Without one, the version is information
for authors, not something a consumer can act on.

## More Information

The surface and the rules, written for mod authors, are in the README section [Versioning](../../README.md#versioning).
The specification itself is at <https://semver.org/spec/v2.0.0.html>.
