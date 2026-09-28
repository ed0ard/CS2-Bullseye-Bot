// SPDX-License-Identifier: AGPL-3.0-only
namespace BotAimImprover.Geometry;

internal sealed class PawnHitboxes(NativeBindings native)
{
    private readonly Dictionary<uint, (int Slot, int Count)> snapshots = new(64);
    private readonly Capsule[][] buffers = Enumerable.Range(0, 64).Select(_ => new Capsule[32]).ToArray();
    private readonly int thread = Environment.CurrentManagedThreadId;
    private int tick = -1;

    internal bool TryGet(nint pawn, uint identity, int currentTick, out ReadOnlySpan<Capsule> capsules)
    {
        capsules = default;
        if (Environment.CurrentManagedThreadId != thread || pawn == 0) return false;
        if (currentTick != tick) { snapshots.Clear(); tick = currentTick; }
        if (!snapshots.TryGetValue(identity, out var saved))
        {
            int slot = snapshots.Count;
            if (slot >= buffers.Length) return false;
            int count = native.Read(pawn, buffers[slot]);
            if (count <= 0 || count > 32) count = 0;
            for (int i = 0; i < count; i++) if (!buffers[slot][i].Valid) { count = 0; break; }
            snapshots.Add(identity, saved = (slot, count));
        }
        if (saved.Count == 0) return false;
        capsules = buffers[saved.Slot].AsSpan(0, saved.Count);
        return true;
    }
}
