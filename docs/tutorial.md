# Mod Settings Menu — Tutorial

A hands-on, end-to-end guide to **Mod Settings Menu** (MSM) — the Core Keeper
framework mod that gives *other* mods an in-game settings screen under
**Options → Mod Settings**.

This tutorial has two halves, and you can read whichever you need:

- **Part I — Using it.** For mod authors who want to add a settings section to
  their own mod. Starts from zero and builds a complete example.
- **Part II — How it works.** For anyone maintaining or extending MSM itself, or
  curious how a mod clones a vanilla menu. Walks the code path from mod-load to
  a rendered, persisted widget.

> The repo's `README.md` is the **reference** (signatures, tables, look-ups).
> This file is the **tutorial** (a narrative you follow once). When in doubt
> about an exact signature, the README wins; when you want to understand *why*,
> come here.

Current framework version at time of writing: **1.1.0** (modId `6211950`).

---

## Table of contents

**Part I — Using it (consumer integration)**
1. [The 30-second mental model](#1-the-30-second-mental-model)
2. [Prerequisites & wiring](#2-prerequisites--wiring-one-time-per-mod)
3. [Your first section](#3-your-first-section-a-single-toggle)
4. [The four widgets](#4-the-four-widgets)
5. [Reading values (live) — and the adapter pattern](#5-reading-values-live--and-the-adapter-pattern)
6. [Persistence: where values live](#6-persistence-where-values-live)
7. [Localization](#7-localization-optional-but-nice)
8. [RequiresRestart & the EarlyInit/Init bake-time trap](#8-requiresrestart--the-earlyinitinit-bake-time-trap)
9. [Ordering options and sections](#9-ordering-options-and-sections)
10. [Pitfall checklist](#10-pitfall-checklist)

**Part II — How it works (internals)**
11. [Architecture at a glance](#11-architecture-at-a-glance)
12. [Bootstrap & lifecycle](#12-bootstrap--lifecycle-modsettingsmenumod)
13. [Mounting into the Options menu](#13-mounting-into-the-options-menu-menupatch)
14. [The settings screen](#14-the-settings-screen-modsettingsscreen)
15. [The universal widget](#15-the-universal-widget-settingwidget)
16. [Persistence internals](#16-persistence-internals-configstore--corelib)
17. [Localization internals](#17-localization-internals)
18. [PreWarm: the first-open freeze fix](#18-prewarm-the-first-open-freeze-fix)
19. [RequiresRestart internals](#19-requiresrestart-internals)
20. [The hard-won CK-UI gotchas](#20-the-hard-won-ck-ui-gotchas)

[Appendix A: API cheat-sheet](#appendix-a-api-cheat-sheet) ·
[Appendix B: glossary](#appendix-b-glossary)

---

# Part I — Using it

## 1. The 30-second mental model

Your mod **declares** a few settings in one place. MSM does everything else:
builds a labelled box of widgets in the Options menu, renders one row per
setting, and persists every value to a per-mod config file that restores on the
next launch. You never touch UI, prefabs, or `System.IO`.

The one thing that stays yours is the **translations for your own labels** — and
even those are optional: ship a localization term per setting and it's shown,
omit it and the menu falls back to the raw key (see §7). What you never wire is
the framework's *own* localization plumbing (the term look-up, the On/Off and
title strings, the per-widget render sites) — that's all MSM's.

```csharp
using ModSettingsMenu.Settings;
using PugMod;

public sealed class MyMod : IMod
{
    public static SettingHandle<bool> Enabled;

    public void Init()
    {
        ModSettings.Section(this)      // "this" identifies your mod
            .Toggle(out Enabled, "enabled", true)
            .Build();
    }
    // ... read MyMod.Enabled.Value wherever you need it.
    public void EarlyInit() {}
    public void ModObjectLoaded(UnityEngine.Object o) {}
    public void Shutdown() {}
    public void Update() {}
}
```

Three moving parts, and that is the whole surface:

| Concept | What it is |
|---|---|
| `ModSettings.Section(this)` | Begins a **section** (one box in the menu) for your mod. Returns a fluent builder. |
| `.Toggle/.Slider/.Stepper/.Choice(...)` | Declares one **setting** and hands you a typed `SettingHandle<T>` via `out`. |
| `SettingHandle<T>.Value` | The **live value**. Read it where you use it; it reflects menu edits immediately. |

Everything is declarative: the order you chain widgets is (by default) the order
they render, and each widget both *creates the UI row* and *binds the persisted
value* in one call.

---

## 2. Prerequisites & wiring (one-time per mod)

MSM is a **hard dependency** of your mod, and it needs **CoreLib** at runtime
(that is where the config file lives). Declare both in **two** places, or your
mod either won't compile or won't load.

### 2a. Runtime asmdef — so your code compiles against the API

In your mod's runtime `.asmdef`, add the references:

```json
{
  "name": "MyMod",
  "references": [
    "PugMod.SDK",
    "CoreLib",
    "ModSettingsMenu"
  ]
}
```

### 2b. ModBuilderSettings `.asset` — so the game refuses to load you without them

In your ModBuilderSettings `.asset` — the file carrying your mod's `metadata:`
block — edit its YAML directly; the `ModManifest.json` that ends up in the build
is generated from it:

```yaml
dependencies:
  - modName: CoreLib
    required: 1
  - modName: ModSettingsMenu
    required: 1
```

### 2c. mod.io — so subscribers get the dependencies automatically

When you publish, list **Mod Settings Menu** and **CoreLib** as dependencies of
your mod on mod.io, so subscribers get them pulled in automatically.

> **Why both asmdef *and* `.asset`?** They answer different questions. The asmdef
> is compile-time ("can my C# see the `ModSettings` type?"); the `.asset`
> dependency is load-time ("will Core Keeper start my mod if MSM is missing?" —
> no, and that's what you want, because your `Init` calls into MSM
> unconditionally).

---

## 3. Your first section (a single toggle)

Let's build a real, if tiny, example mod called **Glow Mod** (internal name
`GlowMod`, namespace `GlowMod`). It makes placed lights brighter, and we want an
in-game master switch.

```csharp
using ModSettingsMenu.Settings;
using PugMod;
using UnityEngine;

namespace GlowMod
{
    public sealed class GlowMod : IMod
    {
        public static SettingHandle<bool> Enabled;

        public void Init()
        {
            ModSettings.Section(this)
                .Hint("Brighter placed lights.")     // optional one-line subtitle
                .Toggle(out Enabled, "enabled", true)
                .Build();
        }

        public void EarlyInit() {}
        public void ModObjectLoaded(Object o) {}
        public void Shutdown() {}
        public void Update() {}
    }
}
```

What happens when the game starts:

1. `Section(this)` reads your mod identity from the `IMod` reference — the
   **section id / config folder** is `metadata.name` (`GlowMod`), the **heading**
   is `metadata.displayName` (falls back to `name`).
2. `.Toggle(out Enabled, "enabled", true)` binds a persisted boolean under the
   key `"enabled"` (default `true`) and hands you a `SettingHandle<bool>`.
3. `.Build()` registers the section.
4. MSM renders a box titled **Glow Mod** with a subtitle "Brighter placed
   lights." and one row: `enabled  [on]`.
5. The value is saved to `mods/GlowMod/config.cfg` on first run and every change.

To act on it, read `GlowMod.Enabled.Value` wherever your logic runs (a Harmony
prefix, an ECS `OnUpdate`, an `Update`, …):

```csharp
if (!GlowMod.Enabled.Value)
    return;   // master switch off → do nothing
```

That's a complete, shippable integration. Everything below is *more* widgets and
*more* polish.

---

## 4. The four widgets

Every widget method has the same shape: `out` handle first, then a stable
string `key`, then per-kind parameters. Each returns the builder so you chain
them. Let's grow Glow Mod to use all four.

```csharp
public sealed class GlowMod : IMod
{
    public static SettingHandle<bool>  Enabled;
    public static SettingHandle<float> Radius;
    public static SettingHandle<int>   MaxLights;
    public static SettingHandle<Tint>  Colour;

    public enum Tint { Warm, Neutral, Cool }

    public void Init()
    {
        ModSettings.Section(this)
            .Hint("Brighter placed lights.")
            // bool on/off:
            .Toggle(out Enabled, "enabled", true)
            // float min..max, default, step; Number shows the raw value ("4.0"):
            .Slider(out Radius, "radius", 1f, 8f, 4f, 0.5f, SliderDisplay.Number)
            // int ±1, clamped to [min,max]:
            .Stepper(out MaxLights, "maxLights", 1, 20, 6)
            // cycle a fixed set of any type T; token = value.ToString():
            .Choice(out Colour, "colour",
                    new[] { Tint.Warm, Tint.Neutral, Tint.Cool }, Tint.Neutral)
            .Build();
    }
    // ... IMod boilerplate ...
}
```

### Toggle
`Toggle(out SettingHandle<bool> h, string key, bool def)` → on/off. `←/→` or
click flips it.

### Stepper
`Stepper(out SettingHandle<int> h, string key, int min, int max, int def)` →
an integer you step by ±1 with `←/→`, clamped to `[min, max]`. Use it for small
integer counts (how many of something).

### Slider
`Slider(out SettingHandle<float> h, string key, float min, float max, float def, float step, SliderDisplay display = SliderDisplay.Steps)`
→ a float. `←/→` changes it by `step` (clamped). The `display` controls how the
current value is *shown*:

| `SliderDisplay` | Shows | Example (`min=1,max=8,step=0.5,val=4`) |
|---|---|---|
| `Steps` (default) | a `♦♦♦♢♢♢` chain of `(max-min)/step` segments | `♦♦♦♢♢♢♢♢♢♢♢♢♢♢` |
| `Number` | the raw value, dot separator, ≥1 decimal | `4.0` |
| `Percent` | **position in the range**, i.e. `(v-min)/(max-min)·100%` | `43%` |

> ⚠️ **`Percent` is position-in-range, not the raw number.** With `min=1, max=8`,
> a value of `4` shows `43%`, not `400%`. If you want a value to read as *its own*
> percent (e.g. a `0.25` factor → "25%"), the slider **must** be `min=0, max=1`
> so `(v-min)/(max-min) == v`. This bites people; pick `Number` unless you
> specifically want the range-fraction reading.

If you pass `step ≤ 0`, MSM treats the whole range as a single step.

### Choice&lt;T&gt;
`Choice<T>(out SettingHandle<T> h, string key, T[] values, T def)` → cycles a
fixed, ordered set with `←/→` (wraps around). `T` can be **anything**; MSM stores
`value.ToString()` as the persisted token.

```csharp
enum Tint { Warm, Neutral, Cool }
.Choice(out Colour, "colour", new[] { Tint.Warm, Tint.Neutral, Tint.Cool }, Tint.Neutral)
// Colour.Value is a Tint; the saved token is "Warm"/"Neutral"/"Cool".
```

- Prefer an **enum** — you get clean, self-documenting tokens for free (`"Warm"`
  reads better in the config file than `"0"`). `int[]` and `string[]` work too.
- The values must have **distinct** `ToString()`.
- An unknown/removed token in an old config falls back to `def`.
- Declaring `Choice` with an empty/null array logs a warning and degrades to the
  single default (it won't throw).

> **Choice vs Stepper vs Slider.** Use `Choice` for a small set of *named*
> presets (difficulty levels, tint names, a curated multiplier list like
> `[1,2,3,5,10]`). Use `Stepper` for a plain bounded integer. Use `Slider` for a
> continuous-ish float range.

---

## 5. Reading values (live) — and the adapter pattern

### `SettingHandle<T>` in two lines

```csharp
public T Value { get; set; }        // read = live; set = clamp + persist + raise OnChanged
public event Action<T> OnChanged;   // fires on menu edit, code set, or reload
```

**Read `Value` at the point of use — never cache it at startup.** It always
returns the current value, so a menu change applies immediately, no restart:

```csharp
// e.g. in a Harmony prefix:
if (!GlowMod.Enabled.Value) return;
float r = GlowMod.Radius.Value;
```

**Set `Value`** to change it from code; CoreLib clamps to the widget's range,
auto-saves, and raises `OnChanged`.

**`OnChanged`** is for the cases where polling isn't enough and you must *react*
at the moment of change — rebuild a cache, inject/remove a recipe, hide a HUD:

```csharp
public void Init()
{
    ModSettings.Section(this).Toggle(out Enabled, "enabled", true).Build();
    Enabled.OnChanged += on =>
    {
        if (on) InjectRecipe();   // idempotent
        else    RemoveRecipe();
    };
}
```

(That's the clean way to inject or remove something the moment the toggle flips
— adding/removing a crafting recipe, say — instead of polling `Value`.)

### The adapter pattern (recommended for non-trivial mods)

If your patches already read config from some object (a `ModConfig` singleton,
say), you don't have to thread `SettingHandle`s through them. Keep the same
`ModConfig.member` access your patches already use, and back the members with
handles. This is what the reference consumer **Faster Talents** does:

```csharp
// FasterTalentsMod.Init():
ModSettings.Section(this)
    .Hint("A faster talent-point curve plus a skill-XP boost - fill your talent trees sooner.")
    .Toggle(out var enabled, "enabled", true)
    .Choice(out var xp, "xpMultiplier", new[] { 1, 2, 3, 5, 10, 20, 50 }, 3)
    .Build();
ModConfig.Instance.Bind(enabled, xp);   // stash the handles for the patches
```

```csharp
// ModConfig — the adapter the patches read (field → property is source-compatible):
private SettingHandle<bool> _enabledHandle;
private SettingHandle<int>  _xpHandle;

public void Bind(SettingHandle<bool> enabled, SettingHandle<int> xp)
{ _enabledHandle = enabled; _xpHandle = xp; }

// Hardcoded fallback for the brief pre-Bind window at mod load:
public bool  enabled      => _enabledHandle != null ? _enabledHandle.Value : true;
public float xpMultiplier => _xpHandle      != null ? _xpHandle.Value      : 3f;
```

The patch code stays literally unchanged — it still reads
`ModConfig.Instance.xpMultiplier` — but the value now comes live from the menu.
The `!= null` fallback covers the window between mod load and `Bind` (and makes
the mod behave sanely even if, hypothetically, the framework were absent).

> **Tip:** a tidy shape is a single `ModConfig` adapter class in your mod's root
> namespace (a `ModConfig.cs`) that owns the handles; your patch code
> then keeps reading `ModConfig.member` completely unchanged.

---

## 6. Persistence: where values live

Each consumer gets its **own** CoreLib `ConfigFile` at `mods/<ModId>/config.cfg`
inside CoreLib's config filesystem, e.g.:

```
…/LocalLow/Pugstorm/Core Keeper/Steam/<accountId>/mods/GlowMod/config.cfg
```

```ini
[Settings]
enabled = true
radius = 4
maxLights = 6
colour = Neutral
```

- All settings land under the `[Settings]` section.
- Writes **auto-save immediately** — setting `handle.Value` or editing in the
  menu.
- The file is created on first run with your declared defaults.
- **No `System.IO` on your side.** CoreLib does all file access in its own
  trusted assembly, so your mod stays inside the RoslynCSharp sandbox — you do
  **not** need `skipSafetyChecks`.

Because the folder is `<ModId>` (= `metadata.name`), two mods never collide, and
you can inspect or hand-edit a config while the game is closed.

---

## 7. Localization (optional but nice)

Labels are localized through Core Keeper's own text system. MSM looks up a term
per UI element and **falls back to the raw key/token** when it's missing — so
localization is entirely optional. Skip it and the menu simply shows your keys
(`enabled`, `radius`, `Neutral`, …).

### The term scheme

| UI element | Loc term |
|---|---|
| Setting label | `<ModId>-Config/<key>` |
| Section hint | `<ModId>-Config/_hint` |
| `Choice` option label | `<ModId>-Config/<key>/<token>` |

The section **heading** is your `displayName`, rendered as-is — mod names are
proper nouns and are **not** localized. The Toggle on/off words and the "Mod
Settings" screen title are supplied by the framework
(`ModSettingsMenu-UI/On`, `/Off`, `/Title`) — you don't localize those.

### Authoring the terms

Core Keeper keys every localization `TextDataBlock` by `header + "/" + name`, so
a term is exactly `<header>/<name>`. Ship **one `TextDataBlock` per term** —
authored with whatever localization workflow you use — packed into your mod's
AssetBundle. Each block carries the per-language strings (English, German, ...).

For Glow Mod that is one block per label/hint, plus one per `Choice` option:

| Term | `TextDataBlock` header | `TextDataBlock` name |
|---|---|---|
| `GlowMod-Config/_hint` | `GlowMod-Config` | `_hint` |
| `GlowMod-Config/enabled` | `GlowMod-Config` | `enabled` |
| `GlowMod-Config/radius` | `GlowMod-Config` | `radius` |
| `GlowMod-Config/colour` | `GlowMod-Config` | `colour` |
| `GlowMod-Config/colour/Warm` | `GlowMod-Config/colour` | `Warm` |
| `GlowMod-Config/colour/Neutral` | `GlowMod-Config/colour` | `Neutral` |
| `GlowMod-Config/colour/Cool` | `GlowMod-Config/colour` | `Cool` |

**The one rule that trips people up:** a `Choice` option term has a *second*
slash (`<key>/<token>`), and a term is `header/name` — so the token cannot be a
`name` on its own. Make the **setting term** (`GlowMod-Config/colour`) the
`header` and the **bare token** (`Warm`) the `name`; `header + "/" + name` then
reproduces the exact term the widget looks up. (`int` tokens work the same way:
header `GlowMod-Config/xpMultiplier`, names `1`, `2`, `50`, …)

Sliders and Steppers render numerically, so they need **no** per-option terms —
only a label term.

> **Dev-loop caveat (development only, not published updates).** Core Keeper
> merges mod loc into the game-wide `Localization.csv` (its I2 source)
> **first-write-wins**. Once a term is exported, *changing* its text in a
> rebuilt bundle does **not** refresh the CSV row unless the mod's *version*
> changes. So while iterating on loc against the same local build, delete (or
> back up + edit) the stale `<ModId>-Config/*` rows in the game's
> `localization/Localization.csv` so the new text takes. A real mod.io update
> (new version) is unaffected.

---

## 8. RequiresRestart & the EarlyInit/Init bake-time trap

Most settings apply **live** — you read `handle.Value` every frame/tick, so a
menu change takes effect at once. But some values are only ever read **once, at
load** — e.g. a recipe cost baked into the database at world load. Changing such
a setting mid-session does nothing until the next launch, and the player should
be told.

### Marking a setting restart-required

Chain `.RequiresRestart()` **right after** the widget it applies to:

```csharp
ModSettings.Section(this)
    .Choice(out reduction, "reductionFactor",
            new[] { Reduction.OneIngot, Reduction.Quarter, Reduction.Half, Reduction.Vanilla },
            Reduction.Quarter)
    .RequiresRestart()          // marks the setting just declared
    .Build();
```

When a player *actually changes* a marked setting and then leaves the Mod
Settings screen, MSM raises Core Keeper's **own** "restart to apply mod changes"
popup (Cancel / Yes → relaunch) — the same dialog the game shows when your mod
subscriptions change. No dialog or localization of your own; it's CK's shipped
term in every language. (A clamped no-op — e.g. skimming a slider already at its
bound — does *not* trigger it; MSM compares before/after.)

### ⚠️ The trap: bake-time settings must bind in `EarlyInit`, not `Init`

Core Keeper's lifecycle order is:

```
(all mods) EarlyInit  →  DB/world conversion (PugDatabasePostConverter.PostConvert)  →  (all mods) Init  →  first Update
```

If your setting is consumed **during that conversion** (a recipe rewrite, an
object-data bake), you **must** register the section **and** `Bind` its handle
in `IMod.EarlyInit`. Binding in `Init` is too late: the bake runs first, your
handle is still null, so the bake reads your **hardcoded default** — and an
idempotency guard can lock that in *permanently, even across restarts*. This is
a real bug a bake-time consumer hit until it moved its binding to `EarlyInit`.

`API.ConfigFilesystem` is initialised before any mod's `EarlyInit`, so the
persisted value is already loadable there — `EarlyInit` binding works.

Rule of thumb:

| Your setting is read… | Register/Bind in | RequiresRestart? |
|---|---|---|
| every frame / tick / grant (live) | `Init` | no |
| once, during DB/world bake | `EarlyInit` | yes (`.RequiresRestart()`) |

---

## 9. Ordering options and sections

- **Sections (boxes)** always render **alphabetically by display name** — a
  stable, findable order regardless of which mod loaded first. You don't control
  this (and shouldn't need to).
- **Options within your box** default to **declaration order** (your builder
  chain = author intent). Opt into a sort with `.SortOptions(...)`:

```csharp
ModSettings.Section(this)
    .SortOptions(OptionSort.ByLabel)   // AsDeclared (default) | ByKey | ByLabel
    .Toggle(...).Slider(...).Build();
```

| `OptionSort` | Orders options by |
|---|---|
| `AsDeclared` (default) | your builder-chain order |
| `ByKey` | the raw `key` string |
| `ByLabel` | the **localized** label — so it re-sorts per active language |

---

## 10. Pitfall checklist

Before you ship, verify:

- [ ] **Wiring in both places** — asmdef `references` *and* `.asset`
      `dependencies` list `CoreLib` + `ModSettingsMenu`; mod.io deps set too.
- [ ] **`Init` vs `EarlyInit`** — live values in `Init`; bake-time values (with
      `.RequiresRestart()`) registered *and* bound in `EarlyInit`.
- [ ] **Read `Value` at point of use** — never cache it at startup, or menu
      changes won't apply.
- [ ] **Stable keys** — `key` is both the save key and the loc leaf. Renaming it
      orphans the saved value and the term.
- [ ] **Unique section id** — two mods with the same `metadata.name` collide;
      first registered wins (later ones are ignored with a warning).
- [ ] **Slider `Percent`** — remember it's position-in-range; use `Number` unless
      you set `min=0,max=1`.
- [ ] **Unquoted loc leaf keys** — `10:` not `"10":`; Choice options under their
      own header.
- [ ] **Distinct `Choice` tokens** — `ToString()` must be unique (prefer enums).

---

# Part II — How it works

This half traces the code from mod-load to a rendered, persisted widget. The
central design choice is that MSM does **not** build a menu from scratch — it
**clones Core Keeper's own `UISettings` menu** and swaps its own component in,
inheriting all of CK's scroll/navigation/look machinery. That's what keeps the
consumer API tiny and the visuals native.

## 11. Architecture at a glance

```
        mod load
          │
   ┌──────▼───────────────┐         Harmony patches (auto-discovered)
   │ ModSettingsMenuMod    │        ┌───────────────────────────────┐
   │ (IMod bootstrap)      │        │ MenuPatch                     │
   │  EarlyInit: grab      │        │  MenuManager.Init  (prefix):  │
   │   AssetBundle         │        │   clone the Options "UI"      │
   │  ModObjectLoaded:     │        │   entry → point at our MenuType│
   │   grab MenuPrefab     │        │  MenuManager.Init (postfix):  │
   │  Update: PreWarm +    │        │   instantiate MenuPrefab      │
   │   deferred restart    │        │  RadicalMenu.TypeToMenu:      │
   └──────┬───────────────┘         │   our id → our instance       │
          │                         └───────────────┬───────────────┘
          │ registry                                 │
   ┌──────▼───────────────┐                  ┌──────▼───────────────┐
   │ ModSettings (static)  │  Sections        │ ModSettingsScreen     │
   │  Section(this) →       │─────────────────▶│ : RadicalMenu,        │
   │   SectionBuilder       │                  │   IScrollable         │
   │  Register(section)     │                  │  Activate→Populate→   │
   └──────┬───────────────┘                  │   base.Activate→Render │
          │ per-widget                        │  builds one SectionBox │
   ┌──────▼───────────────┐                  │  + N SettingWidget rows│
   │ SectionBuilder         │  binds           └──────┬───────────────┘
   │  .Toggle/.Slider/...   │  ConfigEntry            │ per row
   │   → SettingHandle<T>   │                  ┌──────▼───────────────┐
   └──────┬───────────────┘                  │ SettingWidget          │
          │                                    │ : RadicalMenuOption    │
   ┌──────▼───────────────┐  ConfigFile        │  Adjust → BoxedValue   │
   │ ConfigStore → CoreLib │◀───────────────── │  ValueString per kind  │
   │  mods/<Id>/config.cfg  │  read/write       └───────────────────────┘
   └───────────────────────┘
```

Two data structures connect the halves:

- **`ModSection` / `SettingDef`** (in `SettingModel.cs`) — the non-generic
  descriptors the UI reads. A `SettingDef` carries the widget `Kind`, numeric
  bounds, the derived loc `Term`, and the live `ConfigEntryBase Entry`.
- **`SettingHandle<T>`** — the typed façade the *consumer* holds; delegate-backed
  so it can front either a `ConfigEntry<T>` directly or a token-mapped
  `ConfigEntry<string>` (for `Choice<T>`).

---

## 12. Bootstrap & lifecycle (`ModSettingsMenuMod`)

`ModSettingsMenuMod : IMod` is the entry point the loader instantiates. Its job
is three lifecycle hooks plus a per-frame `Update`:

- **`EarlyInit`** — grabs the mod's own `AssetBundle` (`GetModInfo().AssetBundles[0]`).
  That bundle holds the menu prefab and the framework's loc `TextDataBlock`s.
- **`ModObjectLoaded(obj)`** — the loader raises this for each asset in the
  bundle; MSM keeps the `GameObject` that has a `ModSettingsScreen` component —
  that's `MenuPrefab`.
- **`Update`** — does two one-shot / deferred jobs:
  1. **PreWarm** the menu once, on the first frame the instance exists and there
     is at least one consumer section (see §18).
  2. **Deferred restart prompt** — a frame countdown that fires
     `ModSettingsScreen.ShowRestartPrompt()` a few frames after a
     `RequiresRestart` change (see §19).

It also owns a constant you'll see referenced everywhere:

```csharp
// A free id outside the vanilla RadicalMenu.MenuType enum (distinct from GMCM/HealthBars):
public const RadicalMenu.MenuType SettingsMenuType = (RadicalMenu.MenuType)29314;
```

Casting an arbitrary int to the `MenuType` enum is how a mod adds a *new* menu id
without the game knowing about it — the patches below teach CK to resolve it.

---

## 13. Mounting into the Options menu (`MenuPatch`)

`MenuPatch` is a `[HarmonyPatch]` class (auto-discovered — no `PatchAll()`). It
hooks two methods.

### `MenuManager.Init` **prefix** — add the entry

CK's Options menu contains push-menu entries ("Go to UI settings", …). The
prefix finds the one that pushes `UI_OPTIONS`, **clones it**, repoints the clone
at our `SettingsMenuType`, and inserts it right after the original:

```csharp
var uiEntry = Array.Find(pushOptions, x => x.menuToPush == RadicalMenu.MenuType.UI_OPTIONS);
var entry = Object.Instantiate(uiEntry.transform);   // clone PARENTLESS first…
entry.SetParent(uiEntry.transform.parent);           // …then parent it
entry.SetSiblingIndex(uiEntry.transform.GetSiblingIndex() + 1);
entry.name = "GoToModSettings";
SetEntryLabel(entry.gameObject.GetComponentInChildren<PugText>());
entry.GetComponent<RadicalOptionsMenuOption_PushMenu>().menuToPush = ModSettingsMenuMod.SettingsMenuType;
```

Two subtle things here — both cost real debugging time and are worth internalizing:

- **Clone parentless, *then* `SetParent`.** `Instantiate(go, parent)` activates
  the clone *mid-clone* and fires `OnEnable`/`ResetEffect` before the inner
  `PugText` is fully cloned → NRE. A parentless clone finishes cloning first;
  parenting then activates it cleanly.
- **Set the label with `SetText`, NOT `Render`** (the "red twin" bug). The
  Options entries live on the **shared** `optionsMenuPrefab`, which this prefix
  mutates. `PugText.Render` bakes glyph `SpriteRenderer`s into that prefab;
  CK's `InstantiateMenu` then clones them as **orphaned** renderers the live
  `PugText` never tracks or clears — a frozen duplicate label. `SetText` only
  sets `textString` (0 glyphs), leaving the prefab a clean template that the live
  instance renders fresh. See §20.

### `MenuManager.Init` **postfix** — instantiate our screen

```csharp
var menu = Object.Instantiate(prefab, Manager.camera.uiCamera.transform)
                 .GetComponent<ModSettingsScreen>();
menu.gameObject.SetActive(false);
MenuInstance = menu;
```

The prefab is instantiated under the **UI camera** transform — identical to how
vanilla `MenuManager` mounts its menus — and kept inactive until opened.

### `RadicalMenu.TypeToMenu` **prefix** — resolve our id

CK maps a `MenuType` to a `RadicalMenu` instance via `TypeToMenu`. Since our id
isn't in the vanilla enum, we intercept and return our instance:

```csharp
if (type == ModSettingsMenuMod.SettingsMenuType) { __result = MenuInstance; return false; }
return true;   // everything else → vanilla
```

Now clicking "Mod Settings" pushes our menu exactly like any built-in submenu.

---

## 14. The settings screen (`ModSettingsScreen`)

`ModSettingsScreen : RadicalMenu, IScrollable` **is** the adapted vanilla
`UISettings` prefab — the imported prefab has this component swapped in where
CK's `RadicalOptionsMenu` used to be. By subclassing `RadicalMenu` we inherit
CK's open/close, navigation, and layout; by implementing `IScrollable` we plug
into CK's scroll window.

### The open sequence

```csharp
public override void Activate()
{
    RestartPending = false;   // fresh visit
    Populate();               // build structure + fill menuOptions
    base.Activate();          // RadicalMenu opens → hierarchy goes active
    RenderContent();          // NOW render layouts (heights are real)
}
```

Why three steps and not one? **`LinearLayout` skips children while the hierarchy
is inactive** — their heights compute as 0. So we *build* before `base.Activate`
(so options exist and are navigable) but *render the layouts* after (so the boxes
size to real text heights). Splitting build from render is the whole trick to
getting correct box sizing.

### `Populate` — build from the registry

Per open (because the vanilla `PugText`s free their glyphs on disable, so a
once-only build shows empty on reopen), `Populate`:

1. Renders the title, deactivates the template objects.
2. **Detaches** old section roots *before* `Destroy` (Destroy is deferred to
   end-of-frame; a still-present old section would be counted by the layout this
   frame and push the fresh ones off-screen).
3. Sorts a **local copy** of `ModSettings.Sections` alphabetically by
   `DisplayName` (the registry keeps insertion order).
4. For each section: instantiate the `sectionTemplate` (→ a `SectionBox`), render
   its heading + optional hint, then for each `SettingDef` (in `OptionSort`
   order) instantiate the `toggleTemplate`, `Bind` a `SettingWidget` to the def,
   nest it into the box's `widgetContainer`, and add it to `menuOptions` for
   keyboard nav.
5. Wire the scroll window (`scrollingContent = contentRoot; ResetScroll()`).

### `RenderContent` — inner-to-outer

After activation, layouts are rendered **innermost first** so each parent
measures real child heights: the widgets box → the heading sub-group → the
section root → the top layout. Content *position* is deliberately **not** set
here — `UIScrollWindow` owns `scrollingContent.localPosition.y` every
`LateUpdate`, so any anchor set here is overwritten the same frame.

### `SectionBox`

A tiny `MonoBehaviour` on the section template that exposes `header`, `hint`, and
`widgetContainer` as **serialized references**. The screen wires them by
reference (robust) instead of by `Find()` path (fragile). The `widgetContainer`
is a `LinearLayout` with a 9-slice border background that auto-sizes to the rows
nested into it — that's the visible box.

---

## 15. The universal widget (`SettingWidget`)

One class renders **all four** kinds. `SettingWidget : RadicalMenuOption` so it
joins menu navigation; the base `Awake` auto-assigns `labelText` ("Label" child)
and `valueText` ("Value" child).

### Type-agnostic read/write via `BoxedValue`

The widget never sees `T`. It drives the value through the non-generic
`ConfigEntryBase.BoxedValue`, casting per `Kind`:

```csharp
private void Adjust(int dir)
{
    var e = _def.Entry;
    var before = e.BoxedValue;                 // for RequiresRestart change-detect
    switch (_def.Kind)
    {
        case SettingKind.Toggle:  e.BoxedValue = !(bool)e.BoxedValue; break;
        case SettingKind.Stepper: e.BoxedValue = Mathf.Clamp((int)e.BoxedValue + dir, (int)_def.Min, (int)_def.Max); break;
        case SettingKind.Slider:  e.BoxedValue = Mathf.Clamp((float)e.BoxedValue + dir*_def.Step, _def.Min, _def.Max); break;
        case SettingKind.Choice:  /* IndexOf the current token, step, wrap, write toks[next] */ break;
    }
    if (_def.RequiresRestart && !object.Equals(before, e.BoxedValue))
        ModSettingsScreen.RestartPending = true;
    Refresh();
}
```

Every write goes through CoreLib, which **clamps** to the `AcceptableValue*`
attached at bind and **auto-saves**. That's why the API never has to re-validate
ranges — CoreLib is the single choke point. `←/→` call `OnSkimLeft/Right`;
click/Space call `OnActivated` → `Adjust(+1)` (step forward, like CK's stepper).

### Per-kind value display (`ValueString`)

- **Toggle** → `Loc.T("ModSettingsMenu-UI/On"|"/Off")`.
- **Stepper** → the int.
- **Choice** → `Loc.T(term + "/" + token, token)` (localized option, falls back
  to the token).
- **Slider** → `Number` / `Percent` / `Steps` as in §4. The `Steps` `♦/♢` chain is
  special: **those glyphs only exist in the `boldLarge` font atlas**, not
  `thinMedium` (which renders `?`). `Bind` switches a Steps-slider's *value* font
  to `boldLarge` accordingly — one runtime tweak because a single shared template
  can't encode a per-kind font. In the source those glyphs are written as the
  unicode escapes `'\u2666'` / `'\u2662'` (keeping the file pure-ASCII) — a
  *literal* diamond char is encoding-unsafe in the Roslyn sandbox, the same class
  of bug as the `'ö'` issue in `PugFont`.

---

## 16. Persistence internals (`ConfigStore` + CoreLib)

`ConfigStore` is a `Dictionary<modId, ConfigFile>` cache. The first time a
consumer's section is built, it creates one CoreLib `ConfigFile`:

```csharp
file = new ConfigFile($"{modId}/config.cfg", saveOnInit: true, info);
```

- **`info`** is the consumer's `LoadedMod` (from `GetModInfo()`), which scopes the
  file to that mod's config folder.
- **CoreLib does all `System.IO`** in its own trusted assembly via
  `API.ConfigFilesystem`, so MSM (and the consumer) stay sandbox-clean — no
  `skipSafetyChecks`. This is the reason the framework can persist at all from
  inside the RoslynCSharp sandbox.
- **`saveOnInit`** writes the file with defaults on first run; `SaveOnConfigSet`
  (CoreLib default) makes every subsequent `handle.Value = …` persist immediately.

Each `SectionBuilder.<widget>` calls `_file.Bind("Settings", key, def, desc)` →
a `ConfigEntry`. The `desc` carries the `AcceptableValueRange`/`AcceptableValueList`
that gives CoreLib the clamp bounds. For `Choice<T>`, the entry is a
`ConfigEntry<string>` (the token) plus two mapping delegates so the consumer's
`SettingHandle<T>` still reads/writes `T`.

---

## 17. Localization internals

`Loc` is four lines:

```csharp
public static string T(string term)           => API.Localization.GetLocalizedTerm(term) ?? term;
public static string T(string term, string fb) => API.Localization.GetLocalizedTerm(term) ?? fb;
```

`GetLocalizedTerm` returns `null` for an unregistered term, so framework strings
use `T(term)` (its own shipped term always resolves) and consumer strings use
`T(term, fallback)` — falling back to the raw key/token when the consumer ships
no term. That's the mechanism behind "localization is optional."

MSM's own terms are compiled into `TextDataBlock` assets and packed into its
AssetBundle at build (a consumer does the same for its labels — see §7). At
runtime Core Keeper merges those blocks into its game-wide `Localization.csv` (its
I2 source) — **first-write-wins**, which is the dev-loop caveat from §7.

MSM ships only its own three UI terms — `ModSettingsMenu-UI/Title`, `/On`, `/Off`
(English + German).

---

## 18. PreWarm: the first-open freeze fix

Symptom: the **first** open of the menu froze up to ~1 s (worse on slower
setups); every later open
was instant. Measured (per-phase `Time.realtimeSinceStartup` logs around
`Activate`): **~98 % of the time sits inside the instance's first
`gameObject.SetActive(true)` `OnEnable` cascade** — first-time AssetBundle asset
load / shader-variant compile. One-time per session, and **not** shared with
vanilla menus (opening the identical-font vanilla `UISettings` first did *not*
warm ours → the cost is instance-specific, not a global font atlas).

Crucially, "build the structure at startup" was measured **useless** (instantiate
= 1.3 ms) — the expensive thing is the *enable cascade*, not the build. So the
lever is **pre-warm, not pre-build**:

```csharp
public void PreWarm()
{
    Populate();
    gameObject.SetActive(true);
    gameObject.SetActive(false);   // same frame → OnEnable runs, no frame is rendered
}
```

`ModSettingsMenuMod.Update` calls this once on the first frame `MenuInstance`
exists **and** there is ≥1 consumer section (so a MSM install with no consumers
doesn't spend 1 s at startup for nothing). Deliberately **not**
`RadicalMenu.Activate()` — that would toggle the HUD, play SFX, and push onto the
menu stack. The ~1 s is synchronous *inside* `SetActive(true)`, so a same-frame
`SetActive(false)` pays the cost with no visible flash. First real open dropped
**1039 ms → 15.7 ms**. Known limit: a section a consumer registers *after* the
first Update frame isn't pre-warmed (consumers register in `Init`, which is
before the first Update, so this is graceful degradation, not a real gap).

---

## 19. RequiresRestart internals

The dirty flag lives on the screen: `SettingWidget.Adjust` sets
`ModSettingsScreen.RestartPending = true` when a `RequiresRestart` setting's
`BoxedValue` **actually** changes (the before/after compare skips clamped
no-ops). `Activate` resets it (so only this-visit changes count), and
`Deactivate(bool pop)` — RadicalMenu's leave hook — consumes it.

But `Deactivate` **must not** show the popup synchronously:

> ⚠️ **Re-entrancy trap.** `StartNewDisplaySequence` calls
> `Manager.menu.ShowPopUpMenu(options)` → `PushMenu(POP_UP)` — a menu-stack
> *push*. `Deactivate` runs *inside* the stack *pop* that triggered it, so
> pushing there re-enters the stack mid-pop → the popup never pops and its
> Cancel/Yes buttons **orphan across every later menu** (they persist into the
> main menu). Editor build is clean; this only shows in-game.

The fix mirrors CK's own restart flow (which uses
`Invoke("RestartToApplyModChanges", 0.1f)` — that delay is the tell): defer the
show **off** the `Deactivate` call stack. `Deactivate` calls
`ModSettingsMenuMod.RequestRestartPrompt()`, which sets a 3-frame countdown;
`Update` fires `ShowRestartPrompt()` once the stack has settled:

```csharp
internal static void ShowRestartPrompt()
{
    Manager.menu.centerPopUpText.StartNewDisplaySequence(
        "Menu/RestartToApplyModChanges", null, menuInputCooldown: true, 0f, 1.5f,
        useUnscaledTime: true, 0f, 1f, localize: true, TextManager.FontFace.boldMedium,
        response => { if (response.IsConfirm) Manager.platform.Restart(); },
        new List<string> { "cancelDialogue", "yes" }, 10f, 0.8f, 0, 20f);
}
```

Reusing CK's shipped `Menu/RestartToApplyModChanges` term means the dialog is
localized in every language for free, and `Manager.platform.Restart()` is CK's
real relaunch — identical to the game's own mods-changed prompt. This whole
recipe (found in `Pug.Other`'s `ModChanged`/`RestartToApplyModChanges`) is a
reusable pattern for any mod that needs CK's restart dialog.

---

## 20. The hard-won CK-UI gotchas

Adapting a vanilla `UISettings` prefab into a mod AssetBundle surfaced a series
of non-obvious traps. Each was verified in-game. If you touch the prefab or the
render path, keep these in mind:

- **The UI camera z-sorts transparent sprites — not by `sortingOrder`.** Glyphs
  at `sortingOrder=9999` were still occluded by chrome at `z<=0`. Push chrome
  *behind* the text plane in the prefab (`z += 2`). Camera at `z=-10` looks `+z`,
  so **smaller z = nearer = front**.
- **`SpriteMask` needs the built-in material.** The AssetRipper-imported
  `ViewportMask` had a placeholder sprite *and* material
  (`0000000deadbeef15deadf00d0000000`). Use Unity's built-in **Sprites-Mask**
  (`fileID 10758, guid 0000000000000000f000000000000000`). And the mask's **scale
  is its size** — a mask calibrated for another sprite clipped nothing (whole
  screen "inside mask"); scale it to the viewport.
- **A custom shader ignores the SpriteRenderer tint in a bundle.** A copied
  custom material's shader did not apply `m_Color` (sprite rendered full-bright).
  For a tinted sprite use built-in **Sprites-Default** (`fileID 10754`). This is
  why the darkening backdrop rendered bright until switched.
- **`VisibleInsideMask` glyphs are invisible with no active mask.** PugText glyphs
  inherit `style.maskInteraction=VisibleInsideMask`; the scroll viewport's mask is
  what makes them visible *and* clips them. Title/chrome stay `maskInteraction=0`
  so they never clip — which is why no explicit sorting band is needed.
- **`activeInTitle`.** `RadicalMenuOption.GetActiveStateInCurrentScene()` returns
  INACTIVE in the title screen for options cloned from in-game-only entries. The
  widget overrides it to ACTIVE **but gated on binding** (`_def != null`), so the
  unbound template row stays hidden (else it shows as a phantom row).
- **The Editor reserializes prefabs on save.** Opening the Unity Editor and
  letting it save **overwrites hand-authored prefab YAML edits** (it has reset
  background active/z and deleted objects). Make prefab edits with the Editor
  **closed**, and confirm "editor zu" before mutating prefab files.
- **The "red twin" (see §13).** Rendering a `PugText` on the shared
  `optionsMenuPrefab` bakes orphaned glyph renderers into every clone → a frozen
  duplicate label, exposed when relocalization made the live label render a
  different language than the frozen glyphs. Fix: `SetText` (0 glyphs), never
  `Render`, on a shared prefab template.

---

## Appendix A: API cheat-sheet

```csharp
using ModSettingsMenu.Settings;

// Begin — in IMod.Init (or EarlyInit for bake-time settings):
SectionBuilder b = ModSettings.Section(this);

// Section-level:
b.Hint(string text);                    // optional subtitle (localizable via <ModId>-Config/_hint)
b.SortOptions(OptionSort mode);         // AsDeclared (default) | ByKey | ByLabel

// Widgets (each: out handle, stable key, per-kind params) — chainable:
b.Toggle (out SettingHandle<bool>  h, string key, bool def);
b.Slider (out SettingHandle<float> h, string key, float min, float max, float def, float step,
          SliderDisplay display = SliderDisplay.Steps);   // Steps | Number | Percent(=position-in-range)
b.Stepper(out SettingHandle<int>   h, string key, int min, int max, int def);
b.Choice (out SettingHandle<T>     h, string key, T[] values, T def);   // token = value.ToString()

b.RequiresRestart();                    // marks the LAST-declared setting restart-required
b.Build();                              // registers the section

// Handle:
T   value = h.Value;                    // live read
h.Value   = something;                  // set → clamp + persist + OnChanged
h.OnChanged += v => { /* react */ };
```

**Conventions** — the naming and storage the calls above don't spell out:

| Loc term | Localizes |
|---|---|
| `<ModId>-Config/<key>` | a setting label |
| `<ModId>-Config/_hint` | the section hint |
| `<ModId>-Config/<key>/<token>` | a `Choice` option |

**Persistence:** `mods/<ModId>/config.cfg`, section `[Settings]`.
Throughout, `<ModId>` = your `metadata.name`.

## Appendix B: glossary

| Term | Meaning |
|---|---|
| **Consumer** | A mod that declares settings through MSM. |
| **Section** | One consumer's box in the menu (heading + hint + widgets). |
| **`SettingDef`** | Non-generic descriptor of one setting (kind, bounds, term, live entry). |
| **`SettingHandle<T>`** | Typed value façade the consumer holds (`.Value`, `.OnChanged`). |
| **`ConfigEntry` / `ConfigFile`** | CoreLib's persisted-value / config-file types. |
| **Token** | A `Choice` value's `ToString()` — the persisted + loc-leaf string. |
| **PreWarm** | Paying the one-time first-enable cost at load instead of first open. |
| **RadicalMenu / RadicalMenuOption** | CK's base classes for a menu screen / a menu row. |
| **`MenuType`** | CK's enum id for a menu; MSM casts a free int (`29314`) for its own. |
