using System;

using Silk.NET.Vulkan;

namespace Prowl.Graphite.Vk;

internal static unsafe class VkBarriers
{
    private const PipelineStageFlags AttachmentStages =
        PipelineStageFlags.ColorAttachmentOutputBit
        | PipelineStageFlags.EarlyFragmentTestsBit
        | PipelineStageFlags.LateFragmentTestsBit;

    private const AccessFlags AttachmentAccess =
        AccessFlags.ColorAttachmentReadBit
        | AccessFlags.ColorAttachmentWriteBit
        | AccessFlags.DepthStencilAttachmentReadBit
        | AccessFlags.DepthStencilAttachmentWriteBit;

    public static PipelineStageFlags AllShaderStages(VkGraphicsDevice gd)
    {
        PipelineStageFlags stages = PipelineStageFlags.VertexShaderBit
            | PipelineStageFlags.FragmentShaderBit
            | PipelineStageFlags.ComputeShaderBit;
        if (gd.Features.GeometryShader)
            stages |= PipelineStageFlags.GeometryShaderBit;
        if (gd.Features.TessellationShaders)
            stages |= PipelineStageFlags.TessellationControlShaderBit | PipelineStageFlags.TessellationEvaluationShaderBit;
        return stages;
    }

    public static SubpassDependency ExternalToPass(VkGraphicsDevice gd) => new()
    {
        SrcSubpass = Silk.NET.Vulkan.Vk.SubpassExternal,
        DstSubpass = 0,
        SrcStageMask = AllShaderStages(gd) | PipelineStageFlags.TransferBit | AttachmentStages,
        SrcAccessMask = AccessFlags.ShaderWriteBit | AccessFlags.TransferWriteBit
            | AccessFlags.ColorAttachmentWriteBit | AccessFlags.DepthStencilAttachmentWriteBit,
        DstStageMask = AttachmentStages,
        DstAccessMask = AttachmentAccess,
    };

    public static SubpassDependency PassToExternal(VkGraphicsDevice gd) => new()
    {
        SrcSubpass = 0,
        DstSubpass = Silk.NET.Vulkan.Vk.SubpassExternal,
        SrcStageMask = AttachmentStages,
        SrcAccessMask = AccessFlags.ColorAttachmentWriteBit | AccessFlags.DepthStencilAttachmentWriteBit,
        DstStageMask = AllShaderStages(gd) | PipelineStageFlags.TransferBit | AttachmentStages,
        DstAccessMask = AccessFlags.ShaderReadBit | AccessFlags.ShaderWriteBit
            | AccessFlags.TransferReadBit | AccessFlags.TransferWriteBit | AttachmentAccess,
    };

    public static ImageLayout RestingLayout(VkTexture texture)
    {
        if (texture.IsSwapchainTexture)
            return ImageLayout.PresentSrcKhr;
        if ((texture.Usage & TextureUsage.Sampled) != 0)
            return ImageLayout.ShaderReadOnlyOptimal;
        if ((texture.Usage & TextureUsage.Storage) != 0)
            return ImageLayout.General;
        if ((texture.Usage & TextureUsage.RenderTarget) != 0)
            return ImageLayout.ColorAttachmentOptimal;
        if ((texture.Usage & TextureUsage.DepthStencil) != 0)
            return ImageLayout.DepthStencilAttachmentOptimal;
        return ImageLayout.General;
    }

    public static ImageLayout AttachmentLayout(VkTexture texture)
        => (texture.Usage & TextureUsage.DepthStencil) != 0
            ? ImageLayout.DepthStencilAttachmentOptimal
            : ImageLayout.ColorAttachmentOptimal;

    public static ImageLayout Layout(VkTexture texture, TextureState state) => state switch
    {
        TextureState.Sampled => ImageLayout.ShaderReadOnlyOptimal,
        TextureState.Storage => ImageLayout.General,
        TextureState.Attachment => AttachmentLayout(texture),
        TextureState.TransferSrc => ImageLayout.TransferSrcOptimal,
        TextureState.TransferDst => ImageLayout.TransferDstOptimal,
        _ => RestingLayout(texture),
    };

    public static ImageLayout CurrentLayout(CommandBufferBase owner, VkTexture texture)
        => Layout(texture, owner.StateOf(texture));

