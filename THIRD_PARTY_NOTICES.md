# Attribution

BotAimImprover retains the upstream credits to ed0ard, htfy96 and XBribo.

`native/hitbox_snapshot.{h,cpp}` and `native/tests/hitbox_snapshot_tests.cpp`
are adapted from the AGPL-3.0-only hitbox snapshot implementation in
[unicbm/CS2-bot-improver-light](https://github.com/unicbm/CS2-bot-improver-light),
under `native/BotVision/` (snapshot source revision
`93794d58819d27bc79478460619fdb8680dbd4cb`). That component originates from the BotVision project
derived from [XBribo/CS2-Bot-Vision](https://github.com/XBribo/CS2-Bot-Vision).
The snapshot adaptation uses an explicitly supplied engine allocator release
function and does not include BotVision's smoke processing or hooks.

The added geometry/selection code is provided under AGPL-3.0-only, consistent
with this repository's license. CounterStrikeSharp remains an external runtime
dependency with its own license. No Valve game binaries or extracted assets
are included; the gamedata contains audited metadata only.
