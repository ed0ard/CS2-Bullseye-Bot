// SPDX-License-Identifier: AGPL-3.0-only
using System.Numerics;

namespace BotAimImprover.Geometry;

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
    // Positive values are native hitgroups; Jaw is a strategy point, not a hitbox.
    private enum Region
    {
        Jaw = 0,
        Head = 1,
        Chest = 2,
        Stomach = 3,
        LeftArm = 4,
        RightArm = 5,
        LeftLeg = 6,
        RightLeg = 7,
        Neck = 8
    }

    private static readonly Region[] HeadFirst =
    [
        Region.Head, Region.Neck, Region.Jaw, Region.Chest, Region.Stomach,
        Region.LeftArm, Region.RightArm, Region.LeftLeg, Region.RightLeg
    ];

    private static readonly Region[] JawFirst =
    [
        Region.Jaw, Region.Neck, Region.Head, Region.Chest, Region.Stomach,
        Region.LeftArm, Region.RightArm, Region.LeftLeg, Region.RightLeg
    ];

    private static readonly Region[] BodyFirst =
    [
        Region.Stomach, Region.Chest, Region.LeftArm, Region.RightArm,
        Region.Jaw, Region.Neck, Region.Head, Region.LeftLeg, Region.RightLeg
    ];

    private static readonly HashSet<string> BodyWeapons =
    [
        "weapon_awp", "weapon_ssg08", "weapon_p90", "weapon_bizon", "weapon_nova",
        "weapon_xm1014", "weapon_sawedoff", "weapon_mag7", "weapon_revolver"
    ];

    internal static AimPreference PreferenceFor(AimMode mode, string? weapon) => mode switch
    {
        AimMode.Body => AimPreference.Body,
        AimMode.Head => weapon == "weapon_awp" ? AimPreference.Body : AimPreference.Head,
        _ when weapon != null && BodyWeapons.Contains(weapon) => AimPreference.Body,
        AimMode.Mixed => AimPreference.Jaw,
        _ => AimPreference.Head
    };

    internal static bool TryJawPoint(ReadOnlySpan<Capsule> capsules, out Vector3 point)
    {
        point = default;
        Capsule head = default;
        Capsule neck = default;
        foreach (ref readonly var capsule in capsules)
        {
            if (capsule.Group == (int)Region.Head && !head.Valid)
                head = capsule;
            if (capsule.Group == (int)Region.Neck && !neck.Valid)
                neck = capsule;
        }

        if (!head.Valid || !neck.Valid)
            return false;

        Vector3 towardHead = head.Center - neck.Center;
        float distanceSquared = towardHead.LengthSquared();
        if (!float.IsFinite(distanceSquared) || distanceSquared <= 1e-6f)
            return false;

        // JAW is a deliberate lower aiming bias, not a native anatomical landmark.
        // Up to half a neck radius toward the head stays inside the neck capsule.
        // Cap at half the center distance so overlapping poses cannot overshoot the head.
        // This follows rotation/translation/scale without eye-height guesses.
        float distance = MathF.Sqrt(distanceSquared);
        float offset = MathF.Min(neck.Radius * .5f, distance * .5f);
        point = neck.Center + towardHead / distance * offset;
        return Capsule.Finite(point);
    }

    internal static bool TrySelect<T>(ReadOnlySpan<Capsule> capsules, AimPreference preference,
        ref T probe, out Vector3 point) where T : struct, IAimProbe
    {
        point = default;
        // Reject an incomplete/invalid snapshot rather than using its valid prefix.
        if (!Capsule.ValidSnapshot(capsules))
            return false;

        Region[] order = preference switch
        {
            AimPreference.Body => BodyFirst,
            AimPreference.Jaw => JawFirst,
            _ => HeadFirst
        };
        foreach (Region region in order)
        {
            if (region == Region.Jaw)
            {
                if (!TryJawPoint(capsules, out var jaw))
                    continue;

                var state = probe.Query(jaw, out int actualGroup);
                if (state == ProbeState.Failed)
                    return false;

                // Keep the original compromise: a neck hit is valid, not a failed headshot.
                if (state == ProbeState.Hit && (actualGroup is (int)Region.Head or (int)Region.Neck))
                {
                    point = jaw;
                    return true;
                }

                continue;
            }

            foreach (ref readonly var capsule in capsules)
            {
                if (capsule.Group != (int)region)
                    continue;

                var state = probe.Query(capsule.Center, out int actualGroup);
                if (state == ProbeState.Failed)
                    return false;

                // A clear ray or a different body part is not a confirmed hit on this group.
                if (state != ProbeState.Hit || actualGroup != (int)region)
                    continue;

                point = capsule.Center;
                return true;
            }
        }

        return false;
    }
}
