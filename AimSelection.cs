using CounterStrikeSharp.API;
using CounterStrikeSharp.API.Core;
using CounterStrikeSharp.API.Modules.Utils;

namespace BotAimImprover;

internal enum AimMode { MIXED, HEAD, BODY }

internal static class AimSelection
{
    // Height is a fraction of live eye height, except feet, measured from the ground.
    // Lateral is the signed distance along the target's right vector in world units.
    private readonly record struct AimPoint(string Name, float Height, float Lateral, bool FeetAbs = false);

    private static readonly AimPoint[] _aimPoints =
    {
        new("HEAD",           1.00f,  0f),   // 0
        new("NECK",           0.97f,  0f),   // 1
        new("JAW",            0.92f,  0f),   // 2
        new("CHEST",          0.82f,  0f),   // 3
        new("GUT",            0.67f,  0f),   // 4
        new("PELVIS",         0.60f,  0f),   // 5
        new("LEFT_CHEST",     0.82f, -8f),   // 6
        new("RIGHT_CHEST",    0.82f,  8f),   // 7
        new("LEFT_SHOULDER",  0.92f, -8f),   // 8
        new("RIGHT_SHOULDER", 0.92f,  8f),   // 9
        new("LEFT_GUT",       0.67f, -7f),   // 10
        new("RIGHT_GUT",      0.67f,  7f),   // 11
        new("LEFT_THIGH",     0.38f, -5f),   // 12
        new("RIGHT_THIGH",    0.38f,  5f),   // 13
        new("LEFT_SHIN",      0.15f, -5f),   // 14
        new("RIGHT_SHIN",     0.15f,  5f),   // 15
        new("FEET",           5.0f,   0f, true), // 16  // absolute z + 5
    };
    // Priority orders (values are indices into _aimPoints), highest priority first.
    // Tiers: core > centerline > side > shoulder > limb > feet.
    // Within a tier, higher points come first. Left/right of equal height share a tier

    private static readonly int[] _priorityHead =
    {
        0, 1, 2,         // HEAD, NECK, JAW
        3, 4, 5,         // CHEST, GUT, PELVIS
        6, 7, 10, 11,    // L_CHEST, R_CHEST, L_GUT, R_GUT
        8, 9,            // L_SHOULDER, R_SHOULDER
        12, 13, 14, 15,  // L_THIGH, R_THIGH, L_SHIN, R_SHIN
        16               // FEET
    };

    private static readonly int[] _priorityJaw =
    {
        2, 1, 0,         // JAW, NECK, HEAD
        3, 4, 5,         // CHEST, GUT, PELVIS
        6, 7, 10, 11,    // L_CHEST, R_CHEST, L_GUT, R_GUT
        8, 9,            // L_SHOULDER, R_SHOULDER
        12, 13, 14, 15,  // L_THIGH, R_THIGH, L_SHIN, R_SHIN
        16               // FEET
    };

    private static readonly int[] _priorityTrunk =
    {
        4, 5, 3,         // GUT, PELVIS, CHEST,
        10, 11, 6, 7,    // L_GUT, R_GUT, L_CHEST, R_CHEST
        8, 9,            // L_SHOULDER, R_SHOULDER
        2, 1, 0,         // JAW, NECK, HEAD
        12, 13, 14, 15,  // L_THIGH, R_THIGH, L_SHIN, R_SHIN
        16               // FEET
    };

    private static readonly int[] _priorityBody =
    {
        4, 5, 10, 11,    // GUT, PELVIS, L_GUT, R_GUT
        3, 6, 7,         // CHEST, L_CHEST, R_CHEST
        8, 9,            // L_SHOULDER, R_SHOULDER
        2, 1, 0,         // JAW, NECK, HEAD
        12, 13, 14, 15,  // L_THIGH, R_THIGH, L_SHIN, R_SHIN
        16               // FEET
    };

    private static readonly HashSet<string> _bodyFirstWeapons = new()
    {
        "weapon_awp", "weapon_ssg08", "weapon_p90", "weapon_bizon",
        "weapon_nova", "weapon_xm1014", "weapon_sawedoff", "weapon_mag7", "weapon_revolver"
    };

    private static readonly TraceOptions WorldOnly = new() { InteractsWith = Masks.SolidBrushOnly };

    internal static (Vector Position, string Name)? FirstVisible(CCSPlayerPawn target, Vector eye, AimMode mode, string? weapon)
    {
        if (target.AbsOrigin is not { } origin) return null;
        float ox = origin.X, oy = origin.Y, oz = origin.Z;
        float eyeHeight = target.ViewOffset?.Z ?? 64f;
        double yaw = (target.EyeAngles?.Y ?? 0f) * Math.PI / 180.0;
        float rightX = (float)Math.Sin(yaw), rightY = -(float)Math.Cos(yaw);

        int[] order = mode switch
        {
            AimMode.HEAD => weapon == "weapon_awp" ? _priorityTrunk : _priorityHead,
            AimMode.BODY => _priorityBody,
            _ => weapon != null && _bodyFirstWeapons.Contains(weapon) ? _priorityTrunk : _priorityJaw
        };
        foreach (int index in order)
        {
            ref readonly var part = ref _aimPoints[index];
            float x = part.FeetAbs ? ox : ox + rightX * part.Lateral;
            float y = part.FeetAbs ? oy : oy + rightY * part.Lateral;
            float z = oz + (part.FeetAbs ? part.Height : eyeHeight * part.Height);
            if (!float.IsFinite(x) || !float.IsFinite(y) || !float.IsFinite(z)) continue;

            var point = new Vector(x, y, z);
            if (Trace.TraceEndShape(eye, point, options: WorldOnly).Fraction >= 0.999f)
                return (point, part.Name);
        }
        return null;
    }
}
