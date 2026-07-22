# Foreign CoreLib `ConfigFile` survey

A snapshot of the Core Keeper mod.io ecosystem's use of CoreLib's `ConfigFile`
API, gathered as a reference/test corpus for Mod Settings Menu's foreign-config
auto-discovery (ADR-001). These are the configs MSM's generic discovery would
find and render on a real install.

- **Date:** 2026-07-22 · **mod.io game:** Core Keeper (5289)
- **Method:** all 254 public mods enumerated via the mod.io REST API; the 68 that
  declare a CoreLib dependency were downloaded and byte-scanned for the
  `CoreLib.Data.Configuration` namespace (bundled `CoreLib*.dll` skipped to avoid
  false positives). Of those, **23** actually reference the config API; parameters,
  types, and defaults were parsed from the shipped `Scripts/*.cs` `.Bind(...)` calls.
- **Scope:** the author's own mods (the 7 MSM consumers + MSM itself) are excluded;
  **General Mod Config Menu** is excluded (a GMCM config-menu framework — it reads
  *other* mods' configs, declares none of its own). Leaves **21 mods with real
  parameters** (Quick Stack To Nearby Chest imports the namespace but binds nothing).

**Type → MSM widget mapping:** `bool` → Toggle · `int` → Stepper · `float` →
Slider · enum → `Choice<T>` · `string` (comma-list) → List widget / Info.

> Types come from the `ConfigEntry<T>` / `Bind<T>` declaration where present, else
> inferred from the default literal (enum types named explicitly). Values are the
> code defaults, not a live `.cfg`.

## Cleanly extracted

### 2.5DKeeper
| Section | Key | Type | Default | Description |
|---|---|---|---|---|
| Camera | PerspectiveFieldOfView | float | 23.0f | Perspective vertical FOV in 2.5D mode; lower reduces distortion |
| Camera | EnableAspectCompensation | bool | true | Keep horizontal framing consistent across aspect ratios |
| Camera | ReferenceAspectRatio | float | 16f/9f | Baseline aspect ratio when compensation is on |

### Antifragile Glass
| Section | Key | Type | Default | Description |
|---|---|---|---|---|
| WallGlassBlock | startHealth | int | 1 | Durability tuning (affected by mining damage) |
| WallGlassBlock | maxHealth | int | 1 | |
| ThinWallGlass | startHealth | int | 1 | |
| ThinWallGlass | maxHealth | int | 1 | |
| GlassBridge | startHealth | int | 3 | Tile: 1 dmg/hit |
| GlassBridge | maxHealth | int | 3 | |
| GlassFloor | startHealth | int | 3 | |
| GlassFloor | maxHealth | int | 3 | |

### BuildingBlueprint
| Section | Key | Type | Default | Description |
|---|---|---|---|---|
| General | DestoryPrivileges | bool | true | |
| OnUIOpen | ClearSelected | bool | true | |
| OnUIOpen | ExitPlaceMode | bool | true | |

### CK QOL
Per-feature sections; each feature has an `IsEnabled` toggle plus tuning keys.

| Section | Key | Type | Default | Description |
|---|---|---|---|---|
| ChestAutoUnlock | IsEnabled | bool | true | |
| CraftingRange | IsEnabled | bool | true | |
| CraftingRange | MaxRange | float | 25f | |
| CraftingRange | MaxChests | int | 8 | |
| ItemPickUpNotifier | IsEnabled | bool | true | |
| ItemPickUpNotifier | AggregateDelay | float | 2f | |
| NoDeathPenalty | IsEnabled | bool | false | |
| NoEquipmentDurabilityLoss | IsEnabled | bool | false | |
| QuickEat | IsEnabled | bool | true | |
| QuickEat | EquipmentSlotIndex | int | 8 | |
| QuickHeal | IsEnabled | bool | true | |
| QuickHeal | EquipmentSlotIndex | int | 9 | |
| QuickStash | IsEnabled | bool | true | |
| QuickStash | MaxRange | float | 50f | |
| QuickStash | MaxChests | int | 50 | |
| QuickSummon | IsEnabled | bool | true | |
| QuickSummon | EquipmentSlotIndex | int | 0 | |
| ShiftClick | IsEnabled | bool | true | |
| Wormhole | IsEnabled | bool | true | |
| Wormhole | RequiredAncientGemstones | int | 5 | |
| Wormhole | AllMarkersAllowed | bool | false | |

### Close Keeper
| Section | Key | Type | Default | Description |
|---|---|---|---|---|
| Zoom | ZoomInLevel | float | 0.5f | Zoomed-in camera level |
| Zoom | PartialZoomLevel | float | 0.75f | Middle camera level |
| Zoom | ZoomOutLevel | float | 1.0f | Default camera level (1.0x) |
| Zoom | ZoomSpeed | float | 0.5f | Transition speed between zoom levels |
| Input | DefaultKeyCode | KeyboardKeyCode | UpArrow | ⚠ ControlMapping seed — zoom-toggle key |
| Input | Modifier1 | ModifierKey | Control | ⚠ ControlMapping seed — primary modifier |
| Input | Modifier2 | ModifierKey | None | ⚠ ControlMapping seed — secondary modifier |
| Input | Modifier3 | ModifierKey | None | ⚠ ControlMapping seed — tertiary modifier |

> **Keybind-seed configs (⚠).** Close Keeper's `[Input]` entries are `ConfigFile` values that
> only *seed* a CoreLib **ControlMapping** keybind: it reads them and passes them to
> `ControlMappingModule.AddKeyboardBind(...)` under a new category, so the authoritative
> rebinding happens in the game's **Controls menu** (Rewired), not the config. Once the player
> rebinds there, editing the config value has no effect. Foreign-config discovery should detect
> this pattern (a `ConfigEntry<KeyboardKeyCode>` / `ConfigEntry<ModifierKey>` whose `.Value`
> feeds `AddKeyboardBind`) and **skip or flag** these so it doesn't offer a redundant, misleading
> dropdown that competes with the Controls menu. (Contrast QuickToolSwap, which passes a
> `KeyboardKeyCode` literal to `AddKeyboardBind` and never mirrors the key into its config.)

### CustomizeWaterPriority
| Section | Key | Type | Default | Description |
|---|---|---|---|---|
| General | HighestPriorityTileset | Tileset | Dirt | Which tileset gets highest priority |

### Exp Multiplier
| Section | Key | Type | Default | Description |
|---|---|---|---|---|
| General | ExperienceMultiplier | int | 10 | Multiplier for experience gain |

### KeepFarming
| Section | Key | Type | Default | Description |
|---|---|---|---|---|
| ExtraChance | Enabled | bool | false | Enable extra-seed-chance mechanic |
| ExtraChance | ExtraSeedChanceMultiplier | float | 0.1f | Multiplier to derive extra seed chance |
| Misc | EnableMigrationMode | bool | false | Migration mode (with a warning) |

### PlacementPlus
| Section | Key | Type | Default | Description |
|---|---|---|---|---|
| General | MaxBrushSize | int | 7 | Max brush range (3–9) |
| General | ExcludeItems | string | "" | Comma-list of items to disable area placement for |
| General | MinHoldTime | float | 0.15f | Hold time before +/- auto-increment |

### Point Shop
| Section | Key | Type | Default | Description |
|---|---|---|---|---|
| General | ShowSwitch | bool | true | |

### QuickChug
| Section | Key | Type | Default | Description |
|---|---|---|---|---|
| General | PotionCooldownSeconds | float | 5.15f | Seconds before another potion use |
| General | FoodCooldownSeconds | float | 1.0f | Seconds before another food use |
| General | PotionDrinkCount | int | 1 | Potions per key press (1–10) |
| General | FoodEatCount | int | 1 | Food per key press (1–10) |
| General | SearchScope | int | 2 | 0=Hotbar, 1=Inventory/pouches, 2=both |
| General | CooldownMode | int | 0 | 0=respect cooldowns, 1=fast chain |

### Secure Attachment
| Section | Key | Type | Default | Description |
|---|---|---|---|---|
| General | attachChests | bool | true | Make chests indestructible (wrench-only removal) |
| General | additionalItems | string | "" | Comma-list of extra items to secure |

### Stash and get from nearby chests  *(and the "(No Commands)" variant — identical)*
| Section | Key | Type | Default | Description |
|---|---|---|---|---|
| General | StashChestDistance | float | 5f | Max distance to look for chests |
| General | GetChestDistance | float | 5f | Max distance to look for chests |

### Trophy Keeper
| Section | Key | Type | Default | Description |
|---|---|---|---|---|
| General | DropChance | float | 0.1f | |
| General | TrophyEntityDrop | bool | true | |

### Water Priority Control  *(and Water Priority Overhaul — identical)*
| Section | Key | Type | Default | Description |
|---|---|---|---|---|
| WaterPriority | FirstPriority | WaterTileset | Dirt | Priority ordered first → sixth |
| WaterPriority | SecondPriority | WaterTileset | Passage | |
| WaterPriority | ThirdPriority | WaterTileset | Crystal | |
| WaterPriority | FourthPriority | WaterTileset | Mold | |
| WaterPriority | FifthPriority | WaterTileset | Sea | |
| WaterPriority | SixthPriority | WaterTileset | LarvaHive | |

## Statically resolved schemas (the "dynamic" mods)

These bind their config in loops / base classes, but the loops iterate **fixed,
statically-known collections** (enums, hardcoded lists) — so a complete schema is
derivable from the mod's own source without running it. 3 of the 4 resolve fully;
MiscTools is genuinely user-data, not a settings schema.

### Fighter of Core Keeper (更好的战斗) — enum `FighterCategory`
`_`-prefixed enum members are section headers, the rest are `bool` toggles (default
`true`); `AddValue()` adds the typed extras.

`CoreFighter/Config.cfg`

| Section | Key | Type | Default | Notes |
|---|---|---|---|---|
| Infinity | Mana / Hunger / Explosive | bool | true | |
| Equip | NoRecoil / EnableAllPreset | bool | true | |
| Immune | Explosion / PushBack | bool | true | |
| Multiple | Drops / EnemyHealth | bool | true | |
| Multiple | Drops.Multiplier | int | 1 | range 1–10 |
| Multiple | Drops.BossOnly | bool | true | |
| Multiple | EnemyHealth.Multiplier | float | 1 | range 1–100 |
| Multiple | EnemyHealth.BossOnly | bool | false | |
| Misc | MapMarkerTeleport / Vampire | bool | true | |
| Misc | Vampire (value) | float | 0.01 | range 0.01–1 |

`CoreFighter/AttackSpeed.cfg` 

**12 fixed `OriginCoolDown<speed>` sections**. Every speed section has the same shape.

<speed> => 0.125, 0.2, 0.25, 0.3, 0.4, 0.5, 0.6, 0.7, 1.0, 1.5, 2.0, 5.0

| Section | Key | Type | Default | Notes |
|---|---|---|---|---|
| General | MainSwitch | bool | false | Admin |
| OriginCoolDown<speed> | Switch | bool | false |  |
| OriginCoolDown<speed> | Tool | float | <speed> | range 0.1–5 |
| OriginCoolDown<speed> | Melee | float | <speed> | range 0.1–5 |
| OriginCoolDown<speed> | Range | float | <speed> | range 0.1–5 |
| OriginCoolDown<speed> | Magic | float | <speed> | range 0.1–5 |
| OriginCoolDown<speed> | Throw | float | <speed> | range 0.1–5 |
| OriginCoolDown<speed> | Beam | float | <speed> | range 0.1–5 |
| OriginCoolDown<speed> | OffHand | float | <speed> | range 0.1–5 |
| OriginCoolDown<speed> | Consume | float | <speed> | range 0.1–5 |

Total: `MainSwitch` + 12 × 9 Speed-Keys (`Switch` + 8 weapon-class floats) = **109 entries**.

### Quality of Core Keeper (更好的体验) — mod ID 4786550 / "CoreEnhance", enum `EnhanceCategory`
Same pattern; default `true` except the members overridden to `false` (some Admin/Server-scoped):
| Section | Keys (bool=true unless noted) |
|---|---|
| Infinity | Durability, Arena (+`int=1000`, 100–9999), Minion, BossScan; **Boulder=false** |
| Accelerate | Merchant (+`int=0`, 0–3500), Casting, Portal; **Titan/Plant/Crafting=false**, **Level=false (Admin)** |
| Automation | Door; **FishingNetNoCritter/FishingNetCanGetItem=false**, **GoldenPlantToSeed=false (Server)** |
| Pet | TalentDisplay, ResetSkin; **RollSkill=false (Admin)** |
| Misc | IgnoreRayChecksForPickup, DeathNoDrop, ChainMining, ContainerDisplay; **ModifySledgeRange=false** |
| Quick | MoveChestContent, OpenLockedChest, OpenLockedMelody |

### QuickToolSwap — 5 fixed default sections (user-extensible)
`SetDefaults()` binds `Mining`, `Farming`, `Catching`, `Fishing`, `Digging`, each:
| Key | Type | Default |
|---|---|---|
| activeOn | string | comma-list of CK `TileType`/`ObjectID` names (e.g. Mining = `ore, wall`) |
| priorityList | string | comma-list of CK `ObjectID` tool names (e.g. Mining = `LightningGun, LaserDrillTool, …, WoodMiningPick`) |

Shape is static; the user may add further sections at runtime (instances dynamic, schema fixed).

### MiscTools — not a settings schema
File `MiscTools/UserMarkerLabels.cfg`. One fixed section with a dynamic key per placed map
marker (user data, not enumerable settings) — only the shape is static.

| Section | Key | Type | Default | Description |
|---|---|---|---|---|
| Markers | `<markerId>` (one per placed marker) | string | "" | User-placed map-marker label (Client-scoped) |

> **CK-decompile role.** The three resolvable schemas above need **no** decompile — their
> collections are mod-internal enums/lists. The decompile helps only for **static / offline**
> enumeration (like *this doc*): listing the members of the CK enums behind the enum-typed
> configs — `WaterTileset` (Water Priority), `Tileset` (CustomizeWaterPriority),
> `KeyboardKeyCode` / `ModifierKey` (Close Keeper), and the `ObjectID` / `TileType` names in
> QuickToolSwap's lists. MSM itself does **not** need it at runtime — it reads the members via
> `Enum.GetNames` (see "MSM coverage" below).

## Summary

| | Count |
|---|---|
| Public Core Keeper mods (game 5289) | 254 |
| Depend on CoreLib | 68 |
| Foreign (excluding own) | 60 |
| Foreign mods using `ConfigFile` | 23 |
| … with real parameters (excl. GMCM, Quick Stack) | 21 |
| Cleanly extracted (parameters + types + defaults) | 17 mods (~90 params) |
| "Dynamic" but statically resolved from source (enum/list-driven) | 3 mods (Fighter, Quality, QuickToolSwap) |
| Not a settings schema (per-marker user data) | 1 mod (MiscTools) |

The **23 → 21** step drops the two config-API users that expose nothing to configure:
**General Mod Config Menu** references the API only to read *other* mods' configs (a
config-menu framework — declares none of its own), and **Quick Stack To Nearby Chest**
imports `CoreLib.Data.Configuration` but never calls `.Bind(...)` — a vestigial/leftover
reference with **zero parameters** (which is why the byte-scan flagged it but nothing could
be extracted). Both would surface as empty in foreign-config discovery.

