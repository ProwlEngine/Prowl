using System;
using Prowl.Graphite.Debugging;

namespace Prowl.Graphite.Debugger.Data;

/// <summary>Overlap, containment, intersection and union math for <see cref="ResourceRange"/>.</summary>
public static class ResourceRangeExtensions
{
    /// <summary>True if both ranges have the same form and share at least one byte or subresource.</summary>
    public static bool Overlaps(this in ResourceRange range, in ResourceRange other)
    {
        if (range.IsEmpty || other.IsEmpty || range.IsTexture != other.IsTexture)
            return false;

        if (!range.IsTexture)
            return Axis.Overlaps(range.Offset, range.Size, other.Offset, other.Size);

        return Axis.Overlaps(range.BaseMipLevel, range.MipLevels, other.BaseMipLevel, other.MipLevels)
            && Axis.Overlaps(range.BaseArrayLayer, range.ArrayLayers, other.BaseArrayLayer, other.ArrayLayers);
    }

    /// <summary>True if both ranges have the same form and the range covers all of the other. An empty other is always covered.</summary>
    public static bool Contains(this in ResourceRange range, in ResourceRange other)
    {
        if (range.IsTexture != other.IsTexture && !other.IsEmpty)
            return false;

        if (other.IsEmpty)
            return range.IsTexture == other.IsTexture || other == default;

        if (range.IsEmpty)
            return false;

        if (!range.IsTexture)
            return Axis.Contains(range.Offset, range.Size, other.Offset, other.Size);

        return Axis.Contains(range.BaseMipLevel, range.MipLevels, other.BaseMipLevel, other.MipLevels)
            && Axis.Contains(range.BaseArrayLayer, range.ArrayLayers, other.BaseArrayLayer, other.ArrayLayers);
    }

    /// <summary>The shared part of two ranges, or the default empty range when they do not overlap.</summary>
    public static ResourceRange Intersect(this in ResourceRange range, in ResourceRange other)
    {
        if (!range.Overlaps(other))
            return default;

        if (!range.IsTexture)
        {
            Axis.Intersect(range.Offset, range.Size, other.Offset, other.Size, out uint offset, out uint size);
            return ResourceRange.Bytes(offset, size);
        }

        Axis.Intersect(range.BaseMipLevel, range.MipLevels, other.BaseMipLevel, other.MipLevels, out uint mip, out uint mips);
        Axis.Intersect(range.BaseArrayLayer, range.ArrayLayers, other.BaseArrayLayer, other.ArrayLayers, out uint layer, out uint layers);
        return ResourceRange.Subresources(mip, mips, layer, layers);
    }

    /// <summary>The smallest range covering both, ignoring an empty input. Throws <see cref="ArgumentException"/> for mixed forms.</summary>
    public static ResourceRange Union(this in ResourceRange range, in ResourceRange other)
    {
        if (range.IsEmpty)
            return other;

        if (other.IsEmpty)
            return range;

        if (range.IsTexture != other.IsTexture)
            throw new ArgumentException("Cannot union a byte range with a subresource range.", nameof(other));

        if (!range.IsTexture)
        {
            Axis.Bound(range.Offset, range.Size, other.Offset, other.Size, out uint offset, out uint size);
            return ResourceRange.Bytes(offset, size);
        }

        Axis.Bound(range.BaseMipLevel, range.MipLevels, other.BaseMipLevel, other.MipLevels, out uint mip, out uint mips);
        Axis.Bound(range.BaseArrayLayer, range.ArrayLayers, other.BaseArrayLayer, other.ArrayLayers, out uint layer, out uint layers);
        return ResourceRange.Subresources(mip, mips, layer, layers);
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
