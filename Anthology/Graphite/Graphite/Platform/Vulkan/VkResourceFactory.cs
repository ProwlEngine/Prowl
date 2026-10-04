using Silk.NET.Vulkan;

namespace Prowl.Graphite.Vk;

internal class VkResourceFactory : ResourceFactory
{
    private readonly VkGraphicsDevice _gd;

    public VkResourceFactory(VkGraphicsDevice vkGraphicsDevice)
        : base(vkGraphicsDevice, vkGraphicsDevice.Features)
    {
        _gd = vkGraphicsDevice;
    }

    public override GraphicsBackend BackendType => GraphicsBackend.Vulkan;

    public override Framebuffer CreateFramebuffer(in FramebufferDescription description)
    {
        return new VkFramebuffer(_gd, description);
    }

    protected override Sampler CreateSamplerCore(in SamplerDescription description)
    {
        return new VkSampler(_gd, description);
    }

    protected override GraphicsProgram CreateGraphicsProgramCore(in ShaderDescription description)
    {
        return new VkGraphicsProgram(_gd, description);
    }

    protected override ComputeProgram CreateComputeProgramCore(in ComputeDescription description)
    {
        return new VkComputeProgram(_gd, description);
    }

    protected override Texture CreateTextureCore(in TextureDescription description)
    {
        return new VkTexture(_gd, description);
    }

    public override Texture CreateTexture(ulong nativeTexture, in TextureDescription description)
    {
        return new VkTexture(
            _gd,
            description.Width, description.Height,
            description.MipLevels, description.ArrayLayers,
            VkFormats.ToVkPixelFormat(description.Format, (description.Usage & TextureUsage.DepthStencil) != 0),
            description.Usage,
            description.SampleCount,
            new Image(nativeTexture));
    }

    protected override TextureView CreateTextureViewCore(in TextureViewDescription description)
    {
        return new VkTextureView(_gd, description);
    }

    protected override DeviceBuffer CreateBufferCore(in BufferDescription description)
    {
        return new VkBuffer(_gd, description);
    }

    public override Swapchain CreateSwapchain(in SwapchainDescription description)
    {
        return new VkSwapchain(_gd, description);
    }
}
