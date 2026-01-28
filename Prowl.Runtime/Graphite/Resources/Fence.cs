// This file is part of the Prowl Game Engine
// Licensed under the MIT License. See the LICENSE file in the project root for details.

namespace Prowl.Runtime.Graphite;

/// <summary>
/// A GPU fence for synchronization between CPU and GPU.
/// </summary>
public abstract class Fence : GraphiteResource
{
    /// <summary>Whether the fence is currently signaled.</summary>
    public abstract bool IsSignaled { get; }

    /// <summary>Resets the fence to unsignaled state.</summary>
    public abstract void Reset();

    /// <summary>Blocks the CPU until the fence is signaled.</summary>
    public abstract void Wait();

    /// <summary>Blocks the CPU until the fence is signaled or timeout expires.</summary>
    /// <param name="timeoutMs">Timeout in milliseconds.</param>
    /// <returns>True if signaled, false if timed out.</returns>
    public abstract bool Wait(uint timeoutMs);
}
