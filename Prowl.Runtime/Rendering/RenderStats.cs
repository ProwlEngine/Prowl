// This file is part of the Prowl Game Engine
// Licensed under the MIT License. See the LICENSE file in the project root for details.

namespace Prowl.Runtime.Rendering;

/// <summary>
/// What the renderer drew last frame and how long frames take, for stats overlays and performance captures. Call
/// <see cref="EndFrame"/> once a frame; <see cref="Last"/> holds the counts of the last frame the renderer recorded.
/// </summary>
public static class RenderStats
{
    public struct Frame
    {
        public float FrameTimeMs;

        /// <summary>Views drawn, cameras and editor views alike.</summary>
        public int Views;

        /// <summary>Indirect draw calls, and the mesh pieces drawn by them, for camera views.</summary>
        public int DrawCalls;
        public int DrawCommands;

        /// <summary>The same for shadow views.</summary>
        public int ShadowDrawCalls;
        public int ShadowDrawCommands;

        /// <summary>Local light shadows given tiles, grown and shrunk.</summary>
        public int ShadowsAdded;
        public int ShadowsGrown;
        public int ShadowsShrunk;

        /// <summary>Shader variants still compiling in the background.</summary>
        public int ShaderCompiles;

        /// <summary>Main thread time spent pushing scene changes into the render worlds.</summary>
        public float SyncMs;

        /// <summary>Renderers, lights and probes synced, and static ones left alone while playing.</summary>
        public int Synced;
        public int Parked;
    }

    private static Frame s_sync;

    internal static void AddSceneSync(double milliseconds, int synced, int parked)
    {
        s_sync.SyncMs += (float)milliseconds;
        s_sync.Synced += synced;
        s_sync.Parked += parked;
    }

    private static readonly float[] s_frameTimeHistory = new float[120];
    private static int s_frameTimeIndex;

    /// <summary>The most recent frame's counts. Safe for UI to read.</summary>
    public static Frame Last { get; private set; }

    /// <summary>Ring buffer of recent frame times (ms) for plotting. Length = 120.</summary>
    public static float[] FrameTimeHistory => s_frameTimeHistory;

    /// <summary>Current write index in the history ring buffer.</summary>
    public static int FrameTimeIndex => s_frameTimeIndex;

    /// <summary>Takes the renderer's counts of its last frame and this frame's time.</summary>
    public static void EndFrame()
    {
        Frame frame = new()
        {
            FrameTimeMs = (float)Time.UnscaledDeltaTime * 1000f,
            SyncMs = s_sync.SyncMs,
            Synced = s_sync.Synced,
            Parked = s_sync.Parked,
        };
        s_sync = default;
        if (Graphics.Renderer is { } renderer)
        {
            Graphite.Rendering.FrameStats stats = renderer.LastFrameStats;
            frame.Views = stats.Views;
            frame.DrawCalls = stats.DrawCalls;
            frame.DrawCommands = stats.DrawCommands;
            frame.ShadowDrawCalls = stats.ShadowDrawCalls;
            frame.ShadowDrawCommands = stats.ShadowDrawCommands;
            frame.ShadowsAdded = stats.ShadowsAdded;
            frame.ShadowsGrown = stats.ShadowsGrown;
            frame.ShadowsShrunk = stats.ShadowsShrunk;
            frame.ShaderCompiles = renderer.ShaderCompilesPending;
        }
        Last = frame;

        s_frameTimeHistory[s_frameTimeIndex] = frame.FrameTimeMs;
        s_frameTimeIndex = (s_frameTimeIndex + 1) % s_frameTimeHistory.Length;
    }
}
