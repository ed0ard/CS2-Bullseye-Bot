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
internal enum AimPreference { Head, Jaw, Body }
internal enum ProbeState { Failed, Miss, Hit }
internal interface IAimProbe
{
    ProbeState Query(Vector3 point, out int actualGroup);
}

// Fixed preferences only: no penetration, damage scoring, edge search or history.
internal static class AimSelection
{
    private const int Jaw = 0; // Strategy point, not an engine hitgroup or a separate hitbox.
    private static readonly int[] HeadFirst = [1, 8, Jaw, 2, 3, 4, 5, 6, 7];
    private static readonly int[] JawFirst = [Jaw, 8, 1, 2, 3, 4, 5, 6, 7];
    private static readonly int[] BodyFirst = [3, 2, 4, 5, Jaw, 8, 1, 6, 7];
    private static readonly HashSet<string> BodyWeapons =
    [
        "weapon_awp", "weapon_ssg08", "weapon_p90", "weapon_bizon", "weapon_nova",
        "weapon_xm1014", "weapon_sawedoff", "weapon_mag7", "weapon_revolver"
    ];

    internal static bool PrefersBody(AimMode mode, string? weapon) => mode == AimMode.Body
        || (mode == AimMode.Head ? weapon == "weapon_awp" : weapon != null && BodyWeapons.Contains(weapon));

    internal static AimPreference PreferenceFor(AimMode mode, string? weapon) => PrefersBody(mode, weapon)
        ? AimPreference.Body : mode == AimMode.Mixed ? AimPreference.Jaw : AimPreference.Head;

    internal static bool TryJawPoint(ReadOnlySpan<Capsule> capsules, out Vector3 point)
    {
        point = default;
        Capsule head = default, neck = default;
        foreach (ref readonly var capsule in capsules)
        {
            if (capsule.Group == 1 && !head.Valid) head = capsule;
            if (capsule.Group == 8 && !neck.Valid) neck = capsule;
        }
        if (!head.Valid || !neck.Valid) return false;
        Vector3 towardHead = head.Center - neck.Center;
        float distanceSquared = towardHead.LengthSquared();
        if (!float.IsFinite(distanceSquared) || distanceSquared <= 1e-6f) return false;
        // JAW is a deliberate lower aiming bias, not a native anatomical landmark.
        // Up to half a neck radius toward the head stays inside the neck capsule.
        // Cap at half the center distance so overlapping poses cannot overshoot the head.
        // This follows rotation/translation/scale without eye-height guesses.
        float distance = MathF.Sqrt(distanceSquared);
        point = neck.Center + towardHead / distance * MathF.Min(neck.Radius * .5f, distance * .5f);
        return Capsule.Finite(point);
    }

    internal static bool TrySelect<T>(ReadOnlySpan<Capsule> capsules, AimPreference preference,
        ref T probe, out Vector3 point) where T : struct, IAimProbe
    {
        point = default;
        // Reject an incomplete/invalid snapshot rather than using its valid prefix.
        if (capsules.IsEmpty || capsules.Length > 32) return false;
        foreach (ref readonly var capsule in capsules) if (!capsule.Valid) return false;
        int[] order = preference switch
        {
            AimPreference.Body => BodyFirst,
            AimPreference.Jaw => JawFirst,
            _ => HeadFirst
        };
        foreach (int group in order)
        {
            if (group == Jaw)
            {
                if (!TryJawPoint(capsules, out var jaw)) continue;
                var state = probe.Query(jaw, out int actualGroup);
                if (state == ProbeState.Failed) return false;
                // Keep the original compromise: a neck hit is valid, not a failed headshot.
                if (state == ProbeState.Hit && (actualGroup is 1 or 8)) { point = jaw; return true; }
                continue;
            }
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
