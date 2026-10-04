using System;

namespace Prowl.Graphite.ShaderDef;


/// <summary>
/// One bit per pipeline field a PassState can override. Unset bits defer to the base state.
/// </summary>
[Flags]
public enum PassStateFields : uint
{
#pragma warning disable CS1591
    None = 0,

    CullMode = 1u << 0,
    FrontFace = 1u << 1,
    DepthClip = 1u << 2,
    DepthBias = 1u << 3,
    DepthBiasSlope = 1u << 4,
    DepthBiasConstant = 1u << 5,

    DepthTest = 1u << 6,
    DepthWrite = 1u << 7,
    DepthComparison = 1u << 8,

    StencilTest = 1u << 9,
    StencilReference = 1u << 10,
    StencilReadMask = 1u << 11,
    StencilWriteMask = 1u << 12,

    StencilFrontFail = 1u << 13,
    StencilFrontPass = 1u << 14,
    StencilFrontDepthFail = 1u << 15,
    StencilFrontComparison = 1u << 16,

    StencilBackFail = 1u << 17,
    StencilBackPass = 1u << 18,
    StencilBackDepthFail = 1u << 19,
    StencilBackComparison = 1u << 20,

    BlendEnabled = 1u << 21,
    ColorWriteMask = 1u << 22,
    SourceColorFactor = 1u << 23,
    DestinationColorFactor = 1u << 24,
    ColorFunction = 1u << 25,
    SourceAlphaFactor = 1u << 26,
    DestinationAlphaFactor = 1u << 27,
    AlphaFunction = 1u << 28,

    AlphaToCoverage = 1u << 29,
#pragma warning restore CS1591
}


/// <summary>
/// Pipeline overrides held as Core descriptions plus a mask of the fields that were set.
/// </summary>
public sealed class PassState : IEquatable<PassState>
{
    /// <summary>
    /// Fields that carry a value. Description fields outside this mask are ignored.
    /// </summary>
    public PassStateFields Set;

    /// <summary>
    /// Rasterizer values for the set fields.
    /// </summary>
    public RasterizerStateDescription Raster;

    /// <summary>
    /// Depth and stencil values for the set fields.
    /// </summary>
    public DepthStencilStateDescription DepthStencil;

    /// <summary>
    /// Attachment 0 blend values for the set fields.
    /// </summary>
    public BlendAttachmentDescription Blend;

    /// <summary>
    /// Alpha to coverage value when AlphaToCoverage is set.
    /// </summary>
    public bool AlphaToCoverage;


    /// <summary>
    /// Writes the set blend fields over attachment 0 of the base state.
    /// </summary>
    public BlendStateDescription ToBlendState(BlendStateDescription baseState)
    {
        BlendAttachmentDescription attachment = baseState.AttachmentStates.Length > 0
            ? baseState.AttachmentStates[0]
            : BlendAttachmentDescription.Disabled;

        CopyBlend(ref attachment, Blend, Set);

        if (Has(Set, PassStateFields.AlphaToCoverage))
            baseState.AlphaToCoverageEnabled = AlphaToCoverage;

        baseState.AttachmentStates = [attachment];
        return baseState;
    }


    /// <summary>
    /// Writes the set depth and stencil fields over the base state.
    /// </summary>
    public DepthStencilStateDescription ToDepthStencilState(DepthStencilStateDescription baseState)
    {
        CopyDepthStencil(ref baseState, DepthStencil, Set);
        return baseState;
    }


    /// <summary>
    /// Writes the set rasterizer fields over the base state.
    /// </summary>
    public RasterizerStateDescription ToRasterizerState(RasterizerStateDescription baseState)
    {
        CopyRaster(ref baseState, Raster, Set);
        return baseState;
    }


    /// <summary>
    /// Merges this and other into a new PassState. This wins where set, other fills the rest.
    /// </summary>
    public PassState Apply(PassState other)
    {
        PassState result = new() { Set = Set | other.Set };
        result.CopyFrom(other, other.Set);
        result.CopyFrom(this, Set);
        return result;
    }


    /// <summary>
    /// Field-wise equality over the set fields only.
    /// </summary>
    public bool Equals(PassState? other)
    {
        if (other is null)
            return false;
        if (ReferenceEquals(this, other))
            return true;

        return Set == other.Set && Masked() == other.Masked();
    }


    /// <inheritdoc/>
    public override bool Equals(object? obj) => obj is PassState other && Equals(other);


    /// <inheritdoc/>
    public override int GetHashCode()
    {
        (RasterizerStateDescription raster, DepthStencilStateDescription depthStencil, BlendAttachmentDescription blend, bool alphaToCoverage) = Masked();
        return HashCode.Combine(Set, raster, depthStencil, blend, alphaToCoverage);
    }


    internal PassState Clone() => Apply(new PassState());


