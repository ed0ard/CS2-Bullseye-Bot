using System.Numerics;
using System.Runtime.InteropServices;
using BotAimImprover.Geometry;

internal static class InteropSmoke
{
    [UnmanagedFunctionPointer(CallingConvention.Cdecl)] private delegate nint Skeleton(nint self);
    [UnmanagedFunctionPointer(CallingConvention.Cdecl)] private delegate int Hitboxes(nint self, nint boxes, nint poses, int flags);
    [UnmanagedFunctionPointer(CallingConvention.Cdecl)] private delegate void Free(nint pointer);

    internal static void Run(string library)
    {
        nint memory = Marshal.AllocHGlobal(256);
        nint module = 0;
        try
        {
            Marshal.Copy(new byte[256], 0, memory, 256);
            nint pawn = memory, node = memory + 16, table = memory + 32, box = memory + 64;
            Skeleton getSkeleton = self => self;
            Free free = _ => { };
            Hitboxes getHitboxes = (self, boxes, poses, flags) =>
            {
                if (flags != 0 || poses % 16 != 0) return -1;
                nint descriptors = Marshal.ReadIntPtr(boxes + 8), transforms = Marshal.ReadIntPtr(poses + 16);
                Marshal.WriteIntPtr(descriptors, box);
                // position=(10,20,30), scale=2, identity rotation.
                Marshal.Copy(new float[] { 10, 20, 30, 2, 0, 0, 0, 1 }, 0, transforms, 8);
                Marshal.WriteInt32(boxes, 1); Marshal.WriteInt32(poses, 1);
                return 1;
            };
            Marshal.WriteIntPtr(pawn, node); Marshal.WriteIntPtr(node, table);
            Marshal.WriteIntPtr(table, Marshal.GetFunctionPointerForDelegate(getSkeleton));
            // A=(0,0,0), B=(0,0,2), radius=3; group=1; shape=2; index=7.
            Marshal.Copy(new float[] { 0, 0, 0, 0, 0, 2, 3 }, 0, box, 7);
            Marshal.WriteInt32(box + 28, 1); Marshal.WriteByte(box + 32, 2); Marshal.WriteInt16(box + 34, 7);
            var config = new NativeBindings.Configuration
            {
                Size = (uint)Marshal.SizeOf<NativeBindings.Configuration>(), Version = 1,
                GetHitboxes = Marshal.GetFunctionPointerForDelegate(getHitboxes), FreeMemory = Marshal.GetFunctionPointerForDelegate(free),
                PawnSceneNode = 0, SkeletonVtable = 0, BoxA = 0, BoxB = 12,
                BoxRadius = 24, BoxGroup = 28, BoxShape = 32, BoxIndex = 34
            };
            module = NativeLibrary.Load(Path.GetFullPath(library));
            var read = Marshal.GetDelegateForFunctionPointer<NativeBindings.ReadSnapshot>(
                NativeLibrary.GetExport(module, "Bullseye_ReadHitboxesV1"));
            var output = new Capsule[32];
            if (config.Size != 56 || Marshal.SizeOf<Capsule>() != 36 || read(in config, pawn, output, 32) != 1
                || output[0].A != new Vector3(10, 20, 30) || output[0].B != new Vector3(10, 20, 34)
                || output[0].Radius != 6 || output[0].Group != 1 || output[0].Index != 7)
                throw new Exception("Managed/native snapshot ABI mismatch");
            GC.KeepAlive(getSkeleton); GC.KeepAlive(getHitboxes); GC.KeepAlive(free);
            Console.WriteLine("Managed/native DLL round-trip passed (fixture, no game process)");
        }
        finally
        {
            if (module != 0) NativeLibrary.Free(module);
            Marshal.FreeHGlobal(memory);
        }
    }
}
