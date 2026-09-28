# CS2-Bullseye-Bot
CS2-Bullseye-Bot is a CounterStrikeSharp plugin that improves bots' aim in CS2.

![bullseye_2](https://github.com/user-attachments/assets/b658e64e-028c-42de-817c-1748fe0ca4f4)

## Requirements

- CounterStrikeSharp 1.0.375+ and .NET 10.
- Windows x64, matching the server/tier0 build in `gamedata/deadeye.json`.
- The packaged `BullseyeGeometry.dll`; no additional Ray-Trace or BotVision installation.

Linux and unmatched builds retain native aim: their pose bindings are not yet
validated. Do not change only the profile hash to force an unknown build to load.

## Commands

- `bot_aim mixed`: body-first for snipers/spread weapons, head-first otherwise.
- `bot_aim head`: head-first, retaining the existing AWP body-first exception.
- `bot_aim body`: body-first for all weapons.
- `bot_aim` / `bot_aim status`: current mode, availability and override/fallback counts.

Candidate points are current server hitbox centers, with fixed hitgroup ordering.
The old synthetic JAW point maps to the head group; neck remains a separate group.
A direct trace must hit the intended enemy and hitgroup. Failed queries preserve
native aim. There is no wallbang, damage scoring or adaptive edge search.

## Installation

Extract the package's `addons/` into `game/csgo/`. Keep `gamedata/deadeye.json` and
`native/win-x64/BullseyeGeometry.dll` beside the plugin under
`addons/counterstrikesharp/plugins/BotAimImprover/`. Check `bot_aim status` after loading.

## Build

Windows x64, .NET SDK 10, CMake 3.20+, and Visual Studio C++ build tools with a
Windows SDK are required. No game SDK headers or libraries are needed.

```powershell
./tools/package.ps1
```

This builds both components, runs native/managed tests, and writes a zip under
`artifacts/`. Live animation, hitgroup, reload/takeover and SV/VAR checks remain
required before release; fixture tests do not establish in-game accuracy.

## If you find the plugin useful then please take the time to star⭐ the repository