    private void CopyFrom(PassState source, PassStateFields set)
    {
        CopyRaster(ref Raster, source.Raster, set);
        CopyDepthStencil(ref DepthStencil, source.DepthStencil, set);
        CopyBlend(ref Blend, source.Blend, set);

        if (Has(set, PassStateFields.AlphaToCoverage))
            AlphaToCoverage = source.AlphaToCoverage;
    }


    private (RasterizerStateDescription, DepthStencilStateDescription, BlendAttachmentDescription, bool) Masked()
    {
        RasterizerStateDescription raster = default;
        DepthStencilStateDescription depthStencil = default;
        BlendAttachmentDescription blend = default;

        CopyRaster(ref raster, Raster, Set);
        CopyDepthStencil(ref depthStencil, DepthStencil, Set);
        CopyBlend(ref blend, Blend, Set);

        return (raster, depthStencil, blend, Has(Set, PassStateFields.AlphaToCoverage) && AlphaToCoverage);
    }


    private static bool Has(PassStateFields set, PassStateFields flag) => (set & flag) != 0;


    private static void CopyRaster(ref RasterizerStateDescription destination, in RasterizerStateDescription source, PassStateFields set)
    {
        if (Has(set, PassStateFields.CullMode)) destination.CullMode = source.CullMode;
        if (Has(set, PassStateFields.FrontFace)) destination.FrontFace = source.FrontFace;
        if (Has(set, PassStateFields.DepthClip)) destination.DepthClipEnabled = source.DepthClipEnabled;
        if (Has(set, PassStateFields.DepthBias)) destination.DepthBiasEnabled = source.DepthBiasEnabled;
        if (Has(set, PassStateFields.DepthBiasSlope)) destination.DepthBiasSlopeFactor = source.DepthBiasSlopeFactor;
        if (Has(set, PassStateFields.DepthBiasConstant)) destination.DepthBiasConstantFactor = source.DepthBiasConstantFactor;
    }


    private static void CopyDepthStencil(ref DepthStencilStateDescription destination, in DepthStencilStateDescription source, PassStateFields set)
    {
        if (Has(set, PassStateFields.DepthTest)) destination.DepthTestEnabled = source.DepthTestEnabled;
        if (Has(set, PassStateFields.DepthWrite)) destination.DepthWriteEnabled = source.DepthWriteEnabled;
        if (Has(set, PassStateFields.DepthComparison)) destination.DepthComparison = source.DepthComparison;

        if (Has(set, PassStateFields.StencilTest)) destination.StencilTestEnabled = source.StencilTestEnabled;
        if (Has(set, PassStateFields.StencilReference)) destination.StencilReference = source.StencilReference;
        if (Has(set, PassStateFields.StencilReadMask)) destination.StencilReadMask = source.StencilReadMask;
        if (Has(set, PassStateFields.StencilWriteMask)) destination.StencilWriteMask = source.StencilWriteMask;

        if (Has(set, PassStateFields.StencilFrontFail)) destination.StencilFront.Fail = source.StencilFront.Fail;
        if (Has(set, PassStateFields.StencilFrontPass)) destination.StencilFront.Pass = source.StencilFront.Pass;
        if (Has(set, PassStateFields.StencilFrontDepthFail)) destination.StencilFront.DepthFail = source.StencilFront.DepthFail;
        if (Has(set, PassStateFields.StencilFrontComparison)) destination.StencilFront.Comparison = source.StencilFront.Comparison;

        if (Has(set, PassStateFields.StencilBackFail)) destination.StencilBack.Fail = source.StencilBack.Fail;
        if (Has(set, PassStateFields.StencilBackPass)) destination.StencilBack.Pass = source.StencilBack.Pass;
        if (Has(set, PassStateFields.StencilBackDepthFail)) destination.StencilBack.DepthFail = source.StencilBack.DepthFail;
        if (Has(set, PassStateFields.StencilBackComparison)) destination.StencilBack.Comparison = source.StencilBack.Comparison;
    }


    private static void CopyBlend(ref BlendAttachmentDescription destination, in BlendAttachmentDescription source, PassStateFields set)
    {
        if (Has(set, PassStateFields.BlendEnabled)) destination.BlendEnabled = source.BlendEnabled;
        if (Has(set, PassStateFields.ColorWriteMask)) destination.ColorWriteMask = source.ColorWriteMask;
        if (Has(set, PassStateFields.SourceColorFactor)) destination.SourceColorFactor = source.SourceColorFactor;
        if (Has(set, PassStateFields.DestinationColorFactor)) destination.DestinationColorFactor = source.DestinationColorFactor;
        if (Has(set, PassStateFields.ColorFunction)) destination.ColorFunction = source.ColorFunction;
        if (Has(set, PassStateFields.SourceAlphaFactor)) destination.SourceAlphaFactor = source.SourceAlphaFactor;
        if (Has(set, PassStateFields.DestinationAlphaFactor)) destination.DestinationAlphaFactor = source.DestinationAlphaFactor;
        if (Has(set, PassStateFields.AlphaFunction)) destination.AlphaFunction = source.AlphaFunction;
    }
}