    private static void LayoutScope(VkGraphicsDevice gd, ImageLayout layout, out PipelineStageFlags stages, out AccessFlags access)
    {
        switch (layout)
        {
            case ImageLayout.ShaderReadOnlyOptimal:
                stages = AllShaderStages(gd);
                access = AccessFlags.ShaderReadBit;
                break;
            case ImageLayout.General:
                stages = AllShaderStages(gd) | PipelineStageFlags.TransferBit;
                access = AccessFlags.ShaderReadBit | AccessFlags.ShaderWriteBit
                    | AccessFlags.TransferReadBit | AccessFlags.TransferWriteBit;
                break;
            case ImageLayout.ColorAttachmentOptimal:
                stages = PipelineStageFlags.ColorAttachmentOutputBit;
                access = AccessFlags.ColorAttachmentReadBit | AccessFlags.ColorAttachmentWriteBit;
                break;
            case ImageLayout.DepthStencilAttachmentOptimal:
                stages = PipelineStageFlags.EarlyFragmentTestsBit | PipelineStageFlags.LateFragmentTestsBit;
                access = AccessFlags.DepthStencilAttachmentReadBit | AccessFlags.DepthStencilAttachmentWriteBit;
                break;
            case ImageLayout.TransferSrcOptimal:
                stages = PipelineStageFlags.TransferBit;
                access = AccessFlags.TransferReadBit;
                break;
            case ImageLayout.TransferDstOptimal:
                stages = PipelineStageFlags.TransferBit;
                access = AccessFlags.TransferWriteBit;
                break;
            default:
                stages = PipelineStageFlags.None;
                access = AccessFlags.None;
                break;
        }
    }

    private static bool IsReadOnly(ImageLayout layout)
        => layout is ImageLayout.ShaderReadOnlyOptimal or ImageLayout.TransferSrcOptimal or ImageLayout.PresentSrcKhr;

    private static bool NeedsBarrier(ImageLayout oldLayout, ImageLayout newLayout)
        => oldLayout != newLayout || !IsReadOnly(oldLayout);

    private static ImageMemoryBarrier ImageBarrier(
        VkGraphicsDevice gd,
        VkTexture texture,
        ImageLayout oldLayout,
        ImageLayout newLayout,
        uint baseMipLevel,
        uint levelCount,
        uint baseArrayLayer,
        uint layerCount,
        ref PipelineStageFlags srcStages,
        ref PipelineStageFlags dstStages)
    {
        LayoutScope(gd, oldLayout, out PipelineStageFlags srcStage, out AccessFlags srcAccess);
        LayoutScope(gd, newLayout, out PipelineStageFlags dstStage, out AccessFlags dstAccess);
        srcStages |= srcStage;
        dstStages |= dstStage;

        return new ImageMemoryBarrier
        {
            SType = StructureType.ImageMemoryBarrier,
            SrcAccessMask = srcAccess,
            DstAccessMask = dstAccess,
            OldLayout = oldLayout,
            NewLayout = newLayout,
            SrcQueueFamilyIndex = Silk.NET.Vulkan.Vk.QueueFamilyIgnored,
            DstQueueFamilyIndex = Silk.NET.Vulkan.Vk.QueueFamilyIgnored,
            Image = texture.OptimalDeviceImage,
            SubresourceRange = new ImageSubresourceRange(texture.AspectMask, baseMipLevel, levelCount, baseArrayLayer, layerCount),
        };
    }

    public static void Transition(
        VkGraphicsDevice gd,
        Silk.NET.Vulkan.CommandBuffer cb,
        VkTexture texture,
        ImageLayout oldLayout,
        ImageLayout newLayout)
        => Transition(gd, cb, texture, oldLayout, newLayout, 0, texture.MipLevels, 0, texture.ActualArrayLayers);

    public static void Transition(
        VkGraphicsDevice gd,
        Silk.NET.Vulkan.CommandBuffer cb,
        VkTexture texture,
        ImageLayout oldLayout,
        ImageLayout newLayout,
        uint baseMipLevel,
        uint levelCount,
        uint baseArrayLayer,
        uint layerCount)
    {
        if (texture.IsStaging || !NeedsBarrier(oldLayout, newLayout))
            return;

        PipelineStageFlags srcStages = PipelineStageFlags.None;
        PipelineStageFlags dstStages = PipelineStageFlags.None;
        ImageMemoryBarrier barrier = ImageBarrier(
            gd, texture, oldLayout, newLayout,
            baseMipLevel, levelCount, baseArrayLayer, layerCount,
            ref srcStages, ref dstStages);

        Emit(gd, cb, srcStages, dstStages, null, 1, &barrier);
        gd.Profiler?.RecordBarrier(BarrierBin.TextureTransition, 1);
    }

