# BornAgainM

A work-in-progress personal **learning project** built with [MelonLoader](https://melonwiki.xyz/#/),
developed in collaboration with some AIs — I don't really know how to code, so it is certainly full of
flawed logic and rough edges.

It uses **Born Again** only as a theme, because I'm enjoying the game at the moment. I do **not** modify
any game data: the mod only *reads* values to display information, and swaps bank slots for sorting. It is
not meant to break the game, affect anyone's journey, or gain an unfair advantage. Thank you.

## What it does

All overlays are rebuilt with the game's own UI assets (TextMeshPro font, portrait and slot prefabs, stat
sprites) so they match the game's look, and are read from the live simulation state.

- **Online Players** — a scrollable list of connected players (level tinted by thread color, name in white).
  Click a player to open a detail panel showing their primary stats (base / total), thread, blessings and
  equipment with native item icons and tooltips. The detail is captured at click time, so it stays readable
  even after the player leaves the area.
- **Boss Monitor** — a list of the current bosses with portrait, name and an HP bar.
- **Boss Damage** — a consultable per-boss damage meter: pick a boss and see the per-attacker breakdown
  ("who did how much"), attributed from the simulation's live attack buffer.
- **Personal DPS meter** — toggle with `NumPad +`; it records your own attacks for 20 seconds and posts a
  summary to the game chat.
- **Bank sorter** — groups identical items by swapping bank slots.

## How it reads data

The mod reads the game state through MelonLoader / Il2CppInterop: the authoritative player list comes from
`World.Characters`, entity state (equipment, blessings) is read by raw pointer at known offsets, and live
attacks come from the simulation's attack buffer. Nothing is written back to the game.

## Notes

- From what I understand, the client handles **Damage Over Time** as a world message without an `ownerId`,
  which makes it impossible to attribute DoT damage correctly on the client side.
- Secondary stats (crit / block / true damage / cooldown) are intentionally **not** shown for remote players:
  the game's total also folds in crystals, whose crystal path is local profile data never present in the
  networked entity state, so an exact total cannot be reconstructed for someone else.

## Build

Targets `net6.0`. The project references the game's Il2Cpp assemblies from a local MelonLoader install, so the
paths in `BornAgainM.csproj` must be adjusted to your own Born Again / MelonLoader install before building.
