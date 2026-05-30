namespace Prowl.Veldrid;

/// <summary>
/// A device object responsible for the creation of graphics resources.
/// </summary>
public abstract partial class ResourceFactory
{
    /// <summary></summary>
    /// <param name="features"></param>
    protected ResourceFactory(GraphicsDeviceFeatures features)
    {
        Features = features;
    }

    /// <summary>
    /// Gets the <see cref="GraphicsBackend"/> of this instance.
    /// </summary>
    public abstract GraphicsBackend BackendType { get; }

    /// <summary>
    /// Gets the <see cref="GraphicsDeviceFeatures"/> this instance was created with.
    /// </summary>
    public GraphicsDeviceFeatures Features { get; }

    /// <summary>
    /// Creates a new <see cref="Framebuffer"/>.
    /// </summary>
    /// <param name="description">The desired properties of the created object.</param>
    /// <returns>A new <see cref="Framebuffer"/>.</returns>
    public Framebuffer CreateFramebuffer(FramebufferDescription description) => CreateFramebuffer(ref description);
    /// <summary>
    /// Creates a new <see cref="Framebuffer"/>.
    /// </summary>
    /// <param name="description">The desired properties of the created object.</param>
    /// <returns>A new <see cref="Framebuffer"/>.</returns>
    public abstract Framebuffer CreateFramebuffer(ref FramebufferDescription description);

    /// <summary>
    /// Creates a new <see cref="Texture"/>.
    /// </summary>
    /// <param name="description">The desired properties of the created object.</param>
    /// <returns>A new <see cref="Texture"/>.</returns>
    public Texture CreateTexture(TextureDescription description) => CreateTexture(ref description);
    /// <summary>
    /// Creates a new <see cref="Texture"/>.
    /// </summary>
    /// <param name="description">The desired properties of the created object.</param>
    /// <returns>A new <see cref="Texture"/>.</returns>
    public Texture CreateTexture(ref TextureDescription description)
    {
        CreateTexture_CheckDescription(ref description);
        return CreateTextureCore(ref description);
    }

    /// <summary>
    /// Creates a new <see cref="Texture"/> from an existing native texture.
    /// </summary>
    /// <param name="nativeTexture">A backend-specific handle identifying an existing native texture. See remarks.</param>
    /// <param name="description">The properties of the existing Texture.</param>
    /// <returns>A new <see cref="Texture"/> wrapping the existing native texture.</returns>
    /// <remarks>
    /// The nativeTexture parameter is backend-specific, and the type of data passed in depends on which graphics API is
    /// being used.
    /// When using the Vulkan backend, nativeTexture must be a valid VkImage handle.
    /// When using the D3D11 backend, nativeTexture must be a valid pointer to an ID3D11Texture1D, ID3D11Texture2D, or
    /// ID3D11Texture3D.
    /// When using the OpenGL backend, nativeTexture must be a valid OpenGL texture name.
    /// The properties of the Texture will be determined from the <see cref="TextureDescription"/> passed in. These
    /// properties must match the true properties of the existing native texture.
    /// </remarks>
    public Texture CreateTexture(ulong nativeTexture, TextureDescription description)
        => CreateTextureCore(nativeTexture, ref description);

    /// <summary>
    /// Creates a new <see cref="Texture"/> from an existing native texture.
    /// </summary>
    /// <param name="nativeTexture">A backend-specific handle identifying an existing native texture. See remarks.</param>
    /// <param name="description">The properties of the existing Texture.</param>
    /// <returns>A new <see cref="Texture"/> wrapping the existing native texture.</returns>
    /// <remarks>
    /// The nativeTexture parameter is backend-specific, and the type of data passed in depends on which graphics API is
    /// being used.
    /// When using the Vulkan backend, nativeTexture must be a valid VkImage handle.
    /// When using the D3D11 backend, nativeTexture must be a valid pointer to an ID3D11Texture1D, ID3D11Texture2D, or
    /// ID3D11Texture3D.
    /// When using the OpenGL backend, nativeTexture must be a valid OpenGL texture name.
    /// The properties of the Texture will be determined from the <see cref="TextureDescription"/> passed in. These
    /// properties must match the true properties of the existing native texture.
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
    /// Creates a new <see cref="TextureView"/>.
    /// </summary>
    /// <param name="target">The target <see cref="Texture"/> used in the new view.</param>
    /// <returns>A new <see cref="TextureView"/>.</returns>
    public TextureView CreateTextureView(Texture target) => CreateTextureView(new TextureViewDescription(target));
    /// <summary>
    /// Creates a new <see cref="TextureView"/>.
    /// </summary>
    /// <param name="description">The desired properties of the created object.</param>
    /// <returns>A new <see cref="TextureView"/>.</returns>
    public TextureView CreateTextureView(TextureViewDescription description) => CreateTextureView(ref description);
    /// <summary>
    /// Creates a new <see cref="TextureView"/>.
    /// </summary>
    /// <param name="description">The desired properties of the created object.</param>
    /// <returns>A new <see cref="TextureView"/>.</returns>
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
    /// Creates a new <see cref="DeviceBuffer"/>.
    /// </summary>
    /// <param name="description">The desired properties of the created object.</param>
    /// <returns>A new <see cref="DeviceBuffer"/>.</returns>
    public DeviceBuffer CreateBuffer(BufferDescription description) => CreateBuffer(ref description);
    /// <summary>
    /// Creates a new <see cref="DeviceBuffer"/>.
    /// </summary>
    /// <param name="description">The desired properties of the created object.</param>
    /// <returns>A new <see cref="DeviceBuffer"/>.</returns>
    public DeviceBuffer CreateBuffer(ref BufferDescription description)
    {
        CreateBuffer_CheckDescription(ref description);
        return CreateBufferCore(ref description);
    }