Types seen (→ MSM widget):

- **bool** — toggles (most common) → Toggle
- **int** — counts / indices → Stepper
- **float** — ranges / multipliers → Slider
- **string** — comma-lists → List widget / Info
- **enum** — `KeyboardKeyCode`, `ModifierKey`, `Tileset`, `WaterTileset` → `Choice<T>` (members via `Enum.GetNames` at runtime)

The enum and comma-list cases are where generic discovery must go beyond simple toggles.

## MSM coverage / gaps

Cross-checked against MSM's current foreign-config discovery
(`ForeignConfigDiscovery.BuildDef`): every type that occurs is rendered; one is not yet editable.

| Type | MSM kind | Editable? |
|---|---|---|
| bool | Toggle | yes |
| int (range / none) | Stepper (bounded / unbounded) | yes |
| float (range) | Slider | yes |
| float (none) | Stepper (unbounded) | yes |
| enum | Choice (`Enum.GetNames`) | yes |
| string (comma-list) | List / Info | **no — read-only** |

- **Enums are fully supported at runtime.** `BuildDef` maps `t.IsEnum → Choice` with
  `Enum.GetNames(t)`, and the widget round-trips through `Get/SetSerializedValue` (Toml stores the
  enum name). No CK decompile is needed for this; the decompile only helps *static* enumeration.
- **The one type gap is editable `string`.** Comma-lists (PlacementPlus `ExcludeItems`, Secure
  Attachment `additionalItems`, QuickToolSwap `activeOn`/`priorityList`, MiscTools labels) are
  shown but read-only. The `ListWidget`'s item container is already the intended home for a future
  edit UI (add/remove list members + free-text editing).

Non-type gaps this survey surfaced:
- **Keybind-seed enums** (Close Keeper `[Input]`) would render as editable `Choice` dropdowns that
  compete with the game's Controls menu — discovery should detect and skip/flag them (see above).
- **Server/Admin-scoped** entries fall to read-only `Info` when the player can't change them —
  a scope gate, not a type limit.
