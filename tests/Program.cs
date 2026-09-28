using System.Numerics;
using BotAimImprover.Geometry;

int checks = 0;
void Check(bool value, string description)
{
    if (!value) throw new Exception(description);
    checks++;
}
Capsule C(int group, float z, int index) => new(new(0, 0, z), new(0, 0, z + 2), 3, group, index);
Capsule[] body = [C(3, 35, 0), C(2, 45, 1), C(8, 55, 2), C(1, 65, 3)];
var probe = new Probe([(ProbeState.Hit, 1)]);
Check(AimSelection.TrySelect(body, false, ref probe, out var point) && point.Z == 66, "head-first uses head, not input order");
probe = new([(ProbeState.Hit, 3)]);
Check(AimSelection.TrySelect(body, true, ref probe, out point) && point.Z == 36, "body-first uses stomach");
probe = new([(ProbeState.Miss, 0), (ProbeState.Miss, 0), (ProbeState.Hit, 2)]);
Check(AimSelection.TrySelect(body, false, ref probe, out point) && point.Z == 46, "blocked head and neck fall through to chest");
probe = new([(ProbeState.Hit, 2), (ProbeState.Hit, 8)]);
Check(AimSelection.TrySelect(body, false, ref probe, out point) && point.Z == 56, "head candidate resolving to chest is not a head hit");
probe = new([(ProbeState.Miss, 0)]);
Check(!AimSelection.TrySelect(body, false, ref probe, out _), "no hit (including clear air) preserves native aim");
probe = new([(ProbeState.Failed, 0), (ProbeState.Hit, 8)]);
Check(!AimSelection.TrySelect(body, false, ref probe, out _) && probe.Calls == 1, "failed query stops without selecting an untested point");
probe = new([(ProbeState.Hit, 1)]);
Capsule[] moved = [new(new(20, -5, 42), new(24, -5, 44), 4.3f, 1, 0)];
Check(AimSelection.TrySelect(moved, false, ref probe, out point) && point == new Vector3(22, -5, 43), "animated pose is used without eye-height estimates");
probe = new([(ProbeState.Hit, 1)]);
Capsule[] invalid = [body[3], new(new(float.NaN, 0, 0), Vector3.Zero, 3, 2, 1)];
Check(!AimSelection.TrySelect(invalid, false, ref probe, out _) && probe.Calls == 0, "invalid snapshot is rejected as a whole");
probe = new([(ProbeState.Miss, 0)]);
var maximum = Enumerable.Range(0, 32).Select(i => C(1, i, i)).ToArray();
Check(!AimSelection.TrySelect(maximum, false, ref probe, out _) && probe.Calls == 32, "one trace per capsule, bounded at 32");
probe = new([(ProbeState.Hit, 1)]);
Check(!AimSelection.TrySelect(new Capsule[33], false, ref probe, out _) && probe.Calls == 0, "oversized snapshot rejected");
Check(!AimSelection.PrefersBody(AimMode.Mixed, "weapon_ak47"), "mixed rifle keeps head preference");
Check(AimSelection.PrefersBody(AimMode.Mixed, "weapon_ssg08"), "mixed sniper keeps body preference");
Check(AimSelection.PrefersBody(AimMode.Head, "weapon_awp"), "head mode preserves upstream AWP exception");
Check(!AimSelection.PrefersBody(AimMode.Head, "weapon_ssg08"), "head mode scout aims head");
Check(AimSelection.PrefersBody(AimMode.Body, null), "body mode independent of weapon");
Check(HitValidation.Classify(1, false, false, 0, 10, 0) == ProbeState.Miss, "unobstructed air is not a target hit");
Check(HitValidation.Classify(.5f, false, true, 11, 10, 12) == ProbeState.Miss, "another player blocks candidate");
Check(HitValidation.Classify(.5f, false, true, 0, 10, 0) == ProbeState.Miss, "world collision blocks candidate");
Check(HitValidation.Classify(.5f, false, true, 10, 10, 0) == ProbeState.Miss, "target hull without hitbox rejected");
Check(HitValidation.Classify(0, true, true, 10, 10, 12) == ProbeState.Miss, "starting solid rejected");
Check(HitValidation.Classify(float.NaN, false, true, 10, 10, 12) == ProbeState.Failed, "invalid trace fails closed");
Check(HitValidation.Classify(-.1f, false, true, 10, 10, 12) == ProbeState.Failed, "negative fraction invalid");
Check(HitValidation.Classify(1.1f, false, true, 10, 10, 12) == ProbeState.Failed, "oversized fraction invalid");
Check(HitValidation.Classify(.9999f, false, true, 10, 10, 12) == ProbeState.Hit, "actual target hit accepted near endpoint");
Console.WriteLine($"Aim selection: {checks} checks passed");
if (args.Length == 1) InteropSmoke.Run(args[0]);

internal struct Probe((ProbeState State, int Group)[] responses) : IAimProbe
{
    internal int Calls;
    public ProbeState Query(Vector3 point, out int actualGroup)
    {
        var response = responses[Math.Min(Calls++, responses.Length - 1)];
        actualGroup = response.Group;
        return response.State;
    }
}
