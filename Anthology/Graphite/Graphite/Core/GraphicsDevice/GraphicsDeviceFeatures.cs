namespace Prowl.Graphite;

/// <summary>
/// Optional features a device supports.
/// </summary>
public class GraphicsDeviceFeatures
{
    /// <summary>
    /// Compute shaders usable.
    /// </summary>
    public bool ComputeShader { get; }
    /// <summary>
    /// Geometry shaders usable.
    /// </summary>
    public bool GeometryShader { get; }
    /// <summary>
    /// Tessellation shaders usable.
    /// </summary>
    public bool TessellationShaders { get; }
    /// <summary>
    /// Multiple viewports can be set at once. If not, only viewport 0 is used for all outputs.
    /// </summary>
    public bool MultipleViewports { get; }
    /// <summary>
    /// Sampler LodBias can be non-zero. Otherwise non-zero bias is an error.
    /// </summary>
    public bool SamplerLodBias { get; }
    /// <summary>
    /// Non-zero vertexStart allowed in Draw/DrawIndexed.
    /// </summary>
    public bool DrawBaseVertex { get; }
    /// <summary>
    /// Non-zero instanceStart allowed in Draw/DrawIndexed.
    /// </summary>
    public bool DrawBaseInstance { get; }
    /// <summary>
    /// Indirect draw commands supported.
    /// </summary>
    public bool DrawIndirect { get; }
    /// <summary>
    /// Indirect draw structs can have non-zero FirstInstance.
    /// </summary>
    public bool DrawIndirectBaseInstance { get; }
    /// <summary>
    /// Anisotropic sampler filter supported.
    /// </summary>
    public bool SamplerAnisotropy { get; }
    /// <summary>
    /// DepthClipEnabled can be set false.
    /// </summary>
    public bool DepthClipDisable { get; }
    /// <summary>
    /// 1D textures supported.
    /// </summary>
    public bool Texture1D { get; }
    /// <summary>
    /// Per-attachment blend state supported. Otherwise all attachments share one blend state.
    /// </summary>
    public bool IndependentBlend { get; }
    /// <summary>
    /// Structured buffers (read-only/read-write) supported. Otherwise cannot create them.
    /// </summary>
    public bool StructuredBuffer { get; }
    /// <summary>
    /// TextureView can view a subset of mips/layers or use a different format than its target texture.
    /// </summary>
    public bool SubsetTextureView { get; }
    /// <summary>
    /// CommandBuffer debug markers (PushDebugGroup/PopDebugGroup/InsertDebugMarker) actually do something. Otherwise they're no-ops.
    /// </summary>
    public bool CommandBufferDebugMarkers { get; }
    /// <summary>
    /// Uniform/structured buffers can bind with offset+size. Otherwise must bind full range.
    /// </summary>
    public bool BufferRangeBinding { get; }
    /// <summary>
    /// 64-bit floats usable in shaders.
    /// </summary>
    public bool ShaderFloat64 { get; }

    internal GraphicsDeviceFeatures(
        bool computeShader,
        bool geometryShader,
        bool tessellationShaders,
        bool multipleViewports,
        bool samplerLodBias,
        bool drawBaseVertex,
        bool drawBaseInstance,
        bool drawIndirect,
        bool drawIndirectBaseInstance,
        bool samplerAnisotropy,
        bool depthClipDisable,
        bool texture1D,
        bool independentBlend,
        bool structuredBuffer,
        bool subsetTextureView,
        bool commandBufferDebugMarkers,
        bool bufferRangeBinding,
        bool shaderFloat64)
    {
        ComputeShader = computeShader;
        GeometryShader = geometryShader;
        TessellationShaders = tessellationShaders;
        MultipleViewports = multipleViewports;
        SamplerLodBias = samplerLodBias;
        DrawBaseVertex = drawBaseVertex;
        DrawBaseInstance = drawBaseInstance;
        DrawIndirect = drawIndirect;
        DrawIndirectBaseInstance = drawIndirectBaseInstance;
        SamplerAnisotropy = samplerAnisotropy;
        DepthClipDisable = depthClipDisable;
        Texture1D = texture1D;
        IndependentBlend = independentBlend;
        StructuredBuffer = structuredBuffer;
        SubsetTextureView = subsetTextureView;
        CommandBufferDebugMarkers = commandBufferDebugMarkers;
        BufferRangeBinding = bufferRangeBinding;
        ShaderFloat64 = shaderFloat64;
    }
}
