# Mod Settings Menu

A framework mod for [Core Keeper](https://mod.io/g/corekeeper) that gives other
mods an in-game settings screen. Consumer mods declare their settings in a few
lines of `IMod.Init`; the framework renders them as a box of widgets under
**Options → Mod Settings** and persists every value through a CoreLib
`ConfigFile`. No UI, prefab, or localization code on the consumer side.

- **Players:** install this mod because a mod you use depends on it. There is
  nothing to configure here directly — each dependent mod adds its own section.
- **Mod authors:** read the API below — or the full
  **[tutorial](docs/tutorial.md)** for a guided, end-to-end walkthrough
  (integration *and* internals). Adding a settings section is ~5 lines.

Personal-use, non-commercial (Pugstorm EULA). Built against Pugstorm's
`CoreKeeperModSDK`; distributed on mod.io (not Thunderstore/BepInEx).

---

## Quick start (for mod authors)

```csharp
using ModSettingsMenu.Settings;
using PugMod;

public sealed class MyMod : IMod
{
    public static SettingHandle<bool> Enabled;
    public static SettingHandle<int>  Power;

    public void Init()
    {
        ModSettings.Section(this)
            .Hint("Tune MyMod")                                   // optional subtitle
            .Toggle(out Enabled, "enabled", true)                 // bool
            .Choice(out Power,   "power", new[] {1, 2, 5, 10}, 2) // discrete set
            .Build();
    }

    // ... elsewhere, read the LIVE value wherever you need it:
    //     if (MyMod.Enabled.Value) amount *= MyMod.Power.Value;

    public void EarlyInit() {}
    public void ModObjectLoaded(UnityEngine.Object o) {}
    public void Shutdown() {}
    public void Update() {}
}
```

That is the whole integration. The section appears in the Options menu labelled
with your mod's display name, and `enabled`/`power` persist to
`mods/MyMod/config.cfg`.

---

## Wiring (one-time per consumer mod)

Mod Settings Menu is a **hard dependency**. Declare it — and CoreLib, which it
needs at runtime — in **both** places:

**1. Runtime asmdef** (your mod's runtime `.asmdef`) — add the references so your
code compiles against the API:

```json
"references": [
    "PugMod.SDK",
    "CoreLib",
    "ModSettingsMenu"
]
```

**2. ModBuilderSettings `.asset`** (the `metadata:` block of your mod's
ModBuilderSettings `.asset`) — add the loader dependencies so the game refuses to
load your mod without them:

```yaml
dependencies:
  - modName: CoreLib
    required: 1
  - modName: ModSettingsMenu
    required: 1
```

On mod.io, also list Mod Settings Menu (and CoreLib) as dependencies of your
mod so subscribers get them automatically.

---

## The API

Everything lives in the `ModSettingsMenu.Settings` namespace.

### `ModSettings.Section(IMod consumer)` → `SectionBuilder`

Begins a section for the calling mod. Call it in `IMod.Init` (or `IMod.EarlyInit` for
bake-time settings — see **Behaviour & gotchas**) and pass `this`.
The framework resolves your mod's identity from the `IMod` reference:

- **Section id / term prefix** ← `metadata.name` (your internal PascalCase name).
- **Section heading** ← `metadata.displayName` (falls back to `name`). Rendered
  as-is — it is **not** localized (mod names are proper nouns).

Registering the same `modId` twice is ignored (first `Build()` wins, logs a
warning), so a section is registered exactly once.

### `SectionBuilder` — fluent declaration

Each widget method binds a persisted CoreLib entry, hands you a typed
`SettingHandle<T>` via `out`, and returns the builder for chaining. Finish with
`Build()`.

| Method | Renders as | `out` handle | Notes |
|---|---|---|---|
| `Hint(string text)` | subtitle under the heading | — | optional; one line |
| `SortOptions(OptionSort mode)` | — | — | option order in the box; default `AsDeclared` |
| `Toggle(out SettingHandle<bool> h, string key, bool def)` | on/off | `bool` | |
| `Slider(out SettingHandle<float> h, string key, float min, float max, float def, float step, SliderDisplay display = SliderDisplay.Steps)` | slider bar | `float` | `step ≤ 0` → whole range as one step; bar segments = `(max-min)/step` |
| `Choice<T>(out SettingHandle<T> h, string key, T[] values, T def)` | ←/→ cycle | `T` | any `T`; token = `value.ToString()` |
| `Stepper(out SettingHandle<int> h, string key, int min, int max, int def)` | ←/→ integer | `int` | clamped to `[min, max]` |
| `RequiresRestart()` | — | — | marks the **last-declared** setting as restart-required (see below) |
| `Build()` | — | — | registers the section |

`key` is the persistence key and the loc-term leaf (see **Localization**). Keep it stable across releases — changing it orphans the saved value.

`RequiresRestart()` — chain it directly after a widget (`.Choice(out h, "key", …).RequiresRestart()`) to mark that setting as needing a game restart to take effect (e.g. a bake-time / load-time value that is only read at world load). When a so-marked setting is actually changed and you leave the Mod Settings screen, the framework raises Core Keeper's own *restart to apply mod changes* popup (Cancel / Yes → relaunch) — the same prompt the game shows when your mod subscriptions change. Settings whose value applies live (read every frame / tick) should **not** be marked. ⚠️ A bake-time setting consumed during world/database conversion must be registered in `IMod.EarlyInit`, not `Init` — that conversion runs before `Init` (see **Behaviour & gotchas**), so an `Init`-bound handle makes the bake read your hardcoded default instead of the saved value.

### `SettingHandle<T>` — reading and writing values

```csharp
public T Value { get; set; }          // read = live value; set = persists + clamps
public event Action<T> OnChanged;     // fires on menu edit, code set, or reload
```

- **Read `Value` where you use it.** It always returns the current value, so a
  menu change applies immediately — no restart, no caching needed:

  ```csharp
  // in a Harmony prefix / ECS OnUpdate / wherever:
  if (MyMod.Enabled.Value)
      amount *= MyMod.Power.Value;
  ```

- **Set `Value`** to change it from code; CoreLib clamps to the widget's range
  (`Slider`/`Stepper` bounds, `Choice` list), auto-saves, and raises `OnChanged`.
- **`OnChanged`** is handy if you must react at the moment of change (rebuild a
  cache, re-apply a patch) rather than polling `Value`.

### `Choice<T>` in detail

`Choice` cycles a fixed, ordered set of values of **any** type `T`. The stored
token and the (default) displayed text are `value.ToString()`.

```csharp
enum Difficulty { Easy, Normal, Hard }        // enums make self-documenting tokens
ModSettings.Section(this)
    .Choice(out var diff, "difficulty",
            new[] { Difficulty.Easy, Difficulty.Normal, Difficulty.Hard },
            Difficulty.Normal)
    .Build();
// diff.Value is a Difficulty; the saved token is "Easy"/"Normal"/"Hard".
```

- `values` must have **distinct** `ToString()`. Prefer an `enum` (clean tokens);
  `int[]`/`string[]` also work.
- An unknown/removed token in an old config falls back to `def`.
- Declaring `Choice` with an empty/null `values` logs a warning and degrades to
  the single default option (rather than throwing).

### Enums

```csharp
public enum SettingKind  { Toggle, Slider, Stepper, Choice }   // widget type
public enum SliderDisplay { Steps, Number, Percent }           // how a Slider shows its value
public enum OptionSort    { AsDeclared, ByKey, ByLabel }       // option order within a section
```

`OptionSort.ByLabel` sorts by the **localized** label, so it re-sorts per active
language; `ByKey` sorts by the raw `key`; `AsDeclared` (default) keeps your
builder-chain order.

---

## Persistence

Each consumer gets its own CoreLib `ConfigFile` at **`mods/<ModId>/config.cfg`**
inside CoreLib's config filesystem, e.g.:

```
…/LocalLow/Pugstorm/Core Keeper/Steam/<accountId>/mods/MyMod/config.cfg
```

```ini
[Settings]
enabled = true
power = 5
```

All settings land under the `[Settings]` section. Writes auto-save immediately
(setting `handle.Value` or editing in the menu). The file is created on first
run with the declared defaults. No `System.IO` is involved on your side —
CoreLib does all file access in its own trusted assembly, so your mod stays
inside the RoslynCSharp sandbox (no `skipSafetyChecks` needed).

---

## Localization (optional)

Labels are localized through Core Keeper's own text system. The framework looks
up these terms and **falls back to the raw key/token** when a term is missing —
so localization is entirely optional; skip it and the menu shows your keys.

| UI element | Loc term |
|---|---|
| Setting label | `<ModId>-Config/<key>` |
| Section hint | `<ModId>-Config/_hint` |
| `Choice` option label | `<ModId>-Config/<key>/<token>` |

Core Keeper keys every localization `TextDataBlock` by `header + "/" + name`, so
a term is exactly `<header>/<name>`. Ship one `TextDataBlock` per term — authored
with whatever localization workflow you use — packed into your mod's AssetBundle.
Because a `Choice` option term (`.../<key>/<token>`) has a second slash, the token
cannot be a single `name`: make the setting term the `header` and the bare token
the `name`.

| Term | `TextDataBlock` header | `TextDataBlock` name |
|---|---|---|
| `MyMod-Config/_hint` | `MyMod-Config` | `_hint` |
| `MyMod-Config/enabled` | `MyMod-Config` | `enabled` |
| `MyMod-Config/power` | `MyMod-Config` | `power` |
| `MyMod-Config/power/2` | `MyMod-Config/power` | `2` |

Each block carries the per-language strings (English, German, …).

The Toggle on/off words and the "Mod Settings" menu title are supplied by this
framework (`ModSettingsMenu-UI/On`, `/Off`, `/Title`) — you don't localize
those.

> **Dev-loop caveat.** Core Keeper merges mod loc into the game-wide
> `Localization.csv` (its I2 source) **first-write-wins**: once a term is
> exported, *changing* its text in a rebuilt bundle does **not** refresh the CSV
> row unless the mod's version changes. So while iterating on loc during
> development (re-testing the same local build), edit or remove the stale
> `<ModId>-Config/*` rows in the game's `localization/Localization.csv` (back it
> up first) so the new text takes. A published mod.io update (new version) is not
> affected.

---

## Full example: Faster Talents

The [Faster Talents](https://mod.io/g/corekeeper/m/fastertalents) mod is the reference consumer.
Its entire integration:

```csharp
// FasterTalentsMod.Init()
ModSettings.Section(this)
    .Hint("A faster talent-point curve plus a skill-XP boost - fill your talent trees sooner.")
    .Toggle(out var enabled, "enabled", true)
    .Choice(out var xp, "xpMultiplier", new[] { 1, 2, 3, 5, 10, 20, 50 }, 3)
    .Build();
ModConfig.Instance.Bind(enabled, xp);   // stash the handles for the patches to read
```

```csharp
// ModConfig — the adapter the Harmony patches read (unchanged field→property access):
public bool  enabled      => _enabledHandle != null ? _enabledHandle.Value : true;
public float xpMultiplier => _xpHandle      != null ? _xpHandle.Value      : 3f;
```

Every skill-XP grant multiplies by `ModConfig.Instance.xpMultiplier` — read live
in the patch, so changing the Choice in the menu takes effect immediately.

---

## Behaviour & gotchas

- **Call in `IMod.Init` — or `IMod.EarlyInit` for values read during world/database
  conversion.** Every mod's `Init` (consumers included) runs before the first
  `Update`, so registering in `Init` still has all sections in place before the menu
  first renders (the framework pre-warms after `Init`). **But** Core Keeper runs its
  world/database conversion — where `PugDatabasePostConverter.PostConvert` bakes
  recipes and other object data — *after* `EarlyInit` and *before* `Init`. If your
  setting is consumed at that bake (a bake-time `RequiresRestart` knob), register the
  section **and** bind its handle in `EarlyInit`; binding in `Init` lets the bake read
  the handle before it exists, silently falling back to your hardcoded default.
  `API.ConfigFilesystem` is initialised before any mod's `EarlyInit`, so the persisted
  value is already loadable there. Settings read live (every frame/tick, long after
  `Init`) can stay in `Init`.
- **`modId` = `metadata.name`.** The term prefix and config folder both use your
  internal name, not the display name.
- **Values are live.** Read `handle.Value` at the point of use; don't cache it at
  startup, or menu changes won't apply.
- **Stable keys.** `key` is the save key and the loc leaf — renaming it orphans
  the stored value and loc terms.
- **Section id must be unique.** Two mods with the same `metadata.name` collide;
  the first registered wins.

---

## Building

Built with Unity and Pugstorm's Core Keeper Mod SDK (batchmode, with the Editor
closed).

## License

Personal-use, non-commercial. Not affiliated with Pugstorm.
