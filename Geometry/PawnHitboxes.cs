// SPDX-License-Identifier: AGPL-3.0-only
namespace BotAimImprover.Geometry;

internal sealed class PawnHitboxes(NativeBindings native)
{
    private const int MaxSnapshots = 64;
    private readonly Dictionary<uint, (int Slot, int Count)> snapshots = new(MaxSnapshots);
    private readonly Capsule[][] buffers = Enumerable.Range(0, MaxSnapshots)
        .Select(_ => new Capsule[Capsule.MaxCount]).ToArray();
    private readonly int thread = Environment.CurrentManagedThreadId;
    private int tick = -1;

    internal bool TryGet(nint pawn, uint identity, int currentTick, out ReadOnlySpan<Capsule> capsules)
    {
        capsules = default;
        if (Environment.CurrentManagedThreadId != thread || pawn == 0)
            return false;

        if (currentTick != tick)
        {
            snapshots.Clear();
            tick = currentTick;
        }

        if (!snapshots.TryGetValue(identity, out var saved))
        {
            int slot = snapshots.Count;
            if (slot >= buffers.Length)
                return false;

            saved = (slot, ReadSnapshot(pawn, buffers[slot]));
            snapshots.Add(identity, saved);
        }

        if (saved.Count == 0)
            return false;

        capsules = buffers[saved.Slot].AsSpan(0, saved.Count);
        return true;
    }

    private int ReadSnapshot(nint pawn, Capsule[] buffer)
    {
        int count = native.Read(pawn, buffer);
        if (count <= 0 || count > buffer.Length)
            return 0;

        return Capsule.ValidSnapshot(buffer.AsSpan(0, count)) ? count : 0;
    }
}
