using System.Runtime.InteropServices;
using BotAimImprover.Geometry;
using CounterStrikeSharp.API;
using CounterStrikeSharp.API.Core;
using CounterStrikeSharp.API.Core.Attributes;
using CounterStrikeSharp.API.Modules.Commands;
using CounterStrikeSharp.API.Modules.Memory.DynamicFunctions;
using CounterStrikeSharp.API.Modules.Utils;
using Microsoft.Extensions.Logging;
using Vec3 = System.Numerics.Vector3;

namespace BotAimImprover;

[MinimumApiVersion(375)]
public class BotAimImprover : BasePlugin
{
    public override string ModuleName => "BotAimImprover";
    public override string ModuleVersion => "3.0.0";
    public override string ModuleAuthor => "ed0ard & htfy96 & XBribo & unicbm";
    public override string ModuleDescription => "Pose-aware, validated aim part selection for CS2 bots.";

    private MemoryFunctionVoid<IntPtr>? pick;
    private NativeBindings? native;
    private PawnHitboxes? geometry;
    private AimMode mode = AimMode.Mixed;
    private bool hooked, faultLogged;
    private string status = "native aim";
    private long overrides, fallbacks;
    private int budgetTick = -1, raysThisTick;
    private const int MaxRaysPerTick = 256;
    private static readonly TraceOptions ShotOptions = new() { InteractsWith = Masks.Shot };

    public override void Load(bool hotReload)
    {
        AddCommand("bot_aim", "Set bot aim mode: head, body, mixed; or status", OnAimCommand);

        try
        {
            native = new NativeBindings(ModuleDirectory);
            geometry = new PawnHitboxes(native);
            pick = new MemoryFunctionVoid<IntPtr>(native.Data.PickSignature);
            if (pick.Handle != native.PickAddress)
                throw new InvalidOperationException("PickNewAimSpot signature resolved outside the verified entry");
            pick.Hook(OnPickNewAimSpotPost, HookMode.Post);
            hooked = true;
            status = "pose geometry ready (Windows x64)";
            Logger.LogInformation("[BotAimImprover] {Status}; {Build}", status, native.Data.Build);
        }
        catch (Exception ex)
        {
            geometry = null;
            native?.Dispose();
            native = null;
            status = "unavailable; native aim retained";
            Logger.LogWarning(ex, "[BotAimImprover] {Status}", status);
        }
    }

    public override void Unload(bool hotReload)
    {
        // If removing the hook fails, do not unload code still reachable by it.
        if (hooked)
        {
            pick!.Unhook(OnPickNewAimSpotPost, HookMode.Post);
            hooked = false;
        }

        geometry = null;
        native?.Dispose();
        native = null;
    }

    private void OnAimCommand(CCSPlayerController? caller, CommandInfo command)
    {
        string argument = command.ArgCount > 1 ? command.GetArg(1).Trim().ToLowerInvariant() : "";
        mode = argument switch
        {
            "head" => AimMode.Head,
            "body" => AimMode.Body,
            "mixed" => AimMode.Mixed,
            _ => mode
        };

        Server.PrintToConsole($"[BotAimImprover] mode={mode}; {status}; overrides={overrides}; fallbacks={fallbacks}. "
            + "Commands: head, body, mixed, status. Head mode preserves the AWP body preference.");
    }

    private HookResult OnPickNewAimSpotPost(DynamicHook hook)
    {
        if (geometry == null || native == null) return HookResult.Continue;
        try
        {
            nint address = hook.GetParam<IntPtr>(0);
            if (address == 0) return HookResult.Continue;
            var bot = new CCSBot(address);
            if (!bot.IsEnemyVisible) return HookResult.Continue;

            // CSS schema accessors resolve current offsets; CHandle keeps the serial number.
            var pawn = bot.Player;
            var enemy = bot.Enemy.Value;
            if (pawn is not { IsValid: true, Health: > 0 } || enemy is not { IsValid: true, Health: > 0 }
                || pawn.Handle == enemy.Handle || pawn.TeamNum == enemy.TeamNum || enemy.GunGameImmunity)
                return HookResult.Continue;

            var controller = pawn.Controller.Value?.As<CCSPlayerController>();
            if (controller is not { IsValid: true, IsBot: true } || controller.ControllingBot
                || controller.PlayerPawn.Value?.Handle != pawn.Handle || pawn.Bot?.Handle != address)
                return HookResult.Continue;

            int tick = Server.TickCount;
            if (tick != budgetTick)
            {
                budgetTick = tick;
                raysThisTick = 0;
            }

            if (raysThisTick >= MaxRaysPerTick
                || !geometry.TryGet(enemy.Handle, enemy.EntityHandle.Raw, tick, out var capsules))
            {
                fallbacks++;
                return HookResult.Continue;
            }

            Vector eye = bot.EyePosition;
            if (!Capsule.Finite(new Vec3(eye.X, eye.Y, eye.Z)))
            {
                fallbacks++;
                return HookResult.Continue;
            }

            string? weapon = pawn.WeaponServices?.ActiveWeapon.Value?.DesignerName;
            var probe = new AimProbe(this, pawn, enemy, eye, native.Data.Layout.BoxGroup);
            var preference = AimSelection.PreferenceFor(mode, weapon);
            if (!AimSelection.TrySelect(capsules, preference, ref probe, out var point))
            {
                fallbacks++;
                return HookResult.Continue;
            }

            Vector destination = bot.TargetSpot;
            destination.X = point.X;
            destination.Y = point.Y;
            destination.Z = point.Z;
            overrides++;
        }
        catch (Exception ex)
        {
            fallbacks++;
            if (!faultLogged)
            {
                faultLogged = true;
                Logger.LogWarning(ex, "[BotAimImprover] Aim query failed; preserving native result");
            }
        }

        return HookResult.Continue;
    }

    private struct AimProbe(BotAimImprover owner, CCSPlayerPawn shooter, CCSPlayerPawn target,
        Vector eye, int groupOffset) : IAimProbe
    {
        public ProbeState Query(Vec3 point, out int actualGroup)
        {
            actualGroup = 0;
            if (owner.raysThisTick >= MaxRaysPerTick) return ProbeState.Failed;
            owner.raysThisTick++;

            try
            {
                var result = Trace.TraceEndShape(eye, new Vector(point.X, point.Y, point.Z), shooter, ShotOptions);
                var state = HitValidation.Classify(result.Fraction, result.IsAllSolid, result.DidHit(),
                    result.HitEntity().Handle, target.Handle, result.Hitbox());
                if (state != ProbeState.Hit) return state;

                // Use engine-returned CHitBox metadata, not the candidate's label.
                actualGroup = Marshal.ReadInt32(result.Hitbox() + groupOffset);
                return actualGroup is >= 1 and <= 8 ? ProbeState.Hit : ProbeState.Failed;
            }
            catch
            {
                return ProbeState.Failed;
            }
        }
    }
}
