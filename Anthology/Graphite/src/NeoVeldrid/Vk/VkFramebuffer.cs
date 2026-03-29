using System.Collections.Generic;
using Silk.NET.Vulkan;
using static NeoVeldrid.Vk.VulkanUtil;
using System;
using System.Diagnostics;
using VkFramebufferHandle = Silk.NET.Vulkan.Framebuffer;

namespace NeoVeldrid.Vk
{
    internal unsafe class VkFramebuffer : VkFramebufferBase
    {
        private readonly VkGraphicsDevice _gd;
        private readonly VkFramebufferHandle _deviceFramebuffer;
        private readonly RenderPass _renderPassNoClearLoad;
        private readonly RenderPass _renderPassNoClear;
        private readonly RenderPass _renderPassClear;
        private readonly List<ImageView> _attachmentViews = new List<ImageView>();
        private bool _destroyed;
        private string _name;

        public override VkFramebufferHandle CurrentFramebuffer => _deviceFramebuffer;
        public override RenderPass RenderPassNoClear_Init => _renderPassNoClear;
        public override RenderPass RenderPassNoClear_Load => _renderPassNoClearLoad;
        public override RenderPass RenderPassClear => _renderPassClear;

        public override uint RenderableWidth => Width;
        public override uint RenderableHeight => Height;

        public override uint AttachmentCount { get; }

        public override bool IsDisposed => _destroyed;

