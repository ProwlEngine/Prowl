namespace Prowl.Graphite;

/// <summary>
/// Makes graphics resources.
/// </summary>
public abstract partial class ResourceFactory
{
    protected ResourceFactory(GraphicsDevice device, GraphicsDeviceFeatures features)
    {
        Device = device;
        Features = features;
    }

    /// <summary>
    /// Backend used.
    /// </summary>
    public abstract GraphicsBackend BackendType { get; }

    /// <summary>
    /// The owning device.
    /// </summary>
    public GraphicsDevice Device { get; }

    /// <summary>
    /// Features it was made with.
    /// </summary>
    public GraphicsDeviceFeatures Features { get; }

    /// <summary>
    /// Makes a framebuffer.
    /// </summary>
    /// <param name="description">Wanted props.</param>
    /// <returns>New framebuffer.</returns>
    public abstract Framebuffer CreateFramebuffer(in FramebufferDescription description);

    /// <summary>
    /// Makes a render texture: attachments, maybe depth, plus the framebuffer wrapper.
    /// </summary>
    /// <param name="description">Wanted props.</param>
    /// <returns>New render texture.</returns>
    public virtual RenderTexture CreateRenderTexture(in RenderTextureDescription description) => new(Device, description);

    /// <summary>
    /// Makes a texture.
    /// </summary>
    /// <param name="description">Wanted props.</param>
    /// <returns>New texture.</returns>
    public Texture CreateTexture(in TextureDescription description)
    {
        CreateTexture_CheckDescription(description);
        return CreateTextureCore(description);
    }

    /// <summary>
    /// Wraps an existing native texture. No validation, no Core counterpart.
    /// </summary>
    /// <param name="nativeTexture">Backend-specific handle.</param>
    /// <param name="description">Its real properties.</param>
    /// <returns>New texture wrapping it.</returns>
    /// <remarks>
    /// Handle format depends on backend. Vulkan wants a valid VkImage. Description must match reality.
    /// </remarks>
    public abstract Texture CreateTexture(ulong nativeTexture, in TextureDescription description);

    /// <summary>
    /// Wraps a native texture owned and synchronized elsewhere, such as an OpenXR swapchain image. Graphite leaves its
    /// contents alone when wrapping, expects it in <paramref name="restingState"/> whenever Graphite uses it and returns it
    /// there afterwards, and never frees it. Vulkan wants a valid VkImage. Description must match reality.
    /// </summary>
    /// <param name="nativeTexture">Backend-specific handle.</param>
    /// <param name="description">Its real properties.</param>
    /// <param name="restingState">The state it is in between uses, such as Attachment for an OpenXR colour swapchain image.</param>
    /// <returns>New texture wrapping it.</returns>
    public virtual Texture CreateTexture(ulong nativeTexture, in TextureDescription description, TextureState restingState)
        => throw new System.NotSupportedException($"{GetType().Name} cannot wrap external textures.");

    protected abstract Texture CreateTextureCore(in TextureDescription description);

    /// <summary>
    /// Makes a texture view.
    /// </summary>
    /// <param name="target">Texture to view.</param>
    /// <returns>New texture view.</returns>
    public TextureView CreateTextureView(Texture target) => CreateTextureView(new TextureViewDescription(target));
    /// <summary>
    /// Makes a texture view.
    /// </summary>
    /// <param name="description">Wanted props.</param>
    /// <returns>New texture view.</returns>
    public TextureView CreateTextureView(in TextureViewDescription description)
    {
        CreateTextureView_CheckDescription(description);

        return CreateTextureViewCore(description);
    }

    protected abstract TextureView CreateTextureViewCore(in TextureViewDescription description);

    /// <summary>
    /// Makes a buffer.
    /// </summary>
    /// <param name="description">Wanted props.</param>
    /// <returns>New buffer.</returns>
    public DeviceBuffer CreateBuffer(in BufferDescription description)
    {
        CreateBuffer_CheckDescription(description);
        return CreateBufferCore(description);
    }

    protected abstract DeviceBuffer CreateBufferCore(in BufferDescription description);

    /// <summary>
    /// Makes a sampler.
    /// </summary>
    /// <param name="description">Wanted props.</param>
    /// <returns>New sampler.</returns>
    public Sampler CreateSampler(in SamplerDescription description)
    {
        CreateSampler_CheckDescription(description);

        return CreateSamplerCore(description);
    }

    protected abstract Sampler CreateSamplerCore(in SamplerDescription description);


    /// <summary>
    /// Makes a descriptor table for a set whose layout has <see cref="ResourceLayoutDescription.Table"/> set.
    /// </summary>
    /// <param name="layout">The set's layout, the same one the programs that bind the table declare.</param>
    /// <returns>New, empty table.</returns>
    public DescriptorTable CreateDescriptorTable(in ResourceLayoutDescription layout)
    {
        if (!layout.Table)
            throw new RenderException("A descriptor table needs a layout with ResourceLayoutDescription.Table set.");

        return CreateDescriptorTableCore(layout);
    }

    protected abstract DescriptorTable CreateDescriptorTableCore(in ResourceLayoutDescription layout);


    /// <summary>
    /// Makes a graphics program.
    /// </summary>
    /// <param name="description">Wanted props.</param>
    /// <returns>New graphics program.</returns>
    public GraphicsProgram CreateGraphicsProgram(in ShaderDescription description)
    {
        CreateGraphicsProgram_ValidatePipelineStateArrays(description);
        CreateGraphicsProgram_CheckDescription(description);
        return CreateGraphicsProgramCore(description);
    }

    private static void CreateGraphicsProgram_ValidatePipelineStateArrays(in ShaderDescription description)
    {
        if (description.BlendState.AttachmentStates == null)
        {
            throw new RenderException(
                $"{nameof(ShaderDescription)}.{nameof(ShaderDescription.BlendState)}.{nameof(BlendStateDescription.AttachmentStates)} must not be null. Use an empty array if the program has no color attachments.");
        }
    }

    protected abstract GraphicsProgram CreateGraphicsProgramCore(in ShaderDescription description);


    /// <summary>
    /// Makes a compute program.
    /// </summary>
    /// <param name="description">Wanted props.</param>
    /// <returns>New compute program.</returns>
    public ComputeProgram CreateComputeProgram(in ComputeDescription description)
    {
        CreateComputeProgram_CheckDescription(description);
        return CreateComputeProgramCore(description);
    }

    protected abstract ComputeProgram CreateComputeProgramCore(in ComputeDescription description);

    /// <summary>
    /// Makes a swapchain.
    /// </summary>
    /// <param name="description">Wanted props.</param>
    /// <returns>New swapchain.</returns>
    public abstract Swapchain CreateSwapchain(in SwapchainDescription description);
}
