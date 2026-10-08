using System;
using Prowl.Graphite.Debugger.Trace;
using Prowl.Graphite.Debugging;
using Prowl.Graphite.RenderGraph;

namespace Prowl.Graphite.Debugger;

/// <summary>Where a recorded resource comes from. External is anything that is not a graph backing.</summary>
public enum ResourceOrigin : byte
{
    Transient,
    Imported,
    ViewTarget,
    External,
}

/// <summary>A buffer or texture referenced by the recording, under a trace local id.</summary>
public sealed record RecordedResource(
    TraceResourceId Id,
    string Name,
    GraphResourceKind Kind,
    ResourceOrigin Origin,
    TextureDescription? Texture,
    BufferDescription? Buffer);

/// <summary>One stage of a recorded program. The SPIR-V is a blob.</summary>
public sealed record RecordedStage(ShaderStages Stage, string EntryPoint, BlobRef Code);

/// <summary>A program, one per key. Graphics only fields are null for compute.</summary>
public sealed record RecordedProgram(
    ProgramKey Key,
    bool IsCompute,
    EquatableArray<RecordedStage> Stages,
    EquatableArray<ResourceLayoutDescription> Layouts,
    BlendStateDescription? Blend,
    DepthStencilStateDescription? DepthStencil,
    RasterizerStateDescription? Rasterizer,
    EquatableArray<VertexLayoutDescription> VertexLayouts,
    uint ThreadGroupX,
    uint ThreadGroupY,
    uint ThreadGroupZ)
{
    /// <summary>Compares layouts element by element.</summary>
    public bool Equals(RecordedProgram? other)
        => other is not null
            && Key.Equals(other.Key)
            && IsCompute == other.IsCompute
            && Stages.Equals(other.Stages)
            && ProgramEquality.Equal(Layouts, other.Layouts)
            && Blend.Equals(other.Blend)
            && DepthStencil.Equals(other.DepthStencil)
            && Rasterizer.Equals(other.Rasterizer)
            && ProgramEquality.Equal(VertexLayouts, other.VertexLayouts)
            && ThreadGroupX == other.ThreadGroupX
            && ThreadGroupY == other.ThreadGroupY
            && ThreadGroupZ == other.ThreadGroupZ;

    /// <inheritdoc/>
    public override int GetHashCode()
        => HashCode.Combine(Key, IsCompute, Stages, ProgramEquality.Hash(Layouts), ProgramEquality.Hash(VertexLayouts), ThreadGroupX, ThreadGroupY, ThreadGroupZ);
}

/// <summary>Content keyed by hash.</summary>
public sealed record RecordedBlob(BlobRef Ref, EquatableArray<byte> Data);

/// <summary>One backing of a graph resource in a view.</summary>
public sealed record RecordedBacking(TraceResourceId Id, uint EntryVersion, BackingRole Role, uint Index);

/// <summary>A graph resource as a view saw it.</summary>
public sealed record RecordedGraphResource(
    string Name,
    GraphResourceKind Kind,
    GraphResourceOrigin Origin,
    EquatableArray<RecordedBacking> Backings,
    GraphTextureDesc? Texture,
    GraphBufferDesc? Buffer);

/// <summary>A pass's declared access to a graph resource, by name.</summary>
public sealed record RecordedAccess(
    string Resource,
    GraphResourceKind Kind,
    bool IsOutput,
    TextureState TextureUsage,
    TextureState? DepthUsage,
    BufferAccess BufferUsage);

/// <summary>A resource a pass touched, with its version at first reference and after the pass.</summary>
public sealed record RecordedReference(TraceResourceId Resource, uint First, uint Last);

/// <summary>A copy taken for a pass. The blob holds the staging bytes laid out by the regions.</summary>
public sealed record RecordedCopy(TraceVersion Version, CopyPlacement Placement, EquatableArray<CopyRegion> Regions, BlobRef Blob);

/// <summary>A pass with everything needed to replay it. NotReplayable holds the reason, or null.</summary>
public sealed record DeepPass(
    string Name,
    int Index,
    EquatableArray<RecordedAccess> Accesses,
    EquatableArray<RecordedReference> References,
    EquatableArray<RecordedCommand> Commands,
    EquatableArray<RecordedCopy> Copies,
    string? NotReplayable);

/// <summary>A view of a deep recorded execution.</summary>
public sealed record DeepView(
    string Name,
    int Index,
    uint PixelWidth,
    uint PixelHeight,
    EquatableArray<RecordedGraphResource> Resources,
    EquatableArray<DeepPass> Passes);

/// <summary>One graph execution of a deep recording.</summary>
public sealed record DeepExecution(ulong ExecutionId, EquatableArray<DeepView> Views);

/// <summary>The optional features of the recording device.</summary>
public sealed record RecordedFeatures(
    bool GeometryShader,
    bool TessellationShaders,
    bool DrawIndirectBaseInstance,
    bool SamplerAnisotropy,
    bool DepthClipDisable,
    bool IndependentBlend,
    bool CommandBufferDebugMarkers,
    bool ShaderFloat64)
{
    internal static RecordedFeatures From(GraphicsDeviceFeatures f)
        => new(f.GeometryShader, f.TessellationShaders, f.DrawIndirectBaseInstance, f.SamplerAnisotropy, f.DepthClipDisable, f.IndependentBlend, f.CommandBufferDebugMarkers, f.ShaderFloat64);
}
