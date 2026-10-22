namespace Prowl.Graphite;

/// <summary>
/// Optional features a device supports.
/// </summary>
public class GraphicsDeviceFeatures
{
    /// <summary>
    /// Geometry shaders usable.
    /// </summary>
    public bool GeometryShader { get; }
    /// <summary>
    /// Tessellation shaders usable.
    /// </summary>
    public bool TessellationShaders { get; }
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
    /// Per-attachment blend state supported. Otherwise all attachments share one blend state.
    /// </summary>
    public bool IndependentBlend { get; }
    /// <summary>
    /// CommandBuffer debug markers (PushDebugGroup/PopDebugGroup/InsertDebugMarker) actually do something. Otherwise they're no-ops.
    /// </summary>
    public bool CommandBufferDebugMarkers { get; }
    /// <summary>
    /// 64-bit floats usable in shaders.
    /// </summary>
    public bool ShaderFloat64 { get; }
    /// <summary>
    /// Indirect draws can issue more than one draw per call.
    /// </summary>
    public bool MultiDrawIndirect { get; }
    /// <summary>
    /// Indirect draws can read their draw count from a buffer.
    /// </summary>
    public bool DrawIndirectCount { get; }
    /// <summary>
    /// Partially bound, update after bind, non uniformly indexed arrays of sampled images and storage buffers, for bindless
    /// descriptor tables. Storage images in a table are written after bind only where the device can.
    /// </summary>
    public bool DescriptorIndexing { get; }
    /// <summary>
    /// One draw renders to several layers through gl_ViewIndex.
    /// </summary>
    public bool Multiview { get; }
    /// <summary>
    /// Vertex shaders can write gl_Layer.
    /// </summary>
    public bool ShaderOutputLayer { get; }
    /// <summary>
    /// Vertex shaders can write gl_ViewportIndex.
    /// </summary>
    public bool ShaderOutputViewportIndex { get; }
    /// <summary>
    /// Samplers can reduce with min or max instead of averaging.
    /// </summary>
    public bool SamplerFilterMinmax { get; }
    /// <summary>
    /// BC1 to BC7 block compressed textures can be sampled, as on desktop GPUs.
    /// </summary>
    public bool TextureCompressionBC { get; }
    /// <summary>
    /// ETC2 and EAC block compressed textures can be sampled, as on mobile GPUs.
    /// </summary>
    public bool TextureCompressionEtc2 { get; }
    /// <summary>
    /// ASTC block compressed textures can be sampled, as on mobile GPUs.
    /// </summary>
    public bool TextureCompressionAstc { get; }

    internal GraphicsDeviceFeatures(
        bool geometryShader,
        bool tessellationShaders,
        bool drawIndirectBaseInstance,
        bool samplerAnisotropy,
        bool depthClipDisable,
        bool independentBlend,
        bool commandBufferDebugMarkers,
        bool shaderFloat64,
        bool multiDrawIndirect,
        bool drawIndirectCount,
        bool descriptorIndexing,
        bool multiview,
        bool shaderOutputLayer,
        bool shaderOutputViewportIndex,
        bool samplerFilterMinmax,
        bool textureCompressionBC,
        bool textureCompressionEtc2,
        bool textureCompressionAstc)
    {
        GeometryShader = geometryShader;
        TessellationShaders = tessellationShaders;
        DrawIndirectBaseInstance = drawIndirectBaseInstance;
        SamplerAnisotropy = samplerAnisotropy;
        DepthClipDisable = depthClipDisable;
        IndependentBlend = independentBlend;
        CommandBufferDebugMarkers = commandBufferDebugMarkers;
        ShaderFloat64 = shaderFloat64;
        MultiDrawIndirect = multiDrawIndirect;
        DrawIndirectCount = drawIndirectCount;
        DescriptorIndexing = descriptorIndexing;
        Multiview = multiview;
        ShaderOutputLayer = shaderOutputLayer;
        ShaderOutputViewportIndex = shaderOutputViewportIndex;
        SamplerFilterMinmax = samplerFilterMinmax;
        TextureCompressionBC = textureCompressionBC;
        TextureCompressionEtc2 = textureCompressionEtc2;
        TextureCompressionAstc = textureCompressionAstc;
    }
}
