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

- `bot_aim mixed`: body-first for snipers/spread weapons, JAW-first otherwise.
- `bot_aim head`: head-first, retaining the existing AWP body-first exception.
- `bot_aim body`: body-first for all weapons.
- `bot_aim` / `bot_aim status`: current mode, availability and override/fallback counts.

Candidates use current server hitbox centers plus one JAW-style point inside the
neck toward the head center (up to half a neck radius, capped before the head center).
This is a pose-derived aiming bias,
not a separate hitbox; it accepts actual head or neck hits. Mixed rifles try
`JAW -> NECK -> HEAD`; head mode tries `HEAD -> NECK -> JAW`. Body preference keeps
torso/arms first, then `JAW -> NECK -> HEAD`. Missing/degenerate head-neck geometry
skips JAW. Each selection tests at most 32 capsule centers and one JAW point.
The bias preserves the low-aiming intent, not a measured historical headshot rate.
A direct trace must hit the intended enemy and an allowed hitgroup. Failed queries preserve
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
