using Prowl.Graphite.Debugger.Trace;
using Prowl.Graphite.Debugging;
using Prowl.Graphite.RenderGraph;
using Prowl.Vector;

namespace Prowl.Graphite.Debugger;

/// <summary>One command a pass recorded, with resources as trace ids and versions.</summary>
public abstract record RecordedCommand;

/// <summary>A texture subresource bound as a render target.</summary>
public sealed record RecordedAttachment(TraceVersion Texture, uint MipLevel, uint ArrayLayer);

/// <summary>A vertex buffer bound to a layout slot.</summary>
public sealed record RecordedVertexBinding(uint Slot, TraceVersion Buffer, uint Offset, uint Stride);

/// <summary>A property of the resolved table. The sampler is an index into the recording's samplers, or -1.</summary>
public sealed record RecordedProperty(
    string Name,
    PropertyKind Kind,
    UniformScalarType UniformType,
    EquatableArray<byte> Uniform,
    TraceVersion Resource,
    ResourceRange Range,
    PixelFormat? ViewFormat,
    int Sampler);

public sealed record SetFramebufferCommand(
    EquatableArray<RecordedAttachment> Colors,
    RecordedAttachment? Depth,
    OutputDescription Outputs,
    uint Width,
    uint Height,
    TargetLoadStoreOps Ops) : RecordedCommand;

public sealed record ClearColorTargetCommand(uint Index, Color Color) : RecordedCommand;

public sealed record ClearDepthStencilCommand(float Depth, byte Stencil) : RecordedCommand;

public sealed record SetPipelineCommand(ProgramKey Program, bool IsCompute, OutputDescription? Outputs, PrimitiveTopology? Topology) : RecordedCommand;

public sealed record SetViewportCommand(Viewport Viewport) : RecordedCommand;

public sealed record SetScissorCommand(uint X, uint Y, uint Width, uint Height) : RecordedCommand;

public sealed record SetStencilReferenceCommand(uint Reference) : RecordedCommand;

public sealed record SetBlendConstantsCommand(Color Constants) : RecordedCommand;

/// <summary>Slots that changed since the previous bind in the pass, and the slot count after the bind.</summary>
public sealed record BindVertexBuffersCommand(EquatableArray<RecordedVertexBinding> Changed, int Count) : RecordedCommand;

public sealed record BindIndexBufferCommand(TraceVersion Buffer, IndexFormat Format, uint IndexCount) : RecordedCommand;

/// <summary>Properties that changed since the previous table in the pass, and the names that left it.</summary>
public sealed record SetPropertiesCommand(EquatableArray<RecordedProperty> Changed, EquatableArray<string> Removed) : RecordedCommand;

public sealed record DrawCommand(uint VertexCount, uint InstanceCount, uint FirstVertex, uint FirstInstance) : RecordedCommand;

public sealed record DrawIndexedCommand(uint InstanceCount, uint FirstIndex, int VertexOffset, uint FirstInstance) : RecordedCommand;

public sealed record DrawIndirectCommand(TraceVersion Buffer, uint Offset, uint DrawCount, uint Stride) : RecordedCommand;

public sealed record DrawIndexedIndirectCommand(TraceVersion Buffer, uint Offset, uint DrawCount, uint Stride) : RecordedCommand;

public sealed record DispatchCommand(uint GroupCountX, uint GroupCountY, uint GroupCountZ) : RecordedCommand;

public sealed record DispatchIndirectCommand(TraceVersion Buffer, uint Offset) : RecordedCommand;

public sealed record UpdateBufferCommand(TraceVersion After, uint Offset, BlobRef Data) : RecordedCommand;

public sealed record UpdateTextureCommand(TraceVersion After, TextureRegion Region, BlobRef Data) : RecordedCommand;

public sealed record CopyBufferCommand(TraceVersion Source, uint SourceOffset, TraceVersion DestinationAfter, uint DestinationOffset, uint SizeInBytes) : RecordedCommand;

public sealed record CopyTextureCommand(TraceVersion Source, TextureRegion SourceRegion, TraceVersion DestinationAfter, TextureRegion DestinationRegion, uint LayerCount) : RecordedCommand;

public sealed record CopyTextureToBufferCommand(TraceVersion Source, TextureRegion Region, TraceVersion DestinationAfter, uint DestinationOffset) : RecordedCommand;

public sealed record ResolveTextureCommand(TraceVersion Source, TraceVersion DestinationAfter) : RecordedCommand;

public sealed record GenerateMipsCommand(TraceVersion TextureAfter) : RecordedCommand;
