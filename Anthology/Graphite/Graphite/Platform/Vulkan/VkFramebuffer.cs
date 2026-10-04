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
    private readonly RenderPass[] _renderPasses = new RenderPass[3 * RenderPassOps.Count];
    private readonly uint _colorAttachmentCount;
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
        _colorAttachmentCount = colorAttachmentCount;

        CreateDeviceFramebuffer(ref description, colorAttachmentCount, out _deviceFramebuffer);

        AttachmentCount = (uint)ColorTargets.Count + (DepthTarget is not null ? 1u : 0u);

        _gd.Profiler?.Allocate(AllocBin.Framebuffer, 0);
    }

    public override RenderPass GetRenderPass(FramebufferMode mode, RenderPassOps ops)
    {
        int index = (int)mode * RenderPassOps.Count + ops.Index;
        lock (_renderPasses)
        {
            if (_renderPasses[index].Handle == default)
                _renderPasses[index] = CreateRenderPass(_colorAttachmentCount, mode, ops);
            return _renderPasses[index];
        }
    }

    private RenderPass CreateRenderPass(uint colorAttachmentCount, FramebufferMode mode, RenderPassOps ops)
    {
        bool graphMode = mode != FramebufferMode.Resting;

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
                LoadOp = ops.ColorLoad,
                StoreOp = ops.ColorStore,
                StencilLoadOp = AttachmentLoadOp.DontCare,
                StencilStoreOp = AttachmentStoreOp.DontCare,
                InitialLayout = ops.ColorLoad == AttachmentLoadOp.Load ? outside : ImageLayout.Undefined,
                FinalLayout = outside,
            };
            colorAttachmentRefs[i] = new AttachmentReference((uint)i, ImageLayout.ColorAttachmentOptimal);
        }

        AttachmentReference depthAttachmentRef = default;
        if (DepthTarget != null)
        {
            VkTexture vkDepthTex = Util.AssertSubtype<Texture, VkTexture>(DepthTarget.Value.Target);
            bool hasStencil = FormatHelpers.IsStencilFormat(vkDepthTex.Format);
            bool readOnly = mode == FramebufferMode.GraphDepthReadOnly;
            ImageLayout inside = readOnly ? ImageLayout.DepthStencilReadOnlyOptimal : ImageLayout.DepthStencilAttachmentOptimal;
            ImageLayout outside = graphMode ? inside : VkBarriers.RestingLayout(vkDepthTex);
            AttachmentLoadOp depthLoadOp = readOnly ? AttachmentLoadOp.Load : ops.DepthLoad;
            AttachmentStoreOp depthStoreOp = readOnly ? AttachmentStoreOp.Store : ops.DepthStore;
            depthAttachmentRef = new AttachmentReference(attachmentCount, inside);
            attachments[attachmentCount++] = new AttachmentDescription
            {
                Format = vkDepthTex.VkFormat,
                Samples = vkDepthTex.VkSampleCount,
                LoadOp = depthLoadOp,
                StoreOp = depthStoreOp,
                StencilLoadOp = hasStencil ? depthLoadOp : AttachmentLoadOp.DontCare,
                StencilStoreOp = hasStencil ? depthStoreOp : AttachmentStoreOp.DontCare,
                InitialLayout = depthLoadOp == AttachmentLoadOp.Load ? outside : ImageLayout.Undefined,
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
        fbCI.RenderPass = GetRenderPass(FramebufferMode.Resting, RenderPassOps.Load);

        _gd.Vk.CreateFramebuffer(_gd.Device, in fbCI, null, out deviceFramebuffer).CheckResult();
    }

    private protected override void NameChanged(string name) => _gd.SetResourceName(this, name);

    protected override void DestroyNative()
    {
        _gd.Vk.DestroyFramebuffer(_gd.Device, _deviceFramebuffer, null);
        foreach (RenderPass renderPass in _renderPasses)
        {
            if (renderPass.Handle != default)
                _gd.Vk.DestroyRenderPass(_gd.Device, renderPass, null);
        }
        foreach (ImageView view in _attachmentViews)
        {
            _gd.Vk.DestroyImageView(_gd.Device, view, null);
        }

        _gd.Profiler?.Free(AllocBin.Framebuffer, 0);
    }
}
