// This file is part of the Prowl Game Engine
// Licensed under the MIT License. See the LICENSE file in the project root for details.

using System;

namespace Prowl.Runtime.Graphite;

/// <summary>
/// The graphics backend type.
/// </summary>
public enum GraphicsBackendType
{
    OpenGL
}

/// <summary>
/// Buffer usage flags indicating how a buffer will be used.
/// </summary>
[Flags]
public enum BufferUsage
{
    None = 0,
    /// <summary>Buffer can be used as a vertex buffer.</summary>
    Vertex = 1 << 0,
    /// <summary>Buffer can be used as an index buffer.</summary>
    Index = 1 << 1,
    /// <summary>Buffer can be used as a uniform/constant buffer.</summary>
    Uniform = 1 << 2,
    /// <summary>Buffer can be used as a shader storage buffer.</summary>
    Storage = 1 << 3,
    /// <summary>Buffer can be used for indirect draw arguments.</summary>
    Indirect = 1 << 4,
    /// <summary>Buffer can be used as a copy source.</summary>
    CopySource = 1 << 5,
    /// <summary>Buffer can be used as a copy destination.</summary>
    CopyDestination = 1 << 6,
}

/// <summary>
/// Memory access pattern for buffers.
/// </summary>
public enum MemoryAccess
{
    /// <summary>Fastest GPU access, no CPU access. Use staging buffer for updates.</summary>
    GpuOnly,
    /// <summary>CPU write, GPU read. Good for uniforms and dynamic vertex data.</summary>
    CpuToGpu,
    /// <summary>GPU write, CPU read. Good for readback operations.</summary>
    GpuToCpu,
}

/// <summary>
/// Texture dimension type.
/// </summary>
public enum TextureDimension
{
    Texture1D,
    Texture2D,
    Texture3D,
    TextureCube,
}

/// <summary>
/// Texture usage flags indicating how a texture will be used.
/// </summary>
[Flags]
public enum TextureUsage
{
    None = 0,
    /// <summary>Texture can be sampled in shaders.</summary>
    Sampled = 1 << 0,
    /// <summary>Texture can be written to in compute shaders.</summary>
    Storage = 1 << 1,
    /// <summary>Texture can be used as a color attachment.</summary>
    RenderTarget = 1 << 2,
    /// <summary>Texture can be used as a depth/stencil attachment.</summary>
    DepthStencil = 1 << 3,
    /// <summary>Texture can be used as a copy source.</summary>
    CopySource = 1 << 4,
    /// <summary>Texture can be used as a copy destination.</summary>
    CopyDestination = 1 << 5,
}

/// <summary>
/// Texture formats for color, depth, and compressed textures.
/// </summary>
public enum TextureFormat
{
    // Unknown/undefined
    Undefined = 0,

    // 8-bit formats
    R8Unorm,
    R8Snorm,
    R8Uint,
    R8Sint,

    // 16-bit formats (2 channel 8-bit)
    RG8Unorm,
    RG8Snorm,
    RG8Uint,
    RG8Sint,

    // 32-bit formats (4 channel 8-bit)
    RGBA8Unorm,
    RGBA8UnormSrgb,
    RGBA8Snorm,
    RGBA8Uint,
    RGBA8Sint,
    BGRA8Unorm,
    BGRA8UnormSrgb,

    // 16-bit per channel formats
    R16Uint,
    R16Sint,
    R16Float,
    RG16Uint,
    RG16Sint,
    RG16Float,
    RGBA16Uint,
    RGBA16Sint,
    RGBA16Float,

    // 32-bit per channel formats
    R32Uint,
    R32Sint,
    R32Float,
    RG32Uint,
    RG32Sint,
    RG32Float,
    RGBA32Uint,
    RGBA32Sint,
    RGBA32Float,

    // Packed formats
    RGB10A2Unorm,
    RG11B10Float,

    // Depth/stencil formats
    Depth16Unorm,
    Depth24Plus,
    Depth24PlusStencil8,
    Depth32Float,
    Depth32FloatStencil8,

    // Compressed formats (BC/DXT)
    BC1Unorm,
    BC1UnormSrgb,
    BC2Unorm,
    BC2UnormSrgb,
    BC3Unorm,
    BC3UnormSrgb,
    BC4Unorm,
    BC4Snorm,
    BC5Unorm,
    BC5Snorm,
    BC6HUfloat,
    BC6HSfloat,
    BC7Unorm,
    BC7UnormSrgb,
}

/// <summary>
/// Multisample count for textures and render targets.
/// </summary>
public enum SampleCount
{
    Count1 = 1,
    Count2 = 2,
    Count4 = 4,
    Count8 = 8,
    Count16 = 16,
}

/// <summary>
/// Texture filtering mode.
/// </summary>
public enum TextureFilter
{
    Nearest,
    Linear,
}

/// <summary>
/// Texture address/wrap mode.
/// </summary>
public enum TextureAddressMode
{
    Repeat,
    MirrorRepeat,
    ClampToEdge,
    ClampToBorder,
}

