# CS2-Bullseye-Bot
CS2-Bullseye-Bot is a CounterStrikeSharp plugin that improves bots' aim in CS2.

This Windows preview adapts PR #9 to the BotAimImprover 2.1.8 behavior shipped in
CS2-Bot-Improver v1.4.7 (upstream `c5a2e0f`). It replaces aim-point estimation only;
the bundle's BotAI, BotState, BotController and other components remain separate.

![bullseye_2](https://github.com/user-attachments/assets/b658e64e-028c-42de-817c-1748fe0ca4f4)

## Requirements

- CounterStrikeSharp 1.0.378+ and .NET 10.
- Windows x64, matching the server/tier0 build in `gamedata/deadeye.json`.
- The packaged `BullseyeGeometry.dll`; no additional Ray-Trace or BotVision installation.

The Windows binding targets CS2 1.41.9.2 / revision 11162119. Linux is not included
in this preview: keep the v1.4.7 Linux plugin unchanged. If this preview is loaded
on an unsupported platform/build, it leaves Valve's native aim untouched; that
is not the same as retaining ed0ard's measured-point selection. Do not change
only the profile hash to force an unknown build to load.

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
The 2.1.8 Trunk/Body distinction orders estimated side points differently. Those
side points no longer exist here: both body preferences test actual stomach
capsules before chest capsules. Command routing, the AWP exception and weapon
classification are preserved, not an identical sequence of estimated points.
A direct trace must hit the intended enemy and an allowed hitgroup. The point must
also pass the live native position-visibility entry, including installed FOV and
smoke hooks. Both calls share a 256-query budget per tick. Failed queries preserve
native aim. There is no wallbang, damage scoring or adaptive edge search.

BotVision's target reveal is scoped to its player-visibility scan. A standalone
point check cannot inherit that context: if smoke rejects all candidates for a
revealed enemy, this plugin retains the engine's original aim point. It does not
cancel the reveal, change enemy visibility or duplicate BotState's reveal policy.

Animated capsule centers can be more accurate than origin/eye-height estimates,
but center-only sampling can miss an exposed edge. This preview does not establish
higher headshot rate, damage or lower server cost. Compare against the original
v1.4.7 BotAimImprover with the same mode, profiles and other plugins; check actual
hitgroups, misses, override/fallback counts and server frame time. Pure head-mode
comparison separates geometry from the MIXED JAW policy.

## Installation

With the server stopped, replace only the Windows v1.4.7 BotAimImprover folder
using this package's `addons/` tree; keep the original folder for comparison.
Keep `gamedata/deadeye.json` and
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
