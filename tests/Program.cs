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
Check(AimSelection.TrySelect(body, AimPreference.Head, ref probe, out var point) && point.Z == 66, "head-first uses head, not input order");
probe = new([(ProbeState.Hit, 3)]);
Check(AimSelection.TrySelect(body, AimPreference.Body, ref probe, out point) && point.Z == 36, "body-first uses stomach");
probe = new([(ProbeState.Miss, 0), (ProbeState.Miss, 0), (ProbeState.Hit, 2)]);
Check(AimSelection.TrySelect(body, AimPreference.Head, ref probe, out point) && point.Z == 46, "blocked head and neck fall through to chest");
probe = new([(ProbeState.Hit, 2), (ProbeState.Hit, 8)]);
Check(AimSelection.TrySelect(body, AimPreference.Head, ref probe, out point) && point.Z == 56, "head candidate resolving to chest is not a head hit");
probe = new([(ProbeState.Miss, 0)]);
Check(!AimSelection.TrySelect(body, AimPreference.Head, ref probe, out _), "no hit (including clear air) preserves native aim");
probe = new([(ProbeState.Failed, 0), (ProbeState.Hit, 8)]);
Check(!AimSelection.TrySelect(body, AimPreference.Head, ref probe, out _) && probe.Calls == 1, "failed query stops without selecting an untested point");
probe = new([(ProbeState.Hit, 1)]);
Capsule[] moved = [new(new(20, -5, 42), new(24, -5, 44), 4.3f, 1, 0)];
Check(AimSelection.TrySelect(moved, AimPreference.Head, ref probe, out point) && point == new Vector3(22, -5, 43), "animated pose is used without eye-height estimates");
probe = new([(ProbeState.Hit, 1)]);
Capsule[] invalid = [body[3], new(new(float.NaN, 0, 0), Vector3.Zero, 3, 2, 1)];
Check(!AimSelection.TrySelect(invalid, AimPreference.Head, ref probe, out _) && probe.Calls == 0, "invalid snapshot is rejected as a whole");
probe = new([(ProbeState.Miss, 0)]);
var maximum = Enumerable.Range(0, 32).Select(i => C(1, i, i)).ToArray();
Check(!AimSelection.TrySelect(maximum, AimPreference.Head, ref probe, out _) && probe.Calls == 32, "one trace per capsule, bounded at 32");
probe = new([(ProbeState.Hit, 1)]);
Check(!AimSelection.TrySelect(new Capsule[33], AimPreference.Head, ref probe, out _) && probe.Calls == 0, "oversized snapshot rejected");
Check(!AimSelection.PrefersBody(AimMode.Mixed, "weapon_ak47"), "mixed rifle does not use body preference");
Check(AimSelection.PrefersBody(AimMode.Mixed, "weapon_ssg08"), "mixed sniper keeps body preference");
Check(AimSelection.PrefersBody(AimMode.Head, "weapon_awp"), "head mode preserves upstream AWP exception");
Check(!AimSelection.PrefersBody(AimMode.Head, "weapon_ssg08"), "head mode scout aims head");
Check(AimSelection.PrefersBody(AimMode.Body, null), "body mode independent of weapon");
Check(AimSelection.PreferenceFor(AimMode.Mixed, "weapon_ak47") == AimPreference.Jaw, "mixed rifle restores JAW-first");
Check(AimSelection.PreferenceFor(AimMode.Mixed, "weapon_ssg08") == AimPreference.Body, "mixed sniper remains body-first");
Check(AimSelection.PreferenceFor(AimMode.Head, "weapon_awp") == AimPreference.Body, "AWP exception survives JAW restoration");
Check(AimSelection.PreferenceFor(AimMode.Head, "weapon_ak47") == AimPreference.Head, "explicit head mode remains head-first");
Check(AimSelection.TryJawPoint(body, out var jaw) && jaw == new Vector3(0, 0, 57.5f), "JAW is a neck-internal point toward the current head");
Check(jaw != body[3].Center && jaw != body[2].Center, "JAW remains a distinct strategy point");
probe = new([(ProbeState.Hit, 8)]);
Check(AimSelection.TrySelect(body, AimPreference.Jaw, ref probe, out point) && point == jaw && probe.Calls == 1,
    "MIXED accepts native neck hit at JAW without forcing a headshot");
