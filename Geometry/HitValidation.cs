// SPDX-License-Identifier: AGPL-3.0-only
namespace BotAimImprover.Geometry;

internal static class HitValidation
{
    // A no-hit trace is clear air, not evidence that a candidate hits the target.
    internal static ProbeState Classify(float fraction, bool startSolid, bool didHit,
        nint hitEntity, nint targetEntity, nint hitbox)
    {
        if (!float.IsFinite(fraction) || fraction < 0 || fraction > 1) return ProbeState.Failed;
        return startSolid || !didHit || targetEntity == 0 || hitEntity != targetEntity || hitbox == 0
            ? ProbeState.Miss : ProbeState.Hit;
    }
}
