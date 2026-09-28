// SPDX-License-Identifier: AGPL-3.0-only
using System.Numerics;
using System.Runtime.InteropServices;

namespace BotAimImprover.Geometry;

[StructLayout(LayoutKind.Sequential)]
internal readonly record struct Capsule(Vector3 A, Vector3 B, float Radius, int Group, int Index)
{
    internal Vector3 Center => (A + B) * .5f;
    internal bool Valid => Finite(A) && Finite(B) && float.IsFinite(Radius) && Radius > 0
        && Group is >= 1 and <= 8 && Index is >= 0 and <= ushort.MaxValue;
    internal static bool Finite(Vector3 v) => float.IsFinite(v.X) && float.IsFinite(v.Y) && float.IsFinite(v.Z);
}

internal enum AimMode { Mixed, Head, Body }
internal enum ProbeState { Failed, Miss, Hit }
internal interface IAimProbe
{
    ProbeState Query(Vector3 point, out int actualGroup);
}

// Fixed preferences only: no penetration, damage scoring, edge search or history.
internal static class AimSelection
{
    private static readonly int[] HeadFirst = [1, 8, 2, 3, 4, 5, 6, 7];
    private static readonly int[] BodyFirst = [3, 2, 4, 5, 1, 8, 6, 7];
    private static readonly HashSet<string> BodyWeapons =
    [
        "weapon_awp", "weapon_ssg08", "weapon_p90", "weapon_bizon", "weapon_nova",
        "weapon_xm1014", "weapon_sawedoff", "weapon_mag7", "weapon_revolver"
    ];

    internal static bool PrefersBody(AimMode mode, string? weapon) => mode == AimMode.Body
        || (mode == AimMode.Head ? weapon == "weapon_awp" : weapon != null && BodyWeapons.Contains(weapon));

    internal static bool TrySelect<T>(ReadOnlySpan<Capsule> capsules, bool bodyFirst,
        ref T probe, out Vector3 point) where T : struct, IAimProbe
    {
        point = default;
        // Reject an incomplete/invalid snapshot rather than using its valid prefix.
        if (capsules.IsEmpty || capsules.Length > 32) return false;
        foreach (ref readonly var capsule in capsules) if (!capsule.Valid) return false;
        foreach (int group in bodyFirst ? BodyFirst : HeadFirst)
        {
            foreach (ref readonly var capsule in capsules)
            {
                if (capsule.Group != group) continue;
                var state = probe.Query(capsule.Center, out int actualGroup);
                if (state == ProbeState.Failed) return false;
                // A clear ray or a different body part is not a confirmed hit on this group.
                if (state != ProbeState.Hit || actualGroup != group) continue;
                point = capsule.Center;
                return true;
            }
        }
        return false;
    }
}
