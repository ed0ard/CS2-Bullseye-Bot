using System.Collections.Concurrent;
using CounterStrikeSharp.API;
using CounterStrikeSharp.API.Core;
using CounterStrikeSharp.API.Core.Attributes;
using CounterStrikeSharp.API.Modules.Memory;
using CounterStrikeSharp.API.Modules.Utils;
using CounterStrikeSharp.API.Modules.Memory.DynamicFunctions;
using CounterStrikeSharp.API.Core.Capabilities;
using RayTraceAPI;
using BotHiderApi;
using Microsoft.Extensions.Logging;


namespace BotAimImprover;

[MinimumApiVersion(305)]
public class BotAimImprover : BasePlugin
{
    public override string ModuleName => "BotAimImprover";
    public override string ModuleVersion => "2.0.2";
    public override string ModuleAuthor => "ed0ard";
    public override string ModuleDescription => "Restores intelligent aim part selection for CS2 bots.";

    // ============================================================
    // Full-body derived aim points. Each point is defined in the enemy's local frame:
    //   pos.xy = origin.xy + RIGHT * Lateral   (RIGHT = player's right, from yaw)
    //   pos.z  = origin.z  + eyeZ * Frac        (FeetAbsRise>0 means absolute z+rise)
    // Heights (Frac of live eyeZ) come from tm_phoenix/ctm_sas spine bone world heights;
    // lateral offsets from hitbox radii + measured shoulder/elbow widths.
    // Index in this array is the part id used everywhere else.
    // ============================================================
    private readonly struct AimPoint
    {
        public readonly string Name;
        public readonly float Frac;        // height as fraction of live eyeZ (ignored if FeetAbs)
        public readonly float Lateral;     // +right / -left, world units
        public readonly bool  FeetAbs;     // true => z = origin.z + Frac (absolute rise), lateral 0
        public AimPoint(string n, float f, float lat, bool feetAbs = false)
        { Name = n; Frac = f; Lateral = lat; FeetAbs = feetAbs; }
    }

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

    private static readonly int[] _priorityBody =
    {
        3, 4, 5,         // CHEST, GUT, PELVIS
        6, 7, 10, 11,    // L_CHEST, R_CHEST, L_GUT, R_GUT
        8, 9,            // L_SHOULDER, R_SHOULDER
        2, 1, 0,         // JAW, NECK, HEAD
        12, 13, 14, 15,  // L_THIGH, R_THIGH, L_SHIN, R_SHIN
        16               // FEET
    };
    // ============================================================
    // Memory offsets (2026-05-19)
    // ============================================================
    // CCSBot fields:
    private const int OFF_M_TARGETSPOT       = 0x59A4; // Vector(3 floats)
    private const int OFF_M_ENEMY_HANDLE     = 0x5A10; // CHandle (4 bytes)
    private const int OFF_M_IS_ENEMY_VISIBLE = 0x5A14; // bool

    // CCSPlayerPawn->m_pBot is a CCSBot*. Used to map controller -> pawn -> bot for caching.
    private const int OFF_PAWN_M_PBOT = 0x1298;

    // ============================================================
    // Function signature - server.dll 2026-05-19 build, Windows x64.
    // ============================================================
    private const string SIG_PICK_NEW_AIM_SPOT =
        "48 8B C4 55 57 48 8D 68 A1 48 81 EC A8 00 00 00 48 8B F9 0F 29 70 D8 8B 89 10 5A 00 00 83 F9 FF";

    private MemoryFunctionVoid<IntPtr>? _pickNewAimSpot;
    private static readonly PluginCapability<CRayTraceInterface> _rayTraceCapability =
        new("raytrace:craytraceinterface");

    // BotHider exposes disguised bots whose m_bFakePlayer is cleared, so IsBot reads false.
    // Optional dependency: resolved in OnAllPluginsLoaded, null if BotHider isn't installed.
    private static readonly PluginCapability<IBotHiderApi> _botHiderCapability =
        new("bothider:api");
    private IBotHiderApi? _botHiderApi;

    // Cache: CCSBot* -> bot's UserId .
    // Cleared on round_start and per-bot on disconnect.
    private readonly ConcurrentDictionary<IntPtr, int> _botToControllerUserId = new();

