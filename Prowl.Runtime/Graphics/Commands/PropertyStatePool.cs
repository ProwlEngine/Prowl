// This file is part of the Prowl Game Engine
// Licensed under the MIT License. See the LICENSE file in the project root for details.

using System.Collections.Concurrent;
using System.Threading;

using Prowl.Runtime.Rendering;

namespace Prowl.Runtime;

/// <summary>
/// Pool for the <see cref="PropertyState"/> snapshots that <see cref="CommandBuffer"/>
/// rents when it needs to capture a material's properties at encode time, so a caller
/// can mutate the original after encoding without touching draws already recorded.
///
/// <para>
/// A source keeps its last snapshot and hands it out again while its contents still match,
/// so an unchanged material or renderer is not copied every draw. A snapshot is held once by
/// its source and once by every buffer that encoded it, and goes back to the pool when the
/// last hold is dropped. Snapshots are rented while encoding, which is one thread at a time,
/// and released on the render thread.
/// </para>
/// </summary>
internal static class PropertyStatePool
{
    private static readonly ConcurrentQueue<PropertyState> s_free = new();

    /// <summary>A snapshot of <paramref name="source"/>. Caller owns one hold until <see cref="Return"/>.</summary>
    public static PropertyState RentSnapshot(PropertyState source)
    {
        PropertyState? last = source._lastSnapshot;
        if (last != null && last.SnapshotMatches(source))
        {
            Interlocked.Increment(ref last._snapshotHolds);
            return last;
        }

        if (!s_free.TryDequeue(out var ps))
            ps = new PropertyState();
        ps.ApplyOverride(source);
        ps.ResolveHandles();
        ps._sourceVersion = source._version;
        ps._snapshotHolds = 2;
        source._lastSnapshot = ps;
        if (last != null) Return(last);
        return ps;
    }

    public static void Return(PropertyState ps)
    {
        if (ps == null || Interlocked.Decrement(ref ps._snapshotHolds) != 0) return;
        ps.Clear();
        s_free.Enqueue(ps);
    }
}
