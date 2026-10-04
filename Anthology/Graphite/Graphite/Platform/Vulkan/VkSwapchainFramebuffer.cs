using System;
using System.Collections.Generic;
using System.Linq;

using Silk.NET.Vulkan;

using VkFramebufferHandle = Silk.NET.Vulkan.Framebuffer;

namespace Prowl.Graphite.Vk;

internal unsafe class VkSwapchainFramebuffer : VkFramebufferBase
{
    private readonly VkGraphicsDevice _gd;
    private readonly VkSwapchain _swapchain;
    private readonly SurfaceKHR _surface;
    private readonly PixelFormat? _depthFormat;
    private uint _currentImageIndex;

    private VkFramebuffer[] _scFramebuffers = [];
    private Image[] _scImages = [];
    private Format _scImageFormat;
    private Extent2D _scExtent;
    private FramebufferAttachment[][] _scColorTextures = [];

    private FramebufferAttachment? _depthAttachment;
    private OutputDescription _outputDescription;

    public override VkFramebufferHandle CurrentFramebuffer => _scFramebuffers[(int)_currentImageIndex].CurrentFramebuffer;

    public override RenderPass GetRenderPass(FramebufferMode mode, RenderPassOps ops) => _scFramebuffers[0].GetRenderPass(mode, ops);

    public override IReadOnlyList<FramebufferAttachment> ColorTargets => _scColorTextures[(int)_currentImageIndex];

    public override FramebufferAttachment? DepthTarget => _depthAttachment;

    public override uint RenderableWidth => _scExtent.Width;
    public override uint RenderableHeight => _scExtent.Height;

    // The swapchain framebuffer always reports its physical (pixel) extent. The logical size requested
    // at creation is only a window hint; render targets, viewports and scissors operate in pixels.
    public override uint Width => _scExtent.Width;
    public override uint Height => _scExtent.Height;

    public uint ImageIndex => _currentImageIndex;

    public int ImageCount => _scImages.Length;

    public override OutputDescription OutputDescription => _outputDescription;

    public override uint AttachmentCount { get; }

    public VkSwapchain Swapchain => _swapchain;

    public VkSwapchainFramebuffer(
        VkGraphicsDevice gd,
        VkSwapchain swapchain,
        SurfaceKHR surface,
        uint width,
        uint height,
        PixelFormat? depthFormat)
        : base()
    {
        _gd = gd;
        _swapchain = swapchain;
        _surface = surface;
        _depthFormat = depthFormat;

        AttachmentCount = depthFormat.HasValue ? 2u : 1u; // 1 Color + 1 Depth
    }

    internal void SetImageIndex(uint index)
    {
        _currentImageIndex = index;
    }

    internal void SetNewSwapchain(
        SwapchainKHR deviceSwapchain,
        SurfaceFormatKHR surfaceFormat,
        Extent2D swapchainExtent)
    {
        uint scImageCount = 0;
        _gd.KhrSwapchain.GetSwapchainImages(_gd.Device, deviceSwapchain, ref scImageCount, null).CheckResult();
        if (_scImages.Length < scImageCount)
        {
            _scImages = new Image[(int)scImageCount];
        }
        _gd.KhrSwapchain.GetSwapchainImages(_gd.Device, deviceSwapchain, ref scImageCount, out _scImages[0]).CheckResult();

        _scImageFormat = surfaceFormat.Format;
        _scExtent = swapchainExtent;

        CreateDepthTexture();
        CreateFramebuffers();

        _outputDescription = OutputDescription.CreateFromFramebuffer(this);
    }

    private void DestroySwapchainFramebuffers()
    {
        for (int i = 0; i < _scFramebuffers.Length; i++)
        {
            _scFramebuffers[i]?.Dispose();
        }
        Array.Clear(_scFramebuffers, 0, _scFramebuffers.Length);
    }

    private void CreateDepthTexture()
    {
        if (_depthFormat.HasValue)
        {
            _depthAttachment?.Target.Dispose();
            VkTexture depthTexture = (VkTexture)_gd.ResourceFactory.CreateTexture(TextureDescription.Texture2D(
                Math.Max(1, _scExtent.Width),
                Math.Max(1, _scExtent.Height),
                1,
                1,
                _depthFormat.Value,
                TextureUsage.DepthStencil));
            _depthAttachment = new FramebufferAttachment(depthTexture, 0);
        }
    }

    private void CreateFramebuffers()
    {
        DestroySwapchainFramebuffers();

        Util.EnsureArrayMinimumSize(ref _scFramebuffers, (uint)_scImages.Length);
        Util.EnsureArrayMinimumSize(ref _scColorTextures, (uint)_scImages.Length);
        for (uint i = 0; i < _scImages.Length; i++)
        {
            VkTexture colorTex = new(
                _gd,
                Math.Max(1, _scExtent.Width),
                Math.Max(1, _scExtent.Height),
                1,
                1,
                _scImageFormat,
                TextureUsage.RenderTarget,
                TextureSampleCount.Count1,
                _scImages[i]);
            FramebufferDescription desc = new(_depthAttachment?.Target, colorTex);
            VkFramebuffer fb = new(_gd, ref desc);
            _scFramebuffers[i] = fb;
            _scColorTextures[i] = [new FramebufferAttachment(colorTex, 0)];
        }
    }

    private protected override VkGraphicsDevice OwnerDevice => _gd;

    private protected override void NameChanged(string name) => _gd.SetResourceName(this, name);

    protected override void DestroyNative()
    {
        _depthAttachment?.Target.Dispose();
        DestroySwapchainFramebuffers();
    }
}