        public VkFramebuffer(VkGraphicsDevice gd, ref FramebufferDescription description, bool isPresented)
            : base(description.DepthTarget, description.ColorTargets)
        {
            _gd = gd;

            RenderPassCreateInfo renderPassCI = new RenderPassCreateInfo
            {
                SType = StructureType.RenderPassCreateInfo
            };

            StackList<AttachmentDescription> attachments = new StackList<AttachmentDescription>();

            uint colorAttachmentCount = (uint)ColorTargets.Count;
            StackList<AttachmentReference> colorAttachmentRefs = new StackList<AttachmentReference>();
            for (int i = 0; i < colorAttachmentCount; i++)
            {
                VkTexture vkColorTex = Util.AssertSubtype<Texture, VkTexture>(ColorTargets[i].Target);
                AttachmentDescription colorAttachmentDesc = new AttachmentDescription();
                colorAttachmentDesc.Format = vkColorTex.VkFormat;
                colorAttachmentDesc.Samples = vkColorTex.VkSampleCount;
                colorAttachmentDesc.LoadOp = AttachmentLoadOp.Load;
                colorAttachmentDesc.StoreOp = AttachmentStoreOp.Store;
                colorAttachmentDesc.StencilLoadOp = AttachmentLoadOp.DontCare;
                colorAttachmentDesc.StencilStoreOp = AttachmentStoreOp.DontCare;
                colorAttachmentDesc.InitialLayout = isPresented
                    ? ImageLayout.PresentSrcKhr
                    : ((vkColorTex.Usage & TextureUsage.Sampled) != 0)
                        ? ImageLayout.ShaderReadOnlyOptimal
                        : ImageLayout.ColorAttachmentOptimal;
                colorAttachmentDesc.FinalLayout = ImageLayout.ColorAttachmentOptimal;
                attachments.Add(colorAttachmentDesc);

                AttachmentReference colorAttachmentRef = new AttachmentReference();
                colorAttachmentRef.Attachment = (uint)i;
                colorAttachmentRef.Layout = ImageLayout.ColorAttachmentOptimal;
                colorAttachmentRefs.Add(colorAttachmentRef);
            }

            AttachmentDescription depthAttachmentDesc = new AttachmentDescription();
            AttachmentReference depthAttachmentRef = new AttachmentReference();
            if (DepthTarget != null)
            {
                VkTexture vkDepthTex = Util.AssertSubtype<Texture, VkTexture>(DepthTarget.Value.Target);
                bool hasStencil = FormatHelpers.IsStencilFormat(vkDepthTex.Format);
                depthAttachmentDesc.Format = vkDepthTex.VkFormat;
                depthAttachmentDesc.Samples = vkDepthTex.VkSampleCount;
                depthAttachmentDesc.LoadOp = AttachmentLoadOp.Load;
                depthAttachmentDesc.StoreOp = AttachmentStoreOp.Store;
                depthAttachmentDesc.StencilLoadOp = AttachmentLoadOp.DontCare;
                depthAttachmentDesc.StencilStoreOp = hasStencil
                    ? AttachmentStoreOp.Store
                    : AttachmentStoreOp.DontCare;
                depthAttachmentDesc.InitialLayout = ((vkDepthTex.Usage & TextureUsage.Sampled) != 0)
                    ? ImageLayout.ShaderReadOnlyOptimal
                    : ImageLayout.DepthStencilAttachmentOptimal;
                depthAttachmentDesc.FinalLayout = ImageLayout.DepthStencilAttachmentOptimal;

                depthAttachmentRef.Attachment = (uint)description.ColorTargets.Length;
                depthAttachmentRef.Layout = ImageLayout.DepthStencilAttachmentOptimal;
            }

            SubpassDescription subpass = new SubpassDescription();
            subpass.PipelineBindPoint = PipelineBindPoint.Graphics;
            if (ColorTargets.Count > 0)
            {
                subpass.ColorAttachmentCount = colorAttachmentCount;
                subpass.PColorAttachments = (AttachmentReference*)colorAttachmentRefs.Data;
            }

            if (DepthTarget != null)
            {
                subpass.PDepthStencilAttachment = &depthAttachmentRef;
                attachments.Add(depthAttachmentDesc);
            }

            SubpassDependency subpassDependency = new SubpassDependency();
            subpassDependency.SrcSubpass = Silk.NET.Vulkan.Vk.SubpassExternal;
            subpassDependency.SrcStageMask = PipelineStageFlags.ColorAttachmentOutputBit;
            subpassDependency.DstStageMask = PipelineStageFlags.ColorAttachmentOutputBit;
            subpassDependency.DstAccessMask = AccessFlags.ColorAttachmentReadBit | AccessFlags.ColorAttachmentWriteBit;

            renderPassCI.AttachmentCount = attachments.Count;
            renderPassCI.PAttachments = (AttachmentDescription*)attachments.Data;
            renderPassCI.SubpassCount = 1;
            renderPassCI.PSubpasses = &subpass;
            renderPassCI.DependencyCount = 1;
            renderPassCI.PDependencies = &subpassDependency;

            Result creationResult = _gd.Vk.CreateRenderPass(_gd.Device, in renderPassCI, null, out _renderPassNoClear);
            CheckResult(creationResult);

            for (int i = 0; i < colorAttachmentCount; i++)
            {
                attachments[i].LoadOp = AttachmentLoadOp.Load;
                attachments[i].InitialLayout = ImageLayout.ColorAttachmentOptimal;
            }
            if (DepthTarget != null)
            {
                attachments[attachments.Count - 1].LoadOp = AttachmentLoadOp.Load;
                attachments[attachments.Count - 1].InitialLayout = ImageLayout.DepthStencilAttachmentOptimal;
                bool hasStencil = FormatHelpers.IsStencilFormat(DepthTarget.Value.Target.Format);
                if (hasStencil)
                {
                    attachments[attachments.Count - 1].StencilLoadOp = AttachmentLoadOp.Load;
                }

            }
            creationResult = _gd.Vk.CreateRenderPass(_gd.Device, in renderPassCI, null, out _renderPassNoClearLoad);
            CheckResult(creationResult);


            // Load version

            if (DepthTarget != null)
            {
                attachments[attachments.Count - 1].LoadOp = AttachmentLoadOp.Clear;
                attachments[attachments.Count - 1].InitialLayout = ImageLayout.Undefined;
                bool hasStencil = FormatHelpers.IsStencilFormat(DepthTarget.Value.Target.Format);
                if (hasStencil)
                {
                    attachments[attachments.Count - 1].StencilLoadOp = AttachmentLoadOp.Clear;
                }
            }

            for (int i = 0; i < colorAttachmentCount; i++)
            {
                attachments[i].LoadOp = AttachmentLoadOp.Clear;
                attachments[i].InitialLayout = ImageLayout.Undefined;
            }

            creationResult = _gd.Vk.CreateRenderPass(_gd.Device, in renderPassCI, null, out _renderPassClear);
            CheckResult(creationResult);

            FramebufferCreateInfo fbCI = new FramebufferCreateInfo
            {
                SType = StructureType.FramebufferCreateInfo
            };
            uint fbAttachmentsCount = (uint)description.ColorTargets.Length;
            if (description.DepthTarget != null)
            {
                fbAttachmentsCount += 1;
            }

            ImageView* fbAttachments = stackalloc ImageView[(int)fbAttachmentsCount];
            for (int i = 0; i < colorAttachmentCount; i++)
            {
                VkTexture vkColorTarget = Util.AssertSubtype<Texture, VkTexture>(description.ColorTargets[i].Target);
                ImageViewCreateInfo imageViewCI = new ImageViewCreateInfo
                {
                    SType = StructureType.ImageViewCreateInfo,
                    Image = vkColorTarget.OptimalDeviceImage,
                    Format = vkColorTarget.VkFormat,
                    ViewType = ImageViewType.Type2D,
                    SubresourceRange = new ImageSubresourceRange(
                        ImageAspectFlags.ColorBit,
                        description.ColorTargets[i].MipLevel,
                        1,
                        description.ColorTargets[i].ArrayLayer,
                        1)
                };
                ImageView* dest = (fbAttachments + i);
                Result result = _gd.Vk.CreateImageView(_gd.Device, in imageViewCI, null, dest);
                CheckResult(result);
                _attachmentViews.Add(*dest);
            }

            // Depth
            if (description.DepthTarget != null)
            {
                VkTexture vkDepthTarget = Util.AssertSubtype<Texture, VkTexture>(description.DepthTarget.Value.Target);
                bool hasStencil = FormatHelpers.IsStencilFormat(vkDepthTarget.Format);
                ImageViewCreateInfo depthViewCI = new ImageViewCreateInfo
                {
                    SType = StructureType.ImageViewCreateInfo,
                    Image = vkDepthTarget.OptimalDeviceImage,
                    Format = vkDepthTarget.VkFormat,
                    ViewType = description.DepthTarget.Value.Target.ArrayLayers == 1
                        ? ImageViewType.Type2D
                        : ImageViewType.Type2DArray,
                    SubresourceRange = new ImageSubresourceRange(
                        hasStencil ? ImageAspectFlags.DepthBit | ImageAspectFlags.StencilBit : ImageAspectFlags.DepthBit,
                        description.DepthTarget.Value.MipLevel,
                        1,
                        description.DepthTarget.Value.ArrayLayer,
                        1)
                };
                ImageView* dest = (fbAttachments + (fbAttachmentsCount - 1));
                Result result = _gd.Vk.CreateImageView(_gd.Device, in depthViewCI, null, dest);
                CheckResult(result);
                _attachmentViews.Add(*dest);
            }

            Texture dimTex;
            uint mipLevel;
            if (ColorTargets.Count > 0)
            {
                dimTex = ColorTargets[0].Target;
                mipLevel = ColorTargets[0].MipLevel;
            }
            else
            {
                Debug.Assert(DepthTarget != null);
                dimTex = DepthTarget.Value.Target;
                mipLevel = DepthTarget.Value.MipLevel;
            }

            Util.GetMipDimensions(
                dimTex,
                mipLevel,
                out uint mipWidth,
                out uint mipHeight,
                out _);

            fbCI.Width = mipWidth;
            fbCI.Height = mipHeight;

            fbCI.AttachmentCount = fbAttachmentsCount;
            fbCI.PAttachments = fbAttachments;
            fbCI.Layers = 1;
            fbCI.RenderPass = _renderPassNoClear;

            creationResult = _gd.Vk.CreateFramebuffer(_gd.Device, in fbCI, null, out _deviceFramebuffer);
            CheckResult(creationResult);

            if (DepthTarget != null)
            {
                AttachmentCount += 1;
            }
            AttachmentCount += (uint)ColorTargets.Count;
        }

