// This file is part of the Prowl Game Engine
// Licensed under the MIT License. See the LICENSE file in the project root for details.

using System;

using Prowl.Echo;

namespace Prowl.Runtime.GraphTools.ShaderGraphs;

/// <summary>
/// A node-based shader source. Compiled by <c>ShaderGraphCompiler</c> into a regular
/// Prowl <see cref="Resources.Shader"/> sub-asset alongside the graph itself
/// (gltf-importer pattern). Materials reference the compiled shader sub-asset directly.
/// </summary>
public sealed class ShaderGraph : Graph
{
    /// <summary>
    /// Stable identifier of the shader type this graph belongs to (Surface / PostEffect /
    /// Grass / Terrain / Particle / ...). Decides which master node, which passes,
    /// and which nodes are available in the editor's node browser. Resolved via
    /// <see cref="ShaderTypeRegistry"/> at compile time.
    /// </summary>
    public string ShaderTypeId = "Surface";

    /// <summary>Fixed-function render state emitted into the .shader pass block -
    /// blend mode, cull, z-write, z-test, render queue. Lives on the graph asset so the
    /// sidebar can edit it without touching individual nodes. Echo-serialised fields
    /// only; initialised to opaque-lit defaults.</summary>
    public ShaderGraphRenderSettings RenderSettings = ShaderGraphRenderSettings.OpaqueDefaults();

    public ShaderGraph() : base("New Shader Graph") { }

    public override Type NodeMarkerInterface => typeof(IShaderGraphNode);

    // The base Graph.Serialize only writes nodes/edges/blackboard/etc it doesn't
    // reflect subclass fields. Override to persist ShaderTypeId + RenderSettings so
    // they round-trip through .shadergraph save/load.
    public override void Serialize(ref EchoObject compound, SerializationContext ctx)
    {
        base.Serialize(ref compound, ctx);
        compound.Add("ShaderTypeId",   Serializer.Serialize(typeof(string),                    ShaderTypeId,   ctx));
        compound.Add("RenderSettings", Serializer.Serialize(typeof(ShaderGraphRenderSettings), RenderSettings, ctx));
    }

    public override void Deserialize(EchoObject value, SerializationContext ctx)
    {
        base.Deserialize(value, ctx);
        var stTag = value.Get("ShaderTypeId");
        if (stTag != null) ShaderTypeId = Serializer.Deserialize<string>(stTag, ctx) ?? "Surface";
        var rsTag = value.Get("RenderSettings");
        if (rsTag != null) RenderSettings = Serializer.Deserialize<ShaderGraphRenderSettings>(rsTag, ctx);
    }
}

/// <summary>High-level blend preset. <see cref="Custom"/> unlocks the raw Src/Dst/Op
/// fields on <see cref="ShaderGraphRenderSettings"/> for fine-grained control.</summary>
public enum ShaderBlendMode
{
    Opaque,         // Blend Off solid surfaces
    Alpha,          // SrcAlpha OneMinusSrcAlpha transparency
    Additive,       // One One sparks, glows
    Override,       // One Zero replace destination (parser preset)
    Multiply,       // DstColor Zero tint overlays (block form no parser preset)
    Premultiplied,  // One OneMinusSrcAlpha premultiplied-alpha (UI / particles)
    Custom,         // Use explicit BlendSrc / BlendDst / BlendOp fields below
}

/// <summary>Source/dest blend factor names match the parser's <c>Blending</c> enum
/// one-for-one so they round-trip into the generated shader verbatim.</summary>
public enum ShaderBlendFactor
{
    Zero, One,
    SrcColor, OneMinusSrcColor,
    DstColor, OneMinusDstColor,
    SrcAlpha, OneMinusSrcAlpha,
    DstAlpha, OneMinusDstAlpha,
    ConstantColor, OneMinusConstantColor,
    ConstantAlpha, OneMinusConstantAlpha,
    SrcAlphaSaturate,
    Src1Color, OneMinusSrc1Color,
    Src1Alpha, OneMinusSrc1Alpha,
}

/// <summary>Blend equation how the source and destination terms are combined.</summary>
public enum ShaderBlendOp { Add, Subtract, ReverseSubtract, Min, Max }

/// <summary>Cull face keyword. GLSL / Vulkan convention: Back = back-face culling on.</summary>
public enum ShaderCullMode { Back, Front, Off, FrontAndBack }

/// <summary>Front-face winding order. Opaque meshes almost always want CW CCW is for
/// inside-out meshes like skyboxes / inverted volumes.</summary>
public enum ShaderWinding { CW, CCW }

/// <summary>Depth test function. "LessEqual" is the default for opaque; "Always" for
/// overlays; "GreaterEqual" for reversed-Z geometry. "Off" disables depth testing
/// entirely (matches the parser's <c>ZTest Off</c>).</summary>
public enum ShaderZTest { Off, Never, Less, Equal, LessEqual, Greater, NotEqual, GreaterEqual, Always }

/// <summary>Render-queue bucket. Maps to the pass's <c>"RenderOrder"</c> tag
/// (Opaque -> "Opaque", Transparent -> "Transparent", etc.). Editor sidebar exposes these
/// as a dropdown so users don't have to remember the string constants.</summary>
public enum ShaderRenderQueue { Background, Opaque, AlphaTest, Transparent, Overlay }

