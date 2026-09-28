// SPDX-License-Identifier: AGPL-3.0-only
using System.Diagnostics;
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
        Entry PickNewAimSpot, Entry HitboxList, string PickSignature, Layout Layout);

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

    internal Profile Data { get; }
    internal nint PickAddress { get; }
    private readonly Configuration config;
    private readonly ReadSnapshot read;
    private nint helper;

    internal NativeBindings(string directory)
    {
        if (!OperatingSystem.IsWindows() || !Environment.Is64BitProcess)
            throw new PlatformNotSupportedException("Pose bindings are audited for Windows x64 only; native aim retained.");
        Data = JsonSerializer.Deserialize<Profile>(File.ReadAllText(Path.Combine(directory, "gamedata", "deadeye.json")))
            ?? throw new InvalidDataException("Missing native profile");
        if (Data.Version != 1) throw new InvalidDataException("Unsupported native profile version");
        var server = FindVerifiedModule("server.dll", Data.ServerSha256);
        var tier0 = FindVerifiedModule("tier0.dll", Data.Tier0Sha256);
        PickAddress = VerifyEntry(server, Data.PickNewAimSpot);
        nint getHitboxes = VerifyEntry(server, Data.HitboxList);
        nint free = NativeLibrary.GetExport(tier0.BaseAddress, "MemAlloc_FreeFunc");
        var l = Data.Layout;
        config = new()
        {
            Size = (uint)Marshal.SizeOf<Configuration>(), Version = 1, GetHitboxes = getHitboxes, FreeMemory = free,
            PawnSceneNode = l.PawnSceneNode, SkeletonVtable = l.SkeletonVtable,
            BoxA = l.BoxA, BoxB = l.BoxB, BoxRadius = l.BoxRadius, BoxGroup = l.BoxGroup,
            BoxShape = l.BoxShape, BoxIndex = l.BoxIndex
        };
        if (Marshal.SizeOf<Capsule>() != 36 || config.Size != 56)
            throw new InvalidDataException("Hitbox snapshot ABI size mismatch");
        helper = NativeLibrary.Load(Path.Combine(directory, "native", "win-x64", "BullseyeGeometry.dll"));
        try { read = Marshal.GetDelegateForFunctionPointer<ReadSnapshot>(NativeLibrary.GetExport(helper, "Bullseye_ReadHitboxesV1")); }
        catch { Dispose(); throw; }
    }

    private static ProcessModule FindVerifiedModule(string name, string hash)
    {
        using var process = Process.GetCurrentProcess();
        foreach (ProcessModule module in process.Modules)
        {
            if (!module.ModuleName.Equals(name, StringComparison.OrdinalIgnoreCase)) continue;
            using var stream = File.OpenRead(module.FileName);
            if (!Convert.ToHexString(SHA256.HashData(stream)).Equals(hash, StringComparison.OrdinalIgnoreCase))
                throw new InvalidOperationException($"{name} differs from deadeye.json; audit required before enabling overrides");
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
