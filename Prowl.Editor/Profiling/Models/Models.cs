using System.Collections.Generic;

using Prowl.Graphite;


namespace Prowl.Editor.Profiling;

public readonly record struct ResourceRef(uint Id, string Name, ResourceRefKind Kind, SnapshotResourceID Resource);

public enum ResourceRefKind
{
    Texture,
    Buffer,
    Unknown
}

public readonly record struct PassEdge(int FromPass, int ToPass, ResourceRef Resource);

/// <summary>
/// Points at a specific version of a SnapshotResource. Invalid/default during live recording -
/// only populated when the frame this ResourceRef/ReferenceBuffer belongs to is part of a Snapshot.
/// </summary>
public readonly record struct SnapshotResourceID(uint ResourceId, uint Version, bool IsValid)
{
    public static readonly SnapshotResourceID Invalid = default;
}

/// <summary>A vertex/index/bound buffer a draw call referenced. Only populated on snapshot readback.</summary>
public readonly record struct ReferenceBuffer(
    string Name, uint SizeInBytes, uint ContentVersion, bool ReadOnly, SnapshotResourceID Resource);

/// <summary>A struct, not a class: Draw/Dispatch/ReferenceBuffers all wrap already-struct Graphite
/// types, and a captured frame can hold thousands of these - one per draw call - so this is one of the
/// hottest allocation sites in the profiler if it's a reference type.</summary>
public readonly record struct ProfiledDrawCall(
    DrawCallInfo? Draw,
    DispatchCallInfo? Dispatch,
    bool Culled,
    ReferenceBuffer[] ReferenceBuffers)
{
    /// <summary>CPU-side estimate for this single draw only, from Draw.Topology + vertex/index +
    /// instance count - computed on read, never stored or rolled up. This is the fallback for
    /// inspecting one draw call when you don't have (or don't need) the real GPU-reported count; it is
    /// deliberately never summed across a pass or command buffer - see ProfiledPass.TrianglesDrawn for
    /// why an indirect-draw-blind CPU sum at that level would be actively misleading. Null for
    /// dispatches and for indirect draws, since the real vertex/index count for those lives in a GPU
    /// buffer the CPU never reads.</summary>
    public ulong? TriangleCount
    {
        get
        {
            if (Draw is not { IsIndirect: false } d)
                return null;

            ulong instances = System.Math.Max(1u, d.InstanceCount);
            return d.Topology switch
            {
                PrimitiveTopology.TriangleList => (d.VertexOrIndexCount / 3) * instances,
                PrimitiveTopology.TriangleStrip => (ulong)System.Math.Max(0, (long)d.VertexOrIndexCount - 2) * instances,
                _ => 0,
            };
        }
    }
}

/// <summary>Full pipeline state as bound at a switch, pulled from the GraphicsProgram/ComputeProgram
/// handed to RecordPipelineSwitch. Null fields are the ones that don't apply (e.g. blend/depth/raster
/// for a compute switch, thread group size for a graphics switch). A struct since every field wraps an
/// already-struct Graphite type - no reason to box a fresh object per pipeline switch.</summary>
public readonly record struct ProfiledPipelineState(
    BlendStateDescription? BlendState,
    DepthStencilStateDescription? DepthStencilState,
    RasterizerStateDescription? RasterizerState,
    uint? ThreadGroupSizeX, uint? ThreadGroupSizeY, uint? ThreadGroupSizeZ);

public readonly record struct SubmitRecord(SubmitKind Kind, string Name, uint CommandBufferCount);

public readonly record struct CounterDef(string Name, CounterCategory Category, CounterUnit Unit);

public readonly record struct CounterValue(string Name, CounterCategory Category, CounterUnit Unit, double Value);

public enum CounterCategory
{
    EngineObject,
    NativeMemory,
    Swapchain,
    BufferMemory,
    ResourceSet,
    BufferUpdate,
    TextureUpdate,
    AllocFree,
    Barrier,
    Submit,
    DrawDispatch
}

public enum CounterUnit
{
    Count,
    Bytes,
    Milliseconds
}

/// <summary>InclusiveMilliseconds is the sum of this node's own time and all of its children's. A
/// struct: one of these is built per GPU execution-time report, every frame, so as a class it would
/// be one of the hottest allocation sites in the profiler.</summary>
public readonly record struct TimeSample(string Name, double InclusiveMilliseconds, bool IsTransfer, TimeSample[] Children);
