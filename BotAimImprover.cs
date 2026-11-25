using System.Numerics;
using CounterStrikeSharp.API;
using CounterStrikeSharp.API.Core;
using CounterStrikeSharp.API.Core.Attributes;
using CounterStrikeSharp.API.Modules.Memory.DynamicFunctions;
using CounterStrikeSharp.API.Modules.Utils;
using Microsoft.Extensions.Logging;
using static CounterStrikeSharp.API.Core.HitGroup_t;
using Vector = CounterStrikeSharp.API.Modules.Utils.Vector;

namespace BotAimImprover;

[MinimumApiVersion(375)]
public sealed unsafe class BotAimImprover : BasePlugin
{
    public override string ModuleName => "BotAimImprover";
    public override string ModuleVersion => "2.2.0";
    public override string ModuleAuthor => "ed0ard & htfy96 & XBribo & unicbm";
    public override string ModuleDescription => "Selects visible aim points on animated player hitboxes.";

    private enum AimMode { MIXED, HEAD, BODY }
    private AimMode _aimMode = AimMode.MIXED;
    private static readonly TraceOptions Shot = new() { InteractsWith = Masks.Shot };
    private static readonly HitGroup_t[] HeadFirst =
        [HITGROUP_HEAD, HITGROUP_NECK, HITGROUP_CHEST, HITGROUP_STOMACH, HITGROUP_LEFTARM, HITGROUP_RIGHTARM, HITGROUP_LEFTLEG, HITGROUP_RIGHTLEG];
    private static readonly HitGroup_t[] JawFirst = [HITGROUP_NECK, HITGROUP_HEAD, .. HeadFirst[2..]];
    private static readonly HitGroup_t[] BodyFirst =
        [HITGROUP_STOMACH, HITGROUP_CHEST, HITGROUP_LEFTARM, HITGROUP_RIGHTARM, HITGROUP_NECK, HITGROUP_HEAD, HITGROUP_LEFTLEG, HITGROUP_RIGHTLEG];
    private static readonly HashSet<string> BodyWeapons =
        ["weapon_awp", "weapon_ssg08", "weapon_p90", "weapon_bizon", "weapon_nova", "weapon_xm1014",
         "weapon_sawedoff", "weapon_mag7", "weapon_revolver"];

    private MemoryFunctionVoid<nint> pick = null!;
    private MemoryFunctionWithReturn<nint, nint, int, nint> part = null!;
    private MemoryFunctionWithReturn<nint, nint, byte, nint, byte> visible = null!;
    private Hitboxes poses = null!;
    private bool active, logged;

    public override void Load(bool hotReload)
    {
        poses = new Hitboxes(Signatures.Boxes);
        pick = new(Signatures.Pick);
        part = new(Signatures.Part);
        visible = new(Signatures.Visible);
        if (pick.Handle == 0 || part.Handle == 0 || visible.Handle == 0)
            throw new InvalidOperationException("Missing bot aim or visibility function.");

        part.Hook(OnGetPartPosition, HookMode.Post);
        try { pick.Hook(OnPickNewAimSpot, HookMode.Post); }
        catch { part.Unhook(OnGetPartPosition, HookMode.Post); throw; }
        active = true;

        AddCommand("bot_aim", "Set bot aim mode: head, body, mixed", (caller, info) =>
        {
            string arg = info.ArgCount > 1 ? info.GetArg(1).Trim().ToLowerInvariant() : "";
            string reply;
            switch (arg)
            {
                case "head":
                    _aimMode = AimMode.HEAD;
                    reply = "[BotAimImprover] aim mode -> HEAD (always head-first)";
                    break;
                case "body":
                    _aimMode = AimMode.BODY;
                    reply = "[BotAimImprover] aim mode -> BODY (always body-first)";
                    break;
                case "mixed":
                    _aimMode = AimMode.MIXED;
                    reply = "[BotAimImprover] aim mode -> MIXED (default)";
                    break;
                default:
                    reply = $"[BotAimImprover] Current aim mode: {_aimMode}. Valid values: head, body, mixed";
                    break;
            }
            Server.PrintToConsole(reply);
        });
    }

    public override void Unload(bool hotReload)
    {
        if (!active) return;
        try { pick.Unhook(OnPickNewAimSpot, HookMode.Post); }
        finally { part.Unhook(OnGetPartPosition, HookMode.Post); }
        active = false;
    }

    private readonly record struct Sight(CCSBot Bot, CCSPlayerPawn Shooter, CCSPlayerPawn Target, int TargetSlot, Vector Eye)
    {
        internal HitGroup_t Hit(Vector3 point)
        {
            var result = Trace.TraceEndShape(Eye, new Vector((nint)(&point)), Shooter, Shot);
            if (result.IsAllSolid || !(result.Fraction >= 0 && result.Fraction < 1) || result.Hitbox() == 0 ||
                result.HitEntity().Handle != Target.Handle) return HITGROUP_INVALID;
            return ((Hitboxes.Hitbox*)result.Hitbox())->Group;
        }
    }

