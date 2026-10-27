using System.Runtime.InteropServices;
using BotAimImprover.Geometry;
using CounterStrikeSharp.API;
using CounterStrikeSharp.API.Core;
using CounterStrikeSharp.API.Core.Attributes;
using CounterStrikeSharp.API.Modules.Commands;
using CounterStrikeSharp.API.Modules.Memory;
using CounterStrikeSharp.API.Modules.Memory.DynamicFunctions;
using CounterStrikeSharp.API.Modules.Utils;
using Microsoft.Extensions.Logging;
using Vec3 = System.Numerics.Vector3;

namespace BotAimImprover;

[MinimumApiVersion(378)]
public class BotAimImprover : BasePlugin
{
    public override string ModuleName => "BotAimImprover";
    public override string ModuleVersion => "3.0.0-preview.1";
    public override string ModuleAuthor => "ed0ard & htfy96 & XBribo & unicbm";
    public override string ModuleDescription => "Pose-aware, validated aim part selection for CS2 bots.";

    private MemoryFunctionVoid<IntPtr>? pick;
    private NativeBindings? native;
    private PawnHitboxes? geometry;
    private AimMode mode = AimMode.Mixed;
    private bool hooked, faultLogged;
    private string status = "native aim";
    private long overrides, fallbacks;
    private int budgetTick = -1, queriesThisTick;
    private const int MaxQueriesPerTick = 256;
    private static readonly TraceOptions ShotOptions = new() { InteractsWith = Masks.Shot };

    public override void Load(bool hotReload)
    {
        AddCommand("bot_aim", "Set bot aim mode: head, body, mixed; or status", OnAimCommand);

        try
        {
            // Keep 2.1.8's startup Schema validation before installing the hook.
            if (Schema.GetSchemaOffset("CCSPlayerPawn", "m_pBot") <= 0
                || Schema.GetSchemaOffset("CBot", "m_pPlayer") <= 0
                || Schema.GetSchemaOffset("CCSBot", "m_targetSpot") <= 0
                || Schema.GetSchemaOffset("CCSBot", "m_enemy") <= 0
                || Schema.GetSchemaOffset("CCSBot", "m_isEnemyVisible") <= 0
                || Schema.GetSchemaOffset("CCSBot", "m_eyePosition") <= 0)
                throw new InvalidOperationException("Required bot Schema fields are missing.");
            native = new NativeBindings(ModuleDirectory);
            if (Schema.GetSchemaOffset("CBaseEntity", "m_pGameSceneNode") != native.Data.Layout.PawnSceneNode)
                throw new InvalidOperationException("Pawn scene node differs from the audited hitbox layout.");
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
                || controller.HasBeenControlledByPlayerThisRound
                || controller.PlayerPawn.Value?.Handle != pawn.Handle || pawn.Bot?.Handle != address)
                return HookResult.Continue;

            int tick = Server.TickCount;
            if (tick != budgetTick)
            {
                budgetTick = tick;
                queriesThisTick = 0;
            }

            if (queriesThisTick >= MaxQueriesPerTick
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
            var probe = new AimProbe(this, native, address, pawn, enemy, eye);
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

    private struct AimProbe(BotAimImprover owner, NativeBindings native, nint bot,
        CCSPlayerPawn shooter, CCSPlayerPawn target, Vector eye) : IAimProbe
    {
        public unsafe ProbeState Query(Vec3 point, out int actualGroup)
        {
            actualGroup = 0;
            if (owner.queriesThisTick >= MaxQueriesPerTick) return ProbeState.Failed;
            owner.queriesThisTick++;

            // Borrow the stack point for this synchronous call; no native Vector allocation.
            var result = Trace.TraceEndShape(eye, new Vector((nint)(&point)), shooter, ShotOptions);
            var state = HitValidation.Classify(result.Fraction, result.IsAllSolid, result.DidHit(),
                result.HitEntity().Handle, target.Handle, result.Hitbox());
            if (state != ProbeState.Hit) return state;

            // Use engine-returned CHitBox metadata, not the candidate's label.
            actualGroup = Marshal.ReadInt32(result.Hitbox() + native.Data.Layout.BoxGroup);
            if (actualGroup is < 1 or > 8) return ProbeState.Failed;

            // A shot ray does not enforce FOV or smoke. Keep the live visibility
            // hook chain and the native player scan's null-ignore semantics.
            if (owner.queriesThisTick >= MaxQueriesPerTick) return ProbeState.Failed;
            owner.queriesThisTick++;
            return native.IsVisible(bot, in point, 1, 0) != 0 ? ProbeState.Hit : ProbeState.Miss;
        }
    }
}
