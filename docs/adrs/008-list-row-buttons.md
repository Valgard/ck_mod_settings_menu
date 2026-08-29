# Row buttons join CK's menu-option model instead of routing around it

- Status: accepted
- Date: 2026-08-30
- Builds on [ADR-002](002-list-widget-drill-in.md), [ADR-003](003-list-widget-editing.md),
  [ADR-005](005-drill-in-row-model.md), [ADR-007](007-horizontal-text-scrolling.md)

## Context and Problem Statement

The list drill-in could edit and add entries but not delete or reorder them — ADR-005
deliberately left removal for later, and reordering was never available at all. Both need a
control that lives *inside* a row, beside its text field, rather than being a row of its own.

CK's menu framework is built around one selectable option per row; giving a row several
independently focusable controls is not something either of its navigation modes does for free —
it has to be built. CK offers two different ways to build it: keep the row as the row's sole
`menuOptions` entry and hand-forward whatever a button needs, or make each button a real,
independently addressable option and reach it through CK's own spatial-neighbour navigation mode
instead of the index arithmetic every other screen in this framework uses. Which of the two to
take, and what each actually costs once built, is what this ADR resolves — and one of them was
built in full before the other replaced it.

## Decision Drivers

- Reachable by keyboard, controller and mouse alike — deletion in particular is the one operation
  a mouse-only user would otherwise have no way to reach at all.
- The drill-in screen is a **singleton**, reused for every list any consumer's config exposes;
  nothing may leak from one open session into the next, or from one row into another within the
  same session.
- Deleting a filled entry writes into a **third-party** mod's config file; an empty one never
  reached it and costs nothing.
- The existing row model (ADR-005) — full teardown-and-recreate on every change — was not up for
  renegotiation; a button design that needs an incremental update instead is the wrong design.
- A repeated action must cost exactly one input per step: pressing an arrow four times must move
  one entry four rows, and deleting several entries in a row must not require walking back to ✕
  each time. The spec's own requirement, and the one property the rejected option below never
  fully delivered before it was replaced.

## Considered Options