    // Aim mode controlled by the `bot_aim` console command:
    //   Mixed = priority logic; snipers + spread weapons aim body-first, others head-first
    //   Head  = always head-first
    //   Body  = always body-first
    private enum AimMode { MIXED, HEAD, BODY }
    private AimMode _aimMode = AimMode.MIXED;

    // Weapons that aim body-first when in Mixed mode (snipers + high-spread / shotguns).
    private static readonly HashSet<string> _bodyFirstWeapons = new()
    {
        "weapon_awp", "weapon_ssg08", "weapon_p90", "weapon_bizon",
        "weapon_nova", "weapon_xm1014", "weapon_sawedoff", "weapon_mag7", "weapon_revolver"
    };

    // One-shot flag so we log a single confirmation that overrides are actually firing.
    private bool _firstOverrideLogged = false;

    // ============================================================
    // Lifecycle
    // ============================================================
    public override void Load(bool hotReload)
    {
        try
        {
            _pickNewAimSpot = new MemoryFunctionVoid<IntPtr>(SIG_PICK_NEW_AIM_SPOT);

            long pnaRuntime = _pickNewAimSpot.Handle.ToInt64();
            if (pnaRuntime == 0)
                throw new InvalidOperationException("PickNewAimSpot signature resolved to zero address.");

            _pickNewAimSpot.Hook(OnPickNewAimSpotPost, HookMode.Post);

            Logger.LogInformation("[BotAimImprover] Loaded. PickNewAimSpot=0x{Pna:X16}", pnaRuntime);
        }
        catch (Exception ex)
        {
            Logger.LogError(ex, "[BotAimImprover] Fatal error during Load() (signature broken?). Plugin inactive.");
            return;
        }

        RegisterEventHandler<EventRoundStart>((_, _) =>
        {
            _botToControllerUserId.Clear();
            return HookResult.Continue;
        });

        RegisterEventHandler<EventPlayerDisconnect>((ev, _) =>
        {
            try
            {
                var disconnecter = ev.Userid;
                if (disconnecter != null && disconnecter.UserId.HasValue)
                {
                    int leavingUserId = disconnecter.UserId.Value;
                    foreach (var kv in _botToControllerUserId)
                        if (kv.Value == leavingUserId)
                            _botToControllerUserId.TryRemove(kv.Key, out int _);
                }
            }
            catch { /* non-fatal */ }
            return HookResult.Continue;
        });

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
        try { _pickNewAimSpot?.Unhook(OnPickNewAimSpotPost, HookMode.Post); }
        catch { /* ignore */ }
    }

    // Resolve the optional BotHider capability once every plugin is loaded.
    public override void OnAllPluginsLoaded(bool hotReload)
    {
        try { _botHiderApi = _botHiderCapability.Get(); }
        catch { _botHiderApi = null; }
        Logger.LogInformation("[BotAimImprover] BotHider API {State}.",
            _botHiderApi != null ? "resolved (disguised bots supported)" : "not present");
    }