probe = new([(ProbeState.Hit, 1)]);
Check(AimSelection.TrySelect(body, AimPreference.Jaw, ref probe, out point) && point == jaw,
    "JAW also accepts actual head hit when head overlaps the gun line");
probe = new([(ProbeState.Miss, 0), (ProbeState.Hit, 8)]);
Check(AimSelection.TrySelect(body, AimPreference.Jaw, ref probe, out point) && point == body[2].Center && probe.Calls == 2,
    "blocked JAW falls through to neck before head");
probe = new([(ProbeState.Miss, 0), (ProbeState.Miss, 0), (ProbeState.Hit, 1)]);
Check(AimSelection.TrySelect(body, AimPreference.Jaw, ref probe, out point) && point == body[3].Center && probe.Calls == 3,
    "blocked JAW and neck fall through to head");
probe = new([(ProbeState.Hit, 2), (ProbeState.Hit, 8)]);
Check(AimSelection.TrySelect(body, AimPreference.Jaw, ref probe, out point) && point == body[2].Center && probe.Calls == 2,
    "unrelated actual hitgroup cannot authorize a JAW candidate");
probe = new([(ProbeState.Failed, 0), (ProbeState.Hit, 8)]);
Check(!AimSelection.TrySelect(body, AimPreference.Jaw, ref probe, out _) && probe.Calls == 1,
    "JAW trace failure preserves native selection");
probe = new([(ProbeState.Miss, 0), (ProbeState.Miss, 0), (ProbeState.Hit, 8)]);
Check(AimSelection.TrySelect(body, AimPreference.Body, ref probe, out point) && point == jaw && probe.Calls == 3,
    "body mode only reaches JAW after torso candidates fail");
Check(!AimSelection.TryJawPoint(moved, out _), "missing neck does not create a guessed JAW");
probe = new([(ProbeState.Hit, 1)]);
Check(AimSelection.TrySelect(moved, AimPreference.Jaw, ref probe, out point) && point == moved[0].Center && probe.Calls == 1,
    "missing neck preserves available native head candidate");
Capsule[] coincident = [C(1, 60, 0), C(8, 60, 1)];
Check(!AimSelection.TryJawPoint(coincident, out _), "coincident centers skip JAW instead of normalizing zero");
Capsule[] closeCenters = [C(1, 61, 0), C(8, 60, 1)];
Check(AimSelection.TryJawPoint(closeCenters, out var closeJaw) && closeJaw.Z == 61.5f,
    "overlapping head-neck centers cannot place JAW above the head center");
var rotation = Quaternion.CreateFromYawPitchRoll(.7f, 1.1f, -.4f);
Vector3 Pose(Vector3 v) => Vector3.Transform(v, rotation) * 2 + new Vector3(101, -53, 17);
var transformed = body.Select(c => new Capsule(Pose(c.A), Pose(c.B), c.Radius * 2, c.Group, c.Index)).ToArray();
Check(AimSelection.TryJawPoint(transformed, out var transformedJaw) && Vector3.Distance(transformedJaw, Pose(jaw)) < .0001f,
    "JAW follows pitch/yaw/roll, translation and scale without world-Z or eye-height offsets");
var neck = transformed[2];
Vector3 axis = neck.B - neck.A;
float parameter = Math.Clamp(Vector3.Dot(transformedJaw - neck.A, axis) / axis.LengthSquared(), 0, 1);
Check(Vector3.DistanceSquared(transformedJaw, neck.A + parameter * axis) < neck.Radius * neck.Radius,
    "rotated JAW stays inside the actual neck capsule");
maximum[31] = C(8, 55, 31);
probe = new([(ProbeState.Miss, 0)]);
Check(!AimSelection.TrySelect(maximum, AimPreference.Jaw, ref probe, out _) && probe.Calls == 33,
    "JAW adds at most one trace to the 32-capsule bound");
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
