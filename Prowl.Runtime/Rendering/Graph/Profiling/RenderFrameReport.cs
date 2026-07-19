// This file is part of the Prowl Game Engine
// Licensed under the MIT License. See the LICENSE file in the project root for details.

using System;
using System.Collections.Generic;

using Prowl.Graphite;

namespace Prowl.Runtime.Rendering;

/// <summary>
/// Per-camera, per-frame profiling snapshot produced by the render pipeline's ExecuteGraph and stored
/// on <see cref="Camera.LastRenderReport"/>. This is the shared contract the editor's live profiler
/// reads and the snapshot capture embeds. Resource identifiers are stored as their interned names
/// (strings), never as <see cref="RenderResourceID"/>, because that struct is a process-local integer
/// that does not round-trip through serialization.
/// </summary>
public sealed class RenderFrameReport
{
    /// <summary>Monotonic frame counter, minted when the report is finished.</summary>
    public long FrameIndex;

    /// <summary>The rendering camera's <see cref="EngineObject.InstanceID"/>.</summary>
    public int CameraId;

    /// <summary>Wall-clock milliseconds for the whole graph execution.</summary>
    public double CpuFrameMs;

    /// <summary>One entry per pass, in execution order.</summary>
    public List<PassReport> Passes = new();

    /// <summary>One entry per distinct graph resource.</summary>
    public List<ResourceReport> Resources = new();

    /// <summary>Aggregate frame counters.</summary>
    public FrameCounters Counters = new();

    /// <summary>Writer-to-reader dependency edges (by pass index) tagged with the resource that links them.</summary>
    public List<GraphEdge> Edges = new();
}

/// <summary>Profiling data for a single render pass.</summary>
public sealed class PassReport
{
    public string Name = "";
    public int Index;

    /// <summary>Wall-clock milliseconds for this pass's Render call.</summary>
    public double CpuMs;

    /// <summary>Root of the nested manual-sample tree recorded via BeginSample/EndSample.</summary>
    public SampleScope Root = new();

    /// <summary>Interned names of the resources this pass declared as inputs.</summary>
    public List<string> Inputs = new();

    /// <summary>Interned names of the resources this pass declared as outputs.</summary>
    public List<string> Outputs = new();

    /// <summary>Whether this pass's main output is the graph's presentation source.</summary>
    public bool IsPresentationSource;

    /// <summary>Draw calls recorded during this pass.</summary>
    public List<DrawCallReport> DrawCalls = new();
}

/// <summary>A timed region inside a pass. Nests arbitrarily via <see cref="Children"/>.</summary>
public sealed class SampleScope
{
    public string Name = "";
    public double CpuMs;
    public List<SampleScope> Children = new();
}

/// <summary>
/// One recorded draw call. Meshes/materials/shaders are referenced by asset guid (plus a display
/// name) rather than live object references, so a report is cheap to keep and safe to serialize.
/// </summary>
public sealed class DrawCallReport
{
    public Guid MeshGuid;
    public int SubMeshIndex = -1;
    public Guid MaterialGuid;
    public Guid ShaderGuid;
    public List<string> VariantKeywords = new();

    /// <summary>Index of the material shader pass this call drew.</summary>
    public int PassIndex;

    /// <summary>Stable id of the renderable that produced this call, or -1 if unknown.</summary>
    public int SourceRenderableId = -1;

    public int IndexCount;
    public int InstanceCount = 1;

    public string MeshName = "";
    public string MaterialName = "";
    public string ShaderName = "";
}

/// <summary>Describes one allocated graph resource and how it is wired into the pass order.</summary>
public sealed class ResourceReport
{
    public string Id = "";
    public int Width;
    public int Height;
    public bool HasDepth;
    public PixelFormat[] ColorFormats = Array.Empty<PixelFormat>();

    /// <summary>Estimated VRAM footprint in bytes.</summary>
    public long BytesEstimated;

    /// <summary>Pass index that writes this resource, or -1 if none.</summary>
    public int ProducedByPassIndex = -1;

    /// <summary>Pass indices that read this resource.</summary>
    public List<int> ConsumedByPassIndex = new();
}

/// <summary>A writer-to-reader dependency in the render graph.</summary>
public struct GraphEdge
{
    public int WriterPassIndex;
    public int ReaderPassIndex;
    public string ResourceId;
}

/// <summary>
/// Aggregate per-frame counters. <see cref="ExtraCounters"/> is an open dictionary so Graphite-native
/// counters (added later) can flow through without changing this schema.
/// </summary>
public sealed class FrameCounters
{
    public int DrawCalls;
    public int Passes;
    public int TrianglesApprox;
    public int RenderablesCollected;
    public int RenderablesCulled;
    public int RenderablesVisible;
    public int PooledRtCount;
    public long PooledRtBytes;
    public Dictionary<string, double> ExtraCounters = new();

    /// <summary>
    /// Every Graphite device profiling counter for this frame, flattened to "Group/Bin Unit" keys
    /// (e.g. "Live/Texture MB", "Allocated/DeviceBuffer Count", "BufferMem/Vertex MB"). Empty when
    /// the device was created without profiling enabled. Shown in the profiler's Graphite tab,
    /// grouped by the segment before '/'.
    /// </summary>
    public Dictionary<string, double> GraphiteCounters = new();
}
