// This file is part of the Prowl Game Engine
// Licensed under the MIT License. See the LICENSE file in the project root for details.

using System.Collections.Concurrent;

namespace Prowl.Runtime;

/// <summary>Pools <see cref="CommandBuffer"/> instances so the byte stream and
/// object list buffers are reused. Bounded at <see cref="MaxPoolSize"/>; over-cap
/// returns are destroyed.</summary>
internal static class CommandBufferPool
{
    public const int MaxPoolSize = 64;

    private static readonly ConcurrentBag<CommandBuffer> s_free = new();
    private static int s_count;

    public static CommandBuffer Rent(string? name)
    {
        if (s_free.TryTake(out var cmd))
        {
            System.Threading.Interlocked.Decrement(ref s_count);
            cmd.OnRent(name);
            return cmd;
        }

        cmd = new CommandBuffer();
        cmd.OnRent(name);
        return cmd;
    }

    /// <summary>Returns the buffer to the pool. Idempotent via the _inPool guard.</summary>
    public static void Return(CommandBuffer cmd)
    {
        if (cmd == null || cmd._inPool) return;
        cmd.OnReturn();
        cmd._inPool = true;

        if (System.Threading.Interlocked.Increment(ref s_count) > MaxPoolSize)
        {
            System.Threading.Interlocked.Decrement(ref s_count);
            cmd.OnDestroy();
            return;
        }

        s_free.Add(cmd);
    }
}
