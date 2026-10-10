# Changelog

All notable changes to this mod are documented here. The topmost `## [x.y.z]`
entry is the version published to mod.io; its body is the modfile changelog.

## [1.2.2]

- Settings are saved even when General Mod Config Menu is installed. That mod
  switches off the automatic saving of every other mod's settings file, so a
  change made in the Mod Settings screen was shown but forgotten on the next
  launch. Every change is now written to disk explicitly.

## [1.2.1]

- Fixes text disappearing all over the game after the Mod Settings screen had
  been opened several times. Every visit used up part of the game's text
  resources without giving them back, so after a handful of visits — fewer the
  more mods show settings — labels in every menu went blank until a restart.

## [1.2.0]

- Works with Core Keeper 1.3, which had left the Mod Settings screen empty. The
  screen shows every mod's box again, with its heading above the box and its
  options inside it, laid out as before the update.

## [1.1.0]

- Settings can now require a game restart. A mod author marks such a setting with
  the new `RequiresRestart()` API (for values only read at bake / world load);
  changing it and leaving the menu then raises Core Keeper's own "restart to apply
  mod changes" prompt — the same dialog the game shows when your mods change.
- Wider setting rows so longer option labels and values fit without clipping.

## [1.0.0]

Initial release.

- In-game settings screen for other mods, mounted under Options → Mod Settings.
- Consumer API: declare settings in `IMod.Init` as toggles, sliders, discrete
  choices, or steppers, and read live values through typed handles.
- Per-mod persistence via a CoreLib config file — values save on change and
  restore on the next launch.
- Optional localization of labels, hints, and choice options, with a fallback
  to the raw key when a translation is missing.
- Per-section option ordering: declaration order (default), by key, or by
  localized label.