    /// <summary>
    /// </summary>
    /// <param name="description"></param>
    /// <returns></returns>
    protected abstract DeviceBuffer CreateBufferCore(ref BufferDescription description);

    /// <summary>
    /// Creates a new <see cref="Sampler"/>.
    /// </summary>
    /// <param name="description">The desired properties of the created object.</param>
    /// <returns>A new <see cref="Sampler"/>.</returns>
    public Sampler CreateSampler(SamplerDescription description) => CreateSampler(ref description);
    /// <summary>
    /// Creates a new <see cref="Sampler"/>.
    /// </summary>
    /// <param name="description">The desired properties of the created object.</param>
    /// <returns>A new <see cref="Sampler"/>.</returns>
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
    /// Creates a new <see cref="GraphicsProgram"/>.
    /// </summary>
    /// <param name="description">The desired properties of the created object.</param>
    /// <returns>A new <see cref="GraphicsProgram"/>.</returns>
    public GraphicsProgram CreateGraphicsProgram(ShaderDescription description) => CreateGraphicsProgram(ref description);

    /// <summary>
    /// Creates a new <see cref="GraphicsProgram"/>.
    /// </summary>
    /// <param name="description">The desired properties of the created object.</param>
    /// <returns>A new <see cref="GraphicsProgram"/>.</returns>
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
    /// Creates a new <see cref="ComputeProgram"/>.
    /// </summary>
    /// <param name="description">The desired properties of the created object.</param>
    /// <returns>A new <see cref="ComputeProgram"/>.</returns>
    public ComputeProgram CreateComputeProgram(ComputeDescription description) => CreateComputeProgram(ref description);

    /// <summary>
    /// Creates a new <see cref="ComputeProgram"/>.
    /// </summary>
    /// <param name="description">The desired properties of the created object.</param>
    /// <returns>A new <see cref="ComputeProgram"/>.</returns>
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
    /// Creates a new <see cref="CommandBuffer"/>.
    /// </summary>
    /// <returns>A new <see cref="CommandBuffer"/>.</returns>
    public CommandBuffer CreateCommandBuffer() => CreateCommandBuffer(new CommandBufferDescription());
    /// <summary>
    /// Creates a new <see cref="CommandBuffer"/>.
    /// </summary>
    /// <param name="description">The desired properties of the created object.</param>
    /// <returns>A new <see cref="CommandBuffer"/>.</returns>
    public CommandBuffer CreateCommandBuffer(CommandBufferDescription description) => CreateCommandBuffer(ref description);
    /// <summary>
    /// Creates a new <see cref="CommandBuffer"/>.
    /// </summary>
    /// <param name="description">The desired properties of the created object.</param>
    /// <returns>A new <see cref="CommandBuffer"/>.</returns>
    public abstract CommandBuffer CreateCommandBuffer(ref CommandBufferDescription description);

    /// <summary>
    /// Creates a new <see cref="Fence"/> in the given state.
    /// </summary>
    /// <param name="signaled">A value indicating whether the Fence should be in the signaled state when created.</param>
    /// <returns>A new <see cref="Fence"/>.</returns>
    public abstract Fence CreateFence(bool signaled);

    /// <summary>
    /// Creates a new <see cref="Swapchain"/>.
    /// </summary>
    /// <param name="description">The desired properties of the created object.</param>
    /// <returns>A new <see cref="Swapchain"/>.</returns>
    public Swapchain CreateSwapchain(SwapchainDescription description) => CreateSwapchain(ref description);
    /// <summary>
    /// Creates a new <see cref="Swapchain"/>.
    /// </summary>
    /// <param name="description">The desired properties of the created object.</param>
    /// <returns>A new <see cref="Swapchain"/>.</returns>
    public abstract Swapchain CreateSwapchain(ref SwapchainDescription description);
}
