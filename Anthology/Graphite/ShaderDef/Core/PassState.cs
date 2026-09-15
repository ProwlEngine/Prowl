using System;

namespace Prowl.Graphite.ShaderDef;


/// <summary>
/// All render state options: rasterizer, blend, depth, stencil, multisampling, write masks.
/// </summary>
public class PassState : IEquatable<PassState>
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
    /// Merges blend fields into a single-attachment blend state, overwriting the base where set.
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
    /// Merges depth and stencil fields into a state, overwriting the base where set.
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
    /// Merges rasterizer fields into a state, overwriting the base where set.
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
    /// Merges this and other into a new PassState. This wins where set, other fills the rest.
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


    /// <summary>
    /// Field-wise equality, unset fields compare equal to each other.
    /// </summary>
    public bool Equals(PassState? other)
    {
        if (other is null)
            return false;
        if (ReferenceEquals(this, other))
            return true;

        return EnableCulling == other.EnableCulling
            && CullMode == other.CullMode
            && FrontFace == other.FrontFace
            && EnablePolygonOffsetFill == other.EnablePolygonOffsetFill
            && PolygonOffsetFactor == other.PolygonOffsetFactor
            && PolygonOffsetUnits == other.PolygonOffsetUnits
            && EnableDepthTest == other.EnableDepthTest
            && DepthFunc == other.DepthFunc
            && DepthWriteMask == other.DepthWriteMask
            && EnableDepthClamp == other.EnableDepthClamp
            && EnableStencilTest == other.EnableStencilTest
            && StencilRef == other.StencilRef
            && StencilReadMask == other.StencilReadMask
            && StencilWriteMask == other.StencilWriteMask
            && StencilFrontFunc == other.StencilFrontFunc
            && StencilFrontFailOp == other.StencilFrontFailOp
            && StencilFrontDepthFailOp == other.StencilFrontDepthFailOp
            && StencilFrontPassOp == other.StencilFrontPassOp
            && StencilBackFunc == other.StencilBackFunc
            && StencilBackFailOp == other.StencilBackFailOp
            && StencilBackDepthFailOp == other.StencilBackDepthFailOp
            && StencilBackPassOp == other.StencilBackPassOp
            && EnableBlend == other.EnableBlend
            && BlendFunctionRgb == other.BlendFunctionRgb
            && BlendFunctionAlpha == other.BlendFunctionAlpha
            && BlendSrcRgb == other.BlendSrcRgb
            && BlendDstRgb == other.BlendDstRgb
            && BlendSrcAlpha == other.BlendSrcAlpha
            && BlendDstAlpha == other.BlendDstAlpha
            && AlphaToMask == other.AlphaToMask
            && WriteMask == other.WriteMask;
    }


    /// <inheritdoc/>
    public override bool Equals(object? obj) => obj is PassState other && Equals(other);


    /// <inheritdoc/>
    public override int GetHashCode()
    {
        HashCode hash = new();
        hash.Add(EnableCulling);
        hash.Add(CullMode);
        hash.Add(FrontFace);
        hash.Add(EnablePolygonOffsetFill);
        hash.Add(PolygonOffsetFactor);
        hash.Add(PolygonOffsetUnits);
        hash.Add(EnableDepthTest);
        hash.Add(DepthFunc);
        hash.Add(DepthWriteMask);
        hash.Add(EnableDepthClamp);
        hash.Add(EnableStencilTest);
        hash.Add(StencilRef);
        hash.Add(StencilReadMask);
        hash.Add(StencilWriteMask);
        hash.Add(StencilFrontFunc);
        hash.Add(StencilFrontFailOp);
        hash.Add(StencilFrontDepthFailOp);
        hash.Add(StencilFrontPassOp);
        hash.Add(StencilBackFunc);
        hash.Add(StencilBackFailOp);
        hash.Add(StencilBackDepthFailOp);
        hash.Add(StencilBackPassOp);
        hash.Add(EnableBlend);
        hash.Add(BlendFunctionRgb);
        hash.Add(BlendFunctionAlpha);
        hash.Add(BlendSrcRgb);
        hash.Add(BlendDstRgb);
        hash.Add(BlendSrcAlpha);
        hash.Add(BlendDstAlpha);
        hash.Add(AlphaToMask);
        hash.Add(WriteMask);
        return hash.ToHashCode();
    }
}
