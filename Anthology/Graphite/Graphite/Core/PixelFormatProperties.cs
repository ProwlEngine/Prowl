namespace Prowl.Graphite;

/// <summary>
/// Supported properties for a pixel format + texture type + usage combo on a device.
/// </summary>
public readonly struct PixelFormatProperties
{
    /// <summary>
    /// Max width.
    /// </summary>
    public readonly uint MaxWidth;
    /// <summary>
    /// Max height.
    /// </summary>
    public readonly uint MaxHeight;
    /// <summary>
    /// Max depth.
    /// </summary>
    public readonly uint MaxDepth;
    /// <summary>
    /// Max mip levels.
    /// </summary>
    public readonly uint MaxMipLevels;
    /// <summary>
    /// Max array layers.
    /// </summary>
    public readonly uint MaxArrayLayers;

    private readonly uint _sampleCounts;

    /// <summary>
    /// Whether the sample count is supported.
    /// </summary>
    /// <param name="count">Sample count to check.</param>
    /// <returns>True if supported.</returns>
    public readonly bool IsSampleCountSupported(TextureSampleCount count)
    {
        int bit = (int)count;
        return (_sampleCounts & (1 << bit)) != 0;
    }

    internal PixelFormatProperties(
        uint maxWidth,
        uint maxHeight,
        uint maxDepth,
        uint maxMipLevels,
        uint maxArrayLayers,
        uint sampleCounts)
    {
        MaxWidth = maxWidth;
        MaxHeight = maxHeight;
        MaxDepth = maxDepth;
        MaxMipLevels = maxMipLevels;
        MaxArrayLayers = maxArrayLayers;
        _sampleCounts = sampleCounts;
    }
}
