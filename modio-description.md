# Mod Settings Menu

A shared in-game settings screen for Core Keeper mods. Other mods register their
options here, and they appear as a tidy panel under **Options → Mod Settings** —
no config files to hand-edit, no separate per-mod menus.

**For players:** you most likely installed this because another mod depends on
it. There is nothing to configure in Mod Settings Menu itself — each dependent
mod adds its own section with its own toggles, sliders, and choices. Open
**Options → Mod Settings** to find them.

- Every setting saves automatically and is restored on the next launch.
- Settings are grouped per mod, under the mod's name as the heading.
- Labels follow your game language wherever the mod author provides translations.

**For mod authors:** adding a settings section takes about five lines in your
mod's `IMod.Init` — declare toggles, sliders, discrete choices, or steppers,
then read the live values wherever you need them. The framework renders the
menu and persists every value (through CoreLib) for you. The full API reference
lives in the source repository's README.

**Dependency:** requires CoreLib.

**Multiplayer:** install on both the client and the server.

---

*Built with the official Pugstorm Core Keeper Mod SDK. Personal-use,
non-commercial (Core Keeper EULA). Not affiliated with or endorsed by Pugstorm.*
