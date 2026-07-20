# Design — Generic CoreLib config discovery ("GMCM behaviour") in Mod Settings Menu

- **Date:** 2026-07-20
- **Status:** Approved (design); implementation pending
- **Scope for this spec:** v1 (see phasing). v2 items are listed under *Future work*.

## 1 · Goal

Give Mod Settings Menu (MSM) a second, **additive** way to populate its screen:
alongside the existing opt-in API (`ModSettings.Section(this).Toggle()…`), MSM
should **auto-discover** the CoreLib `ConfigFile`s created by mods that use
CoreLib config **but never integrated MSM's API** — and render them as editable
settings. This mirrors the standalone Core Keeper mod **"General Mod Config
Menu" (GMCM)**, which does exactly this over CoreLib's `ConfigFile` API.

The current concrete target is **PlacementPlus** (the only installed foreign
CoreLib-config mod). The feature is general (any such mod), not PlacementPlus-
specific.

MSM's existing explicit-registration behaviour is unchanged. GMCM may remain
installed in parallel — the two are orthogonal (no detection/replacement).

## 2 · Background — why this is cheap and first-class

CoreLib was **built for a general config menu**, not reverse-engineered into
one. Evidence in `CoreLib-source-4.0.5`:

- `ConfigFile.AllConfigFilesReadOnly` (`ConfigFile.cs:40`) — a static registry;
  every `ConfigFile` constructor self-registers (`ConfigFile.cs:68-71`). This is
  the enumeration entry point (the BepInEx *Configuration Manager* equivalent).
- `ConfigEntryBase.Scope` carries the comment **"Used by GeneralConfigMenu"**
  (`ConfigEntryBase.cs:96`); `ConfigScope` is documented as *"Necessary
  information to ensure the normal operation of the General Config Menu"*
  (`ConfigScope.cs`), exposing `ConfigAccessLevel {ViewOnly, Client, Server,
  Admin}`, `requireReload`, and `Changeable()` (guest/admin gating).
