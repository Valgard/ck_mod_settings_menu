# Design — Comma-separated list widget for discovered foreign configs

- **Date:** 2026-07-20
- **Status:** Approved (design); implementation pending
- **Builds on:** ADR-001 (generic CoreLib config discovery). This adds a read-only
  *list view* for discovered foreign `string` entries.

## 1 · Goal

When MSM's generic discovery surfaces a foreign `string` config that holds a
**comma-separated list** (the installed example is PlacementPlus `ExcludeItems`),
render it as a dedicated **list view** — every item on its own line — instead of
the current single-line `Info` row truncated with `...`. So the whole list is
readable at a glance. **Generic:** works for *any* comma-separated string, not
just item-ID lists. Read-only in this version; editing is deferred (see §7).

## 2 · Decisions (locked)

1. **Generic, not ID-specific.** Detection is purely value-based; it does not
   assume the tokens are `ObjectID`s. (ObjectID-aware enrichment — icons, a
   picker — is a possible later layer, not part of this version.)
2. **Heuristic default + per-entry toggle override.** A value heuristic sets the
   *initial* list-vs-plain state; the user flips a per-entry override that wins
   over the heuristic. This moves the unreliable "is it a list?" judgement from a
   guess to the human, so the heuristic only needs to guess a good default.
3. **Own persistence for overrides** — NOT the settings `.cfg`, and NOT a CoreLib
   `ConfigFile`. MSM writes the overrides itself via `API.ConfigFilesystem`
   (raw `byte[]`, the sandbox-clean path CoreLib uses internally). Bonus: a raw
   file is not a `ConfigFile`, so it never appears in `AllConfigFilesReadOnly` /
   discovery — no self-listing to exclude.
4. **Dedicated prefab (authored in the Unity Editor).** The list view gets its
   OWN widget template — a distinct prefab with a view-toggle control and a
   list-item container — NOT a newline-joined `valueText`. Deliberately more work
   now so the container is already in place as the home for the later edit UI
   (add/remove per item). Per the project prefab rule, new structural prefab
   objects are authored in the Editor (a batchmode build reserializes and would
   drop hand-authored objects), so implementation alternates **Editor authoring ↔
   close ↔ build** — it is NOT fully batch-buildable. Prefab authoring is a
   user-in-Editor step (spec'd precisely by the assistant); code + wiring +
   persistence are the assistant's.
5. **Separate toggle control.** The row carries a distinct on/off toggle
   ("show as list") as part of its prefab, flipping the persisted list-view flag
   — not overloaded onto row activation. Read-only otherwise (this version).

## 3 · Detection heuristic (default only)

Applied to a discovered foreign entry with `SettingType == string`:

- Split the current value on `,`, trim each token.
- Default to **list view** iff there are **≥ 2 non-empty tokens** and **every**
  token is "compact" — length ≤ 32 and contains no `.` (period). Otherwise plain.

This catches `InventoryChest, Torch, Campfire, …` and rejects prose like
`"Enables X. Also Y"`. It will occasionally misjudge prose that reads like a list
("torches, campfires, lamps") — acceptable because the view is read-only (a
false positive only wraps a string at commas) and the per-entry toggle corrects
it. Thresholds are tunable; this is the starting rule.

## 4 · Persistence — `ListOverrideStore`

A small MSM-owned store, sandbox-clean via `API.ConfigFilesystem`:

- **File:** `ModSettingsMenu/list-overrides` (call `CreateDirectory` first, then
  `Write(byte[])`; `Read` on load if `FileExists`). ASCII, one `key=0|1` line per
  override.
- **Key (stable across restarts):** `ConfigFilePath + "|" + Section + "|" + Key`
  of the foreign entry.
- **API:** `bool? Get(string key)` (null = no override → use heuristic),
  `void Set(string key, bool listView)` (writes through immediately).
- Loaded once (cached dict); `Set` updates the cache and rewrites the file.

## 5 · Architecture

Extends the ADR-001 foreign path; only foreign `string` entries are affected.

- **`SettingModel`:** add `SettingKind.List`; add `SettingDef.OverrideKey` (the
  §4 store key; non-null marks a togglable foreign string).
- **`ListOverrideStore`** (new): the §4 store.
- **New list-widget prefab (Editor-authored):** a template under the menu
  prefab's `WidgetTemplates` holding a label, a distinct **view-toggle control**,
  and a list-item `LinearLayout` container (the future edit UI's home). A small
  `MonoBehaviour` (à la `SectionBox`) exposes label / toggle / container by
  serialized reference.
- **List widget component** (new `RadicalMenuOption` subclass): splits the value
  on `,`, trims, and renders one read-only row per item into the container; the
  view-toggle control calls `ListOverrideStore.Set` and rebuilds the row
  (List ↔ plain). Kept separate from `SettingWidget` (distinct prefab + layout).
- **`ForeignConfigDiscovery.BuildDef`:** route **every** foreign `string` to
  `SettingKind.List` (v1 renders strings read-only, so all strings are uniformly
  the list widget — no comma/eligibility gate) and stamp `OverrideKey`. The
  default list-vs-plain view (`ListOverrideStore.Get(key) ?? HeuristicSaysList(value)`)
  is read by the `ListWidget` itself, not decided here. So even a single-item,
  empty, or prose string is togglable; the heuristic only picks its starting view.
- **`ModSettingsScreen.Populate`:** instantiate the list-widget prefab for
  `SettingKind.List` rows (the toggle template for the rest, as today).

Everything else (Toggle/Slider/Stepper/Choice, integrated sections, the master
toggle, dedup) is untouched.

## 6 · Edge cases

- **Empty / single-token strings:** never list (heuristic needs ≥2 tokens); plain
  `Info`, no toggle affordance needed (but activation may still toggle — harmless,
  a 1-item "list" renders identically).
- **Non-foreign / non-string entries:** unaffected — no `OverrideKey`, no toggle.
- **Value changes underneath (mod rewrites its cfg):** the override is by key, so
  the chosen view persists; the items re-split from the live value each render.
- **`API.ConfigFilesystem` first write:** `CreateDirectory` before `Write`
  (same order CoreLib uses) or the write fails on a missing dir.

## 7 · Out of scope / future

- **Editing** the list (add/remove tokens) — the next version; the prefab's item
  container is authored now as its home. Generic edit = per-token text entry; if
  the tokens all parse as `ObjectID`, an item picker (icons, searchable) becomes
  possible — the ObjectID-aware enrichment.
- **Rich item rendering** (icons/names, chips) — a later polish on the same prefab.

## 8 · Verification (manual, in-game)

With PlacementPlus installed, in a world, open Options → Mod Settings →
PlacementPlus:
- `ExcludeItems` renders as a **multi-line list** (one item per line, full list
  visible, no `...`) by default (heuristic).
- Flipping the row's **"show as list" toggle** → it collapses to the plain
  single-line (truncated) view; toggle again → back to list. Relaunch → the
  last-chosen view persists (check `ModSettings…/list-overrides` was written).
- `MaxBrushSize` / `MinHoldTime` (numeric) are unaffected — still editable steppers.

## 9 · References

- ADR-001 `docs/adrs/001-generic-corelib-config-discovery.md` (the discovery base).
- `API.ConfigFilesystem` byte[] persistence pattern: as used by CoreLib
  `ConfigFile.Save/Reload` (`Read`/`Write`/`FileExists`/`CreateDirectory`).
- PlacementPlus `ExcludeItems` (the concrete list): `PlacementPlusMod.cs:134`.
