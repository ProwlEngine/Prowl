using System.Collections.Generic;
using Silk.NET.Vulkan;
using VkFramebufferHandle = Silk.NET.Vulkan.Framebuffer;

namespace NeoVeldrid.Vk
{
    internal abstract class VkFramebufferBase : Framebuffer
    {
        public VkFramebufferBase(
            FramebufferAttachmentDescription? depthTexture,
            IReadOnlyList<FramebufferAttachmentDescription> colorTextures)
            : base(depthTexture, colorTextures)
        {
            RefCount = new ResourceRefCount(DisposeCore);
        }

        public VkFramebufferBase()
        {
            RefCount = new ResourceRefCount(DisposeCore);
        }

        public ResourceRefCount RefCount { get; }

        public abstract uint RenderableWidth { get; }
        public abstract uint RenderableHeight { get; }

        public override void Dispose()
        {
            RefCount.Decrement();
        }

        protected abstract void DisposeCore();

        public abstract VkFramebufferHandle CurrentFramebuffer { get; }
        public abstract RenderPass RenderPassNoClear_Init { get; }
        public abstract RenderPass RenderPassNoClear_Load { get; }
        public abstract RenderPass RenderPassClear { get; }
        public abstract uint AttachmentCount { get; }
        public abstract void TransitionToIntermediateLayout(CommandBuffer cb);
        public abstract void TransitionToFinalLayout(CommandBuffer cb);
    }
}
