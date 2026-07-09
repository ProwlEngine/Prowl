namespace Prowl.Graphite.ShaderDef;


/// <summary>
/// A defined set of all possible render state options, encapsulating rasterizer settings, blend, depth, stencil, multisampling, and write masks.
/// </summary>
public class PassState
{
#pragma warning disable CS1591
    public bool? EnableCulling;
    public FaceCullMode? CullMode;
    public FrontFace? FrontFace;

    public bool? EnablePolygonOffsetFill;
    public float? PolygonOffsetFactor;
    public float? PolygonOffsetUnits;

    // -------------------- Depth --------------------

    public bool? EnableDepthTest;
    public ComparisonKind? DepthFunc;
    public bool? DepthWriteMask;
    public bool? EnableDepthClamp;

    // -------------------- Stencil --------------------

    public bool? EnableStencilTest;
    public int? StencilRef;
    public uint? StencilReadMask;
    public uint? StencilWriteMask;

    public ComparisonKind? StencilFrontFunc;
    public StencilOperation? StencilFrontFailOp;
    public StencilOperation? StencilFrontDepthFailOp;
    public StencilOperation? StencilFrontPassOp;

    public ComparisonKind? StencilBackFunc;
    public StencilOperation? StencilBackFailOp;
    public StencilOperation? StencilBackDepthFailOp;
    public StencilOperation? StencilBackPassOp;

    // -------------------- Blending (equation / factors) --------------------
    public bool? EnableBlend;
    public BlendFunction? BlendFunctionRgb;
    public BlendFunction? BlendFunctionAlpha;
    public BlendFactor? BlendSrcRgb;
    public BlendFactor? BlendDstRgb;
    public BlendFactor? BlendSrcAlpha;
    public BlendFactor? BlendDstAlpha;

    // -------------------- Multisampling --------------------

    public bool? AlphaToMask;

    // -------------------- Color Write Mask --------------------
    public ColorWriteMask? WriteMask;
#pragma warning restore CS1591


    /// <summary>
    /// Collapses the blend-related fields into a single-attachment <see cref="BlendStateDescription"/>, overlaying any
    /// explicitly parsed values onto the given base description.
    /// </summary>
    public BlendStateDescription ToBlendState(BlendStateDescription baseState)
    {
        BlendAttachmentDescription attachment = baseState.AttachmentStates.Length > 0
            ? baseState.AttachmentStates[0]
            : BlendAttachmentDescription.Disabled;

        attachment.BlendEnabled = EnableBlend ?? attachment.BlendEnabled;
        attachment.ColorWriteMask = WriteMask ?? attachment.ColorWriteMask;
        attachment.SourceColorFactor = BlendSrcRgb ?? attachment.SourceColorFactor;
        attachment.DestinationColorFactor = BlendDstRgb ?? attachment.DestinationColorFactor;
        attachment.ColorFunction = BlendFunctionRgb ?? attachment.ColorFunction;
        attachment.SourceAlphaFactor = BlendSrcAlpha ?? attachment.SourceAlphaFactor;
        attachment.DestinationAlphaFactor = BlendDstAlpha ?? attachment.DestinationAlphaFactor;
        attachment.AlphaFunction = BlendFunctionAlpha ?? attachment.AlphaFunction;

        baseState.AlphaToCoverageEnabled = AlphaToMask ?? baseState.AlphaToCoverageEnabled;
        baseState.AttachmentStates = [attachment];
        return baseState;
    }


    /// <summary>
    /// Collapses the depth and stencil fields into a <see cref="DepthStencilStateDescription"/>, overlaying any explicitly
    /// parsed values onto the given base description.
    /// </summary>
    public DepthStencilStateDescription ToDepthStencilState(DepthStencilStateDescription baseState)
    {
        baseState.DepthTestEnabled = EnableDepthTest ?? baseState.DepthTestEnabled;
        baseState.DepthWriteEnabled = DepthWriteMask ?? baseState.DepthWriteEnabled;
        baseState.DepthComparison = DepthFunc ?? baseState.DepthComparison;
        baseState.StencilTestEnabled = EnableStencilTest ?? baseState.StencilTestEnabled;

        baseState.StencilFront = new StencilBehaviorDescription
        {
            Fail = StencilFrontFailOp ?? baseState.StencilFront.Fail,
            Pass = StencilFrontPassOp ?? baseState.StencilFront.Pass,
            DepthFail = StencilFrontDepthFailOp ?? baseState.StencilFront.DepthFail,
            Comparison = StencilFrontFunc ?? baseState.StencilFront.Comparison,
        };
        baseState.StencilBack = new StencilBehaviorDescription
        {
            Fail = StencilBackFailOp ?? baseState.StencilBack.Fail,
            Pass = StencilBackPassOp ?? baseState.StencilBack.Pass,
            DepthFail = StencilBackDepthFailOp ?? baseState.StencilBack.DepthFail,
            Comparison = StencilBackFunc ?? baseState.StencilBack.Comparison,
        };

        baseState.StencilReadMask = (byte)(StencilReadMask ?? baseState.StencilReadMask);
        baseState.StencilWriteMask = (byte)(StencilWriteMask ?? baseState.StencilWriteMask);
        baseState.StencilReference = (uint)(StencilRef ?? (int)baseState.StencilReference);
        return baseState;
    }


