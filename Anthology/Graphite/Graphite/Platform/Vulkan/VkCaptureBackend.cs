using System;

using Prowl.Graphite.Debugging;
using Prowl.Graphite.RenderGraph;

using Silk.NET.Vulkan;

namespace Prowl.Graphite.Vk;

internal sealed unsafe class VkCaptureBackend : ICaptureBackend
{
    private readonly VkGraphicsDevice _gd;

    public VkCaptureBackend(VkGraphicsDevice gd)
    {
        _gd = gd;
    }

    public void RecordBufferCopy(Prowl.Graphite.CommandBuffer cb, DeviceBuffer src, DeviceBuffer staging)
    {
        Silk.NET.Vulkan.CommandBuffer handle = Util.AssertSubtype<Prowl.Graphite.CommandBuffer, VkCommandBuffer>(cb).CommandBuffer;
        VkBuffer source = Util.AssertSubtype<DeviceBuffer, VkBuffer>(src);
        VkBuffer destination = Util.AssertSubtype<DeviceBuffer, VkBuffer>(staging);

        MemoryBarrier before = new()
        {
            SType = StructureType.MemoryBarrier,
            SrcAccessMask = AccessFlags.MemoryWriteBit,
            DstAccessMask = AccessFlags.TransferReadBit
        };
        _gd.Vk.CmdPipelineBarrier(handle, PipelineStageFlags.AllCommandsBit, PipelineStageFlags.TransferBit, 0, 1, in before, 0, null, 0, null);

        BufferCopy region = new() { Size = src.SizeInBytes };
        _gd.Vk.CmdCopyBuffer(handle, source.DeviceBuffer, destination.DeviceBuffer, 1, in region);

        MemoryBarrier after = new() { SType = StructureType.MemoryBarrier };
        _gd.Vk.CmdPipelineBarrier(handle, PipelineStageFlags.TransferBit, PipelineStageFlags.AllCommandsBit, 0, 1, in after, 0, null, 0, null);
    }

    public void RecordTextureCopy(Prowl.Graphite.CommandBuffer cb, Texture src, TextureState? before, DeviceBuffer staging, Span<CopyRegion> regions)
    {
        Silk.NET.Vulkan.CommandBuffer handle = Util.AssertSubtype<Prowl.Graphite.CommandBuffer, VkCommandBuffer>(cb).CommandBuffer;
        VkTexture texture = Util.AssertSubtype<Texture, VkTexture>(src);
        VkBuffer destination = Util.AssertSubtype<DeviceBuffer, VkBuffer>(staging);
        ImageLayout layout = VkBarriers.Layout(texture, before);

        VkTexture source = texture;
        Texture? resolved = null;
        VkBarriers.Transition(_gd, handle, texture, layout, ImageLayout.TransferSrcOptimal);

        if (texture.SampleCount != TextureSampleCount.Count1)
        {
            if ((texture.Usage & TextureUsage.DepthStencil) != 0)
                throw new NotSupportedException("Multisampled depth textures cannot be copied.");

            resolved = _gd.ResourceFactory.CreateTexture(new TextureDescription(
                texture.Width, texture.Height, 1, 1, texture.ArrayLayers, texture.Format, TextureUsage.Sampled, TextureType.Texture2D, TextureSampleCount.Count1));
            source = Util.AssertSubtype<Texture, VkTexture>(resolved);

            VkBarriers.Transition(_gd, handle, source, VkBarriers.RestingLayout(source), ImageLayout.TransferDstOptimal);
            ImageResolve resolve = new()
            {
                SrcSubresource = new ImageSubresourceLayers(ImageAspectFlags.ColorBit, 0, 0, texture.ActualArrayLayers),
                DstSubresource = new ImageSubresourceLayers(ImageAspectFlags.ColorBit, 0, 0, texture.ActualArrayLayers),
                Extent = new Extent3D(texture.Width, texture.Height, 1)
            };
            _gd.Vk.CmdResolveImage(handle, texture.OptimalDeviceImage, ImageLayout.TransferSrcOptimal, source.OptimalDeviceImage, ImageLayout.TransferDstOptimal, 1, in resolve);
            VkBarriers.Transition(_gd, handle, source, ImageLayout.TransferDstOptimal, ImageLayout.TransferSrcOptimal);
        }

        bool depthStencil = (texture.Usage & TextureUsage.DepthStencil) != 0;
        BufferImageCopy[] copies = new BufferImageCopy[regions.Length];
        for (int i = 0; i < regions.Length; i++)
        {
            CopyRegion region = regions[i];
            ImageAspectFlags aspect = !depthStencil ? ImageAspectFlags.ColorBit
                : region.Format == PixelFormat.R8_UInt ? ImageAspectFlags.StencilBit : ImageAspectFlags.DepthBit;
            copies[i] = new BufferImageCopy
            {
                BufferOffset = region.Offset,
                ImageSubresource = new ImageSubresourceLayers(aspect, region.MipLevel, region.ArrayLayer, 1),
                ImageExtent = new Extent3D(region.Width, region.Height, region.Depth)
            };
        }

        fixed (BufferImageCopy* copiesPtr = copies)
            _gd.Vk.CmdCopyImageToBuffer(handle, source.OptimalDeviceImage, ImageLayout.TransferSrcOptimal, destination.DeviceBuffer, (uint)copies.Length, copiesPtr);

        VkBarriers.Transition(_gd, handle, texture, ImageLayout.TransferSrcOptimal, layout);
        resolved?.Dispose();
    }
}
