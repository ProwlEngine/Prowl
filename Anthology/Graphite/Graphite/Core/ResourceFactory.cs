namespace Prowl.Graphite;

/// <summary>
/// Creates graphics resources.
/// </summary>
public abstract partial class ResourceFactory
{
    /// <summary></summary>
    /// <param name="device">Owning device.</param>
    /// <param name="features"></param>
    protected ResourceFactory(GraphicsDevice device, GraphicsDeviceFeatures features)
    {
        Device = device;
        Features = features;
    }

    /// <summary>
    /// Backend type.
    /// </summary>
    public abstract GraphicsBackend BackendType { get; }

    /// <summary>
    /// Owning device.
    /// </summary>
    public GraphicsDevice Device { get; }

    /// <summary>
    /// Features this was created with.
    /// </summary>
    public GraphicsDeviceFeatures Features { get; }

    /// <summary>
    /// Creates a framebuffer.
    /// </summary>
    /// <param name="description">Desired properties.</param>
    /// <returns>New framebuffer.</returns>
    public Framebuffer CreateFramebuffer(FramebufferDescription description) => CreateFramebuffer(ref description);
    /// <summary>
    /// Creates a framebuffer.
    /// </summary>
    /// <param name="description">Desired properties.</param>
    /// <returns>New framebuffer.</returns>
    public abstract Framebuffer CreateFramebuffer(ref FramebufferDescription description);

    /// <summary>
    /// Creates a render texture: color attachments, optional depth, and the wrapping framebuffer.
    /// </summary>
    /// <param name="description">Desired properties.</param>
    /// <returns>New render texture.</returns>
    public RenderTexture CreateRenderTexture(in RenderTextureDescription description) => new(Device, description);

    /// <summary>
    /// Creates a texture.
    /// </summary>
    /// <param name="description">Desired properties.</param>
    /// <returns>New texture.</returns>
    public Texture CreateTexture(TextureDescription description) => CreateTexture(ref description);
    /// <summary>
    /// Creates a texture.
    /// </summary>
    /// <param name="description">Desired properties.</param>
    /// <returns>New texture.</returns>
    public Texture CreateTexture(ref TextureDescription description)
    {
        CreateTexture_CheckDescription(ref description);
        return CreateTextureCore(ref description);
    }

    /// <summary>
    /// Wraps an existing native texture.
    /// </summary>
    /// <param name="nativeTexture">Backend-specific handle. See remarks.</param>
    /// <param name="description">Properties of the existing texture.</param>
    /// <returns>New texture wrapping the native one.</returns>
    /// <remarks>
    /// Format depends on backend. Vulkan needs a valid VkImage handle. Description must match the real properties.
    /// </remarks>
    public Texture CreateTexture(ulong nativeTexture, TextureDescription description)
        => CreateTextureCore(nativeTexture, ref description);

    /// <summary>
    /// Wraps an existing native texture.
    /// </summary>
    /// <param name="nativeTexture">Backend-specific handle. See remarks.</param>
    /// <param name="description">Properties of the existing texture.</param>
    /// <returns>New texture wrapping the native one.</returns>
    /// <remarks>
    /// Format depends on backend. Vulkan needs a valid VkImage handle. Description must match the real properties.
    /// </remarks>
    public Texture CreateTexture(ulong nativeTexture, ref TextureDescription description)
        => CreateTextureCore(nativeTexture, ref description);

    /// <summary></summary>
    /// <param name="nativeTexture"></param>
    /// <param name="description"></param>
    /// <returns></returns>
    protected abstract Texture CreateTextureCore(ulong nativeTexture, ref TextureDescription description);

    /// <summary>
    /// </summary>
    /// <param name="description"></param>
    /// <returns></returns>
    protected abstract Texture CreateTextureCore(ref TextureDescription description);

    /// <summary>
    /// Creates a texture view.
    /// </summary>
    /// <param name="target">Texture to view.</param>
    /// <returns>New texture view.</returns>
    public TextureView CreateTextureView(Texture target) => CreateTextureView(new TextureViewDescription(target));
    /// <summary>
    /// Creates a texture view.
    /// </summary>
    /// <param name="description">Desired properties.</param>
    /// <returns>New texture view.</returns>
    public TextureView CreateTextureView(TextureViewDescription description) => CreateTextureView(ref description);
    /// <summary>
    /// Creates a texture view.
    /// </summary>
    /// <param name="description">Desired properties.</param>
    /// <returns>New texture view.</returns>
    public TextureView CreateTextureView(ref TextureViewDescription description)
    {
        CreateTextureView_CheckDescription(ref description);

        return CreateTextureViewCore(ref description);
    }

    /// <summary>
    /// </summary>
    /// <param name="description"></param>
    /// <returns></returns>
    protected abstract TextureView CreateTextureViewCore(ref TextureViewDescription description);

