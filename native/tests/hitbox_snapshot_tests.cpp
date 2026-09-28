// SPDX-License-Identifier: AGPL-3.0-only
#include "hitbox_snapshot.h"
#include <cstdlib>
#include <cstring>
#include <cmath>
#include <cstddef>
#include <iostream>

using namespace bullseye::hitbox;
namespace
{
    int freed = 0, mode = 0;
    struct Box { Vec3 a, b; float radius; int group; unsigned char shape; unsigned short index; };
    struct alignas(16) Transform { Vec3 position; float scale; float x, y, z, w; };
    struct Descriptor { void *box; int mesh, padding; };
    Box box{{-1, 1.8f, 0}, {3.5f, .2f, 0}, 4.3f, 1, 2, 6};
    Transform pose{{100, 200, 60}, 2, 0, 0, .70710678f, .70710678f};
    struct Pawn { void *node; };
    void Require(bool value, const char *message)
    { if (!value) { std::cerr << message << '\n'; std::exit(1); } }
    bool Near(float a, float b) { return std::abs(a - b) < .0001f; }
    void *Skeleton(void *self) { return self; }
    int Hitboxes(void *, void *descriptors, void *transforms, int flags)
    {
        Require(flags == 0, "current engine pose request");
        Require(reinterpret_cast<std::uintptr_t>(transforms) % 16 == 0, "aligned native transform vector");
        auto bytes = static_cast<unsigned char *>(descriptors), poses = static_cast<unsigned char *>(transforms);
        auto data = *reinterpret_cast<Descriptor **>(bytes + 8);
        auto transform = *reinterpret_cast<Transform **>(poses + 16);
        Require(*reinterpret_cast<int *>(bytes) == 0 && *reinterpret_cast<int *>(poses) == 0, "fresh private vectors");
        Require(*reinterpret_cast<int *>(bytes + 16) == 32 && *reinterpret_cast<int *>(poses + 24) == 32, "native capacities");
        if (mode == 2)
        {
            data = static_cast<Descriptor *>(std::malloc(33 * sizeof(Descriptor)));
            transform = static_cast<Transform *>(std::malloc(33 * sizeof(Transform)));
            *reinterpret_cast<Descriptor **>(bytes + 8) = data;
            *reinterpret_cast<Transform **>(poses + 16) = transform;
            *reinterpret_cast<int *>(bytes + 20) = *reinterpret_cast<int *>(poses + 28) = 0;
            *reinterpret_cast<int *>(bytes) = *reinterpret_cast<int *>(poses) = 33;
            return 33;
        }
        data[0] = {&box, 0, 0}; transform[0] = pose;
        *reinterpret_cast<int *>(bytes) = 1;
        *reinterpret_cast<int *>(poses) = mode == 1 ? 0 : 1;
        return 1;
    }
}
namespace bullseye::hitbox { void FreeHitboxAllocation(void *p) { ++freed; std::free(p); } }

int main()
{
    void *table[]{reinterpret_cast<void *>(&Skeleton)};
    void *node = table; Pawn pawn{&node}; Capsule output[32]{};
    SnapshotConfig config{sizeof(SnapshotConfig), 1, reinterpret_cast<void *>(&Hitboxes), &FreeHitboxAllocation, offsetof(Pawn, node), 0,
        offsetof(Box, a), offsetof(Box, b), offsetof(Box, radius), offsetof(Box, group), offsetof(Box, shape), offsetof(Box, index)};
    Require(Bullseye_ReadHitboxesV1(&config, &pawn, output, 32) == 1, "read complete snapshot");
    Require(Near(output[0].a.x, 96.4f) && Near(output[0].a.y, 198) && Near(output[0].b.x, 99.6f)
        && Near(output[0].b.y, 207) && Near(output[0].radius, 8.6f), "world quaternion/scale; no extra origin");
    Require(output[0].group == 1 && output[0].index == 6 && freed == 0, "descriptor metadata and stack fast path");
    pose.position.z -= 25; box.group = 6;
    Require(Bullseye_ReadHitboxesV1(&config, &pawn, output, 32) == 1 && Near(output[0].a.z, 35)
        && output[0].group == 6, "fresh pose and limbs included for visibility");
    Require(Bullseye_ReadHitboxesV1(&config, &pawn, output, 1) == -1, "capacity gate");
    mode = 1;
    Require(Bullseye_ReadHitboxesV1(&config, &pawn, output, 32) == -1, "mismatched pose lists rejected");
    mode = 2;
    Require(Bullseye_ReadHitboxesV1(&config, &pawn, output, 32) == -1 && freed == 2, "overflow is released by engine allocator");
    mode = 0; box.shape = 0;
    Require(Bullseye_ReadHitboxesV1(&config, &pawn, output, 32) == -1, "unknown shape falls back");
    box.shape = 2; pose.scale = 0;
    Require(Bullseye_ReadHitboxesV1(&config, &pawn, output, 32) == -1, "invalid pose falls back");
    pawn.node = nullptr;
    Require(Bullseye_ReadHitboxesV1(&config, &pawn, output, 32) == -1, "missing scene node falls back");
    config.version = 2;
    Require(Bullseye_ReadHitboxesV1(&config, &pawn, output, 32) == -1, "ABI version gate");
    std::cout << "Hitbox snapshot: ABI, world pose, metadata, invalid input and engine allocation lifetime passed\n";
}