    public static void Record(
        VkGraphicsDevice gd,
        Silk.NET.Vulkan.CommandBuffer cb,
        ReadOnlySpan<TextureBarrier> textures,
        BufferAccess bufferSrc,
        BufferAccess bufferDst)
    {
        ImageMemoryBarrier* images = stackalloc ImageMemoryBarrier[Math.Max(1, textures.Length)];
        uint imageCount = 0;
        PipelineStageFlags srcStages = PipelineStageFlags.None;
        PipelineStageFlags dstStages = PipelineStageFlags.None;

        foreach (TextureBarrier barrier in textures)
        {
            VkTexture texture = Util.AssertSubtype<Texture, VkTexture>(barrier.Texture);
            ImageLayout oldLayout = Layout(texture, barrier.Before);
            ImageLayout newLayout = Layout(texture, barrier.After);
            if (texture.IsStaging || !NeedsBarrier(oldLayout, newLayout))
                continue;

            images[imageCount++] = ImageBarrier(
                gd, texture, oldLayout, newLayout,
                0, texture.MipLevels, 0, texture.ActualArrayLayers,
                ref srcStages, ref dstStages);
        }

        MemoryBarrier memory = default;
        MemoryBarrier* memoryPtr = null;
        if (bufferSrc != BufferAccess.None)
        {
            BufferScope(gd, bufferSrc, out PipelineStageFlags srcStage, out AccessFlags srcAccess);
            BufferScope(gd, bufferDst, out PipelineStageFlags dstStage, out AccessFlags dstAccess);
            srcStages |= srcStage;
            dstStages |= dstStage;
            memory = new MemoryBarrier
            {
                SType = StructureType.MemoryBarrier,
                SrcAccessMask = srcAccess,
                DstAccessMask = dstAccess,
            };
            memoryPtr = &memory;
        }

        if (imageCount == 0 && memoryPtr == null)
            return;

        Emit(gd, cb, srcStages, dstStages, memoryPtr, imageCount, images);
        if (imageCount > 0)
            gd.Profiler?.RecordBarrier(BarrierBin.TextureTransition, imageCount);
        if (memoryPtr != null)
            gd.Profiler?.RecordBarrier(BarrierBin.MemoryBarrier, 1);
    }

    private static void BufferScope(VkGraphicsDevice gd, BufferAccess access, out PipelineStageFlags stages, out AccessFlags flags)
    {
        stages = PipelineStageFlags.None;
        flags = AccessFlags.None;
        if ((access & (BufferAccess.ShaderRead | BufferAccess.ShaderWrite | BufferAccess.Uniform)) != 0)
            stages |= AllShaderStages(gd);
        if ((access & BufferAccess.ShaderRead) != 0) flags |= AccessFlags.ShaderReadBit;
        if ((access & BufferAccess.ShaderWrite) != 0) flags |= AccessFlags.ShaderWriteBit;
        if ((access & BufferAccess.Uniform) != 0) flags |= AccessFlags.UniformReadBit;
        if ((access & BufferAccess.Vertex) != 0)
        {
            stages |= PipelineStageFlags.VertexInputBit;
            flags |= AccessFlags.VertexAttributeReadBit;
        }
        if ((access & BufferAccess.Index) != 0)
        {
            stages |= PipelineStageFlags.VertexInputBit;
            flags |= AccessFlags.IndexReadBit;
        }
        if ((access & BufferAccess.Indirect) != 0)
        {
            stages |= PipelineStageFlags.DrawIndirectBit;
            flags |= AccessFlags.IndirectCommandReadBit;
        }
        if ((access & (BufferAccess.TransferRead | BufferAccess.TransferWrite)) != 0)
            stages |= PipelineStageFlags.TransferBit;
        if ((access & BufferAccess.TransferRead) != 0) flags |= AccessFlags.TransferReadBit;
        if ((access & BufferAccess.TransferWrite) != 0) flags |= AccessFlags.TransferWriteBit;
    }

    private static void Emit(
        VkGraphicsDevice gd,
        Silk.NET.Vulkan.CommandBuffer cb,
        PipelineStageFlags srcStages,
        PipelineStageFlags dstStages,
        MemoryBarrier* memory,
        uint imageCount,
        ImageMemoryBarrier* images)
    {
        if (srcStages == PipelineStageFlags.None)
            srcStages = PipelineStageFlags.TopOfPipeBit;
        if (dstStages == PipelineStageFlags.None)
            dstStages = PipelineStageFlags.BottomOfPipeBit;

        gd.Vk.CmdPipelineBarrier(
            cb,
            srcStages,
            dstStages,
            DependencyFlags.None,
            memory != null ? 1u : 0u, memory,
            0, null,
            imageCount, images);
    }
}
