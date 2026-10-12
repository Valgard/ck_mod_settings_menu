# Design — A consumer that runs without MSM installed (MSM-38)

- **Date:** 2026-10-12
- **Status:** DRAFT, provisional. Nothing here is decided. It collects
  everything the 2026-10-11/12 sessions established about MSM-38, the three
  platform probes measured on 2026-10-12, and a direction with its open
  questions. The direction is the user's (three requirements, an SDK package).
  Revised the same day after a three-lane spec review (claims against the
  code, the requirements, internal coherence); every finding was checked at
  the source before it changed a sentence.
- **Roadmap point:** MSM-38. The roadmap section was revised on 2026-10-11
  (`2e996d0`) and is partly out of date against §3, §4, §7 and §11 — list in
  §13.
- **Depends on:** MSM-19 (server sync), for the value path in §6.3 — see §8.
- **Builds on:** ADR-001 (no server-sync reimplementation, contradicted by
  MSM-19), ADR-012 (consumer-declared access level, MSM-18).

> **Evidence levels.** Each platform statement says how it is known:
> *measured* (run in the game on this machine, 2026-10-12, CK 1.3.0.6 under
> CrossOver), *read* (decompile `1.3.0.6-6e73` or SDK source, not run), or
> *third-party* (a claim in someone else's source, not re-measured). Decompile
> line numbers are lines of `<Assembly>.decompiled.cs` in the client tree.

## 1 · The problem

Every MSM consumer references `ModSettingsMenu.dll` in its asmdef and declares
MSM as a required dependency. A player who wants one mod's settings screen must
install MSM, and a player without MSM cannot load that mod at all. For a
settings framework this is the largest cost of adoption: an author weighs a
mandatory second download against having no menu.

The cost is not hypothetical. Sharpybara (the author of thirteen of the mods
discussed below; pronouns unknown, written "the proxy's author" here) built a
separate mod, **Mod Settings Proxy**, precisely to avoid it, and asked on
2026-10-02 whether MSM would take the approach on. The same day the author
agreed to it going into MSM 2.0, offered to help work out the details, and
stated the intent to move those mods onto MSM and archive the proxy once MSM
covers the need.

There is no urgency. The proxy works today, so no author is blocked; MSM-38
fixes where the contract lives and how its handling code drifts, which is
worth doing but can wait for the right order (§8).

## 2 · Requirements

The user's words, verbatim (UTC):

| # | When | Requirement | Kind |
|---|---|---|---|
| R1 | 2026-10-11 21:00 | „MSM soll soweit ich weiß nur ein JSON-Vertrag der Mod für MSM sein und nicht der Speicher der Werte der Optionen.“ | The contract describes the schema; it is not the value store. Hedged („soweit ich weiß“); not recorded anywhere before, and the 2026-10-02 instruction was only to take the approach into v2. Asked whether to record it as a decision, the user gave no explicit answer; the direction adopted it all the same. |
| R2 | 2026-10-11 21:05 | „Okay, also gut vom Benutzer lesbar.“ | The `.cfg` being readable by a player is a value worth keeping. |
| R3 | 2026-10-11 21:07 | „Dezimalzahlen in den CFGs sollten generell immer in englischer Notation (mit Punkt) sein. So wie es bei Config-Dateien üblich sind.“ | Rule for every CFG, values and comments. Filed as CoreLib issue #54 (§9). |
| R4 | 2026-10-11 21:53 | „Ich würde es ja bevorzugen, das jede Consumer-Mod seinen MSM-Vertrag in seinem Mod-Confiig-Ordner hat und nicht unter ModSettings.“ | The contract lives with the mod, not in a shared folder. Stated as a preference („bevorzugen“), like R5 and R6 in the same message; treated as requirements since. See the note below. |
| R5 | same | „Zudem sollte der Vertrag "statisch" bei der Auslieferung der Mod da sein und nicht erst dynamisch beim Ausführen erstellen.“ | The contract ships with the mod; nothing writes it at runtime. |
| R6 | same | „Was mich mittlerweile auch stört, dass jede Mod den Code für das Handeln des Vertrags mit im eigenen Code braucht und nicht irgendwo zentral verwaltet wird. Dadurch ist die Gefahr des triffts des Codes sehr hoch.“ | The code that handles the contract is maintained in one place, not copied into each mod. |
| R7 | 2026-10-11 22:01 | „Können wir nicht einfach ein Package für das SDK anbieten, das den Code für die Mods bereitstellt, aber keine Abhängigkeit an MSM verlangt?“ | The user's proposal: an SDK package, no runtime dependency on MSM. Became the direction (§6). |
| R8 | 2026-10-11 22:08 | „Jeder CK Mod Author muss mit dem SDK arbeiten, soweit ich weiß. Ich kenne keinen anderen Weg für CK-Mods.“ | Premise for R7. True in practice; the loader does not enforce it (§4.8). |
| R9 | 2026-10-11 21:59 | „Das würde aber die alten CFGs verwerfen, richtig?“ | Implied requirement: a mod moving to the contract keeps its players' values. |
| R10 | 2026-10-12 06:28 | „ERst die MSM38-Proben, dann die (vorläufige) Spec für MSM-38, damit alles aus der Session (und zwar wirklich alles, von Anfang der Session und nicht nur vom letzten Compaction-Punkt) dazu in der Spec landet.“ | The instruction this document answers: probes first, then a provisional spec holding everything the sessions established. Peripheral record in §14. |

**On R4's wording.** "Mod-Config-Ordner" read literally is the mod's folder
under the shared `mods/` root, which is written at runtime and cannot hold a
file that ships with the mod. A shipped file can only live in the mod's
*install package*. The direction therefore puts the contract in the package's
`Conf/` (§6.1) and keeps the values in the config folder. The user did not
object when this was explained; it should still be confirmed.

**R6 is the second place the design departs from the words.** R6 asks that the
handling code not sit in each mod's own code. The package (§6.3) maintains it in
one place, but still compiles a copy into every consumer, and a published mod
keeps the copy it was built with. Whether "central source, per-mod binary" is
what „zentral verwaltet“ meant needs confirming as well.

## 3 · Probe results (measured 2026-10-12)

The roadmap listed three things "to check before the spec". A scratch build of
MSM `v2` (`2e996d0`) plus two probe files was installed as the fake-ID dev
build; the game was started twice. Raw log kept outside the repository.

### 3.1 Files an `IPugModBuilderProcessor` adds

One probe processor added four kinds of file during `ModBuilder.BuildMod`:

| Route | Shipped in package | Listed in `ModManifest.json` | Compiled / readable in game | Left behind in the mod repo |
|---|---|---|---|---|
| A — `.cs` written into the mod folder and appended to `assetPaths` | yes, `Scripts/` | yes | **compiled** | **yes**, plus a `.meta` Unity creates on the next refresh |
| B — `.cs` written to `<project>/Temp/GeneratedCode/<ModName>/` | yes, `Scripts/Generated/` | yes, with a null GUID | **compiled** | **no** — Unity deletes `Temp/` when it exits |
| C — `.json` written into `<mod>/Conf/` and appended to `assetPaths` | yes, `Conf/` | yes | readable through `GetFile` | **yes**, `Conf/` and `Conf.meta` |
| D — `.json` written straight into the output directory | yes, `Conf/` | **no** | readable through `GetFile` | no |

Why A and C stay behind (*read*, `ModBuilder.cs:149-154`): the `finally` block
deletes `assetPaths.Except(originalAssetPaths)`, but `BuildConf` (`:296`) and
`BuildScripts` (`:349`) have already removed those entries from `assetPaths`,
so the difference is empty. The SDK has no post-build hook, so a processor
cannot clean up after itself either. Route B works because `BuildScripts`
collects `Temp/GeneratedCode/<ModName>` on its own (`:360-379`) — the same
path the DOTS code generator uses.

Route D files are not in the manifest. The loader compiles only `.cs` files the
manifest lists (`PugMod.Loader:1798`), so route D cannot carry code; for a
`.json` it does not matter to `GetFile`. Both publish paths upload the whole
output directory (`CLIPublishHelper` passes the build directory as
`ModfileDetails.directory`; `steam_bundle.py` passes it as `contentPath`), so a
route-D file would ship — *read*, not published.

### 3.2 `LoadedMod.GetFile` across install kinds

*Measured.* `GetFile("ModManifest.json")` succeeded for all 37 loaded mods:
35 mod.io installs (directory `C:\users\Public/mod.io/5289/mods/<id>_<file>`),
the fake-ID dev build (`…/mods/9999991_1`), and one Steam Workshop install,
Never Dark (`C:\Program Files (x86)\Steam\steamapps\workshop\content\1621690\3790491909`).
Reading another mod's file needs nothing from that mod.

| Call (on MSM's own `LoadedMod`) | Result |
|---|---|
| `Conf/X.json`, file present | bytes |
| `Conf\X.json` | bytes — both separators work |
| `conf/x.json` (wrong case) | bytes — the path is case-insensitive under Wine; on Linux it would not be |
| `Conf/Missing.json`, `Conf/` present | **throws** `System.IO.FileNotFoundException` |
| `Conf/Missing.json`, no `Conf/` | **throws** `System.IO.DirectoryNotFoundException` |
| `../ModManifest.json` | `null`, and the log line "tried to access path outside mod directory …" |

So a missing file throws. `GetFile` returns `null` in two cases only: a path
outside the mod's directory (measured, last row), and a `ModId` for which
`API.ModLoader.GetDirectory` finds no directory (*read*,
`PugMod.SDK.Runtime:1139-1143`; the loader's `GetDirectory` walks its list of
all known mods, `PugMod.Loader:1261-1270`).
The common case for MSM — a mod with no contract — is the throwing one. MSM
can avoid it without catching: `LoadedMod.Metadata.files` (public
`List<ModFile>`) lists every file the build put in the manifest, `Conf/*.json`
from `BuildConf` included (§3.1, route C). Checking it first costs nothing and
throws nothing — but only for a contract that reached the manifest.

**Tooling defect found on the way.** The first run failed every `Conf/` read
because `utils/install-macos.sh` copies only `ModManifest.json`, `Scripts/` and
`Bundles/` into the fake-ID install (lines 110-112) and into the cache zip
(line 124). `Conf/` — and a `Localization/` folder of `.csv` files — is
dropped without a word. The SDK built it correctly. Until the installer is
fixed, no contract can be tested through a dev build; this belongs in
`core_keeper/utils`, not in MSM.

### 3.3 `GetFileTime` under Wine

*Measured*, 180 writes through `API.ConfigFilesystem` on consecutive frames
over 1.5 s, each followed by `GetFileTime`:

- 180 distinct values; every one carried a sub-millisecond part; smallest gap
  between two writes 5.2–6.0 ms, which is one frame. The timestamp resolves
  well below a frame, so a change between two polls is never hidden by
  resolution. Values are UTC; compare against `DateTime.UtcNow`.
- Cost per call, 1000 calls each: `GetFileTime` 23.6–23.8 µs; `Read` of a
  726-byte `config.cfg` 197–199 µs; `Read` of a tiny file 203–210 µs. The
  timestamp check is about eight times cheaper than reading.
- `GetFileTime` on a **missing** file throws `System.IO.IOException:
  Unknown error (0x…)` under Wine. On Windows `File.GetLastWriteTimeUtc`
  would return 1601-01-01 instead, by its documented contract (not measured
  here). Either way, `FileExists` first.

**Also read on the way** (`Pug.Other:455641-455707`, `StandaloneFilesystem.Write`):
a write goes to `<path>.pugtmp`, is flushed to disk, and then takes the file's
place through `File.Replace`, keeping the previous one as `<path>.pugbackup`
(the probe's folder showed exactly that pair). In the stock code a reader
polling while MSM writes therefore sees the old file or the new one, never a
half-written one. On this machine `corekeeper-patch` Patch 3 adds a
direct-write fallback to the same DLL for a Wine regression, so the guarantee
is the stock one, not measured here. `GetFiles` hides both helper suffixes.
Every write is a flush-to-disk, which is the cost MSM-09 is about.

## 4 · Platform facts that shape the design

Established on 2026-10-11 (*read* unless marked); most were already in the
handbook (`docs/ck/sandbox.md`, `persistence.md`, `mod-anatomy.md`) before the
session derived them again.

### 4.1 `API.ConfigFilesystem`

- `IConfigFilesystem` (`PugMod.SDK.Runtime:1102`): `DirectoryExists`,
  `CreateDirectory`, `DeleteDirectory`, `CopyDirectory`, `FileExists`, `Read`,
  `Write`, `Delete`, `GetAllFiles`, `GetFiles`, `GetFileTime`. **No event.**
- It is a `StandaloneFilesystem(SavePrefix, "mods", …)` on client and dedicated
  server alike (`Pug.Other:272221`), rooted at the **shared** `mods/` folder.
  MSM's `ConfigStore` writes `<ModId>/config.cfg` under it; nothing stops one
  mod reading or writing another's folder. `GetFileTime` is
  `File.GetLastWriteTimeUtc` (`Pug.Other:455808`).
- `Write` does not create directories and swallows `IOException` with a log
  line (`docs/ck/persistence.md`).
- `FileSystemWatcher` is `System.IO`, which the sandbox refuses. For a
  consumer that shares nothing with MSM but files, polling is the only way to
  notice a change. A consumer that depends on **CoreLib** has another: MSM never
  writes `config.cfg` itself but sets a CoreLib `ConfigEntry`, and every
  `ConfigFile` is listed in CoreLib's public static
  `ConfigFile.AllConfigFilesReadOnly` (CoreLib 5.0.0 `ConfigFile.cs:40`) with a
  `SettingChanged` event (`:502`). Such a consumer can find
  `<ModId>/config.cfg` there and subscribe to the live entries MSM uses, with no
  reference to MSM. The proxy's consumers do not depend on CoreLib.

### 4.2 `API.Reflection` cannot call MSM

`Invoke`, `GetValue` and `SetValue` (`Pug.Other:410387/410401/410419`) pass the
declaring type through `InvokeChecker.CheckType` (`PugMod.Loader:556-611`),
which admits only assemblies whose name starts with `Pug`, `Unity`,
`SpriteInstancing`, `I2` or `Rewired`. A mod's assembly is named after
`metadata.name` (`PugMod.Loader:1373`), so `ModSettingsMenu` is refused. Types
and members can be listed (`GetTypes`, which ignores its `modId` argument and
returns every mod's types) but not called **through `API.Reflection`**; other
ways to run foreign code exist (§4.3). The handbook records a measured
refusal of a CoreLib member. Renaming MSM to a `Pug…` name would pass the gate
and was rejected: the gate is evidently meant as a boundary, and a rename
breaks every consumer.

### 4.3 Calls across mods need a shared type

What does work without `API.Reflection`: CoreLib (sandboxed) takes other mods'
types from `GetTypes`, instantiates them and calls them through CoreLib's own
interfaces (`SubmoduleHandler.cs:186/193`, `CommandModule.cs:79`). That needs
the callee to compile against the caller's interface — a dependency in one
direction. Further routes, *read*, none run across mods:

- `API.Experimental.RegisterAttributeFunction<TAttr, TDel>(Type, …)`
  (`Pug.Other:410459-410466`) turns every static method of an arbitrary type
  that carries attribute `TAttr` into a delegate `TDel`, with no
  `InvokeChecker`. CoreLib uses it sandboxed (`EntityModule.cs:350` in 4.0.5).
  With an attribute type both sides already have (any `System` or
  `UnityEngine` attribute), it works in either direction.
- `Activator.CreateInstance` runs a foreign type's constructor; CoreLib does it
  sandboxed.
- `LoadedMod.Handlers` exposes every mod's live `IMod` objects. `IMod` has six
  members, not five: `EarlyInit`, `Init`, `ModObjectLoaded(UnityEngine.Object)`,
  `Shutdown`, `Update`, and `CanBeUnloaded` with a default body.
  `ModObjectLoaded` on another mod's handler accepts any Unity object — a typed
  channel that needs no dependency in either direction.
- `SendMessage` / `BroadcastMessage` are not on the sandbox's deny lists;
  whether they reach across mods at runtime is unverified.

Whether one of these can carry a callback from MSM into a consumer is the open
question of §7.

### 4.4 Detecting MSM, and when `LoadedMods` is complete

`IModLoader` (`PugMod.SDK.Runtime:539`) offers `LoadedMods`, `GetDirectory`,
`ApplyHarmonyPatches`, `ApplyHarmonyPatch`, `UnloadHarmonyPatches`; `GetMod`
exists on the implementation but not the interface. The loader fills
`LoadedMods` for every mod before the first `EarlyInit`
(`PugMod.Loader:Reload:91`, `:109`). A mod that failed to compile or load is in
a separate not-loaded list, invisible through the API; a mod dropped by the
sorter is in neither. Listed does not mean working: an exception in `EarlyInit`
is logged and the mod stays. So `LoadedMods.Any(m => m.Metadata.name ==
"ModSettingsMenu")` answers "is MSM listed" completely from `EarlyInit` on —
whether that also means "present and switched on" depends on the disabled case
below, and it never means "working". The moment a consumer matches on the
name, the internal name is an interface.

The roadmap also says a **disabled** mod is not in `LoadedMods`. The loader
does not reference `disabledMods`; the filtering happens somewhere else and was
not traced. Unverified.

### 4.5 `Conf/` and the game's own readers

`ModBuilder.BuildConf` (`ModBuilder.cs:277-298`) copies every `.json` under
`<Mod>/Conf/` into the package verbatim — and more: its test is a raw string
prefix without a separator (`:290`), so a `.json` under `<Mod>/Config/` or a
file `<Mod>/ConfDefaults.json` ships too, at its own relative path. Only
imported assets are considered. At startup the game adds each loaded
mod's `Conf/` to `Config.ConfFolders` (`Pug.Other:ModManager:Init:24-27`) and
reads only `*.json` in fixed subfolders: `Factions`, `Fishing`, `Loot`,
`Loot/Fishing/Biome`, `Loot/Fishing/Water` and `Talents` at their top level;
`Spawn/Initial` and `Spawn/Respawn` recursively (`Pug.Mods`). A file directly
in `Conf/` is left alone.

### 4.6 `API.Config` (`ModAPIConfig`)

One JSON file per entry at `<mod>/<section>-<key>.json`
(`Pug.Other:ModAPIConfig:GetPath`). The entry's `Value` getter reads and parses
the file on every access; it caches the last read but never answers from it. A
missing file reads as `default(T)`, not the registered default, and after a
failed read the next `Value = x` throws `NullReferenceException`. The `mod`
argument is a free string. The proxy's author records in Rail Network's source
that two reads a frame were its whole ~7 ms UI cost (*third-party*,
`RailWindows.cs:147-153`). No game code uses `API.Config`.

### 4.7 The SDK build hook

`IPugModBuilderProcessor.Execute(ModBuilderSettings, string installDirectory,
List<string> assetPaths)` (`ModBuilder.cs:589`). `PreProcess` instantiates
**every** implementation found in any loaded editor assembly and runs it for
**every** mod build (`:255-275`) — a processor has to filter for the mods it
serves. Pugstorm's `ModBuilderCustomScenesProcessor` is the shipped example.
Build order: `PreProcess` → `BuildConf` → `BuildLocalization` → `BuildScripts`
→ `BuildLibraries` → `BuildAssets` (`:89-103`).

### 4.8 Compilation, load order, integrity

- `LoadScripts` compiles each mod against the assemblies of every mod loaded
  before it (`PugMod.Loader:LoadScripts:5-7`). Code generated identically into
  several mods must therefore be `internal`, or two mods expose the same public
  type to the next one's compilation.
- `DependencySorter` orders by every declared dependency, `required: 0`
  included; otherwise by `preferredLoadOrder`, then by discovery order.
- The loader checks no hashes or signatures. The SDK is the de-facto toolchain
  (R8), not an enforced one; a hand-written manifest loads.
- The loader instantiates only **public** `IMod` implementations
  (`FindAllSubTypesOf<IMod>(includeNonPublic: false)`, `PugMod.Loader:1512`).

## 5 · How the proxy does it today

### 5.1 The contract file

`mods/ModSettings/<ModId>.json` under the shared root (`ContractIO`, proxy
1.2.1), written through `API.ConfigFilesystem`. The shape was designed flat for
`JsonUtility`, but the proxy does **not** use `JsonUtility`: `ContractJson.cs`
is a hand-rolled codec, and its header explains why — `JsonUtility` emitted only
the primitive fields and dropped the `settings` array, because Unity's
serializer does not know types compiled into a mod's runtime assembly
(*third-party*, with the author's log lines quoted). That applies to MSM's own
types just as much (§6.2):

~~~json
{
  "modId": "ExtendPouches",
  "displayName": "Pouch Expansion",
  "hint": "...",
  "rev": 3,
  "settings": [
    { "key": "Extra pouch slots", "kind": "stepper", "min": 0, "max": 8,
      "step": 1, "def": "8", "value": "8", "tokens": [],
      "requiresRestart": false, "sliderDisplay": "Steps" }
  ]
}
~~~

`key` is both label and persistence key; `kind` ∈ toggle/slider/stepper/choice;
`def` and `value` are strings parsed per kind with the invariant culture;
`tokens` for choice; `sliderDisplay` ∈ Steps/Number/Percent. The source states
it plainly: "That file is BOTH the mod's own persistence AND the channel this
proxy reads" (`ModSettingsContract.cs:17`). The owning mod writes the whole
file at startup (structure from the declaration, values from disk); the proxy
never touches structure, rewrites only `value` fields and bumps `rev`.

### 5.2 The proxy

Internal name `ModSettingsProxy`, mod.io `6273365`; source read at 1.2.1. The
1.2.2 source differs only in its version string and one `EarlyInit` log line. It
alone carries the MSM dependency; without MSM the loader skips it and every
consumer keeps running on its file. It calls MSM's public API
(`ModSettings.Sections`, `ModSettings.Section`), so it is the first thing an API
change in MSM breaks. In `Init` it scans `LoadedMods`. Its own comment gives the
reason that `LoadedMod` entries exist only once every mod is registered — which
§4.4 refutes, since `LoadedMods` is complete before the first `EarlyInit`. The
reasons that do hold: consumers publish their file in `EarlyInit`, and direct
integrations register in `Init` or `EarlyInit`, so a scan in `Init` sees both.
MSM pre-warms on the first `Update` in which its menu instance exists, not
necessarily the first `Update`. The scan skips any `ModId` already registered
with MSM, reads `ModSettings/<ModId>.json`, takes the consumer's first `IMod`
handler and registers `ModSettings.Section(owner)` **on the consumer's behalf**
— heading, loc terms and `config.cfg` are the consumer's own. It rescans once a
second for ten seconds to catch late publishers, and batches all of a frame's
changes into one file write and one `rev` bump per mod. On load, MSM's `.cfg`
wins over the JSON (roadmap, 2026-10-02; `ProxySection.cs` not re-read).

### 5.3 The consumer helper

`ModSettingsDeclaration.cs`, a copy-paste file ("Drop this single file into
your mod's own Scripts\ folder and change the namespace"), only `System`,
`PugMod`, `UnityEngine`; no MSM, CoreLib, proxy, reflection or `System.IO`.

~~~csharp
public void EarlyInit()   // not Init: the proxy scans in its Init
{
    _settings = ModSettingsDeclaration.For(this, "Auto Fishing")
        .Toggle("Auto reel", true, v => AutoFishingSettings.SetAutoReelEnabled(v))
        .Publish();
}
public void Update() => _settings?.Poll();   // self-throttled to ~1 s
~~~

`For(owner)` finds the mod's `LoadedMod` by its handler; `Toggle`, `Slider`,
`Stepper`, `Choice` (string tokens), `Hint`, `RequiresRestart` mirror
`SectionBuilder`; `Publish()` merges the declaration with the file, writes it
and fires every callback once; `Poll()` re-reads about once a second and fires
the callbacks whose value changed once `rev` moved. Keys are sanitised (`'`
dropped, `= " \ [ ] \n \r \t` replaced). The forbidden set is CoreLib's: the
`ConfigDefinition` constructor throws `ArgumentException` for exactly these
characters (CoreLib 5.0.0 `ConfigDefinition.cs:48-51`), reached through `Bind`.
MSM 1.2.2 bound unguarded, so one bad key lost the whole section — the helper
reports that Scanner Rune Marks 1.3 lost all five settings to one apostrophe
(*third-party*). `v2` binds through `BindGuarded` (`SectionBuilder.cs:86-104`)
and drops only the offending setting; the proxy guards per entry as well.
Floats are written `"R"`, invariant (R3 already holds there).

**Why it reports changes back.** A direct MSM consumer and MSM share one live
`ConfigEntry`: `SettingHandle.Value` pulls, `OnChanged` pushes. A consumer
with neither MSM nor CoreLib shares only a file, so `Poll()` stands in for the
missing object — a property of having no dependency and a file-only channel,
not of the proxy. It would not hold for a CoreLib consumer (§4.1), nor if a
shared-type channel works (§7).

### 5.4 Thirteen copies, six versions

Of the author's eighteen mods (mod.io, 2026-10-11), thirteen ship the helper;
none declares a dependency on MSM or the proxy. Normalised for the namespace,
the copies are six distinct versions in three families: the base (six mods),
one adding `LegacyId` (two: adopt values stored under a mod's former name), and
one without the serializable file classes (five; field set only partly
checked). Triple Chests (≥2.4) and Mannequin Expansion (≥1.1) dropped their
settings pages — and the proxy kept rendering the stale contract file, whose
sliders changed nothing, for as long as the mod stayed loaded, until each mod
learned to delete its own leftover at startup. The remaining two have nothing
to declare: My Mods Table has no configuration at all, and Rail Network's only
file is a per-world station store (`RailCore.cs:174`); its one former setting,
a debug-panel toggle in `API.Config`, became a compile-time constant in 2.1.0
(`RailWindows.cs:147-155`).

Two weaknesses MSM should not inherit: a runtime-written contract outlives its
declaration, and handling code that exists once per mod drifts.

### 5.5 Dependency directions

~~~
MSM consumer:   Consumer ──required + asmdef──▶ MSM ──▶ CoreLib
Proxy consumer: Consumer (no dependency; helper copy + file)
                   ▲ found at runtime through LoadedMods + file
                Proxy ──required──▶ MSM ──▶ CoreLib
~~~

The inversion is the whole point: one mod carries the dependency for all
consumers. The price is the missing shared object.

## 6 · Direction (provisional)

### 6.1 The contract ships in the package

`Conf/ModSettingsMenu.json` in the consumer's package, built by `BuildConf` from
`<Mod>/Conf/ModSettingsMenu.json` in the mod's source tree. It satisfies R4 (it
is the mod's own, not in a shared folder) and R5 (nothing writes it at runtime).
It disappears with an uninstall and is replaced by an update, so the *shipped*
contract cannot go stale. The stale-contract problem of §5.4 can still arise
during a transition: a proxy consumer that moves to the contract leaves its
runtime-written `mods/ModSettings/<ModId>.json` behind, and a proxy that is
still installed and registers first renders it (§11). The game ignores a file
directly in `Conf/` (§4.5). A contract that comes through `BuildConf` is also in
the manifest, which lets MSM test for it without an exception (§3.2) — an
argument for authoring the file in the source tree (route C as an original
asset) rather than writing it into the output (route D).

Draft field set, to be settled in the spec review — starting from the proxy's
set minus what belongs to values:

- `formatVersion` (required). The client in an already-published mod cannot be
  changed (§6.3), so MSM reads every older version it ever accepted.
- `displayName`, `hint` (optional; MSM otherwise uses the manifest's
  `displayName`).
- `settings[]`: `key`, `kind`, `def`, `min`, `max`, `step`, `tokens`,
  `sliderDisplay`, `requiresRestart`. **No `value`, no `rev`** (R1). Numbers
  in the contract are written with a dot, whatever the encoding (strings
  parsed invariant, as the proxy does, or JSON numbers) — R3 applies to the
  defaults that end up in the `.cfg`.
- `key` is the `config.cfg` key. For a mod moving from direct integration the
  keys must equal the ones its `config.cfg` already holds, or the values do not
  carry over (R9); a key CoreLib refuses (§5.3) must be rejected when the
  contract is validated, not at `Bind`. A renamed mod changes `<ModId>` and
  with it the `config.cfg` path; the helper's `LegacyId` exists for that case
  and the direction has no equivalent yet.
- Reserved for MSM-19: a scope / access level per setting (MSM-18 is shipped on
  `v2`; a contract without the field stays `Client`). Whether it goes in
  version 1 is part of §8.
- Not carried, by nature: anything that is code — see §7.

MSM's own conventions apply unchanged: loc term `<ModId>-Config/<key>`, the
consumer's `config.cfg`, section heading from the consumer.

### 6.2 MSM reads it

A scan over `LoadedMods`, taking the proxy's five steps as the template:
skip MSM itself and every `ModId` already registered directly; look for
`Conf/ModSettingsMenu.json` in `Metadata.files`; read it with `GetFile` inside a
`try`; reject an unknown `formatVersion` loudly in the log; register a section
on the consumer's behalf. Open points:

- **The manifest is part of the format.** Through `Metadata.files` MSM sees
  only a contract that reached the manifest — any SDK build of a file in the
  source tree's `Conf/`, but not route D and not a hand-built package that does
  not list it. Either the format says "the contract is listed in the
  manifest", or the scan also probes `GetFile` for unlisted ones and catches
  the exception. Not decided.
- **Parsing.** MSM's types are compiled into a mod assembly, so `JsonUtility`
  is likely to fail on them the way it failed for the proxy (§5.1). MSM needs
  a parser of its own; the failure is unmeasured for MSM.
- **When.** `EarlyInit` sees a complete `LoadedMods` (§4.4), but direct
  consumers register in `Init` (ItemChecklist, FasterTalents, SignLabels,
  DisableDurability, …) *or* in `EarlyInit` (RebalanceKeyCrafting, whose values
  feed the database bake between the two; CompleteTools). A scan in
  `EarlyInit` therefore sees some direct registrations depending on load
  order, and a scan anywhere must handle both. A scan deferred to MSM's first
  `Update` sees all of them; that `Update` already hosts PreWarm.
- **Which `IMod` owns the section.** `ModSettings.Section(IMod)` needs one. The
  proxy takes the first handler; a mod with a contract but no `IMod` (data
  only) has none. An internal registration path taking a `LoadedMod` may be
  cleaner.
- **Precedence.** Direct integration wins over a contract for the same mod.

### 6.3 The consumer's code comes from an SDK package (R6, R7)

A UPM package installed into the SDK project — one line in
`Packages/manifest.json`. It contributes:

- an editor-side `IPugModBuilderProcessor` that filters for mods carrying a
  contract (§4.7: every processor runs for every build);
- a **client** compiled into each consumer, `internal`, with no reference to MSM
  or the package at runtime.

What the client does, given §3 and §4:

1. Reads defaults from the contract (its own `GetFile`, or constants generated
   from it) — the mod works with MSM absent and no `config.cfg` on disk.
2. If `mods/<ModId>/config.cfg` exists (`FileExists` first, §3.3), parses it
   the way CoreLib's `ConfigFile.Reload` does: `#` lines are comments,
   `[Section]` lines set the current section (MSM writes one section per group,
   so a key is `(section, key)`), other lines are `key = value`. Values are
   converted per type as `TomlTypeConverter` does — floats invariant (R3), but
   `int` through `ToString()`/`int.Parse` in the **current** culture
   (`TomlTypeConverter.cs:57-60`) — with CoreLib's string escaping
   (`Say \"hi\"`), a flags enum as a comma list (`Alpha, Beta`), and the
   clamp CoreLib applies silently after a bad value (a list falls back to its
   first entry, a range to a bound). Comments are not a schema and are not
   read.
3. Polls: `GetFileTime` (24 µs) and only on a change `Read` + parse (~200 µs);
   compares values and raises a per-setting change callback. MSM's writes are
   tmp-and-replace in the stock code (§3.3). A torn read is not reliably caught
   by a failed parse: a file cut at a line boundary parses, with keys missing.
   Re-checking `GetFileTime` after the read, or the key count against the
   contract, would catch it; not decided.
4. Never writes the file. MSM stays the single writer of `config.cfg`.

Drift does not disappear, it changes kind: if every build regenerates the client
from the package version in use (which depends on §6.4 — a committed client is
open to hand edits unless the build regenerates and compares it exactly), two
mods differ by *version*, not by hand edits. A mod built a year ago keeps last
year's client for as long as it is not rebuilt — the package centralises the
source, not the shipped binaries. That is why the contract needs
`formatVersion`, and why the **value path** (step 2 and 3) has to be right
before the first release: the format forgives an addition, the client in a
published mod does not (§8).

Authors without the SDK (R8) can hand-write the contract and copy the client;
MSM reads the contract whoever produced it, as long as it is in the manifest
(§6.2). That is the proxy's drift model, kept as the exception.

Two questions the session raised and left open: whether other authors accept
installing a UPM package into their SDK project at all — the premise of R7 —
and what the author writes as the declaration. Named in the session were a
ScriptableObject and C# attributes; §6.4 adds the contract JSON itself.

### 6.4 Where the generated code lives — open, with the probe's answer

The consumer's own code calls the client, so the client must exist when the
**Editor** compiles the mod, not only in the package. That rules out
generating it during the build alone. Candidates:

| Option | For | Against |
|---|---|---|
| Generated into the mod's source tree, **committed** (like `LocalizationGenerator`'s output); regenerated on import or by menu, checked by the processor at build | The Editor sees it; the diff is reviewable; leftovers are irrelevant because the file is meant to stay | Two triggers to keep in step; a stale file must fail the build rather than ship |
| Generic client in a package **runtime asmdef** the mod references in the Editor; copied into the build through `Temp/GeneratedCode/<ModName>/` (route B, measured: compiled, self-cleaning) | Nothing generated in the repo; no leftovers | In the Editor it is a separate assembly, so its types must be `public`; at runtime the same types are compiled into every consumer, public, under one full name — exactly what §4.8 warns about. Would need a probe of what Roslyn does with it |
| The contract JSON is the **declaration**: authored by hand in `Conf/`, the package generates only the typed client from it | One source, already static; `BuildConf` ships it as is (manifest included) | Hand-written JSON instead of a C# builder; validation must happen at import, not at runtime |

The third composes with either of the first two. A ScriptableObject or C#
attributes as the declaration (§6.3) would replace the third. Not decided.

### 6.5 Values stay in `config.cfg` (R1, R2, R9)

`mods/<ModId>/config.cfg`, written by MSM exactly as for a direct consumer. A
mod moving from direct integration to the contract keeps its players' values
and its readable file; no migration, and no second store — unless MSM-19 is
resolved by an effective-values file (§8), which is a second file MSM writes
and the client prefers. A proxy consumer is the other case: a player who never
had MSM holds values only in `ModSettings/<ModId>.json`, and nothing in this
direction carries them over (§11).

## 7 · What a file cannot carry

`Button(onClick)` (MSM-01), `Info(Func<string>)` (MSM-02), a lock whose
condition is computed, and `OnChanged` as a push are code, and a contract is
data. With `API.Reflection` closed (§4.2) they stay direct-integration-only in
this direction; the client's polling (§6.3) replaces `OnChanged` with a delay
of up to one poll interval, which only shows for HUD-type options — today
Caveling Divining Rod (a recipe toggled) and Item Checklist (HUD rebuild) use
`OnChanged`; the other consumers read `.Value` on demand.

The roadmap calls this final ("no route"). The review showed that is too
strong. §4.3 lists routes that exist in the code: `RegisterAttributeFunction`
yields callable delegates in either direction given a shared attribute type,
and `ModObjectLoaded` on a handler carries any Unity object between mods. A
consumer could, for instance, mark static `Info`/`Button` methods with a
`System` attribute that MSM binds through `RegisterAttributeFunction`. None of
it has been run across two mods. Worth a probe before the field set is frozen;
if it works, Button and Info could ride on it, and a push channel would take
the place of polling.

## 8 · Coupling with MSM-19, and the order

**The coupling.** The client reads the disk. MSM-19, as designed, applies a
server's value **in memory only** — the player's `.cfg` stays their
preference and `ConfigFile.Reload()` restores it when the session ends. So a
server rule never reaches a contract consumer. Two ways out, and the choice
has to be made before the client ships, because it is the part that cannot be
corrected afterwards:

- MSM also writes the **effective** values to a second file during a session,
  which the client prefers when present; or
- contract consumers never take part in sync — written down as a rule (scope
  `Client` only), not left as a gap.

A scope field in the contract can be added later through `formatVersion`; the
client's reading logic cannot. Both ways out are MSM-19 behaviour, so the
dependency runs both ways in one respect: MSM-19's design has to contain a
branch for contract consumers.

What MSM-19 says that the coupling rests on (roadmap): a server-scoped value is
applied when not hosting (`Manager.ecs.ServerWorld == null`) by send → await →
write, memory only; the initial sync is independent of the menu; the pair of
connected / session-ended events it needs is unverified; whether `AdminOnly`
belongs in MSM or in CoreLib is open; the wire key is FNV-1a 64 over
`"ModId/Section/Key"`.

**How the order recommendation evolved**, kept because two of its arguments
were wrong:

1. 2026-10-11 20:59: "decide MSM-38 first, build after MSM-19's design", as a
   conditional plan — run the reflection probe; if positive, build MSM-38 as
   the reflection option for 2.0 independent of MSM-19; if not, write MSM-19's
   ADR first and design the contract format after it. It rested on a
   reflection option that §4.2 later refuted, and on the claim that the
   proxy's author would be kept waiting "six months", a figure with no source.
2. 2026-10-11 21:01, after R1: the **schema format and MSM's reading of it**
   are independent of MSM-19 and could go first; only the value path waits for
   MSM-19's answer on where effective values live.
3. 2026-10-12, after the reflection option fell: MSM-19's ADR first, then the
   contract format; the coupling is only in the value path.
4. 2026-10-12, next answer: "MSM-19's value model (ADR) first, then build
   MSM-38, then MSM-19" — again with an invented "half a year" and an urgency
   argument.
5. 2026-10-12, after the user pointed out that the figure was unfounded and
   that the proxy works today: **MSM-19 first, ADR and implementation; then
   MSM-38 against a value model that is built and tested in game**, not against
   an ADR that may still change while it is being built. MSM-38 needs MSM-19's
   answer on the value path; MSM-19 needs from MSM-38 only the decision how
   sync treats contract consumers. Step 2's split still holds: the schema
   format could be settled earlier without risk.

The user has not decided the order; they ordered probes and this provisional
spec. MSM-19's own prerequisites are open: an ADR (it contradicts ADR-001), and
whether a server operator may claim a value an author left at `Client` scope.

## 9 · The `.cfg` format today

CoreLib `ConfigFile`, BepInEx dialect. Example (ItemChecklist):

~~~ini
## Settings file was created by plugin ItemChecklist

[Settings]

## counterMode
# Setting type: String
# Default value: Discovery
# Acceptable values: Discovery, Possession
counterMode = Possession

## anchorRadius
# Setting type: Single
# Default value: 48
# Acceptable value range: From 16 to 96
anchorRadius = 48
~~~

- Read back are `[Section]` headers and `key = value` lines; `#` lines are
  comments (CoreLib `ConfigFile.cs:268-275`). The comments are not a
  usable schema — no widget kind (a string with a value list could be Choice
  or Stepper), step, display, restart flag, scope, group or loc term — and
  they are ambiguous to parse. Reading the schema from the `.cfg` was rejected
  for that reason.
- The value line and `# Default value:` go through `TomlTypeConverter` —
  invariant for floating-point and decimal types, current culture for
  integral ones (harmless under de-DE, not under a culture with another minus
  sign). `# Acceptable values:` (`AcceptableValueList.ToDescriptionString`,
  `x.ToString()`) and `# Acceptable value range:` (string interpolation) use the
  **current culture**, so under de-DE a float list reads `0,5, 1,5, 2,5`, and
  `float.Parse("0,5", InvariantInfo)` is 5 (*measured* on .NET 10, not on the
  game's Mono). Under Wine the bottle's culture is de-DE; `corekeeper-patch`
  pins only the UI culture. Filed as CoreLib issue #54 (2026-10-11), suggested
  fix `Convert.ToString(x, CultureInfo.InvariantCulture)` /
  `string.Format(CultureInfo.InvariantCulture, …)`. An MSM-side workaround
  (subclassing the two constraint types) only if CoreLib declines — first
  checking that `ForeignConfigDiscovery` does not match their exact types.
  Until one of the two happens, R3 is **not** met for these comment lines, and
  nothing in this direction commits MSM to the workaround; that is open.
- MSM `v2` writes Choice tokens through `ChoiceToken.Of` (invariant); 1.2.2
  used `ToString()`, which would put `0,5` into a float Choice's **value**
  line. No consumer declares a float Choice.

The client in §6.3 reads exactly this format, so R3 matters to it twice: its
own parsing is invariant, and the comments it skips must not be needed.

## 10 · Alternatives considered and set aside

| Alternative | Why not |
|---|---|
| MSM reads the proxy's file format (roadmap option 1 as first written) | A second store beside `config.cfg`, synchronised by `rev`, in a mod whose persistence already needed an explicit save to survive another mod; values in the contract contradict R1; runtime-written (R5). Against any JSON contract, the direction included: it turns a file format into a public API beside the C# one, and an unknown or missing field fails silently where C# fails at compile time — hence `formatVersion` and validation |
| Optional binding through `API.Reflection` (option 2) | Refused by `InvokeChecker` (§4.2) |
| Nothing in the core, point to the proxy (option 3) | Leaves R4–R6 unmet; remains the de-facto state while the proxy exists |
| Values only in MSM's memory | Never reach the mod |
| Values in a file of the mod's that MSM writes into | The proxy's model split over two files |
| Schema from the `.cfg` comments | §9 |
| `API.Config` as the value channel (one JSON per key) | Expensive reads (§4.6) force caching and polling anyway; no event; keys stated twice; a second value store; **old CFGs not read** (R9) unless migrated; scattered files without value hints (R2). Superseded by the package keeping values in `config.cfg`. A one-time central migration was sketched for it (never delete the old file) and is moot now |
| Contract in the config folder, written at runtime | R5; stale contracts (§5.4) |
| Rename MSM to pass `InvokeChecker` | §4.2 |
| Consumer-side detection of MSM to decide whether to publish | Unneeded with a shipped contract; makes the internal name an interface; still allows no calls |

## 11 · The proxy's existing consumers

The direction does not read `ModSettings/*.json`, so the thirteen consumers keep
needing the proxy until they move. Three paths were named: MSM also reads the
proxy format for a transition (thirteen mods work at once, but a second store
comes back); MSM has its own format and the proxy stays for those mods; or the
proxy's author moves the mods onto the contract — the stated plan, at the cost
of touching thirteen mods. To be agreed with the author **before** the format
is fixed. Two things the agreement has to cover beyond the format: the
**values** — for a player without MSM they exist only in
`ModSettings/<ModId>.json`, keys sanitised, in two mods under a `LegacyId`, and
nothing carries them over yet (R9); and the **leftover file**, which the mod or
MSM has to delete or ignore once the mod carries a contract (§6.1). Where both
serve one mod, the proxy skips a `ModId` already registered and MSM ignores a
duplicate, so whoever registers first wins (and the proxy rescans for ten
seconds).

**Two patches on one method — not a conflict after all.** The roadmap
(2026-10-02) said the outcome depends on which prefix runs first. The game's
Harmony runs every prefix and ANDs the `bool` results into "run the original"
(`0Harmony:10281-10310`). The proxy's `ResetEffect` prefix returns `void`
(`MenuOptionEffectPatch.cs:86-88`), so it neither skips nor forces the body;
its `text` repair is a separate postfix on the `PugTextEffect.text` getter
(`:53-55`). MSM's `bool` prefix (`PugTextEffectPatch.cs:41-44`) alone decides,
in any order.

## 12 · Open questions

1. Confirm R4's reading (contract in the package `Conf/`, values in the config
   folder) and R6's ("central source, per-mod binary") (§2).
2. The order of MSM-19 and MSM-38, and with it the value path (§8).
3. Where the generated code lives and what the declaration is (§6.4).
4. Field set of contract version 1, including whether scope is in it (§6.1).
5. When MSM scans, and who owns a contract section (§6.2).
6. Whether a shared PugMod type can carry callbacks (§7).
7. Transition for the proxy's consumers, agreed with its author — format,
   values and the leftover file (§11).
8. Fix `utils/install-macos.sh` to install `Conf/` and `Localization/` (§3.2).
9. Whether a disabled mod is in `LoadedMods` (§4.4).
10. CoreLib #54's outcome, and whether MSM commits to the workaround if
    CoreLib does nothing (§9).
11. Whether the contract must be in the manifest, or the scan probes for it
    (§6.2); MSM's own JSON parser (§6.2).
12. How the client detects a torn read (§6.3).
13. Key rules: contract keys equal existing `config.cfg` keys; rejecting
    CoreLib-forbidden keys at validation; a `LegacyId` equivalent (§6.1).
14. Whether other authors accept a UPM package; ScriptableObject, attributes
    or JSON as the declaration (§6.3, §6.4).
15. Unread: `ProxySection.cs` (where the proxy's sections put their keys,
    which decides R9 for its consumers); helper family three's field set.

## 13 · The roadmap is behind this document

`docs/roadmap.md` § MSM-38 (`2e996d0`) predates the handbook review of the same
night and the probes. Corrections it needs when it is next edited:

- "a `CompileFailed` or disabled mod is not in it", "a disabled mod is never
  asked" — the disabled half is unverified (§4.4).
- `GetFile` "refuses only paths outside that mod's own directory" — it also
  returns `null` without a directory, throws on a missing file (§3.2), and its
  containment test is a raw string prefix.
- "reads only the subfolders `Factions`, `Loot`, `Fishing`, `Spawn` and
  `Talents`" — only `*.json`, at the depths in §4.5.
- "Rail Network measured …" — the proxy's author's comment, not our
  measurement (§4.6).
- "the timestamp's resolution under Wine is unmeasured" and the three "to
  check" items — measured (§3).
- "with it the last route to callbacks … is gone" — see §7.
- "its file option became the direction" — the direction keeps little of
  option 1: no proxy format, no values, no `rev`, static, generated.
- "With both installed the outcome depends on which prefix runs first" — it
  does not; MSM's prefix decides (§11).
- "A consumer writes `ModSettings/<ModId>.json` … through
  `API.ConfigFilesystem`" stays true; "On load, MSM's `.cfg` wins over the JSON"
  is still unverified (`ProxySection.cs` unread).

## 14 · Further record (R10)

Kept so nothing from the sessions is lost; none of it changes the direction.

- **Artefacts.** `core_keeper` `b561f28` added the handbook sections on
  reaching another mod without a dependency (`docs/ck/mod-anatomy.md`) and on
  `API.Config`, `GetFile` across mods and `Conf/` (`docs/ck/persistence.md`),
  and corrected the `mods/` root in `CLAUDE.md`. CoreLib issue #54 was filed. A
  corpus review report written for that handbook change was never read in the
  session; its one refutation (CoreLib's cross-mod calls) was found and fixed
  independently.
- **`IReflection` details.** `GetTypesFromCurrentAssembly` throws
  `NotImplementedException`; `Invoke` accepts only `MethodInfo`; types marked
  `[DisallowPatching]` and assemblies named `PugMod.Loader*` are refused even
  under an admitted prefix; of 122 decompiled assemblies 42 are admitted and
  80 refused (handbook). Beyond game assemblies a mod needs real
  `System.Reflection`, i.e. `skipSafetyChecks: true` — which is why the proxy
  says reflection would force "Script (Elevated Access)".
- **`GetFile` for a mod that failed to load.** `GetDirectory` walks all known
  mods, failed ones included (`PugMod.Loader:1261-1270`, `:1624`), but a
  `LoadedMod` to call `GetFile` on exists only for loaded mods.
- **`Conf/` switches.** `-confdir` adds folders to `Config.ConfFolders`,
  `-safemode` clears the list; the game's readers catch and log per-file parse
  errors.
- **`API.ConfigFilesystem`** writes a `README.txt` into `mods/` at startup
  ("MODS ARE NOT LOADED FROM HERE").
- **The proxy** reports `CanBeUnloaded() => true` and flushes on `Shutdown`;
  its changelog runs from 1.0 ("fixed fragile option names") to 1.2.1 (log
  spam from rows built while inactive).
- **The author's eighteen mods** (mod.io, 2026-10-11): Rail Network 6434348,
  Locked Chest Unlocker 6424845, PokeBall 6370182, Enemy Summoner 6364352,
  Capybara Pet 6350632, Triple Chests 6346710, Golden Converter 6320584,
  OctaRoofing 6320573, Scanner Rune Marks 6319303, My Mods Table 6314500,
  Mannequin Expansion 6311149, BuffManager 6275698, Mod Settings Proxy
  6273365, Item Duplicator 6271997, Everything Pouch 6271993, Extend Pouches
  6270811, AutoPlanet 6270775, AutoFishing 6270006.
- **Citations** into `Pug.Other:ModManager:Init` resolve one line earlier in
  the dedicated-server tree; citations here are client-relative.
