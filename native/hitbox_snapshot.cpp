// SPDX-License-Identifier: AGPL-3.0-only
// Adapted from unicbm/CS2-bot-improver-light native/BotVision; see THIRD_PARTY_NOTICES.md.
#include "hitbox_snapshot.h"
#include <cmath>
#include <cstring>
#include <cstddef>
#include <initializer_list>

namespace bullseye::hitbox
{
    namespace
    {
        template<class T> T Read(const void *base, int offset)
        { T value; std::memcpy(&value, static_cast<const char *>(base) + offset, sizeof(value)); return value; }
        struct Descriptor { void *box; std::int32_t mesh, padding; };
        struct alignas(16) Transform { Vec3 position; float scale; float x, y, z, w; };
        // Audited fixed-growable CUtlVector layouts; overflow belongs to the
        // engine allocator, never to the CRT. Buffers are private to this call.
        struct Descriptors
        {
            int count = 0, padding = 0;
            Descriptor *data;
            int capacity = 32; std::uint32_t flags = 0x80000000u;
            Descriptor storage[32]{};
            void (*freeMemory)(void *);
            explicit Descriptors(void (*release)(void *)) : data(storage), freeMemory(release) {}
            ~Descriptors() { if (data && data != storage && !(flags & 0xc0000000u)) freeMemory(data); }
        };
        struct alignas(16) Transforms
        {
            int count = 0, padding[3]{};
            Transform *data;
            int capacity = 32; std::uint32_t flags = 0x80000000u;
            Transform storage[32]{};
            void (*freeMemory)(void *);
            explicit Transforms(void (*release)(void *)) : data(storage), freeMemory(release) {}
            ~Transforms() { if (data && data != storage && !(flags & 0xc0000000u)) freeMemory(data); }
        };
        static_assert(sizeof(Descriptor) == 16 && sizeof(Transform) == 32);
        static_assert(offsetof(Descriptors, data) == 8 && offsetof(Descriptors, storage) == 24);
        static_assert(offsetof(Transforms, data) == 16 && offsetof(Transforms, storage) == 32);
        bool Finite(Vec3 v) { return std::isfinite(v.x) && std::isfinite(v.y) && std::isfinite(v.z); }
        Vec3 World(Vec3 v, const Transform &t)
        {
            Vec3 u{2 * (t.y*v.z - t.z*v.y), 2 * (t.z*v.x - t.x*v.z), 2 * (t.x*v.y - t.y*v.x)};
            return {t.position.x + t.scale*(v.x + t.w*u.x + t.y*u.z - t.z*u.y),
                t.position.y + t.scale*(v.y + t.w*u.y + t.z*u.x - t.x*u.z),
                t.position.z + t.scale*(v.z + t.w*u.z + t.x*u.y - t.y*u.x)};
        }
    }

    extern "C" __declspec(dllexport) int Bullseye_ReadHitboxesV1(
        const SnapshotConfig *config, void *pawn, Capsule *output, int capacity)
    {
        if (!config || config->size != sizeof(SnapshotConfig) || config->version != 1
            || !config->getHitboxes || !config->freeMemory || !pawn || !output || capacity < 32) return -1;
        const auto &c = *config;
        if (c.pawnSceneNode < 0 || c.pawnSceneNode > 8192 || c.skeletonVtable < 0
            || c.skeletonVtable > 4096 || c.skeletonVtable % sizeof(void *) != 0) return -1;
        for (int offset : {c.boxA, c.boxB, c.boxRadius, c.boxGroup, c.boxShape, c.boxIndex})
            if (offset < 0 || offset > 4096) return -1;
        void *node = Read<void *>(pawn, c.pawnSceneNode);
        if (!node) return -1;
        void *table = Read<void *>(node, 0);
        if (!table) return -1;
        using GetSkeleton = void *(__fastcall *)(void *);
        auto getSkeleton = Read<GetSkeleton>(table, c.skeletonVtable);
        if (!getSkeleton) return -1;
        void *skeleton = getSkeleton(node);
        if (!skeleton) return -1;
        Descriptors boxes(c.freeMemory);
        Transforms transforms(c.freeMemory);
        using GetHitboxes = int(__fastcall *)(void *, void *, void *, int);
        int count = reinterpret_cast<GetHitboxes>(c.getHitboxes)(skeleton, &boxes, &transforms, 0);
        if (count <= 0 || count != boxes.count || count != transforms.count || count > 32
            || !boxes.data || !transforms.data) return -1;
        int written = 0;
        for (int i = 0; i < count; ++i)
        {
            void *box = boxes.data[i].box;
            if (!box) return -1;
            int group = Read<int>(box, c.boxGroup);
            if (Read<std::uint8_t>(box, c.boxShape) != 2) return -1;
            Vec3 a = Read<Vec3>(box, c.boxA), b = Read<Vec3>(box, c.boxB);
            float radius = Read<float>(box, c.boxRadius);
            const auto &t = transforms.data[i];
            float norm = t.x*t.x + t.y*t.y + t.z*t.z + t.w*t.w;
            if (!Finite(a) || !Finite(b) || !Finite(t.position) || !std::isfinite(radius) || radius <= 0
                || !std::isfinite(t.scale) || t.scale <= 0 || !std::isfinite(norm) || std::abs(norm - 1) > .01f) return -1;
            Capsule result{World(a, t), World(b, t), radius*t.scale, group, Read<std::uint16_t>(box, c.boxIndex)};
            if (!Finite(result.a) || !Finite(result.b) || !std::isfinite(result.radius)) return -1;
            output[written++] = result;
        }
        return written;
    }
}
