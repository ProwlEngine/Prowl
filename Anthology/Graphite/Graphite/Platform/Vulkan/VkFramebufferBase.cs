using System.Collections.Generic;

using Silk.NET.Vulkan;

using VkFramebufferHandle = Silk.NET.Vulkan.Framebuffer;

namespace Prowl.Graphite.Vk;

internal enum FramebufferMode
{
    Resting,
    Graph,
    GraphDepthReadOnly,
}

/// <summary>ColorLoads holds the colour attachments loaded their own way, as <see cref="TargetLoadStoreOps.ColorLoads"/> does.</summary>
internal readonly record struct RenderPassOps(
    AttachmentLoadOp ColorLoad,
    AttachmentLoadOp DepthLoad,
    AttachmentStoreOp ColorStore,
    AttachmentStoreOp DepthStore,
    ushort ColorLoads = 0)
{
    public static RenderPassOps Load => new(AttachmentLoadOp.Load, AttachmentLoadOp.Load, AttachmentStoreOp.Store, AttachmentStoreOp.Store);

    public AttachmentLoadOp ColorLoadAt(int attachment)
    {
        int own = attachment < TargetLoadStoreOps.MaxColorLoads ? (ColorLoads >> (attachment * 2)) & 3 : 0;
        return own switch
        {
            0 => ColorLoad,
            (int)LoadAction.Clear + 1 => AttachmentLoadOp.Clear,
            (int)LoadAction.DontCare + 1 => AttachmentLoadOp.DontCare,
            _ => AttachmentLoadOp.Load,
        };
    }
}

internal abstract class VkFramebufferBase : Framebuffer
{
    public VkFramebufferBase(
        FramebufferAttachment? depthTexture,
        IReadOnlyList<FramebufferAttachment> colorTextures,
        uint viewCount = 0)
        : base(depthTexture, colorTextures, viewCount)
    {
    }

    public VkFramebufferBase()
    {
    }

    public abstract uint RenderableWidth { get; }
    public abstract uint RenderableHeight { get; }

    private protected sealed override void DisposeCore()
    {
        OwnerDevice.DisposeWhenRetired(DestroyNative);
    }

    private protected abstract VkGraphicsDevice OwnerDevice { get; }

    protected abstract void DestroyNative();

    public abstract VkFramebufferHandle CurrentFramebuffer { get; }
    public abstract RenderPass GetRenderPass(FramebufferMode mode, RenderPassOps ops);
    public abstract uint AttachmentCount { get; }
}
