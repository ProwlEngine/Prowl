using Prowl.Graphite.RenderGraph;
using Prowl.Vector;

namespace Prowl.Graphite.Debugger.Trace;

public enum PropertyDeltaKind : byte
{
    Uniform,
    Buffer,
    UniformBuffer,
    Texture,
    Sampler,
    Removed,
}

public readonly record struct TraceViewInfo(
    uint BaseMipLevel,
    uint MipLevels,
    uint BaseArrayLayer,
    uint ArrayLayers,
    PixelFormat? Format);

public sealed record TracePropertyDelta(
    string Name,
    PropertyDeltaKind Kind,
    UniformScalarType UniformType,
    EquatableArray<byte> Uniform,
    TraceVersion? Resource,
    TraceRange Range,
    TraceViewInfo? View,
    SamplerDescription? Sampler);

public readonly record struct TraceAttachment(
    TraceResourceId Resource,
    uint Version,
    uint MipLevel,
    uint ArrayLayer);

public sealed record TracePipelineState(
    TraceProgramId Program,
    BlobRef VariantKey,
    PrimitiveTopology Topology,
    EquatableArray<PixelFormat> ColorFormats,
    PixelFormat? DepthFormat,
    TextureSampleCount SampleCount,
    RasterizerStateDescription Rasterizer,
    DepthStencilStateDescription DepthStencil,
    Color BlendFactor,
    EquatableArray<BlendAttachmentDescription> BlendAttachments,
    bool AlphaToCoverage);

public readonly record struct TraceVertexBinding(
    uint Slot,
    TraceVersion Buffer,
    TraceRange Range,
    uint Stride);

public abstract record TraceCommand;

public sealed record SetFramebufferCommand(
    EquatableArray<TraceAttachment> Colors,
    TraceAttachment? Depth,
    TargetLoadStoreOps Ops) : TraceCommand;

public sealed record ClearColorTargetCommand(uint Index, Color Color) : TraceCommand;

public sealed record ClearDepthStencilCommand(float Depth, byte Stencil) : TraceCommand;

public sealed record SetPipelineCommand(TracePipelineState Pipeline) : TraceCommand;

public sealed record SetViewportCommand(Viewport Viewport) : TraceCommand;

public sealed record SetScissorCommand(uint X, uint Y, uint Width, uint Height) : TraceCommand;

public sealed record SetStencilReferenceCommand(uint Reference) : TraceCommand;

public sealed record SetBlendConstantsCommand(Color Color) : TraceCommand;

public sealed record BindVertexBuffersCommand(EquatableArray<TraceVertexBinding> Bindings) : TraceCommand;

public sealed record BindIndexBufferCommand(TraceVersion Buffer, TraceRange Range, IndexFormat Format) : TraceCommand;

public sealed record ApplyPropertyDeltasCommand(EquatableArray<TracePropertyDelta> Deltas) : TraceCommand;

public sealed record ClearPropertiesCommand : TraceCommand;

public sealed record DrawCommand(uint VertexCount, uint InstanceCount, uint VertexStart, uint InstanceStart) : TraceCommand;

public sealed record DrawIndexedCommand(uint IndexCount, uint InstanceCount, uint IndexStart, int VertexOffset, uint InstanceStart) : TraceCommand;

public sealed record DrawIndirectCommand(TraceVersion Buffer, uint Offset, uint DrawCount, uint Stride, bool Indexed) : TraceCommand;

public sealed record DispatchCommand(uint GroupCountX, uint GroupCountY, uint GroupCountZ) : TraceCommand;

public sealed record DispatchIndirectCommand(TraceVersion Buffer, uint Offset) : TraceCommand;

public sealed record UpdateBufferCommand(TraceVersion After, TraceRange Range, BlobRef Data) : TraceCommand;

public sealed record UpdateTextureCommand(TraceVersion After, TextureRegion Region, BlobRef Data) : TraceCommand;

public sealed record CopyBufferCommand(TraceVersion Source, TraceRange SourceRange, TraceVersion DestinationAfter, TraceRange DestinationRange) : TraceCommand;

public sealed record CopyTextureCommand(TraceVersion Source, TraceRange SourceRange, TraceVersion DestinationAfter, TraceRange DestinationRange) : TraceCommand;

public sealed record CopyTextureToBufferCommand(TraceVersion Source, TextureRegion Region, TraceVersion DestinationAfter, ulong DestinationOffset) : TraceCommand;

public sealed record ResolveTextureCommand(TraceVersion Source, TraceVersion DestinationAfter) : TraceCommand;

public sealed record GenerateMipsCommand(TraceVersion After) : TraceCommand;
