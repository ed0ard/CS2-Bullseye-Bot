using CounterStrikeSharp.API;
using CounterStrikeSharp.API.Core;
using CounterStrikeSharp.API.Core.Attributes;
using CounterStrikeSharp.API.Modules.Memory;
using CounterStrikeSharp.API.Modules.Memory.DynamicFunctions;
using CounterStrikeSharp.API.Modules.Utils;
using Microsoft.Extensions.Logging;

namespace BotAimImprover;

[MinimumApiVersion(305)]
public class BotAimImprover : BasePlugin
{
    public override string ModuleName => "BotAimImprover";
    public override string ModuleVersion => "2.1.8";
    public override string ModuleAuthor => "ed0ard & htfy96 & XBribo & unicbm";
    public override string ModuleDescription => "Restores intelligent aim part selection for CS2 bots.";

    private MemoryFunctionVoid<IntPtr>? _pickNewAimSpot;
    private AimMode _aimMode = AimMode.MIXED;
    private bool _firstOverrideLogged;

    public override void Load(bool hotReload)
    {
        try
        {
            if (Schema.GetSchemaOffset("CCSBot", "m_targetSpot") <= 0
                || Schema.GetSchemaOffset("CCSBot", "m_enemy") <= 0
                || Schema.GetSchemaOffset("CCSBot", "m_isEnemyVisible") <= 0
                || Schema.GetSchemaOffset("CBot", "m_pPlayer") <= 0
                || Schema.GetSchemaOffset("CCSPlayerPawn", "m_pBot") <= 0
                || Schema.GetSchemaOffset("CBasePlayerPawn", "m_hController") <= 0
                || Schema.GetSchemaOffset("CCSPlayerController", "m_hPlayerPawn") <= 0)
                throw new InvalidOperationException("Required bot Schema fields are missing.");

            var data = Gamedata.Load(Path.Combine(ModuleDirectory, "gamedata.json"));
            var pick = new MemoryFunctionVoid<IntPtr>(data.GetSignature("PickNewAimSpot"));
            if (pick.Handle == IntPtr.Zero)
                throw new InvalidOperationException("PickNewAimSpot signature resolved to zero address.");

            pick.Hook(OnPickNewAimSpotPost, HookMode.Post);
            _pickNewAimSpot = pick;
            Logger.LogInformation("[BotAimImprover] Loaded ({Plat}). PickNewAimSpot=0x{Pna:X16}",
                data.Platform == "windows" ? "Windows" : "Linux", pick.Handle.ToInt64());
        }
        catch (Exception ex)
        {
            Logger.LogError(ex, "[BotAimImprover] Fatal error during Load() (signature or Schema unavailable). Plugin inactive.");
            return;
        }

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
        _pickNewAimSpot?.Unhook(OnPickNewAimSpotPost, HookMode.Post);
        _pickNewAimSpot = null;
    }

    private HookResult OnPickNewAimSpotPost(DynamicHook hook)
    {
        try
        {
            nint address = hook.GetParam<IntPtr>(0);
            if (address == 0) return HookResult.Continue;

            var bot = new CCSBot(address);
            if (!bot.IsEnemyVisible) return HookResult.Continue;
            var enemy = bot.Enemy;
            if (enemy.Index is 0 or >= 4096 || enemy.Value is not { IsValid: true } target)
                return HookResult.Continue;

            var pawn = bot.Player;
            if (pawn is not { IsValid: true } || pawn.Bot?.Handle != address)
                return HookResult.Continue;
            var controller = pawn.Controller.Value?.As<CCSPlayerController>();
            if (controller is not { IsValid: true, IsBot: true, Connected: PlayerConnectedState.Connected }
                || controller.PlayerPawn.Raw != pawn.EntityHandle.Raw || pawn.AbsOrigin is not { } origin)
                return HookResult.Continue;

            var eye = new Vector(origin.X, origin.Y, origin.Z + (pawn.ViewOffset?.Z ?? 64f));
            string? weapon = pawn.WeaponServices?.ActiveWeapon.Value?.DesignerName;
            if (AimSelection.FirstVisible(target, eye, _aimMode, weapon) is not { } chosen)
                return HookResult.Continue;

            var spot = bot.TargetSpot;
            spot.X = chosen.Position.X;
            spot.Y = chosen.Position.Y;
            spot.Z = chosen.Position.Z;
            if (!_firstOverrideLogged)
            {
                _firstOverrideLogged = true;
                Logger.LogInformation("[BotAimImprover] Active: first override (weapon={W} point={P}).",
                    weapon ?? "(null)", chosen.Name);
            }
        }
        catch (Exception ex)
        {
            Logger.LogError(ex, "[BotAimImprover] Exception in PostHook");
        }

        return HookResult.Continue;
    }
}
