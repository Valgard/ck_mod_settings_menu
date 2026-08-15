# Section reset to defaults — design

- Date: 2026-08-16
- Status: designed, not implemented
- Roadmap item: `docs/roadmap.md` § "Reset to defaults"

Decompile citations below are from game 1.2.1.5
(`~/Projects/checkouts/CoreKeeperDecompile/`). Class and member names are stable
across patches; **line numbers are not** — re-verify before relying on one.

## 1 · Problem

Every setting MSM renders can be changed but never restored. A user who has
walked a slider away from its default has no way back short of editing the
owning mod's `config.cfg` by hand — which is exactly the thing this framework
exists to avoid, and which is impossible for the mods `ForeignConfigDiscovery`
mounts, since those have no settings UI of their own.

The restore itself is nearly free: `ConfigEntryBase.DefaultValue` is a public
`object` assigned in the constructor, so `entry.BoxedValue = entry.DefaultValue`
is the whole write. `ForeignConfigDiscovery` already reads `DefaultValue` for
its own heuristic, so the value is proven reachable for foreign entries too.
What needs designing is everything around that one line: what a single
activation is allowed to touch, how the user asks for it, and what the screen
does afterwards.

## 2 · Decisions

**Scope is one section.** A reset restores the settings of exactly one mod. The
boundary already exists in the data model — one `ModSection` is one `ConfigFile`
is one owner — so a reset is one file, one owner, one confirmable sentence. A
single global row was rejected: MSM renders many mods side by side, so its blast
radius grows with the user's mod list, and its confirmation can only warn
vaguely. HealthBars' shipped `MenuOptionResetToDefaults` uses one global row,
but that is not a counter-example: HealthBars only ever had its own options, so
global and per-section were the same thing there.

**Discovered sections are included.** A reset writes back exactly the value the
owning mod itself declared at `Bind()` — it never invents one. That makes it
categorically safer than the list-editing write path, whose comma-rejoin can
destroy information in a misclassified string (`docs/roadmap.md` § Small fixes);
that risk does not transfer here. Excluding discovered mods would remove the
feature from the place it is needed most, and would contradict ADR-001's premise
that a detected mod's settings are first-class.

**`ReadOnly` entries are skipped.** A view-only or server-locked entry is not
"resettable but locked" — it is not writable at all.

**The trigger is CK's own footer hint bar, not a row.** `HelpButtonTypes`
(`Pug.Other:338829`) is a closed 7-value enum a mod cannot extend — but it
already contains `RESET_DEFAULTS`, and that slot is fully built and unused:

| | value |
|---|---|
| enum member | `RESET_DEFAULTS` (`:338836`), mapped to the `resetDefaults` `HelpButton` (`:338892`) |
| prefab | `Resources/Assets/Resources/Global Objects (Main Manager).prefab` — `root`, `inputDependentSprite` and `description` all wired |
| label | a `PugText` with `textString: Menu/Reset`, `localize: 1` — CK ships the localized string |
| keyboard glyph | `optionalString: R` |
| controller glyph | the same sprite as `openProfile` (the Y-position face button) |
| vanilla users | **none** — no code path returns this value |

So the hint costs no prefab work, no own localization, and no self-rolled hint
object. ADR-002 §6 concluded a custom prompt needs one; that conclusion held for
a verb the enum does not know, and this one it does.

The consequence runs the other way: both glyphs are **baked**, not derived from
a binding. Whatever input MSM polls must match what the hint claims — keyboard
`R` and the Y-position face button — or the prompt lies.

**The confirmation names the mod; the hint bar does not.** A footer prompt
cannot express which box it applies to, and the two alternatives are both worse:
highlighting the selected row's box means inventing a visual language CK has no
equivalent for, and rewriting the hint text per selection means fighting the
shared singleton's per-frame refresh — the fragility the project's own notes warn
against. The dialog has to exist anyway, so it carries the precision:
`StartNewDisplaySequence`'s second parameter is `string[] formatFields`
(`:342074`), so the mod's display name is a format field, not a concatenation.
Substitution is plain `string.Format` (`PugText.ProcessText`, `:351765`), so the
term uses `{0}` — and the call **must** pass `localizePlaceholders: false`,
because that flag otherwise looks each format field up as a loc term and a mod's
display name is a literal, which would render as `<missing>`. CK passes it false
in every popup carrying a literal (e.g. `:331920`, `:338370`).

**The hint appears only when the section has something to reset** — i.e. at
least one entry with `!ReadOnly && Entry != null`. `GetHelpButtonsToShow()` is
evaluated per selection anyway, so this is one check per selection change. It
deliberately does **not** also hide when every value already equals its default:
that would compare N values per selection change and make the prompt flicker as
the user walks the list.

## 3 · Architecture

### `SectionReset` (new, `ModSettingsMenu.Settings`)

The function, with no UI dependency beyond one flag:

- `static bool CanReset(ModSection)` — any entry with `!ReadOnly && Entry != null`.
- `static void Apply(ModSection)` — per in-scope entry: capture `BoxedValue`,
  write `Entry.BoxedValue = Entry.DefaultValue`, and set
  `ModSettingsScreen.RestartPending` when a `RequiresRestart` entry actually
  changed. That mirrors `SettingWidget.Adjust`'s existing
  `!object.Equals(before, after)` guard rather than inventing a second rule.