    // ============================================================
    // Core override logic (Post-hook on PickNewAimSpot)
    //
    // Native function already set m_targetSpot to GUT or HEAD based on
    // mp_damage_headshot_only. We re-pick based on visible enemy parts and the
    // bot's weapon, then overwrite only the 12 bytes of m_targetSpot.
    // ============================================================
    private HookResult OnPickNewAimSpotPost(DynamicHook hook)
    {
        try
        {
            IntPtr pCCSBot = hook.GetParam<IntPtr>(0);
            if (pCCSBot == IntPtr.Zero)
                return HookResult.Continue;

            // 1) Gate: enemy must be generally visible before we
            //    spend any raytraces. Otherwise the native used last-known position.
            if (ReadByte(pCCSBot + OFF_M_IS_ENEMY_VISIBLE) == 0)
                return HookResult.Continue;

            // 2) Resolve enemy pawn from m_enemy CHandle.
            int enemyHandleRaw = ReadInt32(pCCSBot + OFF_M_ENEMY_HANDLE);
            if (enemyHandleRaw == -1)
                return HookResult.Continue;

            int enemyIdx = enemyHandleRaw & 0x7FFF;
            if (enemyIdx <= 0 || enemyIdx >= 4096)
                return HookResult.Continue;

            CCSPlayerPawn? enemyPawn = Utilities.GetEntityFromIndex<CCSPlayerPawn>(enemyIdx);
            if (enemyPawn == null || !enemyPawn.IsValid || enemyPawn.Handle == IntPtr.Zero)
                return HookResult.Continue;

            // 3) Resolve the bot's controller (for weapon + eye position).
            var botController = ResolveBotController(pCCSBot);
            if (botController == null || !TryGetBotEyePosition(botController, out var botEye))
                return HookResult.Continue;

            string? wpn = botController.PlayerPawn?.Value?.WeaponServices?.ActiveWeapon?.Value?.DesignerName;

            // 4) Compute visible derived points by raytrace from the bot's eye.
            var visiblePoints = ComputeVisiblePoints(botEye, enemyPawn);
            if (visiblePoints.Count == 0)
                return HookResult.Continue;

            // 5) Select the priority order based on aim mode and weapon, then pick.
            // head: awp -> others -> Head. body: all weapons -> Body.
            // mixed: body-first weapons -> Body, others -> Jaw.
            bool isBodyWeapon = wpn != null && _bodyFirstWeapons.Contains(wpn);
            int[] order = _aimMode switch
            {
                AimMode.HEAD => wpn == "weapon_awp" ? _priorityBody : _priorityHead,
                AimMode.BODY => _priorityBody,
                _            => isBodyWeapon ? _priorityBody : _priorityJaw, // MIXED
            };

            int chosenIdx = PickBestPoint(visiblePoints, order);

            // 6) Compute chosen point world position.
            if (!TryComputePartPos(enemyPawn, chosenIdx, out float rx, out float ry, out float rz))
                return HookResult.Continue;

            // 7) Overwrite only m_targetSpot.xyz.
            unsafe
            {
                float* dst = (float*)(pCCSBot + OFF_M_TARGETSPOT).ToPointer();
                dst[0] = rx; dst[1] = ry; dst[2] = rz;
            }

            // One-time confirmation that the override path actually runs end-to-end.
            if (!_firstOverrideLogged)
            {
                _firstOverrideLogged = true;
                Logger.LogInformation(
                    "[BotAimImprover] Active: first override (visible={N} weapon={W} point={P}).",
                    visiblePoints.Count, wpn ?? "(null)", _aimPoints[chosenIdx].Name);
            }
        }
        catch (Exception ex)
        {
            Logger.LogError(ex, "[BotAimImprover] Exception in PostHook");
        }

        return HookResult.Continue;
    }

    /// Find the CCSPlayerController whose pawn's m_pBot field equals pCCSBot.
    /// Cache key: CCSBot pointer. Cache value: bot UserId.
    private CCSPlayerController? ResolveBotController(IntPtr pCCSBot)
    {
        // Cache hit -> look up by UserId.
        if (_botToControllerUserId.TryGetValue(pCCSBot, out int cachedUserId))
        {
            foreach (var ctrl in Utilities.GetPlayers())
                if (ctrl != null && ctrl.IsValid && ctrl.UserId.HasValue && ctrl.UserId.Value == cachedUserId)
                    return ctrl;
            _botToControllerUserId.TryRemove(pCCSBot, out int _); // stale
        }

        // Slow path: find which bot pawn's m_pBot points to pCCSBot.
        foreach (var ctrl in Utilities.GetPlayers())
        {
            if (ctrl == null || !ctrl.IsValid || !ctrl.UserId.HasValue)
                continue;

            // Accept engine bots, plus BotHider-disguised bots whose m_bFakePlayer
            // is cleared (so IsBot reads false). m_pBot match below stays authoritative.
            if (!ctrl.IsBot && !(_botHiderApi?.IsManagedBot(ctrl.Slot) ?? false))
                continue;

            var pawn = ctrl.PlayerPawn?.Value;
            if (pawn == null || !pawn.IsValid || pawn.Handle == IntPtr.Zero)
                continue;

            IntPtr pBotPtr;
            try { pBotPtr = ReadIntPtr(pawn.Handle + OFF_PAWN_M_PBOT); }
            catch { continue; }

            if (pBotPtr == pCCSBot)
            {
                _botToControllerUserId[pCCSBot] = ctrl.UserId.Value;
                return ctrl;
            }
        }
        return null;
    }

