# Mod Settings Menu

One shared **Options → Mod settings** screen that other mods register their
options into. No config files to hand-edit, no separate menu per mod.

If you landed here because something else asked for it: there is nothing to
configure in Mod Settings Menu itself. Each dependent mod adds its own section
under its own name, and every value saves automatically.

## For mod authors

Declaring toggles, sliders, choices or steppers takes about five lines in your
`IMod.Init`; the framework renders the panel and persists the values through
CoreLib. API reference in the repo README.

## Requirements

**CoreLib**, offered when you subscribe.

## Multiplayer

Install on both the client and the server.
