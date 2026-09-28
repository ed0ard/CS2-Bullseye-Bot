# CS2-Bullseye-Bot

Server-side, pose-aware aim part selection for CS2 bots. The plugin keeps a
fixed weapon/body-part preference and only replaces a native aim point after
an engine trace confirms a hit on the intended target and hitgroup.

## Requirements and platform support

- CounterStrikeSharp **1.0.375+**, with its built-in trace natives.
- Windows x64 and the **exact server/tier0 build listed in `gamedata/deadeye.json`**.
- The included `BullseyeGeometry.dll` (built from `native/`). No additional
  Metamod plugin, BotVision, Ray-Trace, or BotController installation is needed
  beyond CounterStrikeSharp's own requirements.

**Linux pose bindings are not yet audited.** On Linux, on an unsupported game
build, or when the helper is missing, the plugin reports that overrides are
unavailable and leaves the game's native aim selection in place. This is an
explicit compatibility limitation compared with the older approximation.
Do not change only a hash to force an unknown game build to load.

## Commands

| Command | Preference |
| --- | --- |
| `bot_aim mixed` | Body-first for the upstream sniper/spread-weapon list; head-first otherwise |
| `bot_aim head` | Head-first, retaining the existing AWP body-first exception |
| `bot_aim body` | Body-first for all weapons |
| `bot_aim` / `bot_aim status` | Mode, binding availability, override/fallback counts |

Body-first means stomach, chest, arms, head, neck, legs. Head-first means head,
neck, chest, stomach, arms, legs. Within each hitgroup, engine hitbox order is
used. The old synthetic JAW/shoulder/foot labels are replaced by actual capsule
centers; there is no separate jaw hitbox. Neck (group 8) is not head (group 1).

## Installation

Build or download the Windows package, then extract its `addons/` folder into
`game/csgo/`. Keep this layout together:

```text
addons/counterstrikesharp/plugins/BotAimImprover/
  BotAimImprover.dll
  BotAimImprover.deps.json
  gamedata/deadeye.json
  native/win-x64/BullseyeGeometry.dll
```

Start/reload the plugin and check `bot_aim status`. `pose geometry ready` means
the binding and helper loaded; it is not a claim that live aim validation has
passed. `overrides` increases only after the complete selection/trace/write path.

## What changed

The old implementation guessed body points from eye-height fractions and yaw.
A point could pass a world-only trace while lying outside the animated body.
The new implementation reads the server's current hitbox descriptors and final
transforms, converts them into world-space capsules, and tests their centers.
It never reconstructs the body from eye height, movement hulls or duck flags.

A shot-mask trace ignores the shooting Pawn and must return the intended enemy,
a real hitbox, and the candidate's hitgroup. A wall, another player, clear air,
an unrelated hitgroup, invalid geometry, or a failed query cannot authorize an
override. All decisions remain on the server. No damage is applied by queries.

The existing `m_isEnemyVisible` gate and native `PickNewAimSpot` post-hook are
retained. CSS Schema accessors replace hard-coded Bot field offsets, and entity
handles retain their serial numbers. Human-controlled Pawns are excluded.

This is **not** a wallbang or damage-optimization system: there is no penetration,
spread scoring, target discovery/FOV change, adaptive edge search or persistent
region selection. A blocked center does not prove the whole capsule is hidden;
if no candidate succeeds, the original result is retained. Likewise, a direct
trace now does not guarantee a later bullet will hit after movement or spread.

Geometry is copied once per target per server tick (up to 64 targets, 32 capsules
each), never retained as engine pointers. Traces are fresh, at most one per
capsule and 256 across the plugin per tick. Exhaustion preserves native aim.

## Build and test

Use Windows x64, .NET SDK 10, CMake 3.20+, and Visual Studio C++ build tools with
a Windows SDK. The helper uses no game SDK headers or import libraries.

```powershell
./tools/package.ps1
# Optional: -Dotnet C:/path/to/dotnet.exe -Generator 'Visual Studio 18 2026'
```

The script builds/tests both components and emits a fresh zip under `artifacts/`.
The GitHub Actions workflow runs the same script. No game DLLs are packaged.
See [validation notes](docs/validation.md) for the boundary between isolated
checks and the outstanding live-server validation matrix.

Original authors: ed0ard, htfy96 and XBribo. See [attribution](THIRD_PARTY_NOTICES.md).
