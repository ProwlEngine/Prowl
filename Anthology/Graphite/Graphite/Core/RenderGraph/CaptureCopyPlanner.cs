using System;
using System.Collections.Generic;

using Prowl.Graphite.Debugging;

namespace Prowl.Graphite.RenderGraph;

internal interface ICaptureBackend
{
    void RecordBufferCopy(CommandBuffer cb, DeviceBuffer src, DeviceBuffer staging);

    void RecordTextureCopy(CommandBuffer cb, Texture src, TextureState? before, DeviceBuffer staging, Span<CopyRegion> regions);
}

internal static class CaptureCopyPlanner
{
    public const ulong Alignment = 16;

    public static CopyRegion[] PlanTexture(in TextureDescription description, out uint totalSize)
    {
        uint layers = description.Type == TextureType.TextureCube ? 6 * description.ArrayLayers : description.ArrayLayers;
        bool depthStencil = (description.Usage & TextureUsage.DepthStencil) != 0;
        List<CopyRegion> regions = new();
        ulong offset = 0;

        for (uint mip = 0; mip < description.MipLevels; mip++)
        {
            uint width = Math.Max(1, description.Width >> (int)mip);
            uint height = Math.Max(1, description.Height >> (int)mip);
            uint depth = Math.Max(1, description.Depth >> (int)mip);
            for (uint layer = 0; layer < layers; layer++)
            {
                if (depthStencil)
                {
                    Add(regions, ref offset, mip, layer, width, height, depth, description.Format, (ulong)width * height * depth * 4);
                    Add(regions, ref offset, mip, layer, width, height, depth, PixelFormat.R8_UInt, (ulong)width * height * depth);
                }
                else
                {
                    Add(regions, ref offset, mip, layer, width, height, depth, description.Format, RegionSize(width, height, depth, description.Format));
                }
            }
        }

        if (offset > uint.MaxValue)
            throw new RenderException("The texture is too large to copy into one staging buffer.");

        totalSize = (uint)offset;
        return regions.ToArray();
    }

    private static void Add(List<CopyRegion> regions, ref ulong offset, uint mip, uint layer, uint width, uint height, uint depth, PixelFormat format, ulong size)
    {
        regions.Add(new CopyRegion(mip, layer, offset, width, height, depth, format));
        offset = (offset + size + Alignment - 1) / Alignment * Alignment;
    }

    private static ulong RegionSize(uint width, uint height, uint depth, PixelFormat format)
    {
        if (!FormatHelpers.IsCompressedFormat(format))
            return (ulong)width * height * depth * format.GetSizeInBytes();

        ulong blocks = (ulong)((width + 3) / 4) * ((height + 3) / 4) * depth;
        return blocks * FormatHelpers.GetBlockSizeInBytes(format);
    }
}
