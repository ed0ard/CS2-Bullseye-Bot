# Validation and native binding notes

## Audited input

The initial Windows profile describes CS2 1.41.8.5 / revision 11039926
(2026-09-26). The profile includes exact server.dll/tier0.dll SHA-256 hashes,
PickNewAimSpot and hitbox-list RVAs/entry bytes, and the hitbox layout. At startup,
loaded modules must match the hashes, the live function bytes must match, and
the CSS signature result must equal the audited PickNewAimSpot address before
installing the hook. Bot fields are resolved through CSS Schema.

`native/hitbox_snapshot.cpp` requests the current hitbox descriptor/transform
lists from the server skeleton, with the audited Windows CUtlVector layouts.
Each world endpoint is `position + scale * rotate(rotation, localEndpoint)`;
world radius is `scale * localRadius`. The returned transform already includes
the entity transform; adding Pawn origin again would be incorrect.

The ABI uses 16-byte-aligned transform vectors, a 56-byte configuration, and
36-byte output capsules. Lists must agree in count, be nonempty, and contain
no more than 32 capsules. Unknown shapes, nonfinite values, nonpositive scale
or radius, and invalid rotations fail the entire snapshot. A positive uniform
scale is required. The caller never retains engine pointers.

Both lists have stack storage. If the engine grows them, the supplied tier0
`MemAlloc_FreeFunc(void*)` releases the allocation; the helper's CRT must not
free it. On the audited tier0 this export forwards to `g_pMemAlloc->Free`.
The helper installs no hooks and has no engine SDK or BotVision dependency.

## Reproducible isolated checks

- .NET plugin build against CSS 1.0.375.
- Selection/trace-validation executable: fixed priorities and AWP exception;
  animated centers; wrong-hitgroup rejection; clear-air, world, other-player,
  hull-only and start-solid rejection; invalid fractions; query failure;
  invalid/oversized snapshots and bounded search.
- C++ snapshot test: ABI layout/alignment; world rotation/scale/translation;
  pose refresh; metadata; shape/pose/list/capacity/ABI rejection; engine-allocator
  cleanup on overflow.
- Managed/native DLL round-trip: the production delegate/config/output layouts
  cross the actual helper DLL boundary using fixture callbacks and transforms.
- The packaging script executes those tests and emits a self-contained plugin
  package. It does not install into or start a running CS2 server.

These tests use controlled fixtures. They do not emulate engine collision,
prove all live animation cases, or establish an accuracy/performance percentage.

## Live matrix required before marking the PR ready

This contribution is a draft until the following are checked on a matching
server. None is claimed as completed by the isolated tests:

- Load, hot reload, unload; unsupported build and missing helper fallback.
- Standing/crouched targets, crouch transitions, looking up/down, movement,
  weapon draw/reload and defuse animations; display selected capsule centers.
- Direct head/body hits, partial cover, player blockers and no exposed centers;
  correlate selected groups with server traces and subsequent `player_hurt`.
- Death/respawn and entity-slot reuse; human takeover never receives overrides.
- All three modes and the AWP exception; native visibility gate remains active.
- Multiple bots, query cap exhaustion, SV/VAR and override/fallback counts.

Actual bullet outcomes can differ from an earlier query due to time, spread and
weapon state. CSS direct trace returns hitbox metadata; this plugin does not
simulate the full weapon damage or penetration pipeline.

## Updating or extending support

For another Windows build, verify signatures, calling conventions, schema,
vector/descriptor/transform layouts and tier0 allocator behavior against its
binaries before changing the profile. Re-run isolated and live checks. Linux
needs a separately audited reader ABI and binding profile; Windows addresses
and MSVC container layouts must not be transplanted to it.

The existing upstream PR #7 fixes the old hard-coded offsets. This change
removes those field constants in favor of Schema, so #7 is related context,
not a required patch to copy. Rebase against main if it lands first.
