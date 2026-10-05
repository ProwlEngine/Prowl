namespace Prowl.Graphite;

internal static class BoundsChecks
{
    internal static void CopyBuffer(DeviceBuffer source, uint sourceOffset,
        DeviceBuffer destination, uint destinationOffset,
        uint sizeInBytes)
    {
        if ((ulong)sourceOffset + sizeInBytes > source.SizeInBytes)
        {
            throw new RenderException(
                $"The source DeviceBuffer's capacity ({source.SizeInBytes}) is not large enough to read {sizeInBytes} bytes at offset {sourceOffset}.");
        }
        if ((ulong)destinationOffset + sizeInBytes > destination.SizeInBytes)
        {
            throw new RenderException(
                $"The destination DeviceBuffer's capacity ({destination.SizeInBytes}) is not large enough to write {sizeInBytes} bytes at offset {destinationOffset}.");
        }
    }

    internal static void CopyTexture(Texture source,
        uint srcX, uint srcY, uint srcZ,
        uint srcMipLevel,
        uint srcBaseArrayLayer,
        Texture destination,
        uint dstX, uint dstY, uint dstZ,
        uint dstMipLevel,
        uint dstBaseArrayLayer,
        uint width, uint height, uint depth,
        uint layerCount)
    {
        if (srcMipLevel >= source.MipLevels)
        {
            throw new RenderException("srcMipLevel must be less than the number of mip levels in the source Texture.");
        }
        if (dstMipLevel >= destination.MipLevels)
        {
            throw new RenderException("dstMipLevel must be less than the number of mip levels in the destination Texture.");
        }

        Util.GetMipDimensions(source, srcMipLevel, out uint srcWidth, out uint srcHeight, out uint srcDepth);
        uint srcBlockSize = FormatHelpers.IsCompressedFormat(source.Format) ? 4u : 1u;
        uint roundedSrcWidth = (srcWidth + srcBlockSize - 1) / srcBlockSize * srcBlockSize;
        uint roundedSrcHeight = (srcHeight + srcBlockSize - 1) / srcBlockSize * srcBlockSize;
        if ((ulong)srcX + width > roundedSrcWidth || (ulong)srcY + height > roundedSrcHeight || (ulong)srcZ + depth > srcDepth)
        {
            throw new RenderException("The given copy region is not valid for the source Texture.");
        }

        Util.GetMipDimensions(destination, dstMipLevel, out uint dstWidth, out uint dstHeight, out uint dstDepth);
        uint dstBlockSize = FormatHelpers.IsCompressedFormat(destination.Format) ? 4u : 1u;
        uint roundedDstWidth = (dstWidth + dstBlockSize - 1) / dstBlockSize * dstBlockSize;
        uint roundedDstHeight = (dstHeight + dstBlockSize - 1) / dstBlockSize * dstBlockSize;
        if ((ulong)dstX + width > roundedDstWidth || (ulong)dstY + height > roundedDstHeight || (ulong)dstZ + depth > dstDepth)
        {
            throw new RenderException("The given copy region is not valid for the destination Texture.");
        }

        if ((ulong)srcBaseArrayLayer + layerCount > ValidationHelpers.GetEffectiveArrayLayers(source))
        {
            throw new RenderException("An invalid array layer range was given for the source Texture.");
        }
        if ((ulong)dstBaseArrayLayer + layerCount > ValidationHelpers.GetEffectiveArrayLayers(destination))
        {
            throw new RenderException("An invalid array layer range was given for the destination Texture.");
        }
    }

    internal static void CopyTextureToBuffer(
        Texture texture,
        DeviceBuffer buffer,
        uint bufferOffset,
        in TextureRegion region)
    {
        if (region.MipLevel >= texture.MipLevels)
        {
            throw new RenderException(
                $"MipLevel ({region.MipLevel}) must be less than the Texture's mip level count ({texture.MipLevels}).");
        }

        uint effectiveArrayLayers = ValidationHelpers.GetEffectiveArrayLayers(texture);
        if (region.ArrayLayer >= effectiveArrayLayers)
        {
            throw new RenderException(
                $"ArrayLayer ({region.ArrayLayer}) must be less than the Texture's effective array layer count ({effectiveArrayLayers}).");
        }

        Util.GetMipDimensions(texture, region.MipLevel, out uint mipWidth, out uint mipHeight, out uint mipDepth);
        if ((ulong)region.X + region.Width > mipWidth || (ulong)region.Y + region.Height > mipHeight || (ulong)region.Z + region.Depth > mipDepth)
        {
            throw new RenderException("The given region does not fit into the Texture mip level.");
        }

        uint size = FormatHelpers.GetRegionSize(region.Width, region.Height, region.Depth, texture.Format);
        if ((ulong)bufferOffset + size > buffer.SizeInBytes)
        {
            throw new RenderException(
                $"The region needs {size} bytes at offset {bufferOffset}, but the buffer holds {buffer.SizeInBytes}.");
        }
    }

    internal static void UpdateTexture(
        Texture texture,
        uint sizeInBytes,
        in TextureRegion region)
    {
        uint expectedSize = FormatHelpers.GetRegionSize(region.Width, region.Height, region.Depth, texture.Format);
        if (sizeInBytes < expectedSize)
        {
            throw new RenderException(
                $"The data size is less than expected for the given update region. At least {expectedSize} bytes must be provided, but only {sizeInBytes} were.");
        }

        uint roundedTextureWidth, roundedTextureHeight;
        if (FormatHelpers.IsCompressedFormat(texture.Format))
        {
            roundedTextureWidth = (texture.Width + 3) / 4 * 4;
            roundedTextureHeight = (texture.Height + 3) / 4 * 4;
        }
        else
        {
            roundedTextureWidth = texture.Width;
            roundedTextureHeight = texture.Height;
        }

        if ((ulong)region.X + region.Width > roundedTextureWidth
            || (ulong)region.Y + region.Height > roundedTextureHeight
            || (ulong)region.Z + region.Depth > texture.Depth)
        {
            throw new RenderException("The given region does not fit into the Texture.");
        }

        if (region.MipLevel >= texture.MipLevels)
        {
            throw new RenderException(
                $"MipLevel ({region.MipLevel}) must be less than the Texture's mip level count ({texture.MipLevels}).");
        }

        uint effectiveArrayLayers = ValidationHelpers.GetEffectiveArrayLayers(texture);
        if (region.ArrayLayer >= effectiveArrayLayers)
        {
            throw new RenderException(
                $"ArrayLayer ({region.ArrayLayer}) must be less than the Texture's effective array layer count ({effectiveArrayLayers}).");
        }
    }
}
