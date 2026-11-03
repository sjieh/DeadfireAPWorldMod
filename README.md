# Deadfire APWorld Mod

A BepInEx mod implementing the Archipelago randomizer for **Pillars of Eternity II: Deadfire**.

## Setup

1. Install BepInEx 5.x in the game directory.
2. Extract `DeadfireArchipelago.zip` into `BepInEx/plugins`.
3. Start the game.

## Connect

After loading into the main menu, use the game's command line (default keybind: `\`) with your room address and slot name:

```
connect <server:port> <slot>
connectp <server:port> <slot> <password>
```

## Other commands

```
hint <item> - requests an Archipelago hint
deathlink - toggles DeathLink
```

## Notes

- The options `LogConsole` and `LogPopUp` in `BepInEx/config/com.sjieh.DeadfireAPWorldMod.cfg` are on by default, but can be disabled.
- Custom adventurer creation and importing custom adventurers are currently disabled.
