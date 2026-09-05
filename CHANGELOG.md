# Changelog

All notable changes to this mod are documented here. The topmost `## [x.y.z]`
entry is the version published to mod.io; its body is the modfile changelog.

## [2.0.0]

- **Mods can group their settings under headings.** A mod with many settings can
  now put a heading above each group, so its box reads as sections instead of one
  long list. Headings are there to be read: they show no value, and keyboard and
  controller navigation steps straight over them.
- **A detected mod's settings can show as separate groups, too.** If a mod
  already organises its own config file into more than one section, this menu
  now shows each of those sections under its own heading instead of one flat
  list — a mod that keeps its file tidy now shows the same way here.
- **Detected mods can show translated names.** A mod that ships translations for
  its own settings now has them used here — for its heading, its setting names,
  and the values of a multiple-choice setting alike. Both naming schemes are
  understood: this menu's own, and the one General Mod Config Menu established,
  so a mod that follows GMCM's naming convention needs nothing new. The translated
  name is what the rest of the screen goes by, too: mods stay in alphabetical
  order by the name you actually see, and the reset confirmation names a mod the
  way its box does. A mod that ships no translations is unaffected and keeps
  showing the plain names from its config file.
- **A detected mod's setting that allows only certain values can now be
  changed.** Such a setting used to be shown but not editable, no matter what
  the mod itself permitted — it now works like any other choice, cycling through
  exactly the values that mod accepts and nothing else. Text and numeric value
  sets alike.
- **List entries can be reordered and deleted.** Every row in the drill-in now
  carries up/down arrows and a delete button, reachable by mouse, keyboard and
  controller alike. Deleting asks first — the confirmation names the entry and
  has to be held, not tapped — while an entry you only just added and never
  filled in disappears without a prompt.
- **Mods can now offer a list setting of their own.** Until now a list only
  ever appeared for a mod that did *not* integrate with this menu — it had to be
  recognised in that mod's config file. A mod author can now declare one
  directly, and say what you may do with it: edit entries freely, only reorder a
  fixed set, or just read it. Where entries cannot be added, the list is kept in
  step with what the mod currently offers — an entry it adds in a later release
  appears instead of staying invisible, one it drops disappears instead of being
  stuck there forever, and the order you put the rest in is left alone.
- **One setting that cannot be saved no longer takes a whole mod's settings
  with it.** If writing a value fails — a permission problem, a full disk — that
  single setting is left out and the mod keeps its own default, instead of the
  mod's entire section disappearing from this screen.
- **Any mod's settings can be restored to their defaults.** Select a row in that
  mod's box and press the reset button shown at the bottom of the screen; a
  confirmation names the mod before anything changes. Works for detected mods
  too, and only ever resets the settings this menu lets you edit — anything
  shown read-only is left untouched.
- **Any mod using CoreLib config is now discovered automatically**, even if it
  never integrates with Mod Settings Menu directly — its settings render as
  their own section (marked "(detected)"), inferring a Toggle/Slider/Stepper/
  Choice/read-only Info row from each value's shape. A master "show detected
  mod settings" toggle in this mod's own section switches auto-discovery off.
- **Comma-separated list values get a dedicated widget.** A discovered setting
  that looks like a genuine list (e.g. an item-name exclusion list) renders as
  a compact row with a preview and opens a full, scrollable drill-in screen —
  fully controller/keyboard navigable, unlike the raw text a plain Info row
  would otherwise show.
- **List entries are directly editable.** Each token in the drill-in is its
  own editable row: change, remove, or add entries without leaving the game or
  hand-editing a config file. Every entry now looks like the input field it is,
  a dedicated button at the end adds a new one, and a freshly added entry may
  stay blank while you work on the others — blank entries are simply not saved.
  A read-only list (server-locked or view-only) still shows every entry, just
  without an edit affordance and without the add button.
- **Fixed: editing a long list entry silently discarded part of it.** An entry
  wider than its row was cut down to what fitted on screen, and typing in it
  saved that shortened text back — the rest was gone, with nothing to indicate
  it had ever been there. Entries now scroll sideways instead: the whole value
  stays intact however long it is, the view follows the cursor as you move
  through it, and you can jump to the start or end of the line, move a word at
  a time, or click straight to the spot you want to edit.
- **Fixed: holding a word-jump key crawled instead of repeating.** Holding Ctrl
  (or Alt) together with an arrow key jumped one word and then moved a single
  character at a time for as long as the key stayed down. It now keeps jumping
  word by word, at the same repeat rate the game uses for every other held key.
- **A setting you cannot change still looks like itself.** A read-only value
  used to collapse into a plain text row; it now keeps its native widget — a
  locked toggle still reads as on/off, a locked slider still shows its
  percentage — and simply stops responding to input.
- **Fixed: rows could render without any text.** Opening and closing the
  settings screen repeatedly eventually left labels and values blank until the
  game was restarted. The screen now releases its text resources correctly and
  renders reliably however often it is reopened.
- **Fixed: keyboard and controller selection could scroll out of view.** With
  several mods installed the list grows past the screen, and moving down walked
  the highlighted row off the bottom edge. The view now follows the selection.
- **Fixed: discovered settings are written back more carefully.** Adjusting a
  numeric value no longer introduces rounding noise into the owning mod's
  config file, and stepping through a choice no longer overwrites a value this
  menu does not recognise. Section headings and hints also space correctly
  again.

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