- `ConfigEntryBase` exposes, **publicly and without reflection**: `SettingType`,
  `DefaultValue`, `Description` (with `AcceptableValues`), `Scope`, `BoxedValue`
  (get/set), and — the key bridge — `GetSerializedValue()` /
  `SetSerializedValue(string)` (via CoreLib's `TomlTypeConverter`).

Because a synthesised setting can point straight at the live foreign
`ConfigEntryBase` and drive it through `BoxedValue` / `SetSerializedValue`, MSM
needs **no** `System.IO` and **no** reflection — it stays sandbox-clean
(`skipSafetyChecks: 0`), exactly like its existing path.

### Ground truth — PlacementPlus (`PlacementPlus.cfg`)

Three entries, all in CoreLib section `General`
(`PlacementPlusMod.cs:130-138`):

| Key | Type | Default | Metadata | Scope (from Bind overload) |
|---|---|---|---|---|
| `MaxBrushSize` | `int` | 7 | `AcceptableValueRange<int>(3,9)` | `Server` (ConfigDescription overload → `ConfigScope.Empty`) |
| `ExcludeItems` | `string` | comma-list | description only | `Client` (string overload default) |
| `MinHoldTime` | `float` | 0.15 | description only, **no range** | `Client` (string overload default) |

So even the single real target exercises all the interesting cases: a ranged
int (→ Slider), an unbounded float (→ Stepper), and a free string (→ read-only
in v1). Scope cannot be trusted to mean "client-only": the overload defaults
differ (`Server` vs `Client`) without the author expressing intent.

### Ground truth — how GMCM renders it (reference screenshots, game 1.2.1.5)

- Title = the config **path** (`PLACEMENTPLUS/PLACEMENTPLUS`); one screen per
  `ConfigFile`.
- **Per-row scope icon**: ☁ cloud = `Server` (`MaxBrushSize`), 🖥 monitor =
  `Client` (`ExcludeItems`, `MinHoldTime`) — matches the overload analysis.
- **Buffered save**: the floppy icon lights up only when changes are pending →
  GMCM commits on click, not live.
- **Every value is a plain text box** — even the ranged int and the float (no
  slider/stepper). The long string **overflows** the panel when unfocused.

**Implication for MSM's value-add:** GMCM is functional but crude. MSM's reason
to exist here is **typed-widget inference** (Slider for the ranged int, Stepper
for the float) inside its `LinearLayout` — nicer and overflow-free. Merely
copying GMCM (text boxes for all) would add nothing over just using GMCM.

## 3 · Decisions (locked)

1. **Ambition = General** — discover *all* foreign CoreLib configs generically;
   coexist with GMCM (no detect/replace, no server-sync reimplementation).
2. **Coverage = Hybrid, phased.**
   - **v1 (now):** typed widgets where inferable (Toggle / Slider / Stepper /
     Choice); everything not safely editable (free strings, unsupported types,
     view-only/locked) → **read-only Info row**.
   - **v2 (later):** an editable free-text field (the risky RadicalMenu
     skim-row text entry) so strings become editable like GMCM.
3. **Presentation = mixed but marked** — one screen, all boxes sorted
   alphabetically; discovered mods carry a light, code-only marker.
4. **Persistence = live-persist** (MSM-consistent). No GMCM-style Save button;
   the foreign `ConfigFile`'s `SaveOnConfigSet` is already on, so setting a
   value persists immediately to that mod's own `.cfg`.
5. **Scope icons (☁/🖥) = deferred** to v2 (needs sprites + Editor prefab work);
   `Changeable()` / `requireReload` are handled functionally without an icon.
6. **Master toggle = in v1.** A framework-owned "Show settings from other mods"
   toggle (default on) lets a user who also runs GMCM suppress MSM's discovery
   and avoid a duplicate listing. Implemented by **dogfooding**: MSM registers
   its *own* section via `ModSettings.Section` holding this one Toggle. Its
   `ConfigFile` is therefore MSM-owned → excluded from discovery by the §7
   filter (no self-reference); `Populate` reads it and skips
   `ForeignConfigDiscovery` when off.

## 4 · Architecture — an adapter, not a second renderer

MSM already turns `SectionBuilder` into the internal descriptors `ModSection`
(one box) + `SettingDef` (one row, already holding a live `ConfigEntryBase`),
which `ModSettingsScreen.Populate` renders via `SectionBox` + `SettingWidget`.

The feature adds **one adapter** that produces the *same* descriptors from
foreign `ConfigEntryBase`s instead of from the builder:

```
ConfigFile.AllConfigFilesReadOnly
      │  (filter: exclude MSM-owned + CoreLib-internal)
      ▼
ForeignConfigDiscovery  ──►  ModSection (foreign=true, DisplayName from path)
                              └─ SettingDef[]  (Kind inferred, RawLabel, Entry=foreign ConfigEntryBase)
      │
      ▼
ModSettingsScreen.Populate  (merge explicit + discovered, sort A→Z, mark foreign)
      ▼
SectionBox + SettingWidget   (unchanged render path; drives ConfigEntryBase.BoxedValue)
```

~90 % reuse. The renderer gains three things: the read-only **Info** kind, a
**float-capable Stepper**, and a **foreign-Choice path** that commits via
`SetSerializedValue` instead of a typed `SettingHandle<T>` (see §6) — MSM's own
Choice rows keep their typed-handle path unchanged.

## 5 · Widget inference cascade (first match wins)

Evaluated per foreign `ConfigEntryBase`:

1. `Scope.accessLevel == ViewOnly` → **Info** (read-only). *Static; no player
   needed.*
2. In-world and `!Scope.Changeable()` (guest on `Server`, non-admin on `Admin`)
   → **Info**, rendered **locked**. *Requires `Manager.main.player`; see §8.*
3. `SettingType == bool` → **Toggle**.
4. `SettingType.IsEnum` → **Choice** over `Enum.GetNames(SettingType)`.
5. `Description.AcceptableValues is AcceptableValueList<…>` → **Choice** over its
   values.
6. numeric (`int`/`float`) **with** `AcceptableValueRange<int|float>` →
   **Slider** (bounds from `MinValue`/`MaxValue`).
7. numeric (`int`/`float`) **without** range → **Stepper** (int: step 1;
   float: heuristic step — `0.05` for `|default| < 1`, else `1` — tunable).
8. everything else (`string`, unknown types, ranged numeric of an unhandled
   type) → **Info** (read-only).

**Range/list extraction detail:** `AcceptableValueRange<T>.MinValue/MaxValue`
and `AcceptableValueList<T>.AcceptableValues` live on the *generic* class, and
reflection is sandbox-banned. So MSM casts to the concrete handled types
(`<int>`, `<float>` for ranges). An unhandled generic type falls through to
Info (rule 8) — safe, never throws.

## 6 · Value bridge & persistence

- **Toggle / Slider / Stepper:** read/write `ConfigEntryBase.BoxedValue`
  (already how `SettingWidget` works for MSM's own rows).
- **Choice (foreign):** operate in **serialized-string space** — options are the
  enum names / `AcceptableValueList` values as strings; current value via
  `GetSerializedValue()`; commit via `SetSerializedValue(token)`. This sidesteps
  needing `T` at compile time.
- **Persistence:** any write triggers CoreLib's `OnSettingChanged` → `Save()`
  into the foreign mod's own `.cfg`. Whether the owning mod *reads* the change
  live or only at load is its concern; `requireReload` (§8) flags the latter.

## 7 · Deduplication & ownership

- **Exclude MSM-owned files by reference identity.** `ConfigStore` creates one
  `ConfigFile` per MSM consumer; every such instance is also in
  `AllConfigFilesReadOnly`. Add an accessor (e.g. `ConfigStore.IsOwn(ConfigFile)`
  or expose the values) and filter by *instance identity*, not by path (a
  foreign mod could coincidentally use `<id>/config.cfg`). This automatically
  excludes every **API-integrated** mod (its file is MSM-owned).
- **Exclude CoreLib's own config** if present (by owner/known path), best-effort.
- **Owner display name** = first path segment of `ConfigFilePath`
  (`"PlacementPlus/PlacementPlus.cfg"` → `PlacementPlus`). The cleaner
  `_ownerMetadata.Metadata.name` is private (no public accessor, reflection
  banned). *Optional refinement:* match the segment against loaded mods for a
  nicer `displayName`.
- **One box per mod.** PlacementPlus has a single CoreLib section (`General`);
  multiple CoreLib sections within one mod are rendered as rows under the one
  box in v1 (sub-section headers are a possible later refinement).

## 8 · Edge cases

- **Title screen / no player.** `Scope.Changeable()` reads
  `Manager.main.player`. Guard: when no player exists, treat non-`Client` scopes
  as locked (Info). Whether MSM's screen is even active at the title is the
  `activeInTitle` question — **verify empirically** during implementation before
  relying on either behaviour.
- **`requireReload == true`** → route to MSM's existing `RequiresRestart` prompt
  (deferred off the `Deactivate` stack, per the existing gotcha).
- **Labels are raw.** Foreign rows show the CoreLib `section`/`key` verbatim and
  use `ConfigDescription.Description` as the hint — foreign mods ship no MSM loc
  terms. This is deliberate and is what the marker (below) signals.

## 9 · Marking (mixed but marked)

Code-only, no new prefab (respects the Editor-reserialize rule): append a short
**localised marker** to a discovered box's header (e.g. header text + a
`Loc.T("ModSettingsMenu-UI/AutoDetected")` suffix/badge). Signals provenance —
why a "PlacementPlus" box shows raw keys and inferred widgets rather than
curated, localised ones.

## 10 · Components to build / change (v1)

| File | Change |
|---|---|
| `Settings/ForeignConfigDiscovery.cs` | **new** — enumerate `AllConfigFilesReadOnly`, filter, infer, build `ModSection`/`SettingDef` |
| `Settings/ConfigStore.cs` | add `IsOwn(ConfigFile)` (or expose values) for dedup |
| `Settings/SettingModel.cs` | add `SettingKind.Info`; make `Stepper` float-capable; add `bool Foreign`, raw-label + `Locked` carriers on `ModSection`/`SettingDef` |
| `UI/SettingWidget.cs` | render **Info** (no `Adjust`, no skim), float stepper, locked state, and a **foreign-Choice** branch driven by `Get/SetSerializedValue` |
| `UI/ModSettingsScreen.cs` | `Populate` merges explicit + discovered sections, sorts A→Z, applies the marker, and **gates discovery on the master toggle** |
| `ModSettingsMenuMod.cs` | register MSM's **own** section (dogfood `ModSettings.Section`) with the master Toggle "Show settings from other mods" (default on) |
| `Loc.cs` + `localization/localization.yaml` | add the `AutoDetected` marker term + the master-toggle label/key term (EN/DE) |

## 11 · Out of scope / future work (v2+)

- **Editable free-text field** for strings (skim-row text entry) — the deferred
  "point 2". Precedent: the ItemChecklist search field.
- **Per-row scope icons** (☁ Server / 🖥 Client) — needs sprites + Editor work.
- **GMCM detection / replacement**, server-sync semantics, a GMCM-style buffered
  Save button, CoreLib sub-section sub-headers.

## 12 · Verification (manual, in-game)

No automated tests (matches the repo). With PlacementPlus installed and *not*
MSM-integrated: open **Options → Mod Settings**, confirm a marked "PlacementPlus"
box appears with `MaxBrushSize` as a 3–9 Slider, `MinHoldTime` as a Stepper, and
`ExcludeItems` as a read-only Info row; edit the slider/stepper and confirm the
value persists to `PlacementPlus.cfg` across a relaunch; confirm MSM-integrated
siblings still render once (no duplicate box).

## 13 · References

- CoreLib 4.0.5 source: `…/CoreKeeperDecompile/CoreLib-source-4.0.5/…/ConfigFile/`
  (`ConfigFile.cs`, `ConfigEntryBase.cs`, `ConfigScope.cs`, `ConfigDescription.cs`,
  `AcceptableValueRange.cs`, `AcceptableValueList.cs`).
- PlacementPlus source: installed mod `3400322_7742541`,
  `Scripts/Scripts/PlacementPlusMod.cs:97-138`.
- Existing MSM architecture: `CLAUDE.md` (§ Architecture), `docs/roadmap.md`
  (the Info widget is already a planned kind).