    private static bool TrySight(CCSBot bot, CCSPlayerPawn? target, out Sight sight)
    {
        sight = default;
        if (target is not { IsValid: true, Health: > 0, LifeState: 0 } || target.GunGameImmunity ||
            bot.Player is not { IsValid: true, Health: > 0, LifeState: 0 } shooter || shooter.TeamNum is not (2 or 3) ||
            target.TeamNum is not (2 or 3) || shooter.TeamNum == target.TeamNum ||
            bot.Controller is not { IsValid: true, IsBot: true } controller || controller.ControllingBot ||
            controller.HasBeenControlledByPlayerThisRound || controller.PlayerPawn.Value?.Handle != shooter.Handle ||
            target.Controller.Value is not { IsValid: true } owner)
            return false;
        int slot = owner.As<CCSPlayerController>().Slot;
        if ((uint)slot >= Hitboxes.Slots) return false;
        sight = new(bot, shooter, target, slot, bot.EyePosition);
        return true;
    }

    // Native player visibility calls GetPartPosition(HEAD) before its point LOS
    // check. Supplying the crown here also works before m_enemy has been set.
    // The caller still applies its FOV, smoke checks and visible-parts mask.
    private HookResult OnGetPartPosition(DynamicHook hook)
    {
        const int nativeHeadPart = 2;
        if (hook.GetParam<int>(2) != nativeHeadPart) return HookResult.Continue;
        nint original = hook.GetReturn<nint>(), target = hook.GetParam<nint>(1);
        if (original == 0 || target == 0 ||
            !TrySight(new CCSBot(hook.GetParam<nint>(0)), new CCSPlayerPawn(target), out var sight))
            return HookResult.Continue;

        if (sight.Hit(*(Vector3*)original) == HITGROUP_HEAD) return HookResult.Continue;
        foreach (ref readonly var box in poses.For(sight.Target, sight.TargetSlot))
        {
            if (box.Group != HITGROUP_HEAD || sight.Hit(box.Crown) != HITGROUP_HEAD) continue;
            hook.SetReturn(poses.CrownReference(sight.TargetSlot, box.Crown));
            return HookResult.Changed;
        }
        return HookResult.Continue;
    }

    private HookResult OnPickNewAimSpot(DynamicHook hook)
    {
        var bot = new CCSBot(hook.GetParam<nint>(0));
        if (!bot.IsEnemyVisible || !TrySight(bot, bot.Enemy.Value, out var sight))
            return HookResult.Continue;

        var boxes = poses.For(sight.Target, sight.TargetSlot);
        var weapon = sight.Shooter.WeaponServices?.ActiveWeapon.Value?.DesignerName;
        bool body = _aimMode == AimMode.BODY || weapon == "weapon_awp" ||
                    (_aimMode == AimMode.MIXED && weapon != null && BodyWeapons.Contains(weapon));
        bool jawFirst = _aimMode == AimMode.MIXED && !body;
        if (jawFirst)
        {
            // Mixed rifles traditionally bias toward the jaw. Follow the posed
            // neck toward the head while keeping the candidate inside the neck.
            Capsule head = default, neck = default;
            foreach (ref readonly var box in boxes)
            {
                if (box.Group == HITGROUP_HEAD) head = box;
                if (box.Group == HITGROUP_NECK) neck = box;
            }
            if (head.Radius > 0 && neck.Radius > 0)
            {
                var delta = head.Center - neck.Center;
                float distance = delta.Length();
                if (distance > 0)
                {
                    float offset = 0.5f * Math.Min(distance, neck.Radius);
                    if (AimAt(sight, neck.Center + delta * (offset / distance), HITGROUP_NECK, allowHead: true))
                        return HookResult.Continue;
                }
            }
        }

        var order = body ? BodyFirst : jawFirst ? JawFirst : HeadFirst;
        foreach (var group in order)
        {
            foreach (ref readonly var box in boxes)
            {
                if (box.Group != group) continue;
                if (AimAt(sight, box.Center, group) || (group == HITGROUP_HEAD && AimAt(sight, box.Crown, group)))
                    return HookResult.Continue;
            }
        }
        return HookResult.Continue;
    }

    private bool AimAt(Sight sight, Vector3 point, HitGroup_t group, bool allowHead = false)
    {
        var hit = sight.Hit(point);
        if (hit != group && !(allowHead && hit == HITGROUP_HEAD)) return false;
        if (visible.Invoke(sight.Bot.Handle, (nint)(&point), 1, 0) == 0) return false;
        var spot = sight.Bot.TargetSpot;
        spot.X = point.X;
        spot.Y = point.Y;
        spot.Z = point.Z;
        if (!logged)
        {
            logged = true;
            Logger.LogInformation("[BotAimImprover] First hitbox override ({HitGroup}).", hit);
        }
        return true;
    }
}
