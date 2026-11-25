// SPDX-License-Identifier: AGPL-3.0-only
// Capsule transforms and CUtlVector layouts adapted from the Bullseye #9 reader
// and unicbm/CS2-bot-improver-light's BotVision (derived from XBribo/CS2-Bot-Vision).
using System.Numerics;
using System.Runtime.InteropServices;
using CounterStrikeSharp.API;
using CounterStrikeSharp.API.Core;
using CounterStrikeSharp.API.Modules.Memory;

namespace BotAimImprover;

internal readonly record struct Capsule(Vector3 A, Vector3 B, float Radius, HitGroup_t Group)
{
    internal Vector3 Center => (A + B) * 0.5f;

    // Highest point of the animated capsule, inset slightly to avoid a tangent ray.
    internal Vector3 Crown => (A.Z >= B.Z ? A : B) + Vector3.UnitZ * (Radius * 0.97f);
}

internal sealed unsafe class Hitboxes
{
    private const int Capacity = 32;
    internal const int Slots = 64;
    private readonly delegate* unmanaged<nint, void*, void*, int, int> getHitboxes;

    // The engine uses fixed-growable vectors with inline storage. The transform
    // vector's data pointer and storage are 16-byte aligned.
    [StructLayout(LayoutKind.Sequential)]
    private struct BoxList
    {
        public int Count, Padding;
        public Descriptor* Data;
        public int Capacity;
        public uint Flags;
    }

    [StructLayout(LayoutKind.Explicit, Size = 32)]
    private struct TransformList
    {
        [FieldOffset(0)] public int Count;
        [FieldOffset(16)] public BoneTransform* Data;
        [FieldOffset(24)] public int Capacity;
        [FieldOffset(28)] public uint Flags;
    }

    [StructLayout(LayoutKind.Explicit, Size = 16)]
    private struct Descriptor { [FieldOffset(0)] public Hitbox* Box; }
    [StructLayout(LayoutKind.Explicit, Size = 32)]
    private struct BoneTransform
    {
        [FieldOffset(0)] public Vector3 Position;
        [FieldOffset(12)] public float Scale;
        [FieldOffset(16)] public Quaternion Rotation;
        public readonly Vector3 World(Vector3 local) => Position + Vector3.Transform(local * Scale, Rotation);
    }

    // animationsystem CHitBox on Windows and Linux x64 (not a server Schema class).
    [StructLayout(LayoutKind.Explicit)]
    internal struct Hitbox
    {
        [FieldOffset(0x18)] public Vector3 Min;
        [FieldOffset(0x24)] public Vector3 Max;
        [FieldOffset(0x30)] public float Radius;
        [FieldOffset(0x38)] public HitGroup_t Group;
        [FieldOffset(0x3c)] public byte Shape;
    }

    [DllImport("tier0", EntryPoint = "MemAlloc_FreeFunc", CallingConvention = CallingConvention.Cdecl)]
    private static extern void FreeEngineMemory(nint memory);

    internal Hitboxes(string signature)
    {
        getHitboxes = (delegate* unmanaged<nint, void*, void*, int, int>)NativeAPI.FindSignature(Addresses.ServerPath, signature);
        if (getHitboxes == null) throw new InvalidOperationException("Missing server hitbox-list function.");
    }

    private sealed class Pose
    {
        internal readonly Capsule[] Shapes = new Capsule[Capacity];
        internal int Tick = -1, Count;
        internal uint Pawn;
    }

    // Player slot indexes storage. The serial-bearing pawn handle distinguishes respawns.
    private readonly Pose[] poses = Enumerable.Range(0, Slots).Select(_ => new Pose()).ToArray();
    // GetPartPosition returns a borrowed reference. Give its callers stable per-slot storage.
    private readonly Vector3[] crowns = GC.AllocateArray<Vector3>(Slots, pinned: true);

    internal ReadOnlySpan<Capsule> For(CCSPlayerPawn pawn, int slot)
    {
        var pose = poses[slot];
        int tick = Server.TickCount;
        uint identity = pawn.EntityHandle.Raw;
        if (pose.Tick != tick || pose.Pawn != identity)
        {
            pose.Count = Read(pawn, pose.Shapes);
            pose.Tick = tick;
            pose.Pawn = identity;
        }
        return pose.Shapes.AsSpan(0, pose.Count);
    }

    internal nint CrownReference(int slot, Vector3 point)
    {
        crowns[slot] = point;
        fixed (Vector3* p = &crowns[slot]) return (nint)p;
    }

    private int Read(CCSPlayerPawn pawn, Capsule[] output)
    {
        var body = pawn.CBodyComponent;
        if (body?.SceneNode is not { } node ||
            body.As<CBodyComponentSkeletonInstance>().SkeletonInstance.Handle != node.Handle) return 0;

        byte* boxBuffer = stackalloc byte[sizeof(BoxList) + Capacity * sizeof(Descriptor)];
        byte* transformBuffer = stackalloc byte[15 + sizeof(TransformList) + Capacity * sizeof(BoneTransform)];
        BoxList* boxes = (BoxList*)boxBuffer;
        TransformList* transforms = (TransformList*)(((nuint)transformBuffer + 15) & ~(nuint)15);
        *boxes = new() { Data = (Descriptor*)(boxes + 1), Capacity = Capacity, Flags = 0x80000000 };
        *transforms = new() { Data = (BoneTransform*)(transforms + 1), Capacity = Capacity, Flags = 0x80000000 };
        try
        {
            int count = getHitboxes(node.Handle, boxes, transforms, 0);
            if (count <= 0 || count > Capacity || count != boxes->Count || count != transforms->Count ||
                boxes->Data == null || transforms->Data == null) return 0;
            int written = 0;
            for (int i = 0; i < count; i++)
            {
                var box = boxes->Data[i].Box;
                if (box == null || box->Shape != 2) continue;
                var bone = transforms->Data[i];
                var capsule = new Capsule(bone.World(box->Min), bone.World(box->Max), box->Radius * bone.Scale, box->Group);
                if (!float.IsFinite(capsule.A.LengthSquared()) || !float.IsFinite(capsule.B.LengthSquared()) ||
                    !float.IsFinite(capsule.Radius) || capsule.Radius <= 0) return 0;
                output[written++] = capsule;
            }
            return written;
        }
        finally
        {
            if (boxes->Data != (Descriptor*)(boxes + 1) && (boxes->Flags & 0xc0000000) == 0)
                FreeEngineMemory((nint)boxes->Data);
            if (transforms->Data != (BoneTransform*)(transforms + 1) && (transforms->Flags & 0xc0000000) == 0)
                FreeEngineMemory((nint)transforms->Data);
        }
    }
}
