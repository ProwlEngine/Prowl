namespace Prowl.Graphite.Debugging;

/// <summary>A byte range for buffers, or a mip and layer range for textures. The default value is an empty buffer range.</summary>
public readonly record struct ResourceRange(
    uint Offset,
    uint Size,
    uint BaseMipLevel,
    uint MipLevels,
    uint BaseArrayLayer,
    uint ArrayLayers)
{
    public static ResourceRange Bytes(uint offset, uint size) => new(offset, size, 0, 0, 0, 0);

    public static ResourceRange Subresources(uint baseMipLevel, uint mipLevels, uint baseArrayLayer, uint arrayLayers)
        => new(0, 0, baseMipLevel, mipLevels, baseArrayLayer, arrayLayers);

    /// <summary>True if the range addresses texture subresources rather than bytes.</summary>
    public bool IsTexture => MipLevels != 0 || ArrayLayers != 0;

    public bool IsEmpty => IsTexture ? MipLevels == 0 || ArrayLayers == 0 : Size == 0;
}
