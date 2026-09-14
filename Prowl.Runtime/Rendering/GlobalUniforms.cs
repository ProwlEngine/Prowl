// This file is part of the Prowl Game Engine
// Licensed under the MIT License. See the LICENSE file in the project root for details.

using Prowl.Graphite;
using Prowl.Vector;

namespace Prowl.Runtime.Rendering;

/// <summary>
/// Storage for the frame-global (camera-independent) part of the shader <c>Frame</c> block: time.
/// Refreshed once per frame by <see cref="Update"/>; passes composite it into their command buffers
/// (via <see cref="CameraView.FrameProperties"/>) rather than binding it here.
/// </summary>
public static class GlobalUniforms
{
    public static PropertySet Properties { get; } = new();

    private static long s_lastFrame = -1;

    public static void Update()
    {
        if (s_lastFrame == Time.FrameCount)
            return;
        s_lastFrame = Time.FrameCount;

        float t = Time.TimeSinceStartup;
        Properties.SetFloat4("_Time", new Float4(t * 0.5f, t, t * 2f, Time.FrameCount));
        Properties.SetFloat4("_SinTime", new Float4(Maths.Sin(t / 8), Maths.Sin(t / 4), Maths.Sin(t / 2), Maths.Sin(t)));
        Properties.SetFloat4("_CosTime", new Float4(Maths.Cos(t / 8), Maths.Cos(t / 4), Maths.Cos(t / 2), Maths.Cos(t)));
        Properties.SetFloat4("prowl_DeltaTime", new Float4(Time.DeltaTime, 1.0f / Time.DeltaTime, Time.SmoothDeltaTime, 1.0f / Time.SmoothDeltaTime));
    }
}
