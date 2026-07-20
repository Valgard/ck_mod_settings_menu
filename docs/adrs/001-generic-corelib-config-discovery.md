# Generic CoreLib config discovery ("GMCM behaviour") in Mod Settings Menu

- Status: accepted
- Date: 2026-07-20

## Context and Problem Statement

Mod Settings Menu (MSM) renders only the settings that mods explicitly register
through its opt-in API (`ModSettings.Section(this).Toggle()…`). Mods that persist
config through CoreLib's `ConfigFile` but never integrate MSM — the installed
example is **PlacementPlus** — therefore have no in-game UI inside MSM. The
standalone mod **"General Mod Config Menu" (GMCM)** already shows that CoreLib
exposes enough metadata to build a generic config menu for *any* such mod. Should
MSM gain that capability, and how, without breaking its sandbox-clean design?

## Decision Drivers

- Reuse MSM's existing render pipeline rather than build a second one.
- Stay sandbox-clean (no `System.IO`, no reflection-emit; `skipSafetyChecks: 0`).
- Coexist with GMCM (no detection/replacement, no server-sync reimplementation).
- Controller-friendly UX (Core Keeper is controller-first).
- Do not require changes to third-party mods.

## Considered Options

1. **Generic auto-discovery** via a `ConfigFile.AllConfigFilesReadOnly` adapter.
2. **Opt-in only** (status quo) — do nothing.
3. **Ask third-party mods to integrate** MSM's API.

## Decision Outcome

Chosen option: **"Generic auto-discovery"**, because it is the only option that
serves mods the author cannot modify, and CoreLib was *built* for it
(`ConfigEntryBase.Scope` is commented "Used by GeneralConfigMenu";
`AllConfigFilesReadOnly` is a public registry).

A new `ForeignConfigDiscovery` adapter enumerates `AllConfigFilesReadOnly`,
excludes MSM-owned files by **instance identity** (`ConfigStore.IsOwn`, which
covers MSM's own dogfooded section and every API-integrated consumer), and emits
the *same* `ModSection`/`SettingDef` descriptors the fluent API produces —
pointing at the live foreign `ConfigEntryBase`. The existing `SectionBox` /
`SettingWidget` render path drives them unchanged (~90 % reuse), gaining only a
read-only `Info` kind, an unbounded float `Stepper`, and a serialized-value
`Choice` path.

Key sub-decisions:

- **Widget inference cascade:** `ViewOnly`/not-`Changeable()` → Info; `bool` →
  Toggle; enum → Choice; `int`+range → bounded Stepper; `float`+range → Slider;
  other `AcceptableValues` constraint → Info; bare `int`/`float` → unbounded
  Stepper; `string`/other → Info.
- **Sandbox-clean value bridge:** numeric via `BoxedValue` with type-exact casts
  (int-ranged → int Stepper, float-ranged → float Slider — never `(float)` on a
  boxed `int`); enum Choice round-trips via `Get`/`SetSerializedValue` (Toml
  serialises an enum as its name).
- **Presentation:** one screen, alphabetical; discovered boxes carry a localised
  "(detected)" marker. Live-persist (no GMCM-style Save button).
- **Editing surface (v1):** in-world editing; at the title screen (no player)
  Server/Admin rows are read-only. Free-text strings and `[Flags]` enums are
  **not** editable in v1 (rendered read-only / not clobbered).
- **Master toggle:** MSM dogfoods its own one-toggle section ("Show settings from
  other mods", default on) to suppress discovery when GMCM runs in parallel.

### Consequences

- Good: no per-mod integration needed; large reuse of the tested render path;
  stays sandbox-clean; PlacementPlus is fully editable in-world.
- Bad: widget kind is heuristic (a mod's intent isn't always expressible);
  foreign labels are raw section/key text (no localisation); title-screen editing
  is limited; `[Flags]` and free-text editing are deferred.

### Confirmation

No automated tests exist in this repo; confirmation is a manual in-game check with
PlacementPlus installed and not MSM-integrated: the marked "PlacementPlus
(detected)" box renders with `MaxBrushSize` as a 3–9 Stepper, `MinHoldTime` as a
float Stepper (stored value equals the displayed value), `ExcludeItems` read-only;
values persist to `PlacementPlus.cfg`; integrated siblings render once.

## Pros and Cons of the Options

### Generic auto-discovery

- Good: serves unmodifiable third-party mods; first-class CoreLib support; reuses
  the render pipeline.
- Bad: heuristic inference; raw labels; some types deferred to v2.

### Opt-in only (status quo)

- Good: zero work; every rendered setting is author-curated.
- Bad: never helps a mod that didn't integrate MSM — the actual problem.

### Ask third-party mods to integrate

- Good: curated, localised UI per mod.
- Bad: out of the author's control; will not happen for arbitrary installed mods.

## More Information

Known v2 candidates: title-screen menu input for Client rows, editable free-text
strings, `[Flags]` enum multi-select, float display beyond 3 decimals.

The full design exploration (alternatives, the GMCM screenshot analysis, the
CoreLib source evidence, and the decisions rejected along the way) is preserved in
the raw design spec. Retrieve it rebase-safely with:

```bash
git show "$(git rev-list -1 HEAD -- docs/specs/2026-07-20-generic-config-discovery-design.md)^:docs/specs/2026-07-20-generic-config-discovery-design.md"
```