/// <summary>
/// Border color for ClampToBorder address mode.
/// </summary>
public enum BorderColor
{
    TransparentBlack,
    OpaqueBlack,
    OpaqueWhite,
}

/// <summary>
/// Shader stage flags.
/// </summary>
[Flags]
public enum ShaderStage
{
    None = 0,
    Vertex = 1 << 0,
    Fragment = 1 << 1,
    Geometry = 1 << 2,
    TessellationControl = 1 << 3,
    TessellationEvaluation = 1 << 4,
    Compute = 1 << 5,

    AllGraphics = Vertex | Fragment | Geometry | TessellationControl | TessellationEvaluation,
    All = AllGraphics | Compute,
}

/// <summary>
/// Shader source type.
/// </summary>
public enum ShaderSourceType
{
    GLSL,
    SPIRV,
}

/// <summary>
/// Primitive topology for draw calls.
/// </summary>
public enum PrimitiveTopology
{
    PointList,
    LineList,
    LineStrip,
    TriangleList,
    TriangleStrip,
}

/// <summary>
/// Vertex input step mode.
/// </summary>
public enum VertexStepMode
{
    /// <summary>Attribute data is per-vertex.</summary>
    Vertex,
    /// <summary>Attribute data is per-instance.</summary>
    Instance,
}

/// <summary>
/// Vertex attribute format.
/// </summary>
public enum VertexFormat
{
    // Floating point
    Float,
    Float2,
    Float3,
    Float4,

    // Signed integer
    Int,
    Int2,
    Int3,
    Int4,

    // Unsigned integer
    Uint,
    Uint2,
    Uint3,
    Uint4,

    // Packed formats
    Short2,
    Short4,
    Short2Norm,
    Short4Norm,
    Byte4,
    Byte4Norm,
    UByte4,
    UByte4Norm,
}

/// <summary>
/// Polygon fill mode.
/// </summary>
public enum PolygonMode
{
    Fill,
    Line,
    Point,
}

/// <summary>
/// Face culling mode.
/// </summary>
public enum CullMode
{
    None,
    Front,
    Back,
}

/// <summary>
/// Front face winding order.
/// </summary>
public enum FrontFace
{
    CounterClockwise,
    Clockwise,
}

/// <summary>
/// Comparison function for depth, stencil, and sampler operations.
/// </summary>
public enum CompareFunction
{
    Never,
    Less,
    Equal,
    LessEqual,
    Greater,
    NotEqual,
    GreaterEqual,
    Always,
}

/// <summary>
/// Stencil operation.
/// </summary>
public enum StencilOp
{
    Keep,
    Zero,
    Replace,
    IncrementClamp,
    DecrementClamp,
    Invert,
    IncrementWrap,
    DecrementWrap,
}

/// <summary>
/// Blend factor.
/// </summary>
public enum BlendFactor
{
    Zero,
    One,
    SrcColor,
    OneMinusSrcColor,
    DstColor,
    OneMinusDstColor,
    SrcAlpha,
    OneMinusSrcAlpha,
    DstAlpha,
    OneMinusDstAlpha,
    ConstantColor,
    OneMinusConstantColor,
    SrcAlphaSaturate,
}

/// <summary>
/// Blend operation.
/// </summary>
public enum BlendOp
{
    Add,
    Subtract,
    ReverseSubtract,
    Min,
    Max,
}

/// <summary>
/// Color write mask.
/// </summary>
[Flags]
public enum ColorWriteMask
{
    None = 0,
    Red = 1 << 0,
    Green = 1 << 1,
    Blue = 1 << 2,
    Alpha = 1 << 3,
    All = Red | Green | Blue | Alpha,
}

/// <summary>
/// Resource binding type in a bind group.
/// </summary>
public enum BindingType
{
    UniformBuffer,
    StorageBuffer,
    ReadOnlyStorageBuffer,
    Sampler,
    SampledTexture,
    StorageTexture,
    CombinedTextureSampler,
}

/// <summary>
/// Load operation for render pass attachments.
/// </summary>
public enum LoadOp
{
    /// <summary>Preserve existing contents.</summary>
    Load,
    /// <summary>Clear to specified value.</summary>
    Clear,
    /// <summary>Contents undefined (fastest).</summary>
    DontCare,
}

/// <summary>
/// Store operation for render pass attachments.
/// </summary>
public enum StoreOp
{
    /// <summary>Write results to memory.</summary>
    Store,
    /// <summary>Contents may be discarded (fastest for transient).</summary>
    DontCare,
}

/// <summary>
/// Index buffer format.
/// </summary>
public enum IndexFormat
{
    Uint16,
    Uint32,
}

/// <summary>
/// Resource state for barriers.
/// </summary>
public enum ResourceState
{
    Undefined,
    Common,
    VertexBuffer,
    IndexBuffer,
    UniformBuffer,
    ShaderResource,
    UnorderedAccess,
    RenderTarget,
    DepthWrite,
    DepthRead,
    CopySource,
    CopyDestination,
    Present,
}