    /// <summary>
    /// Creates a buffer.
    /// </summary>
    /// <param name="description">Desired properties.</param>
    /// <returns>New buffer.</returns>
    public DeviceBuffer CreateBuffer(BufferDescription description) => CreateBuffer(ref description);
    /// <summary>
    /// Creates a buffer.
    /// </summary>
    /// <param name="description">Desired properties.</param>
    /// <returns>New buffer.</returns>
    public DeviceBuffer CreateBuffer(ref BufferDescription description)
    {
        CreateBuffer_CheckDescription(ref description);
        DeviceBuffer buffer = CreateBufferCore(ref description);
        buffer.SetTransientWrites(description.TransientWrites);
        return buffer;
    }

    /// <summary>
    /// </summary>
    /// <param name="description"></param>
    /// <returns></returns>
    protected abstract DeviceBuffer CreateBufferCore(ref BufferDescription description);

    /// <summary>
    /// Creates a sampler.
    /// </summary>
    /// <param name="description">Desired properties.</param>
    /// <returns>New sampler.</returns>
    public Sampler CreateSampler(SamplerDescription description) => CreateSampler(ref description);
    /// <summary>
    /// Creates a sampler.
    /// </summary>
    /// <param name="description">Desired properties.</param>
    /// <returns>New sampler.</returns>
    public Sampler CreateSampler(ref SamplerDescription description)
    {
        CreateSampler_CheckDescription(ref description);

        return CreateSamplerCore(ref description);
    }

    /// <summary></summary>
    /// <param name="description"></param>
    /// <returns></returns>
    protected abstract Sampler CreateSamplerCore(ref SamplerDescription description);

    /// <summary>
    /// Creates a graphics program.
    /// </summary>
    /// <param name="description">Desired properties.</param>
    /// <returns>New graphics program.</returns>
    public GraphicsProgram CreateGraphicsProgram(ShaderDescription description) => CreateGraphicsProgram(ref description);

    /// <summary>
    /// Creates a graphics program.
    /// </summary>
    /// <param name="description">Desired properties.</param>
    /// <returns>New graphics program.</returns>
    public GraphicsProgram CreateGraphicsProgram(ref ShaderDescription description)
    {
        CreateGraphicsProgram_CheckDescription(ref description);
        return CreateGraphicsProgramCore(ref description);
    }

    /// <summary></summary>
    /// <param name="description"></param>
    /// <returns></returns>
    protected abstract GraphicsProgram CreateGraphicsProgramCore(ref ShaderDescription description);

    /// <summary>
    /// Creates a compute program.
    /// </summary>
    /// <param name="description">Desired properties.</param>
    /// <returns>New compute program.</returns>
    public ComputeProgram CreateComputeProgram(ComputeDescription description) => CreateComputeProgram(ref description);

    /// <summary>
    /// Creates a compute program.
    /// </summary>
    /// <param name="description">Desired properties.</param>
    /// <returns>New compute program.</returns>
    public ComputeProgram CreateComputeProgram(ref ComputeDescription description)
    {
        CreateComputeProgram_CheckDescription(ref description);
        return CreateComputeProgramCore(ref description);
    }

    /// <summary></summary>
    /// <param name="description"></param>
    /// <returns></returns>
    protected abstract ComputeProgram CreateComputeProgramCore(ref ComputeDescription description);

    /// <summary>
    /// Creates a command buffer.
    /// </summary>
    /// <returns>New command buffer.</returns>
    public CommandBuffer CreateCommandBuffer() => CreateCommandBuffer(new CommandBufferDescription());
    /// <summary>
    /// Creates a command buffer.
    /// </summary>
    /// <param name="description">Desired properties.</param>
    /// <returns>New command buffer.</returns>
    public CommandBuffer CreateCommandBuffer(CommandBufferDescription description) => CreateCommandBuffer(ref description);
    /// <summary>
    /// Creates a command buffer.
    /// </summary>
    /// <param name="description">Desired properties.</param>
    /// <returns>New command buffer.</returns>
    public abstract CommandBuffer CreateCommandBuffer(ref CommandBufferDescription description);

    /// <summary>
    /// Creates a transfer command buffer for buffer/texture transfers outside the frame system. Throws if the backend doesn't support it.
    /// </summary>
    /// <returns>New transfer command buffer.</returns>
    public virtual TransferCommandBuffer CreateTransferCommandBuffer()
    {
        throw new RenderException($"{GetType().Name} does not support {nameof(CreateTransferCommandBuffer)}.");
    }

    /// <summary>
    /// Creates a fence.
    /// </summary>
    /// <param name="signaled">Start signaled or not.</param>
    /// <returns>New fence.</returns>
    public abstract Fence CreateFence(bool signaled);

    /// <summary>
    /// Creates a swapchain.
    /// </summary>
    /// <param name="description">Desired properties.</param>
    /// <returns>New swapchain.</returns>
    public Swapchain CreateSwapchain(SwapchainDescription description) => CreateSwapchain(ref description);
    /// <summary>
    /// Creates a swapchain.
    /// </summary>
    /// <param name="description">Desired properties.</param>
    /// <returns>New swapchain.</returns>
    public abstract Swapchain CreateSwapchain(ref SwapchainDescription description);
}
