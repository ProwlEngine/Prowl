// This file is part of the Prowl Game Engine
// Licensed under the MIT License. See the LICENSE file in the project root for details.

namespace Prowl.Runtime.Rendering;

/// <summary>Handle for measuring render-thread wall-clock across CommandBuffer ops.
/// Stopwatch samples are taken on the render thread when the matching BeginTimer /
/// EndTimer opcodes run, so the result reflects how long that thread actually spent
/// in the wrapped section (a proxy for GPU cost since the driver stalls the render
/// thread when its command queue fills).</summary>
public sealed class GpuTimer
{
    internal long _startTicks;

    /// <summary>Accumulated render-thread time since the last <see cref="ResetFrame"/>.</summary>
    public float LastMs;

    public void ResetFrame() => LastMs = 0;
}
