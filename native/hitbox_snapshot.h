// SPDX-License-Identifier: AGPL-3.0-only
// Adapted from unicbm/CS2-bot-improver-light native/BotVision; see THIRD_PARTY_NOTICES.md.
#pragma once
#include <cstdint>

namespace bullseye::hitbox
{
    struct Vec3 { float x, y, z; };
    struct Capsule { Vec3 a, b; float radius; std::int32_t group, index; };
    // All build-dependent addresses/layouts come from the verified managed
    // NativeBindings consumer. This API installs no hooks and retains nothing.
    struct SnapshotConfig
    {
        std::uint32_t size, version;
        void *getHitboxes;
        void (*freeMemory)(void *);
        std::int32_t pawnSceneNode, skeletonVtable;
        std::int32_t boxA, boxB, boxRadius, boxGroup, boxShape, boxIndex;
    };
    static_assert(sizeof(Capsule) == 36 && sizeof(SnapshotConfig) == 56);
    extern "C" __declspec(dllexport) int Bullseye_ReadHitboxesV1(
        const SnapshotConfig *config, void *pawn, Capsule *output, int capacity);
}
