using System;

namespace Prowl.Graphite;

internal static class ValidationHelpers
{
    internal static void RequireNotNull(object value, string parameterName, string caller)
    {
        if (!GraphicsDevice.ValidationEnabled)
            return;

        if (value == null)
        {
            throw new ArgumentNullException(parameterName,
                $"'{parameterName}' passed to {caller} must be non-null.");
        }
    }

    internal static void RequireNotNullRender(object value, string typeName, string caller)
    {
        if (!GraphicsDevice.ValidationEnabled)
            return;

        if (value == null)
        {
            throw new RenderException($"{typeName} passed to {caller} must be non-null.");
        }
    }

    /// <summary>
    /// Array layer count for a texture, x6 for cubemap faces.
    /// </summary>
    internal static uint GetEffectiveArrayLayers(Texture texture)
        => (texture.Usage & TextureUsage.Cubemap) != 0 ? texture.ArrayLayers * 6 : texture.ArrayLayers;

    internal static void CopyTextureCheckNotNull(Texture source, Texture destination)
    {
        if (!GraphicsDevice.ValidationEnabled)
            return;

        RequireNotNull(source, nameof(source), "CopyTexture");
        RequireNotNull(destination, nameof(destination), "CopyTexture");
    }

    internal static void CopyTextureCheckDimensionsCompatible(Texture source, Texture destination)
    {
        if (!GraphicsDevice.ValidationEnabled)
            return;

        if (source.SampleCount != destination.SampleCount || source.Width != destination.Width
            || source.Height != destination.Height || source.Depth != destination.Depth
            || source.Format != destination.Format)
        {
            throw new RenderException("Source and destination Textures are not compatible to be copied in CopyTexture.");
        }
    }

    internal static void CopyTextureCheckCompatibilityAll(Texture source, Texture destination, uint effectiveSrcArrayLayers)
    {
        if (!GraphicsDevice.ValidationEnabled)
            return;

        uint effectiveDstArrayLayers = GetEffectiveArrayLayers(destination);
        if (effectiveSrcArrayLayers != effectiveDstArrayLayers || source.MipLevels != destination.MipLevels)
        {
            throw new RenderException("Source and destination Textures are not compatible to be copied in CopyTexture.");
        }
        CopyTextureCheckDimensionsCompatible(source, destination);
    }

    internal static void CopyTextureCheckCompatibilityForSubresource(Texture source, Texture destination, uint mipLevel, uint arrayLayer)
    {
        if (!GraphicsDevice.ValidationEnabled)
            return;

        uint effectiveSrcArrayLayers = GetEffectiveArrayLayers(source);
        uint effectiveDstArrayLayers = GetEffectiveArrayLayers(destination);
        CopyTextureCheckDimensionsCompatible(source, destination);
        if (mipLevel >= source.MipLevels || mipLevel >= destination.MipLevels || arrayLayer >= effectiveSrcArrayLayers || arrayLayer >= effectiveDstArrayLayers)
        {
            throw new RenderException("mipLevel and arrayLayer must be less than the given Textures' mip level count and array layer count.");
        }
    }

    internal static void CopyTextureCheckRegion(
        Texture source,
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
        if (!GraphicsDevice.ValidationEnabled)
            return;

        if (width == 0 || height == 0 || depth == 0)
        {
            throw new RenderException("The given copy region is empty.");
        }
        if (layerCount == 0)
        {
            throw new RenderException("layerCount must be greater than 0.");
        }
        Util.GetMipDimensions(source, srcMipLevel, out uint srcWidth, out uint srcHeight, out uint srcDepth);
        uint srcBlockSize = FormatHelpers.IsCompressedFormat(source.Format) ? 4u : 1u;
        uint roundedSrcWidth = (srcWidth + srcBlockSize - 1) / srcBlockSize * srcBlockSize;
        uint roundedSrcHeight = (srcHeight + srcBlockSize - 1) / srcBlockSize * srcBlockSize;
        if (srcX + width > roundedSrcWidth || srcY + height > roundedSrcHeight || srcZ + depth > srcDepth)
        {
            throw new RenderException("The given copy region is not valid for the source Texture.");
        }
        Util.GetMipDimensions(destination, dstMipLevel, out uint dstWidth, out uint dstHeight, out uint dstDepth);
        uint dstBlockSize = FormatHelpers.IsCompressedFormat(destination.Format) ? 4u : 1u;
        uint roundedDstWidth = (dstWidth + dstBlockSize - 1) / dstBlockSize * dstBlockSize;
        uint roundedDstHeight = (dstHeight + dstBlockSize - 1) / dstBlockSize * dstBlockSize;
        if (dstX + width > roundedDstWidth || dstY + height > roundedDstHeight || dstZ + depth > dstDepth)
        {
            throw new RenderException("The given copy region is not valid for the destination Texture.");
        }
        if (srcMipLevel >= source.MipLevels)
        {
            throw new RenderException("srcMipLevel must be less than the number of mip levels in the source Texture.");
        }
        uint effectiveSrcArrayLayers = GetEffectiveArrayLayers(source);
        if (srcBaseArrayLayer + layerCount > effectiveSrcArrayLayers)
        {
            throw new RenderException("An invalid mip range was given for the source Texture.");
        }
        if (dstMipLevel >= destination.MipLevels)
        {
            throw new RenderException("dstMipLevel must be less than the number of mip levels in the destination Texture.");
        }
        uint effectiveDstArrayLayers = GetEffectiveArrayLayers(destination);
        if (dstBaseArrayLayer + layerCount > effectiveDstArrayLayers)
        {
            throw new RenderException("An invalid mip range was given for the destination Texture.");
        }
    }

    internal static void CopyBufferCheckRange(
        DeviceBuffer source, uint sourceOffset,
        DeviceBuffer destination, uint destinationOffset,
        uint sizeInBytes)
    {
        if (!GraphicsDevice.ValidationEnabled)
            return;

        if (sourceOffset + sizeInBytes > source.SizeInBytes)
        {
            throw new RenderException(
                $"The source DeviceBuffer's capacity ({source.SizeInBytes}) is not large enough to read {sizeInBytes} bytes at offset {sourceOffset}.");
        }
        if (destinationOffset + sizeInBytes > destination.SizeInBytes)
        {
            throw new RenderException(
                $"The destination DeviceBuffer's capacity ({destination.SizeInBytes}) is not large enough to write {sizeInBytes} bytes at offset {destinationOffset}.");
        }
    }
}
