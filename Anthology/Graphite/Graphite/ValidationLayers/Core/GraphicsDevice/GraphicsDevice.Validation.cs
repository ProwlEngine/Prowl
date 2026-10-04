namespace Prowl.Graphite;

public abstract partial class GraphicsDevice
{
    /// <summary>
    /// True = validation layer runs this device's checks. Set once at device creation.
    /// </summary>
    internal bool ValidationEnabled { get; set; }

    private void InitializeFrameOptions_SetValidationEnabled(in GraphicsDeviceOptions options)
    {
        ValidationEnabled = options.GraphiteValidation;
    }

    private void SyncToVerticalBlank_CheckMainSwapchain()
    {
        if (!ValidationEnabled)
            return;

        if (MainSwapchain == null)
        {
            throw new RenderException("This GraphicsDevice was created without a main Swapchain. This property cannot be set.");
        }
    }

    private void Map_CheckResource(DeviceBuffer buffer)
    {
        if (!ValidationEnabled)
            return;

        if ((buffer.Usage & BufferUsage.Dynamic) != BufferUsage.Dynamic
            && (buffer.Usage & BufferUsage.Staging) != BufferUsage.Staging)
        {
            throw new RenderException("Buffers must have the Staging or Dynamic usage flag to be mapped.");
        }
    }

    internal void CopyTextureToBuffer_CheckParameters(
        Texture texture,
        DeviceBuffer buffer,
        uint bufferOffset,
        in TextureRegion region)
    {
        if (!ValidationEnabled)
            return;

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
        if (region.X + region.Width > mipWidth || region.Y + region.Height > mipHeight || region.Z + region.Depth > mipDepth)
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

    internal void UpdateTexture_CheckParameters(
        Texture texture,
        uint sizeInBytes,
        in TextureRegion region)
    {
        if (!ValidationEnabled)
            return;

        uint x = region.X, y = region.Y, z = region.Z;
        uint width = region.Width, height = region.Height, depth = region.Depth;
        uint mipLevel = region.MipLevel, arrayLayer = region.ArrayLayer;

        if (FormatHelpers.IsCompressedFormat(texture.Format))
        {
            if (x % 4 != 0 || y % 4 != 0 || height % 4 != 0 || width % 4 != 0)
            {
                Util.GetMipDimensions(texture, mipLevel, out uint mipWidth, out uint mipHeight, out _);
                if (width != mipWidth && height != mipHeight)
                {
                    throw new RenderException($"Updates to block-compressed textures must use a region that is block-size aligned and sized.");
                }
            }
        }
        uint expectedSize = FormatHelpers.GetRegionSize(width, height, depth, texture.Format);
        if (sizeInBytes < expectedSize)
        {
            throw new RenderException(
                $"The data size is less than expected for the given update region. At least {expectedSize} bytes must be provided, but only {sizeInBytes} were.");
        }

        // Compressed textures don't necessarily need to have a Texture.Width and Texture.Height that are a multiple of 4.
        // But the mipdata width and height *does* need to be a multiple of 4.
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

        if (x + width > roundedTextureWidth || y + height > roundedTextureHeight || z + depth > texture.Depth)
        {
            throw new RenderException($"The given region does not fit into the Texture.");
        }

        if (mipLevel >= texture.MipLevels)
        {
            throw new RenderException(
                $"{nameof(mipLevel)} ({mipLevel}) must be less than the Texture's mip level count ({texture.MipLevels}).");
        }

        uint effectiveArrayLayers = ValidationHelpers.GetEffectiveArrayLayers(texture);
        if (arrayLayer >= effectiveArrayLayers)
        {
            throw new RenderException(
                $"{nameof(arrayLayer)} ({arrayLayer}) must be less than the Texture's effective array layer count ({effectiveArrayLayers}).");
        }
    }
}
