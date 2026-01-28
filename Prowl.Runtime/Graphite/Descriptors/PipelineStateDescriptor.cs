// This file is part of the Prowl Game Engine
// Licensed under the MIT License. See the LICENSE file in the project root for details.

using Prowl.Vector;

namespace Prowl.Runtime.Graphite;

/// <summary>
/// Describes a vertex attribute within a vertex buffer.
/// </summary>
public struct VertexAttribute
{
    /// <summary>Shader location/index.</summary>
    public uint Location;

    /// <summary>Data format of the attribute.</summary>
    public VertexFormat Format;

    /// <summary>Byte offset within the vertex.</summary>
    public uint Offset;

    public VertexAttribute(uint location, VertexFormat format, uint offset)
    {
        Location = location;
        Format = format;
        Offset = offset;
    }
}

/// <summary>
/// Describes a vertex buffer layout.
/// </summary>
public struct VertexBufferLayout
{
    /// <summary>Stride in bytes between vertices.</summary>
    public uint Stride;

    /// <summary>Whether data is per-vertex or per-instance.</summary>
    public VertexStepMode StepMode;

    /// <summary>Attributes within this buffer.</summary>
    public VertexAttribute[] Attributes;

    public VertexBufferLayout(uint stride, VertexStepMode stepMode, params VertexAttribute[] attributes)
    {
        Stride = stride;
        StepMode = stepMode;
        Attributes = attributes;
    }

    public VertexBufferLayout(uint stride, params VertexAttribute[] attributes)
    {
        Stride = stride;
        StepMode = VertexStepMode.Vertex;
        Attributes = attributes;
    }
}

/// <summary>
/// Describes the complete vertex input layout.
/// </summary>
public struct VertexLayoutDescriptor
{
    /// <summary>Array of vertex buffer layouts.</summary>
    public VertexBufferLayout[] Buffers;

    public VertexLayoutDescriptor(params VertexBufferLayout[] buffers)
    {
        Buffers = buffers;
    }

    /// <summary>
    /// Creates a simple vertex layout with position only.
    /// </summary>
    public static VertexLayoutDescriptor Position3D => new(
        new VertexBufferLayout(12, new VertexAttribute(0, VertexFormat.Float3, 0))
    );

    /// <summary>
    /// Creates a vertex layout with position and UV.
    /// </summary>
    public static VertexLayoutDescriptor PositionTexCoord => new(
        new VertexBufferLayout(20,
            new VertexAttribute(0, VertexFormat.Float3, 0),
            new VertexAttribute(1, VertexFormat.Float2, 12))
    );

    /// <summary>
    /// Creates a vertex layout with position, normal, and UV.
    /// </summary>
    public static VertexLayoutDescriptor PositionNormalTexCoord => new(
        new VertexBufferLayout(32,
            new VertexAttribute(0, VertexFormat.Float3, 0),
            new VertexAttribute(1, VertexFormat.Float3, 12),
            new VertexAttribute(2, VertexFormat.Float2, 24))
    );
}

/// <summary>
/// Describes the rasterizer state.
/// </summary>
public struct RasterizerStateDescriptor
{
    /// <summary>Polygon fill mode.</summary>
    public PolygonMode PolygonMode;

    /// <summary>Face culling mode.</summary>
    public CullMode CullMode;

    /// <summary>Front face winding order.</summary>
    public FrontFace FrontFace;

    /// <summary>Enable depth clamping.</summary>
    public bool DepthClampEnable;

    /// <summary>Enable depth bias.</summary>
    public bool DepthBiasEnable;

    /// <summary>Constant depth bias.</summary>
    public float DepthBiasConstant;

    /// <summary>Slope-scaled depth bias.</summary>
    public float DepthBiasSlope;

    public RasterizerStateDescriptor()
    {
        PolygonMode = PolygonMode.Fill;
        CullMode = CullMode.Back;
        FrontFace = FrontFace.CounterClockwise;
        DepthClampEnable = false;
        DepthBiasEnable = false;
        DepthBiasConstant = 0;
        DepthBiasSlope = 0;
    }

    public static RasterizerStateDescriptor Default => new();

    public static RasterizerStateDescriptor NoCull => new() { CullMode = CullMode.None };

    public static RasterizerStateDescriptor Wireframe => new() { PolygonMode = PolygonMode.Line, CullMode = CullMode.None };

    public static RasterizerStateDescriptor ShadowMap => new()
    {
        CullMode = CullMode.Front,
        DepthBiasEnable = true,
        DepthBiasConstant = 1.0f,
        DepthBiasSlope = 1.0f,
    };
}

/// <summary>
/// Describes stencil operations for one face.
/// </summary>
public struct StencilFaceState
{
    /// <summary>Comparison function.</summary>
    public CompareFunction Compare;

