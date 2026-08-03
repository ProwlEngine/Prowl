using System.Collections.Generic;

using Silk.NET.Vulkan;

using VkFramebufferHandle = Silk.NET.Vulkan.Framebuffer;

namespace Prowl.Graphite.Vk;

internal abstract class VkFramebufferBase : Framebuffer
{
    public VkFramebufferBase(
        FramebufferAttachmentDescription? depthTexture,
        IReadOnlyList<FramebufferAttachmentDescription> colorTextures)
        : base(depthTexture, colorTextures)
    {
        RefCount = new ResourceRefCount(DestroyNative);
    }

    public VkFramebufferBase()
    {
        RefCount = new ResourceRefCount(DestroyNative);
    }

    public ResourceRefCount RefCount { get; }

    public abstract uint RenderableWidth { get; }
    public abstract uint RenderableHeight { get; }

    private protected sealed override void DisposeCore()
    {
        RefCount.Decrement();
    }

    protected abstract void DestroyNative();

    public abstract VkFramebufferHandle CurrentFramebuffer { get; }
    public abstract RenderPass RenderPassNoClear_Init { get; }
    public abstract RenderPass RenderPassNoClear_Load { get; }
    public abstract RenderPass RenderPassClear { get; }
    public abstract uint AttachmentCount { get; }
    public abstract void TransitionToIntermediateLayout(Silk.NET.Vulkan.CommandBuffer cb);
    public abstract void TransitionToFinalLayout(Silk.NET.Vulkan.CommandBuffer cb);
}
