# BornAgainM

A work-in-progress personal **learning project** built with [MelonLoader](https://melonwiki.xyz/#/),
developed in collaboration with some AIs — I don't really know how to code, so it is certainly full of
flawed logic and rough edges.

It uses **Born Again** only as a theme, because I'm enjoying the game at the moment. I do **not** modify
any game data: the mod only *reads* values to display information, and swaps bank slots for sorting. It is
not meant to break the game, affect anyone's journey, or gain an unfair advantage. Thank you.

## What it does

- **Online Players** — a scrollable list of connected players in the current scene.
  Click a player to open a detail panel showing their primary stats (base / total), thread, blessings and
  equipment with native item icons and tooltips. The detail is captured at click time, so it stays readable
  even after the player leaves the area.
- **Boss Monitor** — the bosses in the current zone, each with a portrait, name and status bar
  showing whether it is alive, dead or ready to spawn.
- **Boss Damage** — a consultable per-boss damage meter: pick a boss and see the per-attacker breakdown
  ("who did how much"), attributed from the simulation's live attack buffer. Damage-over-time is not tracked.
- **Personal DPS meter** — toggle with `NumPad +`; it records your own attacks for 20 seconds and posts a
  summary to the game chat. (use on town dummy)
- **Bank sorter** — groups identical items by swapping bank slots.
- **Boss Map** — an interactive world map showing every boss at its spawn location, plus the dungeon
  lords. Click a boss to see its loot table, sorted by chest, and hover an item for its native in-game tooltip.
  Loot tables are based on the information currently available and may be incomplete.

## Install

1. Install [MelonLoader](https://melonwiki.xyz/#/) on Born Again (IL2CPP, net6) and run the game once so it
   finishes setting itself up.
2. Download `BornAgainM.dll` from the [Releases](https://github.com/tribalart/BornAgainM/releases) page.
3. Drop it into the game's `Mods` folder (`...\Born Again\Mods`).
4. Launch the game. The overlays appear with toggle buttons on the screen edges; the personal DPS meter
   toggles with `NumPad +`.

## Build

Targets `net6.0`. The project references the game's Il2Cpp assemblies from a local MelonLoader install.

1. Install MelonLoader on Born Again and **run the game once** — that is when MelonLoader's
   `Il2CppAssemblyGenerator` populates `MelonLoader\Il2CppAssemblies\`. Before that the folder does not
   exist and nothing resolves.
2. In `BornAgainM.csproj`, set the single `<GameDir>` property to your own Born Again install path. Every
   reference (and the post-build copy to `Mods`) derives from it.
3. `dotnet build -c Release`. The post-build step copies the DLL into `<GameDir>\Mods`.

## Notes

- From what I understand, the client handles **Damage Over Time** as a world message without an `ownerId`,
  which makes it impossible to attribute DoT damage correctly on the client side.
- Secondary stats (crit / block / true damage / cooldown) are intentionally **not** shown for remote players:
  the game's total also folds in crystals, whose crystal path is local profile data never present in the
  networked entity state, so an exact total cannot be reconstructed for someone else.