    /// <summary>Operation when stencil test fails.</summary>
    public StencilOp FailOp;

    /// <summary>Operation when stencil passes but depth fails.</summary>
    public StencilOp DepthFailOp;

    /// <summary>Operation when both tests pass.</summary>
    public StencilOp PassOp;

    public StencilFaceState()
    {
        Compare = CompareFunction.Always;
        FailOp = StencilOp.Keep;
        DepthFailOp = StencilOp.Keep;
        PassOp = StencilOp.Keep;
    }

    public static StencilFaceState Default => new();
}

/// <summary>
/// Describes the depth/stencil state.
/// </summary>
public struct DepthStencilStateDescriptor
{
    /// <summary>Enable depth testing.</summary>
    public bool DepthTestEnable;

    /// <summary>Enable depth writing.</summary>
    public bool DepthWriteEnable;

    /// <summary>Depth comparison function.</summary>
    public CompareFunction DepthCompare;

    /// <summary>Enable stencil testing.</summary>
    public bool StencilTestEnable;

    /// <summary>Stencil state for front faces.</summary>
    public StencilFaceState StencilFront;

    /// <summary>Stencil state for back faces.</summary>
    public StencilFaceState StencilBack;

    /// <summary>Stencil read mask.</summary>
    public byte StencilReadMask;

    /// <summary>Stencil write mask.</summary>
    public byte StencilWriteMask;

    public DepthStencilStateDescriptor()
    {
        DepthTestEnable = true;
        DepthWriteEnable = true;
        DepthCompare = CompareFunction.Less;
        StencilTestEnable = false;
        StencilFront = StencilFaceState.Default;
        StencilBack = StencilFaceState.Default;
        StencilReadMask = 0xFF;
        StencilWriteMask = 0xFF;
    }

    public static DepthStencilStateDescriptor Default => new();

    public static DepthStencilStateDescriptor DepthRead => new()
    {
        DepthTestEnable = true,
        DepthWriteEnable = false,
        DepthCompare = CompareFunction.LessEqual,
    };

    public static DepthStencilStateDescriptor NoDepth => new()
    {
        DepthTestEnable = false,
        DepthWriteEnable = false,
    };
}

/// <summary>
/// Describes blending for a single color attachment.
/// </summary>
public struct BlendAttachment
{
    /// <summary>Enable blending.</summary>
    public bool BlendEnable;

    /// <summary>Source color blend factor.</summary>
    public BlendFactor SrcColorFactor;

    /// <summary>Destination color blend factor.</summary>
    public BlendFactor DstColorFactor;

    /// <summary>Color blend operation.</summary>
    public BlendOp ColorOp;

    /// <summary>Source alpha blend factor.</summary>
    public BlendFactor SrcAlphaFactor;

    /// <summary>Destination alpha blend factor.</summary>
    public BlendFactor DstAlphaFactor;

    /// <summary>Alpha blend operation.</summary>
    public BlendOp AlphaOp;

    /// <summary>Color write mask.</summary>
    public ColorWriteMask WriteMask;

    public BlendAttachment()
    {
        BlendEnable = false;
        SrcColorFactor = BlendFactor.One;
        DstColorFactor = BlendFactor.Zero;
        ColorOp = BlendOp.Add;
        SrcAlphaFactor = BlendFactor.One;
        DstAlphaFactor = BlendFactor.Zero;
        AlphaOp = BlendOp.Add;
        WriteMask = ColorWriteMask.All;
    }

    /// <summary>No blending, just write.</summary>
    public static BlendAttachment Opaque => new();

    /// <summary>Standard alpha blending.</summary>
    public static BlendAttachment AlphaBlend => new()
    {
        BlendEnable = true,
        SrcColorFactor = BlendFactor.SrcAlpha,
        DstColorFactor = BlendFactor.OneMinusSrcAlpha,
        ColorOp = BlendOp.Add,
        SrcAlphaFactor = BlendFactor.One,
        DstAlphaFactor = BlendFactor.OneMinusSrcAlpha,
        AlphaOp = BlendOp.Add,
        WriteMask = ColorWriteMask.All,
    };

    /// <summary>Premultiplied alpha blending.</summary>
    public static BlendAttachment PremultipliedAlpha => new()
    {
        BlendEnable = true,
        SrcColorFactor = BlendFactor.One,
        DstColorFactor = BlendFactor.OneMinusSrcAlpha,
        ColorOp = BlendOp.Add,
        SrcAlphaFactor = BlendFactor.One,
        DstAlphaFactor = BlendFactor.OneMinusSrcAlpha,
        AlphaOp = BlendOp.Add,
        WriteMask = ColorWriteMask.All,
    };