        public override void TransitionToIntermediateLayout(CommandBuffer cb)
        {
            for (int i = 0; i < ColorTargets.Count; i++)
            {
                FramebufferAttachment ca = ColorTargets[i];
                VkTexture vkTex = Util.AssertSubtype<Texture, VkTexture>(ca.Target);
                vkTex.SetImageLayout(ca.MipLevel, ca.ArrayLayer, ImageLayout.ColorAttachmentOptimal);
            }
            if (DepthTarget != null)
            {
                VkTexture vkTex = Util.AssertSubtype<Texture, VkTexture>(DepthTarget.Value.Target);
                vkTex.SetImageLayout(
                    DepthTarget.Value.MipLevel,
                    DepthTarget.Value.ArrayLayer,
                    ImageLayout.DepthStencilAttachmentOptimal);
            }
        }

        public override void TransitionToFinalLayout(CommandBuffer cb)
        {
            for (int i = 0; i < ColorTargets.Count; i++)
            {
                FramebufferAttachment ca = ColorTargets[i];
                VkTexture vkTex = Util.AssertSubtype<Texture, VkTexture>(ca.Target);
                if ((vkTex.Usage & TextureUsage.Sampled) != 0)
                {
                    vkTex.TransitionImageLayout(
                        cb,
                        ca.MipLevel, 1,
                        ca.ArrayLayer, 1,
                        ImageLayout.ShaderReadOnlyOptimal);
                }
            }
            if (DepthTarget != null)
            {
                VkTexture vkTex = Util.AssertSubtype<Texture, VkTexture>(DepthTarget.Value.Target);
                if ((vkTex.Usage & TextureUsage.Sampled) != 0)
                {
                    vkTex.TransitionImageLayout(
                        cb,
                        DepthTarget.Value.MipLevel, 1,
                        DepthTarget.Value.ArrayLayer, 1,
                        ImageLayout.ShaderReadOnlyOptimal);
                }
            }
        }

        public override string Name
        {
            get => _name;
            set
            {
                _name = value;
                _gd.SetResourceName(this, value);
            }
        }

        protected override void DisposeCore()
        {
            if (!_destroyed)
            {
                _gd.Vk.DestroyFramebuffer(_gd.Device, _deviceFramebuffer, null);
                _gd.Vk.DestroyRenderPass(_gd.Device, _renderPassNoClear, null);
                _gd.Vk.DestroyRenderPass(_gd.Device, _renderPassNoClearLoad, null);
                _gd.Vk.DestroyRenderPass(_gd.Device, _renderPassClear, null);
                foreach (ImageView view in _attachmentViews)
                {
                    _gd.Vk.DestroyImageView(_gd.Device, view, null);
                }

                _destroyed = true;
            }
        }
    }
}
