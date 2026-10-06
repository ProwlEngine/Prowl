using System;

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

    /// <summary>True if both ranges have the same form and share at least one byte or subresource.</summary>
    public bool Overlaps(in ResourceRange other)
    {
        if (IsEmpty || other.IsEmpty || IsTexture != other.IsTexture)
            return false;

        if (!IsTexture)
            return Axis.Overlaps(Offset, Size, other.Offset, other.Size);

        return Axis.Overlaps(BaseMipLevel, MipLevels, other.BaseMipLevel, other.MipLevels)
            && Axis.Overlaps(BaseArrayLayer, ArrayLayers, other.BaseArrayLayer, other.ArrayLayers);
    }

    /// <summary>True if both ranges have the same form and this one covers all of the other. An empty other is always covered.</summary>
    public bool Contains(in ResourceRange other)
    {
        if (IsTexture != other.IsTexture && !other.IsEmpty)
            return false;

        if (other.IsEmpty)
            return IsTexture == other.IsTexture || other == default;

        if (IsEmpty)
            return false;

        if (!IsTexture)
            return Axis.Contains(Offset, Size, other.Offset, other.Size);

        return Axis.Contains(BaseMipLevel, MipLevels, other.BaseMipLevel, other.MipLevels)
            && Axis.Contains(BaseArrayLayer, ArrayLayers, other.BaseArrayLayer, other.ArrayLayers);
    }

    /// <summary>The shared part of two ranges, or the default empty range when they do not overlap.</summary>
    public ResourceRange Intersect(in ResourceRange other)
    {
        if (!Overlaps(other))
            return default;

        if (!IsTexture)
        {
            Axis.Intersect(Offset, Size, other.Offset, other.Size, out uint offset, out uint size);
            return Bytes(offset, size);
        }

        Axis.Intersect(BaseMipLevel, MipLevels, other.BaseMipLevel, other.MipLevels, out uint mip, out uint mips);
        Axis.Intersect(BaseArrayLayer, ArrayLayers, other.BaseArrayLayer, other.ArrayLayers, out uint layer, out uint layers);
        return Subresources(mip, mips, layer, layers);
    }

    /// <summary>The smallest range covering both. May cover more than the two inputs when they do not touch. An empty input is ignored.</summary>
    /// <exception cref="ArgumentException">Thrown when two non-empty ranges have different forms.</exception>
    public ResourceRange Union(in ResourceRange other)
    {
        if (IsEmpty)
            return other;

        if (other.IsEmpty)
            return this;

        if (IsTexture != other.IsTexture)
            throw new ArgumentException("Cannot union a byte range with a subresource range.", nameof(other));

        if (!IsTexture)
        {
            Axis.Bound(Offset, Size, other.Offset, other.Size, out uint offset, out uint size);
            return Bytes(offset, size);
        }

        Axis.Bound(BaseMipLevel, MipLevels, other.BaseMipLevel, other.MipLevels, out uint mip, out uint mips);
        Axis.Bound(BaseArrayLayer, ArrayLayers, other.BaseArrayLayer, other.ArrayLayers, out uint layer, out uint layers);
        return Subresources(mip, mips, layer, layers);
    }

    private static class Axis
    {
        public static bool Overlaps(uint start, uint count, uint otherStart, uint otherCount)
            => start < (ulong)otherStart + otherCount && otherStart < (ulong)start + count;

        public static bool Contains(uint start, uint count, uint otherStart, uint otherCount)
            => start <= otherStart && (ulong)otherStart + otherCount <= (ulong)start + count;

        public static void Intersect(uint start, uint count, uint otherStart, uint otherCount, out uint resultStart, out uint resultCount)
        {
            resultStart = Math.Max(start, otherStart);
            ulong end = Math.Min((ulong)start + count, (ulong)otherStart + otherCount);
            resultCount = (uint)(end - resultStart);
        }

        public static void Bound(uint start, uint count, uint otherStart, uint otherCount, out uint resultStart, out uint resultCount)
        {
            resultStart = Math.Min(start, otherStart);
            ulong end = Math.Max((ulong)start + count, (ulong)otherStart + otherCount);
            resultCount = (uint)Math.Min(end - resultStart, uint.MaxValue);
        }
    }
}