1. **A row-owned control that routes around CK's own selection.** `ListRowButton :
   RadicalMenuOption` overrides `isMenuOption => false`; only the row is ever a real
   `menuOptions` entry, and it forwards navigation, activation and focus state to
   whichever button the player has conceptually reached.
2. **A row-owned control that IS a real menu option.** The row stops intercepting anything; each
   button registers in `menuOptions` exactly like the row's own field, and CK's own
   `useUIElementsForNavigation` mode — a spatial neighbour graph, not index arithmetic — reaches it
   directly through a hand-wired `topUIElements`/`bottomUIElements` chain rebuilt after every
   change.

## Decision Outcome

**Option 2**, chosen after Option 1 was built in full and taken apart fix by fix — see
§ "Pros and Cons of the Options" for why. `useUIElementsForNavigation` was already set on this
screen; `handleNavigationInternally` on the row's template goes from `1` to `0`, since nothing is
intercepted any more.

### The focused column is neighbour wiring, not screen state

Which of a row's controls holds keyboard/controller focus is no longer consulted
anywhere — it is simply which object the currently-selected one names as its neighbour.
`ChainRowsForUIElementNavigation`, run at the end of every rebuild, gives each row's
four controls (field, ↑, ↓, ✕) their own `topUIElements`/`bottomUIElements` naming the
SAME control one row up or down: the ✕ of row *N* points at the ✕ of row *N*-1 and
*N*+1, never at a neighbour's field. There is no field anywhere holding "which column",
so there is nothing to leak into an unrelated list opened afterward and nothing that can
go stale against a row already destroyed and recreated.

Only the wiring *between* two different row instances needs code — row *N* and row *N*+1 cannot
both be named by reference until the rebuild that creates them has run. The wiring *within* one
row — field ↔ ↑ ↔ ↓ ↔ ✕ — is authored once, in the prefab template, and needs none: sibling
`fileID`s survive `Instantiate`, so every cloned row already carries it.

### The neighbour lists are multi-entry, and the add button relies on it

`topUIElements`/`bottomUIElements` are `List<UIelement>`, not single references, and CK resolves
one by walking the list and keeping whichever surviving candidate sits spatially closest —
`GetClosestUIElementInList`, the same helper every direction goes through. The trailing add button
is the one control with no column of its own — a single object below four columns' worth of row
controls — so its own neighbour lists each carry all four of the adjacent row's controls rather
than naming just one. Wrapping onto it from any column always finds a match; wrapping back off it
lands on whichever of the four is spatially closest to where the player wrapped in from, not
necessarily the column they left. CK's own `SelectWorldMenu` wires its per-slot buttons the same
way, for the same reason (`Pug.Other:344739`): a neighbouring slot may not have every button the
current one does, so more than one candidate is offered and distance decides.

### Wrapping is wired explicitly; the horizontal chain stays open at both ends

Nothing in CK's spatial-neighbour dispatch retries past the end of a list or falls back to index
0/Count-1 the way the index-based path's modulo arithmetic does for free — a wrap exists only
where a neighbour list is deliberately made cyclic. This screen's vertical chain wraps once,
through the add button (every control in the last row points down at it; its own downward list
points back at the first row's four controls) rather than row-to-row, which would need every row
to know about every other row instead of just its immediate neighbour. Horizontally, the chain
between a row's field and its three buttons stays open at both ends — matching CK's own convention
for a short in-row button group, the same shape `SelectWorldMenu`'s slots use, rather than wrapping
sideways within a row. That wiring lives entirely in the prefab template and needed no change for
this decision; only the vertical, between-row chain is generated in code.

### A disabled edge arrow is reachable but not activatable — two different tests

CK's own "disabled and unreachable" state, `GRAYED_OUT`, is a dead end rather than a skip
specifically on this navigation mode: a neighbour is found before it is filtered by
`IsSelectionEnabled()`, and a real but disabled neighbour with nothing to fall back to simply ends
the navigation there (`docs/ck/ui-framework.md` § "`GRAYED_OUT` is not 'read-only'" names the same
asymmetry against the index path's own genuine skip). Landing the first row's ↑ or the last row's
↓ that way would strand every control below it. `ListRowButton.GetActiveStateInCurrentScene()`
therefore always reports `ACTIVE` for a live instance, disabled or not — but a disabled arrow must
still refuse to *act*, or Enter/click on it would move an entry that has nowhere to go. CK keeps
those two questions genuinely separate: `CanBeActivated()` gates the effect (and, since the same
"can activate" check is also what CK's own activation-receipt sound is keyed on —
`docs/ck/ui-framework.md` § "Two menu sounds share one `SfxID`" — refusing it here also silences a
press that would otherwise sound for nothing), while `GetActiveStateInCurrentScene()` alone gates
reachability.

### What wiring cannot express: a one-shot landing instruction, not the old column state

Everything above answers "which control does a *further* navigation step reach" — a purely spatial
question, answered once by the wiring and never touched again. Reordering and deleting are not
that question: the entry itself moves to a different row, and no neighbour list can say "follow
this entry wherever it goes," because that is a consequence of an action just taken, not a standing
spatial relationship. `RowSelection` carries that instruction — and the fact that it names a row
*and*, optionally, a button role is **not** a return of the mechanism the rejected option needed.
The two look alike and are not: that mechanism was continuously-consulted state, read on every
selection change and written from every button's and row's own `OnSelected`, which is exactly what
made it able to go stale, leak into an unrelated list, or fight the mouse pointer (see below).

`RowSelection`'s target, by contrast, is produced exactly once, by the specific action
that causes a rebuild (a button passes its own role into the move call; a delete names
the ✕ of whichever row moved up), consumed exactly once by that same rebuild, and reset
to "none" immediately afterward — nothing else ever reads it, and no later navigation
step consults it. An ordinary text commit and a fresh "add entry" both leave it naming
no button at all, landing on the row's own field, which is the right answer for an
action that did not originate from a button.

It is gated on the same mouse-vs-keyboard test as the rest of the post-rebuild reselect,
for the same reason the rejected mechanism needed that gate: a mouse-driven reorder must
let the pointer keep deciding, not drag the selection back to wherever the arrow used to
be. A named button the rebuild no longer has — the row came back read-only, or its
buttons are switched off — falls back to the row's field rather than leaving nothing
selected.

### Consequences

- **`autoPositioning` still has to stay off.** `RadicalMenu.Awake` collects every
  `RadicalMenuOption` in the hierarchy regardless of `menuOptions` membership
  (`docs/ck/ui-framework.md` § "A `RadicalMenu` positions its own options"), so the row's buttons
  are still subject to CK's own auto-layout pass even now that they are genuine options — becoming
  a real `menuOptions` entry changes nothing about that.
- **The click collider is still hand-built.** A menu option's collider is ordinarily derived from
  its rendered text; a button that is only a glyph has neither `labelText` nor `valueText`, so
  `ListRowButton` still builds and sizes its own from the frame sprite regardless of which
  navigation mode reaches it.
- **A future control placed beside a row inherits this same shape**: register it in `menuOptions`,
  give it its own column in `ChainRowsForUIElementNavigation`, and build it a real collider if it
  is not text — none of which needs the hand-forwarding the rejected option required.
- **Any future per-navigation state that genuinely needs to survive a rebuild belongs on
  `RowSelection`, produced once by the action and consumed once by that rebuild — never as a
  continuously-read field**, for the reason the previous subsection spells out.

### Confirmation

The full walkthrough in `docs/manual-tests.md` passed end to end. Four defects surfaced along the
way, and are why this plan needed a walkthrough at all rather than a single smoke test:

- Reordering inside a restart-flagged list and then opening the delete confirmation showed the
  pending-restart prompt's text on the delete dialogue's own buttons — the prompt's flush check
  asked whether one of this framework's screens was the *top* of CK's menu stack, true for the
  drill-in itself but false the moment a dialogue it pushed sat above it. The question actually
  meant was membership, not position at the top.
- The delete dialogue's hold-to-confirm bar showed as already full at some list lengths and
  correctly empty at others, because two sprite masks over one renderer combine as OR, not AND, and
  each row's own field mask reached far enough up the sorting range to cover CK's own popup —
  revealing its bar regardless of how much of the hold had actually completed.
- A greyed-out edge arrow played CK's activation-receipt sound for a press that moved nothing,
  because that sound is keyed on whether the current option *can* activate, not on what the click
  hit.
- Walking an entry past the top of the visible list did not scroll, because CK's own
  scroll-into-view helper only acts while a menu up/down key is physically held, and a selection
  restored by a rebuild is never that — it lands the frame after Enter, with nothing held down.

## Pros and Cons of the Options

### Real menu options on CK's UIElement navigation path (chosen)

- Good, because CK's own selection machinery — the marker, the hover/activation sounds, the click
  path, the footer hint — applies to a button exactly as it does to any other option, once it is a
  real one.
- Good, because there is nothing to forward: a button's `OnSelected`/`OnActivated` fire directly
  from CK's own dispatch, the same call every other menu option in the game receives.
- Bad, because the neighbour graph has to be rebuilt by hand after every change that adds, removes
  or reorders a row — CK's own auto-positioning and index arithmetic have nothing to say about a
  per-row column.

### A control that opts out of CK's own routing (`isMenuOption => false`)

Keeping only the row itself in `menuOptions` looked like the narrower change: CK's index-based
navigation moves through that list and nothing else, so never registering the buttons in it seemed
to guarantee vertical movement could not wander into them by accident. Everything that followed was
the cost of that guarantee — routing a genuinely interactive control outside the one mechanism CK
uses to decide what is selected does not remove the row's obligations, it moves every one of them
onto hand-written code standing in for what CK already does for a real option:

- **`OnSelected` never fired on a button through CK's own hover/select path** — a hand-rolled
  navigation override had to call it directly, and the marker had to be reproduced by hand rather
  than inherited.
- **The click collider** — ordinarily sized from an option's own rendered text — had to be built
  from the frame sprite regardless, but also needed its own mouse-click path, since a control
  outside `menuOptions` gets none of CK's click-to-select handling.
- **Activation had to be forwarded.** CK only ever calls `OnActivated` on
  `menuOptions[selectedIndex]`, so the row had to notice which button conceptually held
  focus and call into it by hand — and, for one round, got this wrong in the other
  direction, opening the row's own text field on Enter/Space while a button had focus.
- **The remembered column** — which control last had focus — needed a field of its own, because
  nothing about the row itself survives from one rebuild to the next, and had to be written on
  entry, cleared on exit, and reset whenever a different list opened: three separate leaks, closed
  one at a time.
- **Directional input had to be intercepted wholesale.** CK's hand-off flag has no partial form: a
  row answering only the horizontal case and returning the inherited "not handled" for every other
  direction did not hand vertical movement back to the menu, it swallowed it dead — caught only
  once someone tried to leave a focused button by pressing up or down.
- **The activation sound took two rounds to place correctly** — first assumed silent everywhere,
  then attributed to the wrong one of CK's two menu-select call sites, before a decompile read
  settled which pitch belongs to which gesture.
- **Mouse-versus-keyboard needed its own test, and even a careful one did not fully hold.** A
  hand-rolled reselection had to be told not to fight the pointer — but *why* one particular sound
  sometimes played and sometimes did not on the mouse-click path specifically was never
  conclusively pinned down before the decision to replace the whole mechanism was made, rather than
  chasing it further.

Each of these looked, in the moment, like a local oversight in one specific method. None of them
were: every one is the same conflict, a control that is genuinely interactive, genuinely part of
the menu's layout, and genuinely reachable by every input device, sitting outside the one list
CK's own selection model actually understands. Re-implementing CK's own machinery piece by piece,
and landing one new gap per round for several rounds running, is the signal an architecture is
being fought rather than a bug being chased — recognising that is what closed this option, not any
single fix among them.

## More Information

- **Builds on** [ADR-002](002-list-widget-drill-in.md) (the drill-in itself),
  [ADR-003](003-list-widget-editing.md) (the editable row), [ADR-005](005-drill-in-row-model.md)
  (the row model these buttons operate inside), and [ADR-007](007-horizontal-text-scrolling.md)
  (the field width these buttons now share the row with).
- The general CK mechanisms behind the decisions above — the two navigation modes and what each
  does with a disabled neighbour, the two menu-select sounds and what disarms the second,
  multi-entry neighbour lists and how the closest candidate is chosen, `RadicalMenu`'s own
  auto-positioning pass, and `SystemIsUsingMouse` as a sticky mode rather than a per-event answer —
  live in `docs/ck/ui-framework.md`, not here; this ADR records only what this feature decided
  given those mechanisms.
- Still-open items are tracked in `docs/roadmap.md`.
- The raw design document this distils predates the reversal recorded here — read it as the
  requirements the feature had to meet, not as the architecture that shipped:

~~~
git show "$(git rev-list -1 HEAD -- docs/specs/2026-08-28-list-row-buttons-design.md)^:docs/specs/2026-08-28-list-row-buttons-design.md"
~~~
