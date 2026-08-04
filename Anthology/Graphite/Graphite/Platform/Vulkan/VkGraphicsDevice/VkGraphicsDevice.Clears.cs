using Silk.NET.Vulkan;

namespace Prowl.Graphite.Vk;

internal unsafe partial class VkGraphicsDevice
{
    internal void ClearColorTexture(VkTexture texture, ClearColorValue color)
    {
        uint effectiveLayers = texture.ArrayLayers;
        if ((texture.Usage & TextureUsage.Cubemap) != 0)
        {
            effectiveLayers *= 6;
        }
        ImageSubresourceRange range = new(
             ImageAspectFlags.ColorBit,
             0,
             texture.MipLevels,
             0,
             effectiveLayers);
        SharedCommandPool pool = GetFreeCommandPool();
        Silk.NET.Vulkan.CommandBuffer cb = pool.BeginNewCommandBuffer();
        texture.TransitionImageLayout(cb, 0, texture.MipLevels, 0, effectiveLayers, ImageLayout.TransferDstOptimal);
        Vk.CmdClearColorImage(cb, texture.OptimalDeviceImage, ImageLayout.TransferDstOptimal, &color, 1, &range);
        ImageLayout colorLayout = texture.IsSwapchainTexture ? ImageLayout.PresentSrcKhr : ImageLayout.ColorAttachmentOptimal;
        texture.TransitionImageLayout(cb, 0, texture.MipLevels, 0, effectiveLayers, colorLayout);
        pool.EndAndSubmit(cb);
    }

    internal void ClearDepthTexture(VkTexture texture, ClearDepthStencilValue clearValue)
    {
        uint effectiveLayers = texture.ArrayLayers;
        if ((texture.Usage & TextureUsage.Cubemap) != 0)
        {
            effectiveLayers *= 6;
        }
        ImageAspectFlags aspect = FormatHelpers.IsStencilFormat(texture.Format)
            ? ImageAspectFlags.DepthBit | ImageAspectFlags.StencilBit
            : ImageAspectFlags.DepthBit;
        ImageSubresourceRange range = new(
            aspect,
            0,
            texture.MipLevels,
            0,
            effectiveLayers);
        SharedCommandPool pool = GetFreeCommandPool();
        Silk.NET.Vulkan.CommandBuffer cb = pool.BeginNewCommandBuffer();
        texture.TransitionImageLayout(cb, 0, texture.MipLevels, 0, effectiveLayers, ImageLayout.TransferDstOptimal);
        Vk.CmdClearDepthStencilImage(
            cb,
            texture.OptimalDeviceImage,
            ImageLayout.TransferDstOptimal,
            &clearValue,
            1,
            &range);
        texture.TransitionImageLayout(cb, 0, texture.MipLevels, 0, effectiveLayers, ImageLayout.DepthStencilAttachmentOptimal);
        pool.EndAndSubmit(cb);
    }

    internal void TransitionImageLayout(VkTexture texture, ImageLayout layout)
    {
        SharedCommandPool pool = GetFreeCommandPool();
        Silk.NET.Vulkan.CommandBuffer cb = pool.BeginNewCommandBuffer();
        texture.TransitionImageLayout(cb, 0, texture.MipLevels, 0, texture.ActualArrayLayers, layout);
        pool.EndAndSubmit(cb);
    }
}
