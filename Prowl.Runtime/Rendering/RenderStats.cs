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
        public int DrawCalls;
        public int InstancedDrawCalls;
        public long Triangles;
        public long Vertices;
        public int Batches;
        public int RenderablesCollected;
        public int RenderablesCulled;
        public int RenderablesDrawn;
        public int ShadowDrawCalls;
        public int Lights;
        public int DirectionalLights;
        public int PointLights;
        public int SpotLights;
        public int ShadowCasters;
        public int Cameras;
    }

    private static Frame s_current;
    private static Frame s_last;
    private static bool s_inShadowPass;

    /// <summary>Snapshot of the most recently completed frame. Safe for UI to read.</summary>
    public static Frame Last => s_last;

    /// <summary>Reset all counters. Called by the pipeline at the start of a frame.</summary>
    public static void BeginFrame()
    {
        s_current = default;
        s_inShadowPass = false;
    }

    /// <summary>Promote the in-progress counters to <see cref="Last"/>. Called by the pipeline at the end of a frame.</summary>
    public static void EndFrame()
    {
        s_last = s_current;
    }

    /// <summary>Mark the start of a shadow pass so following draw calls count toward shadows.</summary>
    public static void BeginShadowPass() => s_inShadowPass = true;

    /// <summary>End the shadow pass; draw calls go back to the main bucket.</summary>
    public static void EndShadowPass() => s_inShadowPass = false;

    public static void AddCamera() => s_current.Cameras++;

    public static void AddBatch() => s_current.Batches++;

    public static void AddRenderables(int collected, int culled, int drawn)
    {
        s_current.RenderablesCollected += collected;
        s_current.RenderablesCulled += culled;
        s_current.RenderablesDrawn += drawn;
    }

    /// <summary>
    /// Accumulate light counts for this frame. Multi-camera renders add to the running totals,
    /// matching how draw calls and triangle counts already sum across cameras.
    /// </summary>
    public static void AddLightCounts(int total, int directional, int point, int spot, int shadowCasters)
    {
        s_current.Lights += total;
        s_current.DirectionalLights += directional;
        s_current.PointLights += point;
        s_current.SpotLights += spot;
        s_current.ShadowCasters += shadowCasters;
    }

    /// <summary>
    /// Record a draw call. Called from <see cref="Graphics"/>. <paramref name="indexCount"/> is
    /// the number of indices and <paramref name="topology"/> determines the triangle count.
    /// <paramref name="instances"/> &gt; 1 marks an instanced call and multiplies triangle / vertex
    /// totals so the per-frame count reflects real submitted geometry.
    /// </summary>
    public static void RecordDraw(Topology topology, uint indexCount, uint instances = 1)
    {
        if (s_inShadowPass)
        {
            s_current.ShadowDrawCalls += (int)instances;
        }
        else
        {
            s_current.DrawCalls++;
            if (instances > 1) s_current.InstancedDrawCalls++;
        }

        long primCount = topology switch
        {
            Topology.Triangles => indexCount / 3,
            Topology.TriangleStrip => indexCount >= 2 ? indexCount - 2 : 0,
            Topology.TriangleFan => indexCount >= 2 ? indexCount - 2 : 0,
            _ => 0
        };

        s_current.Triangles += primCount * instances;
        s_current.Vertices += (long)indexCount * instances;
    }
}
