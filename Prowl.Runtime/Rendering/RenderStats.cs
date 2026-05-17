// This file is part of the Prowl Game Engine
// Licensed under the MIT License. See the LICENSE file in the project root for details.

namespace Prowl.Runtime.Rendering;

/// <summary>
/// Per-frame rendering counters captured by the pipeline and the low-level <see cref="Graphics"/>
/// draw calls. The pipeline calls <see cref="BeginFrame"/> before its first draw and
/// <see cref="EndFrame"/> after the last one; the editor reads the snapshot exposed in
/// <see cref="Last"/>. Counters are global because both <see cref="Graphics"/> and the active
/// pipeline write to the same accumulator; multi-camera renders are summed into one frame.
/// </summary>
public static class RenderStats
{
    public struct Frame
    {
        // ── Geometry (color pass) ────────────────────────────
        public int DrawCalls;
        public int InstancedDrawCalls;
        public long Triangles;
        public long Vertices;
        public int Batches;

        // ── Culling ──────────────────────────────────────────
        public int RenderablesCollected;
        public int RenderablesCulled;
        public int RenderablesDrawn;

        // ── Shadows ──────────────────────────────────────────
        public int ShadowDrawCalls;
        public int ShadowInstancedDrawCalls;
        public long ShadowTriangles;
        public long ShadowVertices;
        public int ShadowBatches;
        public int ShadowPasses;
        public int ShadowRenderablesCollected;
        public int ShadowRenderablesCulled;
        public int ShadowRenderablesDrawn;

        // ── Lighting ─────────────────────────────────────────
        public int Lights;
        public int DirectionalLights;
        public int PointLights;
        public int SpotLights;
        public int ShadowCasters;

        // ── Image Effects / Post-Processing ──────────────────
        public int ImageEffects;
        public int ImageEffectPasses;

        // ── Cameras ──────────────────────────────────────────
        public int Cameras;

        // ── Timing (ms) ──────────────────────────────────────
        public float FrameTimeMs;
        public float ColorPassMs;
        public float ShadowPassMs;
        public float PostFxMs;
    }

    private static Frame s_current;
    private static Frame s_last;
    private static bool s_inShadowPass;
    private static long s_sectionStart;

    // Frame time history for graphs
    private static readonly float[] s_frameTimeHistory = new float[120];
    private static int s_frameTimeIndex;

    /// <summary>Snapshot of the most recently completed frame. Safe for UI to read.</summary>
    public static Frame Last => s_last;

    /// <summary>Ring buffer of recent frame times (ms) for plotting. Length = 120.</summary>
    public static float[] FrameTimeHistory => s_frameTimeHistory;

    /// <summary>Current write index in the history ring buffer.</summary>
    public static int FrameTimeIndex => s_frameTimeIndex;

    /// <summary>Reset all counters. Called by the pipeline at the start of a frame.</summary>
    public static void BeginFrame()
    {
        s_current = default;
        s_inShadowPass = false;
    }

    /// <summary>Promote the in-progress counters to <see cref="Last"/>. Called by the pipeline at the end of a frame.</summary>
    public static void EndFrame()
    {
        s_current.FrameTimeMs = (float)Time.UnscaledDeltaTime * 1000f;
        s_last = s_current;

        s_frameTimeHistory[s_frameTimeIndex] = s_last.FrameTimeMs;
        s_frameTimeIndex = (s_frameTimeIndex + 1) % s_frameTimeHistory.Length;
    }

    /// <summary>Mark the start of a shadow pass so following draw calls count toward shadows.</summary>
    public static void BeginShadowPass()
    {
        s_inShadowPass = true;
        s_current.ShadowPasses++;
        s_sectionStart = System.Diagnostics.Stopwatch.GetTimestamp();
    }

    /// <summary>End the shadow pass; draw calls go back to the main bucket.</summary>
    public static void EndShadowPass()
    {
        s_current.ShadowPassMs += ElapsedMs();
        s_inShadowPass = false;
    }

    /// <summary>Mark the start of the color/geometry pass for timing.</summary>
    public static void BeginColorPass() => s_sectionStart = System.Diagnostics.Stopwatch.GetTimestamp();

    /// <summary>End the color pass timing.</summary>
    public static void EndColorPass() => s_current.ColorPassMs += ElapsedMs();

    /// <summary>Mark the start of post-processing for timing.</summary>
    public static void BeginPostFx() => s_sectionStart = System.Diagnostics.Stopwatch.GetTimestamp();

    /// <summary>End post-processing timing.</summary>
    public static void EndPostFx() => s_current.PostFxMs += ElapsedMs();

    private static float ElapsedMs()
    {
        long now = System.Diagnostics.Stopwatch.GetTimestamp();
        return (float)((now - s_sectionStart) * 1000.0 / System.Diagnostics.Stopwatch.Frequency);
    }

    public static void AddCamera() => s_current.Cameras++;

    public static void AddBatch() => s_current.Batches++;

    public static void AddRenderables(int collected, int culled, int drawn)
    {
        if (s_inShadowPass)
        {
            s_current.ShadowRenderablesCollected += collected;
            s_current.ShadowRenderablesCulled += culled;
            s_current.ShadowRenderablesDrawn += drawn;
        }
        else
        {
            s_current.RenderablesCollected += collected;
            s_current.RenderablesCulled += culled;
            s_current.RenderablesDrawn += drawn;
        }
    }

    public static void AddLightCounts(int total, int directional, int point, int spot, int shadowCasters)
    {
        s_current.Lights += total;
        s_current.DirectionalLights += directional;
        s_current.PointLights += point;
        s_current.SpotLights += spot;
        s_current.ShadowCasters += shadowCasters;
    }

    /// <summary>Record an image effect execution.</summary>
    public static void AddImageEffect(int passes = 1)
    {
        s_current.ImageEffects++;
        s_current.ImageEffectPasses += passes;
    }

    /// <summary>
    /// Record a draw call. Called from <see cref="Graphics"/>. <paramref name="indexCount"/> is
    /// the number of indices and <paramref name="topology"/> determines the triangle count.
    /// </summary>
    public static void RecordDraw(Topology topology, uint indexCount, uint instances = 1)
    {
        long primCount = topology switch
        {
            Topology.Triangles => indexCount / 3,
            Topology.TriangleStrip => indexCount >= 2 ? indexCount - 2 : 0,
            Topology.TriangleFan => indexCount >= 2 ? indexCount - 2 : 0,
            _ => 0
        };

        long tris = primCount * instances;
        long verts = (long)indexCount * instances;

        if (s_inShadowPass)
        {
            s_current.ShadowDrawCalls++;
            if (instances > 1) s_current.ShadowInstancedDrawCalls++;
            s_current.ShadowTriangles += tris;
            s_current.ShadowVertices += verts;
        }
        else
        {
            s_current.DrawCalls++;
            if (instances > 1) s_current.InstancedDrawCalls++;
        }

        // Always count total geometry
        s_current.Triangles += tris;
        s_current.Vertices += verts;
    }
}