    /// <summary>Additive blending.</summary>
    public static BlendAttachment Additive => new()
    {
        BlendEnable = true,
        SrcColorFactor = BlendFactor.SrcAlpha,
        DstColorFactor = BlendFactor.One,
        ColorOp = BlendOp.Add,
        SrcAlphaFactor = BlendFactor.One,
        DstAlphaFactor = BlendFactor.One,
        AlphaOp = BlendOp.Add,
        WriteMask = ColorWriteMask.All,
    };
}

/// <summary>
/// Describes the blend state for all attachments.
/// </summary>
public struct BlendStateDescriptor
{
    /// <summary>Blend state for each color attachment.</summary>
    public BlendAttachment[] Attachments;

    /// <summary>Blend constant color.</summary>
    public Float4 BlendConstants;

    public BlendStateDescriptor()
    {
        Attachments = [BlendAttachment.Opaque];
        BlendConstants = Float4.Zero;
    }

    public BlendStateDescriptor(params BlendAttachment[] attachments)
    {
        Attachments = attachments;
        BlendConstants = Float4.Zero;
    }

    public static BlendStateDescriptor Opaque => new([BlendAttachment.Opaque]);
    public static BlendStateDescriptor AlphaBlend => new([BlendAttachment.AlphaBlend]);
    public static BlendStateDescriptor Additive => new([BlendAttachment.Additive]);
}

/// <summary>
/// Describes the render pass layout for pipeline compatibility.
/// </summary>
public struct RenderPassLayout
{
    /// <summary>Formats of color attachments.</summary>
    public TextureFormat[] ColorFormats;

    /// <summary>Format of depth/stencil attachment (null if none).</summary>
    public TextureFormat? DepthStencilFormat;

    /// <summary>Sample count for multisampling.</summary>
    public SampleCount SampleCount;

    public RenderPassLayout()
    {
        ColorFormats = [];
        DepthStencilFormat = null;
        SampleCount = SampleCount.Count1;
    }

    public RenderPassLayout(TextureFormat[] colorFormats, TextureFormat? depthStencilFormat = null, SampleCount sampleCount = SampleCount.Count1)
    {
        ColorFormats = colorFormats;
        DepthStencilFormat = depthStencilFormat;
        SampleCount = sampleCount;
    }

    public static RenderPassLayout SingleColor(TextureFormat colorFormat, TextureFormat? depthFormat = TextureFormat.Depth24Plus) => new()
    {
        ColorFormats = [colorFormat],
        DepthStencilFormat = depthFormat,
        SampleCount = SampleCount.Count1,
    };
}

/// <summary>
/// Describes how to create a graphics pipeline state.
/// </summary>
public struct PipelineStateDescriptor
{
    /// <summary>Vertex shader module.</summary>
    public ShaderModule? VertexShader;

    /// <summary>Fragment shader module.</summary>
    public ShaderModule? FragmentShader;

    /// <summary>Geometry shader module (optional).</summary>
    public ShaderModule? GeometryShader;

    /// <summary>Vertex input layout.</summary>
    public VertexLayoutDescriptor VertexLayout;

    /// <summary>Primitive topology.</summary>
    public PrimitiveTopology Topology;

    /// <summary>Rasterizer state.</summary>
    public RasterizerStateDescriptor RasterizerState;

    /// <summary>Depth/stencil state.</summary>
    public DepthStencilStateDescriptor DepthStencilState;

    /// <summary>Blend state.</summary>
    public BlendStateDescriptor BlendState;

    /// <summary>Render pass layout for compatibility.</summary>
    public RenderPassLayout RenderPassLayout;

    /// <summary>Bind group layouts used by this pipeline.</summary>
    public BindGroupLayout[]? BindGroupLayouts;

    /// <summary>Optional debug name for graphics debuggers.</summary>
    public string? DebugName;

    public PipelineStateDescriptor()
    {
        VertexShader = null;
        FragmentShader = null;
        GeometryShader = null;
        VertexLayout = new();
        Topology = PrimitiveTopology.TriangleList;
        RasterizerState = RasterizerStateDescriptor.Default;
        DepthStencilState = DepthStencilStateDescriptor.Default;
        BlendState = BlendStateDescriptor.Opaque;
        RenderPassLayout = new();
        BindGroupLayouts = null;
        DebugName = null;
    }
}

/// <summary>
/// Describes how to create a compute pipeline state.
/// </summary>
public struct ComputePipelineStateDescriptor
{
    /// <summary>Compute shader module.</summary>
    public ShaderModule? ComputeShader;

    /// <summary>Bind group layouts used by this pipeline.</summary>
    public BindGroupLayout[]? BindGroupLayouts;

    /// <summary>Optional debug name for graphics debuggers.</summary>
    public string? DebugName;
}
