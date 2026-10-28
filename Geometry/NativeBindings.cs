// SPDX-License-Identifier: AGPL-3.0-only
using System.Diagnostics;
using System.Numerics;
using System.Runtime.InteropServices;
using System.Security.Cryptography;
using System.Text.Json;

namespace BotAimImprover.Geometry;

// Loaded once, before installing any hook. Never scans/hashes on the aim path.
internal sealed class NativeBindings : IDisposable
{
    internal sealed record Entry(int Rva, string Expected);
    internal sealed record Layout(int PawnSceneNode, int SkeletonVtable, int BoxA, int BoxB,
        int BoxRadius, int BoxGroup, int BoxShape, int BoxIndex);
    internal sealed record Profile(int Version, string Build, string ServerSha256, string Tier0Sha256,
        Entry PickNewAimSpot, Entry HitboxList, int IsVisiblePosRva, string PickSignature, Layout Layout);

    [StructLayout(LayoutKind.Sequential)]
    internal struct Configuration
    {
        internal uint Size, Version;
        internal nint GetHitboxes, FreeMemory;
        internal int PawnSceneNode, SkeletonVtable, BoxA, BoxB, BoxRadius, BoxGroup, BoxShape, BoxIndex;
    }
    [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
    internal delegate int ReadSnapshot(in Configuration config, nint pawn,
        [Out, MarshalAs(UnmanagedType.LPArray, SizeParamIndex = 3)] Capsule[] output, int capacity);
    [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
    internal delegate byte PositionVisible(nint bot, in Vector3 point, byte checkFov, nint ignore);

    internal Profile Data { get; }
    internal string Platform { get; }
    internal nint PickAddress { get; }
    internal PositionVisible IsVisible { get; }
    private readonly Configuration config;
    private readonly ReadSnapshot read;
    private nint helper;

    internal NativeBindings(string directory)
    {
        if ((!OperatingSystem.IsWindows() && !OperatingSystem.IsLinux())
            || RuntimeInformation.ProcessArchitecture != Architecture.X64)
            throw new PlatformNotSupportedException("Pose bindings require Windows or Linux x64; native aim retained.");
        bool windows = OperatingSystem.IsWindows();
        Platform = windows ? "win-x64" : "linux-x64";
        Data = JsonSerializer.Deserialize<Profile>(File.ReadAllText(Path.Combine(directory, "gamedata",
            windows ? "deadeye.json" : "deadeye.linux.json")))
            ?? throw new InvalidDataException("Missing native profile");
        if (Data.Version != 1) throw new InvalidDataException("Unsupported native profile version");
        var server = FindVerifiedModule(windows ? "server.dll" : "libserver.so", Data.ServerSha256);
        var tier0 = FindVerifiedModule(windows ? "tier0.dll" : "libtier0.so", Data.Tier0Sha256);
        PickAddress = VerifyEntry(server, Data.PickNewAimSpot);
        nint getHitboxes = VerifyEntry(server, Data.HitboxList);
        if (Data.IsVisiblePosRva <= 0 || Data.IsVisiblePosRva >= server.ModuleMemorySize)
            throw new InvalidDataException("Invalid visibility entry bounds");
        // The file hash pins this RVA. Call the live entry, including BotState/
        // BotVision detours; requiring pristine entry bytes would reject the bundle.
        IsVisible = Marshal.GetDelegateForFunctionPointer<PositionVisible>(server.BaseAddress + Data.IsVisiblePosRva);
        // ELF module bases are not dlopen handles. Retain only our temporary
        // reference; the already-loaded engine owns tier0 for this plugin's life.
        nint allocatorModule = NativeLibrary.Load(tier0.FileName);
        nint free;
        try { free = NativeLibrary.GetExport(allocatorModule, "MemAlloc_FreeFunc"); }
        finally { NativeLibrary.Free(allocatorModule); }
        var layout = Data.Layout;
        config = new()
        {
            Size = (uint)Marshal.SizeOf<Configuration>(), Version = 1, GetHitboxes = getHitboxes, FreeMemory = free,
            PawnSceneNode = layout.PawnSceneNode, SkeletonVtable = layout.SkeletonVtable,
            BoxA = layout.BoxA, BoxB = layout.BoxB, BoxRadius = layout.BoxRadius, BoxGroup = layout.BoxGroup,
            BoxShape = layout.BoxShape, BoxIndex = layout.BoxIndex
        };
        if (Marshal.SizeOf<Capsule>() != 36 || config.Size != 56)
            throw new InvalidDataException("Hitbox snapshot ABI size mismatch");
        helper = NativeLibrary.Load(Path.Combine(directory, "native", Platform,
            windows ? "BullseyeGeometry.dll" : "libBullseyeGeometry.so"));
        try
        {
            nint export = NativeLibrary.GetExport(helper, "Bullseye_ReadHitboxesV1");
            read = Marshal.GetDelegateForFunctionPointer<ReadSnapshot>(export);
        }
        catch
        {
            Dispose();
            throw;
        }
    }

    private static ProcessModule FindVerifiedModule(string name, string hash)
    {
        using var process = Process.GetCurrentProcess();
        foreach (ProcessModule module in process.Modules)
        {
            if (!module.ModuleName.Equals(name, StringComparison.OrdinalIgnoreCase)) continue;
            using var stream = File.OpenRead(module.FileName);
            if (!Convert.ToHexString(SHA256.HashData(stream)).Equals(hash, StringComparison.OrdinalIgnoreCase))
                throw new InvalidOperationException($"{name} differs from the platform profile; audit required before enabling overrides");
            return module;
        }
        throw new InvalidOperationException($"{name} is not loaded");
    }

    private static nint VerifyEntry(ProcessModule module, Entry entry)
    {
        byte[] expected = Convert.FromHexString(entry.Expected);
        if (entry.Rva <= 0 || expected.Length < 16 || entry.Rva > module.ModuleMemorySize - expected.Length)
            throw new InvalidDataException("Invalid native entry bounds");
        byte[] actual = new byte[expected.Length];
        Marshal.Copy(module.BaseAddress + entry.Rva, actual, 0, actual.Length);
        if (!actual.AsSpan().SequenceEqual(expected))
            throw new InvalidOperationException("Native entry bytes changed or are already hooked");
        return module.BaseAddress + entry.Rva;
    }

    internal int Read(nint pawn, Capsule[] output) => read(in config, pawn, output, output.Length);

    public void Dispose()
    {
        if (helper == 0) return;
        NativeLibrary.Free(helper);
        helper = 0;
    }
}
