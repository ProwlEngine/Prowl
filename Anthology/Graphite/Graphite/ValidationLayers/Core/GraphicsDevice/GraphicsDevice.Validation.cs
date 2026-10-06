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

    internal void UpdateTexture_CheckParameters(Texture texture, in TextureRegion region)
    {
        if (!ValidationEnabled)
            return;

        if (FormatHelpers.IsCompressedFormat(texture.Format))
        {
            if (region.X % 4 != 0 || region.Y % 4 != 0 || region.Height % 4 != 0 || region.Width % 4 != 0)
            {
                Util.GetMipDimensions(texture, region.MipLevel, out uint mipWidth, out uint mipHeight, out _);
                if (region.Width != mipWidth && region.Height != mipHeight)
                {
                    throw new RenderException($"Updates to block-compressed textures must use a region that is block-size aligned and sized.");
                }
            }
        }
    }
}
