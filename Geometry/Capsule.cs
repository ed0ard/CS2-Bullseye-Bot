// SPDX-License-Identifier: AGPL-3.0-only
using System.Numerics;
using System.Runtime.InteropServices;

namespace BotAimImprover.Geometry;

// Shared with the native reader: preserve field order and the 36-byte layout.
[StructLayout(LayoutKind.Sequential)]
internal readonly record struct Capsule(Vector3 A, Vector3 B, float Radius, int Group, int Index)
{
    internal const int MaxCount = 32;

    internal Vector3 Center => (A + B) * .5f;

    internal bool Valid => Finite(A) && Finite(B) && float.IsFinite(Radius) && Radius > 0
        && Group is >= 1 and <= 8 && Index is >= 0 and <= ushort.MaxValue;

    internal static bool Finite(Vector3 value) =>
        float.IsFinite(value.X) && float.IsFinite(value.Y) && float.IsFinite(value.Z);

    internal static bool ValidSnapshot(ReadOnlySpan<Capsule> capsules)
    {
        if (capsules.IsEmpty || capsules.Length > MaxCount)
            return false;

        foreach (ref readonly var capsule in capsules)
        {
            if (!capsule.Valid)
                return false;
        }

        return true;
    }
}