Nothing else is needed for persistence or change notification. CoreLib's
`ConfigEntry<T>.Value` setter clamps to any `AcceptableValue*`, returns early on
`Equals(_typedValue, value)`, and otherwise raises `SettingChanged` — so
`SettingHandle<T>.OnChanged` fires for every genuinely changed value and for no
unchanged one, with no code of ours.

### `ISectionRow` (new, `ModSettingsMenu.UI`)

```csharp
internal interface ISectionRow
{
    ModSection Section { get; }
    void Refresh();
}
```

Implemented by `SettingWidget` and `ListWidget`, whose `Bind` gains the owning
section. Two directions are needed and both fall out of this: the hint bar asks
the selected option for its section, and the post-reset re-render asks
`menuOptions` for every row of that section. `SettingWidget.Refresh()` exists today as a
private method and is widened to satisfy the interface; `ListWidget` gets an
equivalent that rebuilds its preview.

The alternatives were a screen-side `Dictionary` (a fourth parallel structure
`Populate()` must keep in sync, and cheap in only one direction) and a
back-reference on `SettingDef` (an invariant that must be set in both
`SectionBuilder` and `ForeignConfigDiscovery`, and that a future third producer
of defs would break silently).

### `ModSettingsScreen`

- `override bool UseCustomHelpButtons => true` and an override of
  `GetHelpButtonsToShow()` that returns the base prompts plus `RESET_DEFAULTS`
  when `SectionReset.CanReset(selected row's section)`. CK renders glyph and
  label itself.
- Poll the reset input while this screen is the top menu; on press, open the
  confirmation.
- The confirmation reuses the call `ShowRestartPrompt` already makes —
  `Manager.menu.centerPopUpText.StartNewDisplaySequence`, options
  `{ "cancelDialogue", "yes" }`, `pauseGame: false` — with a new term and the
  section's `DisplayName` as a format field. Unlike the restart prompt it needs
  **no** deferral: that one fires from `Deactivate`, inside the menu-stack pop it
  would re-enter; this fires from an ordinary input frame.
- On confirm: `SectionReset.Apply(section)`, then `Refresh()` on every
  `ISectionRow` of that section. Not `Populate()` — a full rebuild would discard
  the selection.

No input guard of our own. HealthBars follows its reset with
`TemporarilyPreventInteraction()`, but that static is consumed only in its
`LateUpdate` (`isSelected = IsSelected() && IsInteractionAllowed`) and exists to
stop a still-held mouse button from dragging a freshly reset colour slider. MSM
has no drag path. CK also already ships `accidentalInputBlockDuration = 1f` as a
`StartNewDisplaySequence` default.

### Localization

One new term, `ModSettingsMenu-UI/ResetConfirm` (EN + DE), carrying a
placeholder for the mod name. The hint label (`Menu/Reset`) and the dialog
buttons (`cancelDialogue`, `yes`) are CK's own, already localized in every
language.

## 4 · Out of scope

- **A global reset and a per-row reset.** Both were considered and rejected in §2.
- **A reset row in the box**, and with it any new row prefab or widget class.
- **The drill-in (`ListDetailScreen`).** A reset there would restore one
  setting, which is the per-row scope this design excludes. Left open
  deliberately.
- **The `ListKindStore` marker**, which is untouched by a reset: it records that
  a foreign string has the *shape* of a list, not what its value is.

## 5 · Preconditions to verify before implementation

1. **Which action sits on the Y-position face button.**
   `MenuSecondaryActivate` (Rewired action 221, category `Menu`) is free in a
   settings menu — CK polls it only in the mod.io browser (`:269987`) — but its
   default binding must be confirmed to be the button the baked glyph shows. Its
   category is `_tag: system`, so it is not rebindable and not visible in CK's
   Controls screen; the keyboard side needs a separate path regardless.
2. **The keyboard path for `R`.** Action 221 has no keyboard default. The clean
   route is a CoreLib rebindable action in a mod-owned `player` category
   (`ControlMappingModule.AddKeyboardBind`), but a rebindable key contradicts a
   baked `R` glyph — so either the binding is fixed to `R`, or the hint must be
   replaced by an own hint object. Decide once (1) is known.
3. **That MSM's screen is CK's `topMenu`** when open, since
   `GetHelpButtonsToShow()` is only consulted for the top menu (`:269473`).
4. **Where to poll the press** without colliding with CK's own menu input
   handling.

Only (1) and (2) can still change the shape of this design; (3) and (4) change
the route, not the decisions.

## 6 · Verification

MSM has no automated tests — verification is an in-game pass with the reference
consumer installed:

1. A registered consumer's section: change several values, reset, confirm every
   row shows its declared default and the config file on disk agrees.
2. A discovered (foreign) section: same, against that mod's own `config.cfg`.
3. Cancel: nothing changes, no file write.
4. A section carrying a `RequiresRestart` setting: reset it away from its
   default, then leave the screen — CK's restart prompt must appear; reset it
   while already at its default, leave, and the prompt must **not** appear.
5. A section whose entries are all `ReadOnly`: the hint does not appear.
6. Controller and keyboard both trigger it, and the glyph shown matches the
   input that actually works.
7. The selection survives the reset (no rebuild), and scrolling still follows it.