/// <summary>
/// Fixed-function render state emitted by <c>ShaderGraphCompiler</c> into the Standard
/// pass block, plus lighting-interaction toggles consumed by the generated fragment
/// body. All fields are Echo-serialisable (public, not properties) so the asset
/// round-trips through .shadergraph save/load.
/// </summary>
public struct ShaderGraphRenderSettings
{
    // -- Geometry / depth -------------------------------------------------------------
    public ShaderCullMode     Cull;
    public ShaderWinding      Winding;
    public bool               ZWrite;
    public ShaderZTest        ZTest;

    // -- Blending / queue -------------------------------------------------------------
    public ShaderBlendMode    Blend;
    public ShaderBlendFactor  BlendSrc; // only read when Blend == Custom
    public ShaderBlendFactor  BlendDst; // only read when Blend == Custom
    public ShaderBlendOp      BlendOp;  // only read when Blend == Custom
    public ShaderRenderQueue  Queue;

    // -- Lighting interaction (only meaningful for lit modes) -------------------------

    /// <summary>When true, the fragment adds CalculateAmbient(...) to the lit colour.
    /// Unlit modes ignore this. Disabling gives pure-direct lighting (useful for
    /// stylized shaders that drive their own fill).</summary>
    public bool ReceivesAmbient;

    /// <summary>When true, directional lights multiply by the scene shadow term. Drives
    /// a <c>#define _SG_RECEIVE_SHADOWS</c> in the generated body that the lighting
    /// helper branches on off = unit multiplier, on = full PCF sample.</summary>
    public bool ReceivesShadows;

    /// <summary>When true, the Shadow pass is emitted so this surface casts shadows
    /// onto other geometry. Transparent surfaces typically set this to false to avoid
    /// an opaque silhouette falling across nearby meshes.</summary>
    public bool CastsShadows;

    /// <summary>Opaque PBR baseline: off-blend, back-cull, depth write on, LessEqual,
    /// Opaque queue, receives ambient + shadows, casts shadows. Matches Prowl's
    /// StandardSurface defaults so graph-driven shaders behave identically to authored
    /// ones out of the box.</summary>
    // Shared default for the new Blend Src/Dst/Op fields they only get used when
    // Blend == Custom, so the preset defaults can stay at the Alpha-blend pair.
    private const ShaderBlendFactor DefaultBlendSrc = ShaderBlendFactor.SrcAlpha;
    private const ShaderBlendFactor DefaultBlendDst = ShaderBlendFactor.OneMinusSrcAlpha;
    private const ShaderBlendOp     DefaultBlendOp  = ShaderBlendOp.Add;

    public static ShaderGraphRenderSettings OpaqueDefaults() => new()
    {
        Blend           = ShaderBlendMode.Opaque,
        BlendSrc        = DefaultBlendSrc,
        BlendDst        = DefaultBlendDst,
        BlendOp         = DefaultBlendOp,
        Cull            = ShaderCullMode.Back,
        Winding         = ShaderWinding.CW,
        ZWrite          = true,
        ZTest           = ShaderZTest.LessEqual,
        Queue           = ShaderRenderQueue.Opaque,
        ReceivesAmbient = true,
        ReceivesShadows = true,
        CastsShadows    = true,
    };

    public static ShaderGraphRenderSettings TransparentDefaults() => new()
    {
        Blend           = ShaderBlendMode.Alpha,
        BlendSrc        = DefaultBlendSrc,
        BlendDst        = DefaultBlendDst,
        BlendOp         = DefaultBlendOp,
        Cull            = ShaderCullMode.Back,
        Winding         = ShaderWinding.CW,
        ZWrite          = false,
        ZTest           = ShaderZTest.LessEqual,
        Queue           = ShaderRenderQueue.Transparent,
        ReceivesAmbient = true,
        ReceivesShadows = true,
        CastsShadows    = false,
    };

    public static ShaderGraphRenderSettings AdditiveDefaults() => new()
    {
        Blend           = ShaderBlendMode.Additive,
        BlendSrc        = DefaultBlendSrc,
        BlendDst        = DefaultBlendDst,
        BlendOp         = DefaultBlendOp,
        Cull            = ShaderCullMode.Off,
        Winding         = ShaderWinding.CW,
        ZWrite          = false,
        ZTest           = ShaderZTest.LessEqual,
        Queue           = ShaderRenderQueue.Transparent,
        ReceivesAmbient = false,
        ReceivesShadows = false,
        CastsShadows    = false,
    };

    public static ShaderGraphRenderSettings SkyDefaults() => new()
    {
        Blend           = ShaderBlendMode.Opaque,
        BlendSrc        = DefaultBlendSrc,
        BlendDst        = DefaultBlendDst,
        BlendOp         = DefaultBlendOp,
        Cull            = ShaderCullMode.Front,
        Winding         = ShaderWinding.CW,
        ZWrite          = false,
        ZTest           = ShaderZTest.LessEqual,
        Queue           = ShaderRenderQueue.Background,
        ReceivesAmbient = false,
        ReceivesShadows = false,
        CastsShadows    = false,
    };

    public static ShaderGraphRenderSettings PostEffectDefaults() => new()
    {
        Blend           = ShaderBlendMode.Opaque,
        BlendSrc        = DefaultBlendSrc,
        BlendDst        = DefaultBlendDst,
        BlendOp         = DefaultBlendOp,
        Cull            = ShaderCullMode.Off,
        Winding         = ShaderWinding.CW,
        ZWrite          = false,
        ZTest           = ShaderZTest.Always,
        Queue           = ShaderRenderQueue.Overlay,
        ReceivesAmbient = false,
        ReceivesShadows = false,
        CastsShadows    = false,
    };
}
