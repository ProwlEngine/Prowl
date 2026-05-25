using Silk.NET.Vulkan;

namespace Prowl.Veldrid.Vk;

internal class VkResourceFactory : ResourceFactory
{
    private readonly VkGraphicsDevice _gd;
    private readonly Device _device;

    public VkResourceFactory(VkGraphicsDevice vkGraphicsDevice)
        : base(vkGraphicsDevice.Features)
    {
        _gd = vkGraphicsDevice;
        _device = vkGraphicsDevice.Device;
    }

    public override GraphicsBackend BackendType => GraphicsBackend.Vulkan;

    public override CommandBuffer CreateCommandList(ref CommandBufferDescription description)
    {
        return new VkCommandBuffer(_gd, ref description);
    }

    public override Framebuffer CreateFramebuffer(ref FramebufferDescription description)
    {
        return new VkFramebuffer(_gd, ref description, false);
    }

    protected override Pipeline CreateGraphicsPipelineCore(ref GraphicsPipelineDescription description)
    {
        return new VkPipeline(_gd, this, ref description);
    }

    public override Pipeline CreateComputePipeline(ref ComputePipelineDescription description)
    {
        return new VkPipeline(_gd, this, ref description);
    }

    public override ResourceLayout CreateResourceLayout(ref ResourceLayoutDescription description)
    {
        return new VkResourceLayout(_gd, ref description);
    }

    public override ResourceSet CreateResourceSet(ref ResourceSetDescription description)
    {
        ValidationHelpers.ValidateResourceSet(_gd, ref description);
        return new VkResourceSet(_gd, ref description);
    }

    protected override Sampler CreateSamplerCore(ref SamplerDescription description)
    {
        return new VkSampler(_gd, ref description);
    }

    protected override ShaderProgram CreateShaderProgramCore(ref ShaderDescription description)
    {
        return new VkShaderProgram(_gd, ref description);
    }

    protected override ComputeProgram CreateComputeProgramCore(ref ComputeDescription description)
    {
        return new VkComputeProgram(_gd, ref description);
    }

    protected override Texture CreateTextureCore(ref TextureDescription description)
    {
        return new VkTexture(_gd, ref description);
    }

    protected override Texture CreateTextureCore(ulong nativeTexture, ref TextureDescription description)
    {
        return new VkTexture(
            _gd,
            description.Width, description.Height,
            description.MipLevels, description.ArrayLayers,
            VkFormats.VdToVkPixelFormat(description.Format, (description.Usage & TextureUsage.DepthStencil) != 0),
            description.Usage,
            description.SampleCount,
            new Image(nativeTexture));
    }

    protected override TextureView CreateTextureViewCore(ref TextureViewDescription description)
    {
        return new VkTextureView(_gd, ref description);
    }

    protected override DeviceBuffer CreateBufferCore(ref BufferDescription description)
    {
        return new VkBuffer(_gd, description.SizeInBytes, description.Usage);
    }

    public override Fence CreateFence(bool signaled)
    {
        return new VkFence(_gd, signaled);
    }

    public override Swapchain CreateSwapchain(ref SwapchainDescription description)
    {
        return new VkSwapchain(_gd, ref description);
    }
}
