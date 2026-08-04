using System;

namespace Prowl.Graphite;

/// <summary>
/// Sampler creation params.
/// </summary>
public struct SamplerDescription : IEquatable<SamplerDescription>
{
    /// <summary>
    /// U address mode.
    /// </summary>
    public SamplerAddressMode AddressModeU;
    /// <summary>
    /// V address mode.
    /// </summary>
    public SamplerAddressMode AddressModeV;
    /// <summary>
    /// W address mode.
    /// </summary>
    public SamplerAddressMode AddressModeW;
    /// <summary>
    /// Sample filter.
    /// </summary>
    public SamplerFilter Filter;
    /// <summary>
    /// Comparison kind. Null = off.
    /// </summary>
    public ComparisonKind? ComparisonKind;
    /// <summary>
    /// Max anisotropy. Ignored unless anisotropic filtering.
    /// </summary>
    public uint MaximumAnisotropy;
    /// <summary>
    /// Min LOD.
    /// </summary>
    public uint MinimumLod;
    /// <summary>
    /// Max LOD.
    /// </summary>
    public uint MaximumLod;
    /// <summary>
    /// LOD bias.
    /// </summary>
    public int LodBias;
    /// <summary>
    /// Border color, Border mode only.
    /// </summary>
    public SamplerBorderColor BorderColor;

    /// <summary>
    /// New sampler description.
    /// </summary>
    /// <param name="addressModeU">U address mode.</param>
    /// <param name="addressModeV">V address mode.</param>
    /// <param name="addressModeW">W address mode.</param>
    /// <param name="filter">Sample filter.</param>
    /// <param name="comparisonKind">Comparison kind. Null = off.</param>
    /// <param name="maximumAnisotropy">Max anisotropy.</param>
    /// <param name="minimumLod">Min LOD.</param>
    /// <param name="maximumLod">Max LOD.</param>
    /// <param name="lodBias">LOD bias.</param>
    /// <param name="borderColor">Border color, Border mode only.</param>
    public SamplerDescription(
        SamplerAddressMode addressModeU,
        SamplerAddressMode addressModeV,
        SamplerAddressMode addressModeW,
        SamplerFilter filter,
        ComparisonKind? comparisonKind,
        uint maximumAnisotropy,
        uint minimumLod,
        uint maximumLod,
        int lodBias,
        SamplerBorderColor borderColor)
    {
        AddressModeU = addressModeU;
        AddressModeV = addressModeV;
        AddressModeW = addressModeW;
        Filter = filter;
        ComparisonKind = comparisonKind;
        MaximumAnisotropy = maximumAnisotropy;
        MinimumLod = minimumLod;
        MaximumLod = maximumLod;
        LodBias = lodBias;
        BorderColor = borderColor;
    }

    /// <summary>
    /// Point-filter wrapping sampler.
    /// Settings:
    ///     AddressModeU = SamplerAddressMode.Wrap
    ///     AddressModeV = SamplerAddressMode.Wrap
    ///     AddressModeW = SamplerAddressMode.Wrap
    ///     Filter = SamplerFilter.MinPoint_MagPoint_MipPoint
    ///     LodBias = 0
    ///     MinimumLod = 0
    ///     MaximumLod = uint.MaxValue
    ///     MaximumAnisotropy = 0
    /// </summary>
    public static readonly SamplerDescription Point = new()
    {
        AddressModeU = SamplerAddressMode.Wrap,
        AddressModeV = SamplerAddressMode.Wrap,
        AddressModeW = SamplerAddressMode.Wrap,
        Filter = SamplerFilter.MinPoint_MagPoint_MipPoint,
        LodBias = 0,
        MinimumLod = 0,
        MaximumLod = uint.MaxValue,
        MaximumAnisotropy = 0,
    };

    /// <summary>
    /// Linear-filter wrapping sampler.
    /// Settings:
    ///     AddressModeU = SamplerAddressMode.Wrap
    ///     AddressModeV = SamplerAddressMode.Wrap
    ///     AddressModeW = SamplerAddressMode.Wrap
    ///     Filter = SamplerFilter.MinLinear_MagLinear_MipLinear
    ///     LodBias = 0
    ///     MinimumLod = 0
    ///     MaximumLod = uint.MaxValue
    ///     MaximumAnisotropy = 0
    /// </summary>
    public static readonly SamplerDescription Linear = new()
    {
        AddressModeU = SamplerAddressMode.Wrap,
        AddressModeV = SamplerAddressMode.Wrap,
        AddressModeW = SamplerAddressMode.Wrap,
        Filter = SamplerFilter.MinLinear_MagLinear_MipLinear,
        LodBias = 0,
        MinimumLod = 0,
        MaximumLod = uint.MaxValue,
        MaximumAnisotropy = 0,
    };

    /// <summary>
    /// 4x-anisotropic wrapping sampler.
    /// Settings:
    ///     AddressModeU = SamplerAddressMode.Wrap
    ///     AddressModeV = SamplerAddressMode.Wrap
    ///     AddressModeW = SamplerAddressMode.Wrap
    ///     Filter = SamplerFilter.Anisotropic
    ///     LodBias = 0
    ///     MinimumLod = 0
    ///     MaximumLod = uint.MaxValue
    ///     MaximumAnisotropy = 4
    /// </summary>
    public static readonly SamplerDescription Aniso4x = new()
    {
        AddressModeU = SamplerAddressMode.Wrap,
        AddressModeV = SamplerAddressMode.Wrap,
        AddressModeW = SamplerAddressMode.Wrap,
        Filter = SamplerFilter.Anisotropic,
        LodBias = 0,
        MinimumLod = 0,
        MaximumLod = uint.MaxValue,
        MaximumAnisotropy = 4,
    };

    /// <summary>
    /// Field-by-field equality.
    /// </summary>
    /// <param name="other">Other instance.</param>
    /// <returns>True if all fields match.</returns>
    public readonly bool Equals(SamplerDescription other)
    {
        return AddressModeU == other.AddressModeU
            && AddressModeV == other.AddressModeV
            && AddressModeW == other.AddressModeW
            && Filter == other.Filter
            && ComparisonKind.GetValueOrDefault() == other.ComparisonKind.GetValueOrDefault()
            && MaximumAnisotropy == other.MaximumAnisotropy
            && MinimumLod == other.MinimumLod
            && MaximumLod == other.MaximumLod
            && LodBias == other.LodBias
            && BorderColor == other.BorderColor;
    }

    /// <summary>
    /// Hash code.
    /// </summary>
    /// <returns>Hash code.</returns>
    public override int GetHashCode()
    {
        return HashCode.Combine(
            HashCode.Combine(
                (int)AddressModeU,
                (int)AddressModeV,
                (int)AddressModeW,
                (int)Filter,
                ComparisonKind.GetHashCode(),
                MaximumAnisotropy.GetHashCode(),
                MinimumLod.GetHashCode(),
                MaximumLod.GetHashCode()),
            LodBias.GetHashCode(),
            (int)BorderColor);
    }
}
