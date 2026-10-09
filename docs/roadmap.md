# ModSettingsMenu — roadmap

Planned, not-yet-built work. Shipped widgets (Toggle, Slider, Stepper,
Choice) live in `SettingKind` and the fluent `SectionBuilder` API; this file
tracks the **next** batch.

## Planned widgets

Three new widget kinds, ordered by cost (cheapest first). All three stay
entries in `ModSection.Settings` (the ordered per-section list) so a consumer
places them inline in the builder chain — the `SettingKind` drives which
prefab + behaviour the renderer picks. See `docs/superpowers/plans/` history
for how the existing widgets were built.

### 1. Button / Action-Row

A row that holds **no value** and fires a callback on activate.

- **API:** `.Button(string key, Action onClick)` — no `out` handle (nothing to
  read/write), no `ConfigEntry`.
- **Behaviour:** `SettingWidget.OnActivated` invokes `onClick` instead of
  `Adjust`; no skim (`OnSkimLeft/Right` no-op). `ValueString()` returns empty
  or a `»` chevron affordance.
- **Prefab:** **reuses** the existing option prefab (label left; value column
  empty). No Editor work.
- **Why first:** highest value, lowest cost. Unlocks a framework-built-in
  **"Reset to defaults"** (per-section and/or global) — every `ConfigEntryBase`
  exposes its default, so the reset itself is nearly free. Also serves consumer
  actions ("apply now" for bake-time mods, "clear checklist", "open ledger").
- **Open design question:** reset scope — per-section button vs. one global
  button vs. both. Decide during brainstorming before coding.

### 2. Info (read-only)

A row showing a **computed, non-editable** value in the normal option layout
(label left, value right).

- **API:** `.Info(string key, Func<string> value)` — no `out` handle.
- **Behaviour:** `Adjust` is a no-op; `ValueString()` calls the `Func<string>`
  each `Refresh()` so the display stays live.
- **Prefab:** **reuses** the existing `SettingWidget` prefab (its two-column
  label/value geometry is exactly right). No Editor work.
- **Use cases:** diagnostics / status — "Tracked items: 117", mod version, a
  slider's raw value as plain text.
- **Open design question:** focusable or skipped in navigation? A flag on the
  same widget, not a second kind. Default: focusable (consistent with sibling
  rows), revisit if it feels wrong.

### 3. Separator / Label

A **display-only** row rendered **full-width** (a heading or a divider),
**not** the two-column option layout — for structuring long sections.

- **API:** `.Label(string key)` (heading) / `.Separator()` (bare divider).
- **Behaviour:** never interactive, **skipped in navigation** (not focusable) —
  likely **not** a `RadicalMenuOption` at all, just a `PugText` / divider
  `SpriteRenderer` placed into the section box's layout.
- **Prefab:** needs a **new, full-width prefab** — the one genuinely expensive
  item here. Per the project rule (`feedback_corekeeper_prefab_edits_in_editor`
  memory), new/structural prefab objects **must be authored in the Unity
  Editor**: a `-batchmode` build reserializes and drops hand-authored objects /
  nulls refs. So this is real Editor work, not a code-only change.

## Why 2 and 3 are separate widgets

Logically both are "silent" (non-interactive) rows, but the split is driven by
**layout topology at the prefab layer**, not by intent:

- **Info** keeps the two-column geometry (label left / value right) → reuses the
  existing option prefab → code-only.
- **Separator/Label** spans the full width with no value column → needs its own
  prefab → Editor work.

The moment the geometry diverges, the prefab diverges, and that is precisely
what a distinct `SettingKind` value is for (it selects prefab + behaviour). They
only *share* membership in the ordered `ModSection.Settings` list (so a
separator can sit between option 3 and 4).

## Explicitly out of scope

- **Keybind capture** — attractive for action mods, but real input-capture
  breaks the skim-row model and Core Keeper already owns a rebinding system.
  Only if a consumer actually needs it.
- **Free-text string input** — controller-hostile; CK has scarce text-entry
  surfaces.
- **Colour picker** — model as a `Choice<T>` over preset swatches instead.
- **Multi-select / flags** — N separate toggles already cover it and read
  clearer.
- **Dual-range (min–max) slider** — too niche for the single-row raster.

## Persist with an explicit Save — another mod can switch auto-save off

A value changed in the menu is lost on the next launch whenever General Mod
Config Menu (GMCM) is installed. Reported by the user on 2026-10-09 with
Complete Tools' toggle: the menu showed "on", the file kept `false` through two
writes, and the log showed no error. Changing it on the title screen did not
save either; with GMCM disabled it saved at once.

**Settled: GMCM is the cause (measured by disabling it), and its source shows
how.** Its `ConfigSyncSystem.OnCreate`
(`Scripts/ConfigSync/ConfigSyncSystem.cs:42-46`) walks
`ConfigFile.AllConfigFilesReadOnly` and sets `SaveOnConfigSet = false` on every
file except CoreLib's own — ours included. That the title screen is affected as
well means the flag is already off before any world is entered; which code path
does it there is not traced. This framework relies on exactly that flag:
`SettingWidget.Adjust` writes `BoxedValue` and expects CoreLib to save, and
`ConfigStore` says so in its summary. Every consumer mod is affected, not only
one.

**The fix: save explicitly.** After a write in `SettingWidget.Adjust`, and
wherever else the framework writes a value (`SettingHandle<T>.Value` setters,
`SectionReset` on v2), call `ConfigFile.Save()` on the owning file instead of
trusting the flag, so no other mod can switch persistence off. The `v2`
branch relies on the flag the same way and needs the same change.

**To decide.** Whether to save on every adjust or once when the screen closes
(`ModSettingsScreen.Deactivate`) — every adjust is simpler and loses nothing on
a crash; closing saves fewer writes while a slider is skimmed. And whether to
restore `SaveOnConfigSet` on our files at all, which would fight GMCM's own sync
rather than sidestep it.

## Small fixes

- **English label casing: "Mod Settings" → "Mod settings".** The
  `ModSettingsMenu-UI/Title` term (`localization/localization.yaml:6`, the `en:`
  value) — the Options-menu entry label **and** the screen title — should be
  sentence case, matching the framework's own `On`/`Off` values, which are
  already lowercase (`"on"`/`"off"`). One-line loc edit; the German
  `de: "Mod-Einstellungen"` stays unchanged (German noun capitalization). **Scope:**
  only the loc term is user-facing — the "Mod Settings" mentions in code comments,
  `README.md` and `CHANGELOG.md` are the feature *name* in prose and need no change.
  Requested 2026-07-12.