    /// <summary>
    /// Collapses the rasterizer fields into a <see cref="RasterizerStateDescription"/>, overlaying any explicitly parsed
    /// values onto the given base description.
    /// </summary>
    public RasterizerStateDescription ToRasterizerState(RasterizerStateDescription baseState)
    {
        baseState.CullMode = CullMode ?? baseState.CullMode;
        baseState.FrontFace = FrontFace ?? baseState.FrontFace;

        if (EnableDepthClamp.HasValue)
            baseState.DepthClipEnabled = !EnableDepthClamp.Value;

        return baseState;
    }


    /// <summary>
    /// Merges two <see cref="PassState"/> objects into a single merged <see cref="PassState"/> with values being overwritten on <paramref name="other"/>
    /// </summary>
    public PassState Apply(PassState other)
    {
        return new()
        {
            EnableCulling = EnableCulling ?? other.EnableCulling,
            CullMode = CullMode ?? other.CullMode,
            FrontFace = FrontFace ?? other.FrontFace,
            EnablePolygonOffsetFill = EnablePolygonOffsetFill ?? other.EnablePolygonOffsetFill,
            PolygonOffsetFactor = PolygonOffsetFactor ?? other.PolygonOffsetFactor,
            PolygonOffsetUnits = PolygonOffsetUnits ?? other.PolygonOffsetUnits,
            EnableDepthTest = EnableDepthTest ?? other.EnableDepthTest,
            DepthFunc = DepthFunc ?? other.DepthFunc,
            DepthWriteMask = DepthWriteMask ?? other.DepthWriteMask,
            EnableDepthClamp = EnableDepthClamp ?? other.EnableDepthClamp,
            EnableStencilTest = EnableStencilTest ?? other.EnableStencilTest,
            StencilRef = StencilRef ?? other.StencilRef,
            StencilReadMask = StencilReadMask ?? other.StencilReadMask,
            StencilWriteMask = StencilWriteMask ?? other.StencilWriteMask,
            StencilFrontFunc = StencilFrontFunc ?? other.StencilFrontFunc,
            StencilFrontFailOp = StencilFrontFailOp ?? other.StencilFrontFailOp,
            StencilFrontDepthFailOp = StencilFrontDepthFailOp ?? other.StencilFrontDepthFailOp,
            StencilFrontPassOp = StencilFrontPassOp ?? other.StencilFrontPassOp,
            StencilBackFunc = StencilBackFunc ?? other.StencilBackFunc,
            StencilBackFailOp = StencilBackFailOp ?? other.StencilBackFailOp,
            StencilBackDepthFailOp = StencilBackDepthFailOp ?? other.StencilBackDepthFailOp,
            StencilBackPassOp = StencilBackPassOp ?? other.StencilBackPassOp,
            EnableBlend = EnableBlend ?? other.EnableBlend,
            BlendFunctionRgb = BlendFunctionRgb ?? other.BlendFunctionRgb,
            BlendFunctionAlpha = BlendFunctionAlpha ?? other.BlendFunctionAlpha,
            BlendSrcRgb = BlendSrcRgb ?? other.BlendSrcRgb,
            BlendDstRgb = BlendDstRgb ?? other.BlendDstRgb,
            BlendSrcAlpha = BlendSrcAlpha ?? other.BlendSrcAlpha,
            BlendDstAlpha = BlendDstAlpha ?? other.BlendDstAlpha,
            AlphaToMask = AlphaToMask ?? other.AlphaToMask,
            WriteMask = WriteMask ?? other.WriteMask,
        };
    }
}
