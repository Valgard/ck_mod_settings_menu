# Changelog

All notable changes to this mod are documented here. The topmost `## [x.y.z]`
entry is the version published to mod.io; its body is the modfile changelog.

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
