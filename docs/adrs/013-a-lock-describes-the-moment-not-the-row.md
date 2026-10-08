# A lock describes the moment, not the row

- Status: accepted
- Date: 2026-10-08
- Builds on [ADR-012](012-permission-and-restart-cascade-from-the-nearest-declaration.md), which gave every setting a real `ConfigScope` and so
  created the locks this decision has to show
- Implements roadmap points MSM-10 and MSM-11, which shipped together and took
  their ids with them

## Context and Problem Statement

After ADR-012 a setting carries a scope saying who may change it, and the menu
began honouring it: an entry the player may not change is not written. But the
screen still drew it exactly like any other, so the player pressed confirm,
nothing moved, and nothing said why. A setting that silently refuses is worse
than one that is absent, because the player cannot tell a permission from a bug.

Two kinds of "cannot change this" exist here and they are not the same thing. A
`ViewOnly` entry is never editable by anyone — that is a property of the setting.
A `Server`- or `Admin`-scoped entry is editable in principle and withheld *at the
moment*, because there is no world yet, or because this session does not grant
the right. Treating both alike would either make permanent read-only rows look
temporarily broken, or make a genuine permission look like a design choice.

## Decision Drivers

- The player must be able to tell "not yours to change here" from "nothing to
  change" and from "broken", without reading a log.
- Core Keeper already has a visual language for this. Inventing a second one
  would make this mod's screens read differently from the game's own.
- The reason is a property of the *session*, so it can change while the screen
  is open. Whatever is drawn has to be able to change back.
- A row that hides content behind an interaction cannot simply be made
  unreachable, or the content goes with it.

## Considered Options

- Model the lock as a `SettingKind`, reusing the `Info` kind for anything
  uneditable.
- Give locked rows a colour and a note of this mod's own design.
- Use Core Keeper's shipped `GRAYED_OUT` option state, and keep the lock as a
  separate question from the widget's shape.

## Decision Outcome

**A lock is answered separately from the widget's shape, and only a contextual
lock is shown as withheld.**

`SettingKind` stays a statement about *what the row is* — a toggle, a choice, a
list. Whether it may be changed right now is a second question, answered by
`AccessLock.Reason`, which returns `None`, `NoWorld`, `ConditionUnmet` or
`ViewOnly`. Folding the two together was the available shortcut and would have
been wrong in both directions: a `ViewOnly` setting would have lost its widget
type, and a temporarily withheld one would have been indistinguishable from a
setting that is merely informational.

Only `NoWorld` and `ConditionUnmet` render as withheld. `ViewOnly` keeps the
appearance it already had, because nothing is being *withheld* from a row that
is never editable — and because greying it out would take it out of navigation,
leaving the player unable even to read it comfortably.

Withheld rows report Core Keeper's own `GRAYED_OUT` state, which costs nothing
to adopt and brings the game's existing behaviour with it: the row leaves
directional navigation, its click collider stays off, and the footer drops its
Select prompt. The one exception is the list row, which keeps `GRAYED_OUT` away
from itself and is painted by this mod instead — its contents live behind a
drill-in, and a row nobody can reach is a row whose contents are gone. Every
other row keeps label and value on screen and loses nothing by being
unreachable.

The reason is stated once per section rather than once per row, in one of two
sentences: that the setting needs a world, or that this session does not grant
the right. The two cannot collide, because having no player is a global
condition rather than a per-row one. `Server` and `Admin` are deliberately not
told apart — their conditions differ, but the player's options do not.

Because the reason belongs to the session, the screen follows it while open. The
game raises no event for either admin level or guest mode, so this is a poll;
CoreLib does raise one for a value, so that half is an event. Everything the
lock draws is therefore reversible, which is why the paint records what it
overwrote instead of assuming a default to return to.

### Consequences

Good: the screen reads like the game's own, and a player who meets a withheld
setting is told which of the two reasons applies. A consumer mod gets this by
declaring a scope and nothing else — no UI code, no opt-in. And because the lock
never touches `Kind`, a widget type added later inherits the behaviour without
knowing about permissions.

Bad: this mod now paints over Core Keeper's text effects, which means it has to
restore exactly what it overwrote when a lock lifts — a bookkeeping obligation
that did not exist before, and the part of this change most likely to break
when a future widget renders differently. The poll is per frame while the screen
is open, which is cheap but not free. And the list row's exception means there
are two painters' worth of reasoning to keep in step: the game's for every other
row, this mod's for that one.

Accepted and not fixed: a lock that lifts *while the screen is open* can only be
produced by a second player with admin rights, so that direction ships read and
reviewed but not observed. Driving it without a second account is what roadmap
point CK-01 in the parent repository describes.

## More Information

The design this distils, including the per-criterion reasoning and the options
rejected along the way:

~~~
git show "$(git rev-list -1 HEAD -- docs/specs/2026-10-03-msm-10-locked-settings-design.md)^:docs/specs/2026-10-03-msm-10-locked-settings-design.md"
~~~

How the game decides admin level and guest mode, and why neither raises an
event, is in the parent repository's handbook under
`docs/ck/multiplayer-and-server.md`, section "Who is allowed to change things".
The walk that verifies this decision in game, including which steps a single
account cannot reach, is [`docs/manual-tests.md`](../manual-tests.md).
