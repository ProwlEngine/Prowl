using System;
using System.Collections.Generic;
using System.Diagnostics;

using Silk.NET.Vulkan;

using VkFramebufferHandle = Silk.NET.Vulkan.Framebuffer;

namespace Prowl.Graphite.Vk;

internal unsafe partial class VkFramebuffer : VkFramebufferBase
{
    private readonly VkGraphicsDevice _gd;
    private readonly VkFramebufferHandle _deviceFramebuffer;
    private readonly RenderPass[] _renderPasses = new RenderPass[4];
    private readonly List<ImageView> _attachmentViews = [];

    public override VkFramebufferHandle CurrentFramebuffer => _deviceFramebuffer;

    public override uint RenderableWidth => Width;
    public override uint RenderableHeight => Height;

    public override uint AttachmentCount { get; }

    public VkFramebuffer(VkGraphicsDevice gd, ref FramebufferDescription description)
        : base(description.DepthTarget, description.ColorTargets)
    {
        _gd = gd;

        uint colorAttachmentCount = (uint)ColorTargets.Count;

        for (int i = 0; i < _renderPasses.Length; i++)
            _renderPasses[i] = CreateRenderPass(colorAttachmentCount, graphMode: (i & 2) != 0, clear: (i & 1) != 0);

        CreateDeviceFramebuffer(ref description, colorAttachmentCount, out _deviceFramebuffer);

        AttachmentCount = (uint)ColorTargets.Count + (DepthTarget is not null ? 1u : 0u);

        _gd.Profiler?.Allocate(AllocBin.Framebuffer, 0);
    }

    public override RenderPass GetRenderPass(bool graphMode, bool clear)
        => _renderPasses[(graphMode ? 2 : 0) | (clear ? 1 : 0)];

    private RenderPass CreateRenderPass(uint colorAttachmentCount, bool graphMode, bool clear)
    {
        AttachmentLoadOp loadOp = clear ? AttachmentLoadOp.Clear : AttachmentLoadOp.Load;

        AttachmentDescription* attachments = stackalloc AttachmentDescription[(int)colorAttachmentCount + 1];
        AttachmentReference* colorAttachmentRefs = stackalloc AttachmentReference[(int)colorAttachmentCount];
        uint attachmentCount = 0;

        for (int i = 0; i < colorAttachmentCount; i++)
        {
            VkTexture vkColorTex = Util.AssertSubtype<Texture, VkTexture>(ColorTargets[i].Target);
            ImageLayout outside = graphMode ? ImageLayout.ColorAttachmentOptimal : VkBarriers.RestingLayout(vkColorTex);
            attachments[attachmentCount++] = new AttachmentDescription
            {
                Format = vkColorTex.VkFormat,
                Samples = vkColorTex.VkSampleCount,
                LoadOp = loadOp,
                StoreOp = AttachmentStoreOp.Store,
                StencilLoadOp = AttachmentLoadOp.DontCare,
                StencilStoreOp = AttachmentStoreOp.DontCare,
                InitialLayout = clear ? ImageLayout.Undefined : outside,
                FinalLayout = outside,
            };
            colorAttachmentRefs[i] = new AttachmentReference((uint)i, ImageLayout.ColorAttachmentOptimal);
        }

        AttachmentReference depthAttachmentRef = default;
        if (DepthTarget != null)
        {
            VkTexture vkDepthTex = Util.AssertSubtype<Texture, VkTexture>(DepthTarget.Value.Target);
            bool hasStencil = FormatHelpers.IsStencilFormat(vkDepthTex.Format);
            ImageLayout outside = graphMode ? ImageLayout.DepthStencilAttachmentOptimal : VkBarriers.RestingLayout(vkDepthTex);
            depthAttachmentRef = new AttachmentReference(attachmentCount, ImageLayout.DepthStencilAttachmentOptimal);
            attachments[attachmentCount++] = new AttachmentDescription
            {
                Format = vkDepthTex.VkFormat,
                Samples = vkDepthTex.VkSampleCount,
                LoadOp = loadOp,
                StoreOp = AttachmentStoreOp.Store,
                StencilLoadOp = hasStencil ? loadOp : AttachmentLoadOp.DontCare,
                StencilStoreOp = hasStencil ? AttachmentStoreOp.Store : AttachmentStoreOp.DontCare,
                InitialLayout = clear ? ImageLayout.Undefined : outside,
                FinalLayout = outside,
            };
        }

        SubpassDescription subpass = new()
        {
            PipelineBindPoint = PipelineBindPoint.Graphics,
            ColorAttachmentCount = colorAttachmentCount,
            PColorAttachments = colorAttachmentCount > 0 ? colorAttachmentRefs : null,
            PDepthStencilAttachment = DepthTarget != null ? &depthAttachmentRef : null,
        };

        SubpassDependency* dependencies = stackalloc SubpassDependency[2];
        dependencies[0] = VkBarriers.ExternalToPass(_gd);
        dependencies[1] = VkBarriers.PassToExternal(_gd);

        RenderPassCreateInfo renderPassCI = new()
        {
            SType = StructureType.RenderPassCreateInfo,
            AttachmentCount = attachmentCount,
            PAttachments = attachments,
            SubpassCount = 1,
            PSubpasses = &subpass,
            DependencyCount = 2,
            PDependencies = dependencies,
        };

        _gd.Vk.CreateRenderPass(_gd.Device, in renderPassCI, null, out RenderPass renderPass).CheckResult();
        return renderPass;
    }

    private void CreateDeviceFramebuffer(ref FramebufferDescription description, uint colorAttachmentCount, out VkFramebufferHandle deviceFramebuffer)
    {
        FramebufferCreateInfo fbCI = new()
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
            ImageViewCreateInfo imageViewCI = new()
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
            _gd.Vk.CreateImageView(_gd.Device, in imageViewCI, null, dest).CheckResult();
            _attachmentViews.Add(*dest);
        }

        // Depth
        if (description.DepthTarget != null)
        {
            VkTexture vkDepthTarget = Util.AssertSubtype<Texture, VkTexture>(description.DepthTarget.Value.Target);
            bool hasStencil = FormatHelpers.IsStencilFormat(vkDepthTarget.Format);
            ImageViewCreateInfo depthViewCI = new()
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
            _gd.Vk.CreateImageView(_gd.Device, in depthViewCI, null, dest).CheckResult();
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
        fbCI.RenderPass = _renderPasses[0];

        _gd.Vk.CreateFramebuffer(_gd.Device, in fbCI, null, out deviceFramebuffer).CheckResult();
    }

    private protected override void NameChanged(string name) => _gd.SetResourceName(this, name);

    protected override void DestroyNative()
    {
        _gd.Vk.DestroyFramebuffer(_gd.Device, _deviceFramebuffer, null);
        foreach (RenderPass renderPass in _renderPasses)
            _gd.Vk.DestroyRenderPass(_gd.Device, renderPass, null);
        foreach (ImageView view in _attachmentViews)
        {
            _gd.Vk.DestroyImageView(_gd.Device, view, null);
        }

        _gd.Profiler?.Free(AllocBin.Framebuffer, 0);
    }
}
