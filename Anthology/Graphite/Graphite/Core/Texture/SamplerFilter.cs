namespace Prowl.Graphite;

/// <summary>
/// How texture values get sampled.
/// </summary>
public enum SamplerFilter : byte
{
    /// <summary>
    /// Point sampling everywhere: min, mag, mip.
    /// </summary>
    MinPoint_MagPoint_MipPoint,
    /// <summary>
    /// Point min/mag, linear mip.
    /// </summary>
    MinPoint_MagPoint_MipLinear,
    /// <summary>
    /// Point min/mip, linear mag.
    /// </summary>
    MinPoint_MagLinear_MipPoint,
    /// <summary>
    /// Point min, linear mag/mip.
    /// </summary>
    MinPoint_MagLinear_MipLinear,
    /// <summary>
    /// Linear min, point mag/mip.
    /// </summary>
    MinLinear_MagPoint_MipPoint,
    /// <summary>
    /// Linear min/mip, point mag.
    /// </summary>
    MinLinear_MagPoint_MipLinear,
    /// <summary>
    /// Linear min/mag, point mip.
    /// </summary>
    MinLinear_MagLinear_MipPoint,
    /// <summary>
    /// Linear everywhere: min, mag, mip.
    /// </summary>
    MinLinear_MagLinear_MipLinear,
    /// <summary>
    /// Anisotropic filtering. Max anisotropy set via SamplerDescription.MaximumAnisotropy.
    /// </summary>
    Anisotropic,
}
