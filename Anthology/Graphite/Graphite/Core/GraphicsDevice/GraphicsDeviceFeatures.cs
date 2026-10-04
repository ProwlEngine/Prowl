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
    /// Multiple viewports can be set at once. If not, only viewport 0 is used for all outputs.
    /// </summary>
    public bool MultipleViewports { get; }
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

    internal GraphicsDeviceFeatures(
        bool geometryShader,
        bool tessellationShaders,
        bool multipleViewports,
        bool drawIndirectBaseInstance,
        bool samplerAnisotropy,
        bool depthClipDisable,
        bool independentBlend,
        bool commandBufferDebugMarkers,
        bool shaderFloat64)
    {
        GeometryShader = geometryShader;
        TessellationShaders = tessellationShaders;
        MultipleViewports = multipleViewports;
        DrawIndirectBaseInstance = drawIndirectBaseInstance;
        SamplerAnisotropy = samplerAnisotropy;
        DepthClipDisable = depthClipDisable;
        IndependentBlend = independentBlend;
        CommandBufferDebugMarkers = commandBufferDebugMarkers;
        ShaderFloat64 = shaderFloat64;
    }
}
