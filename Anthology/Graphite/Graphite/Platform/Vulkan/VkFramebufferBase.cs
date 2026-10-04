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

internal readonly record struct RenderPassOps(
    AttachmentLoadOp ColorLoad,
    AttachmentLoadOp DepthLoad,
    AttachmentStoreOp ColorStore,
    AttachmentStoreOp DepthStore)
{
    public const int Count = 36;

    public static RenderPassOps Load => new(AttachmentLoadOp.Load, AttachmentLoadOp.Load, AttachmentStoreOp.Store, AttachmentStoreOp.Store);

    public int Index => (int)ColorLoad * 12 + (int)DepthLoad * 4 + (int)ColorStore * 2 + (int)DepthStore;
}

internal abstract class VkFramebufferBase : Framebuffer
{
    public VkFramebufferBase(
        FramebufferAttachmentDescription? depthTexture,
        IReadOnlyList<FramebufferAttachmentDescription> colorTextures)
        : base(depthTexture, colorTextures)
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
