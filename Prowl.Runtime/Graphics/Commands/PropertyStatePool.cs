// This file is part of the Prowl Game Engine
// Licensed under the MIT License. See the LICENSE file in the project root for details.

using System.Collections.Concurrent;

using Prowl.Runtime.Rendering;

namespace Prowl.Runtime;

/// <summary>
/// Pool for the <see cref="PropertyState"/> snapshots that <see cref="CommandBuffer"/>
/// rents when it needs to capture a material's properties at encode time. Without
/// this pool, every <c>cmd.Blit</c> call inside an image effect would allocate a
/// fresh <see cref="PropertyState"/> (12 dictionaries) just to defend against
/// mid-encoding material mutation.
///
/// <para>
/// Buffers track their own rentals and return them in <see cref="CommandBuffer.OnReturn"/>
/// so the pool's lifetime is bounded by the CommandBuffer pool's lifetime.
/// </para>
/// </summary>
internal static class PropertyStatePool
{
    private static readonly ConcurrentBag<PropertyState> s_free = new();

    /// <summary>Rent a fresh PropertyState, pre-populated with a copy of
    /// <paramref name="source"/>'s entries. Caller owns until <see cref="Return"/>.</summary>
    public static PropertyState RentSnapshot(PropertyState source)
    {
        if (!s_free.TryTake(out var ps))
            ps = new PropertyState();
        else
            ps.Clear();
        ps.ApplyOverride(source);
        return ps;
    }

    public static void Return(PropertyState ps)
    {
        if (ps == null) return;
        ps.Clear();
        s_free.Add(ps);
    }
}