    // Pick the highest-priority visible point. Walks the priority order and returns
    // the first index that is in the visible set. Returns -1 if none.
    private static int PickBestPoint(List<int> visible, int[] order)
    {
        if (visible.Count == 0) return -1;

        foreach (int idx in order)
            if (visible.Contains(idx))
                return idx;
        return -1;
    }

    // Returns the list of visible derived-point indices (eye -> point, world-only LoS).
    private List<int> ComputeVisiblePoints(Vector botEye, CCSPlayerPawn enemyPawn)
    {
        var visible = new List<int>(_aimPoints.Length);
        for (int i = 0; i < _aimPoints.Length; i++)
        {
            if (!TryComputePartPos(enemyPawn, i, out float x, out float y, out float z))
                continue;
            if (PointVisibleFromEye(botEye, x, y, z))
                visible.Add(i);
        }
        return visible;
    }

    // Bot eye position = bot pawn origin + view offset Z.
    private static bool TryGetBotEyePosition(CCSPlayerController bot, out Vector eye)
    {
        eye = new Vector(0, 0, 0);
        var pawn = bot.PlayerPawn?.Value;
        var origin = pawn?.AbsOrigin;
        if (origin == null) return false;
        float ez = pawn!.ViewOffset?.Z ?? 64.0f;
        eye = new Vector(origin.X, origin.Y, origin.Z + ez);
        return true;
    }

    // Compute world position of derived point `idx` from the enemy pawn's schema fields.
    private static bool TryComputePartPos(CCSPlayerPawn enemyPawn, int idx,
                                          out float x, out float y, out float z)
    {
        x = y = z = 0;
        if (idx < 0 || idx >= _aimPoints.Length) return false;
        var origin = enemyPawn.AbsOrigin;
        if (origin == null) return false;

        ref readonly AimPoint p = ref _aimPoints[idx];
        float ox = origin.X, oy = origin.Y, oz = origin.Z;
        float eyeZ = enemyPawn.ViewOffset?.Z ?? 64.0f;

        float yawDeg = enemyPawn.EyeAngles?.Y ?? 0.0f;
        double yawRad = yawDeg * Math.PI / 180.0;
        float rX = (float)Math.Sin(yawRad);   // RIGHT vector x
        float rY = (float)-Math.Cos(yawRad);  // RIGHT vector y

        if (p.FeetAbs)
        {
            x = ox; y = oy; z = oz + p.Frac;   // absolute rise (FEET)
        }
        else
        {
            x = ox + rX * p.Lateral;
            y = oy + rY * p.Lateral;
            z = oz + eyeZ * p.Frac;
        }
        return !(float.IsNaN(x) || float.IsNaN(y) || float.IsNaN(z)
                 || float.IsInfinity(x) || float.IsInfinity(y) || float.IsInfinity(z));
    }

    // World-only LoS test from eye to target point. True if unobstructed (>= 0.999).
    private bool PointVisibleFromEye(Vector eye, float tx, float ty, float tz)
    {
        try
        {
            var rt = _rayTraceCapability.Get();
            if (rt == null) return true; // RayTrace not loaded -> don't block
            var end  = new Vector(tx, ty, tz);
            var opts = new TraceOptions(InteractionLayers.MASK_WORLD_ONLY);
            rt.TraceEndShape(eye, end, null, opts, out TraceResult res);
            return res.Fraction >= 0.999f;
        }
        catch { return true; }
    }

    // ============================================================
    // Raw memory readers
    // ============================================================
    private static unsafe byte   ReadByte(IntPtr addr)   => *(byte*)addr.ToPointer();
    private static unsafe int    ReadInt32(IntPtr addr)  => *(int*)addr.ToPointer();
    private static unsafe IntPtr ReadIntPtr(IntPtr addr) => *(IntPtr*)addr.ToPointer();
}
