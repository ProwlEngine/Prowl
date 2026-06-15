using System.Diagnostics;

namespace Prowl.Graphite;

public abstract partial class GraphicsDevice
{
    [Conditional("VALIDATE_USAGE")]
    private void CurrentFrame_CheckActive()
    {
#if VALIDATE_USAGE
        if (_currentFrame == null)
        {
            throw new RenderException(
                "This operation requires an active frame, but none is open. Call BeginFrame before " +
                "recording frame-dependent commands, and submit them before EndFrame.");
        }
#endif
    }

    [Conditional("VALIDATE_USAGE")]
    private void BeginFrame_CheckNoActive()
    {
#if VALIDATE_USAGE
        if (_currentFrame != null)
        {
            throw new RenderException("BeginFrame called while a frame is already active. Call EndFrame first.");
        }
#endif
    }

    [Conditional("VALIDATE_USAGE")]
    private void EndFrame_CheckHasActive()
    {
#if VALIDATE_USAGE
        if (_currentFrame == null)
        {
            throw new RenderException("EndFrame called with no active frame. Call BeginFrame first.");
        }
#endif
    }

    [Conditional("VALIDATE_USAGE")]
    private void EndFrame_CheckIsActive(Frame frame)
    {
#if VALIDATE_USAGE
        if (CurrentFrame != frame)
        {
            throw new RenderException("The specified Frame is not the currently active frame.");
        }
#endif
    }

    [Conditional("VALIDATE_USAGE")]
    private void SyncToVerticalBlank_CheckMainSwapchain()
    {
#if VALIDATE_USAGE
        if (MainSwapchain == null)
        {
            throw new RenderException("This GraphicsDevice was created without a main Swapchain. This property cannot be set.");
        }
#endif
    }

    [Conditional("VALIDATE_USAGE")]
    private static void Map_CheckResource(MappableResource resource, MapMode mode, uint subresource)
    {
#if VALIDATE_USAGE
        if (resource is DeviceBuffer buffer)
        {
            if ((buffer.Usage & BufferUsage.Dynamic) != BufferUsage.Dynamic
                && (buffer.Usage & BufferUsage.Staging) != BufferUsage.Staging)
            {
                throw new RenderException("Buffers must have the Staging or Dynamic usage flag to be mapped.");
            }
            if (subresource != 0)
            {
                throw new RenderException("Subresource must be 0 for Buffer resources.");
            }
            if ((mode == MapMode.Read || mode == MapMode.ReadWrite) && (buffer.Usage & BufferUsage.Staging) == 0)
            {
                throw new RenderException(
                    $"{nameof(MapMode)}.{nameof(MapMode.Read)} and {nameof(MapMode)}.{nameof(MapMode.ReadWrite)} can only be used on buffers created with {nameof(BufferUsage)}.{nameof(BufferUsage.Staging)}.");
            }
        }
        else if (resource is Texture tex)
        {
            if ((tex.Usage & TextureUsage.Staging) == 0)
            {
                throw new RenderException("Texture must have the Staging usage flag to be mapped.");
            }
            if (subresource >= tex.ArrayLayers * tex.MipLevels)
            {
                throw new RenderException(
                    "Subresource must be less than the number of subresources in the Texture being mapped.");
            }
        }
#endif
    }

    [Conditional("VALIDATE_USAGE")]
    private static void UpdateTexture_CheckParameters(
        Texture texture,
        uint sizeInBytes,
        uint x, uint y, uint z,
        uint width, uint height, uint depth,
        uint mipLevel, uint arrayLayer)
    {
#if VALIDATE_USAGE
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
#endif
    }
}
