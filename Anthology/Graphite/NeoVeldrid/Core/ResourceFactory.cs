using System;

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
    /// Creates a new <see cref="Pipeline"/>.
    /// </summary>
    /// <param name="description">The desired properties of the created object.</param>
    /// <returns>A new <see cref="Pipeline"/>.</returns>
    public Pipeline CreateGraphicsPipeline(GraphicsPipelineDescription description) => CreateGraphicsPipeline(ref description);
    /// <summary>
    /// Creates a new <see cref="Pipeline"/> object.
    /// </summary>
    /// <param name="description">The desired properties of the created object.</param>
    /// <returns>A new <see cref="Pipeline"/> which, when bound to a CommandList, is used to dispatch draw commands.</returns>
    public Pipeline CreateGraphicsPipeline(ref GraphicsPipelineDescription description)
    {
        CreateGraphicsPipeline_CheckDescription(ref description);
        return CreateGraphicsPipelineCore(ref description);
    }

    /// <summary></summary>
    /// <param name="description"></param>
    /// <returns></returns>
    protected abstract Pipeline CreateGraphicsPipelineCore(ref GraphicsPipelineDescription description);

    /// <summary>
    /// Creates a new compute <see cref="Pipeline"/> object.
    /// </summary>
    /// <param name="description">The desirede properties of the created object.</param>
    /// <returns>A new <see cref="Pipeline"/> which, when bound to a CommandList, is used to dispatch compute commands.</returns>
    public Pipeline CreateComputePipeline(ComputePipelineDescription description) => CreateComputePipeline(ref description);

    /// <summary>
    /// Creates a new compute <see cref="Pipeline"/> object.
    /// </summary>
    /// <param name="description">The desirede properties of the created object.</param>
    /// <returns>A new <see cref="Pipeline"/> which, when bound to a CommandList, is used to dispatch compute commands.</returns>
    public abstract Pipeline CreateComputePipeline(ref ComputePipelineDescription description);

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
    /// Creates a new <see cref="ShaderProgram"/>.
    /// </summary>
    /// <param name="description">The desired properties of the created object.</param>
    /// <returns>A new <see cref="ShaderProgram"/>.</returns>
    public ShaderProgram CreateShader(ShaderDescription description) => CreateShader(ref description);
    /// <summary>
    /// Creates a new <see cref="ShaderProgram"/>.
    /// </summary>
    /// <param name="description">The desired properties of the created object.</param>
    /// <returns>A new <see cref="ShaderProgram"/>.</returns>
    public ShaderProgram CreateShader(ref ShaderDescription description)
    {
        CreateShader_CheckDescription(ref description);
        return CreateShaderCore(ref description);
    }

    /// <summary></summary>
    /// <param name="description"></param>
    /// <returns></returns>
    protected abstract ShaderProgram CreateShaderCore(ref ShaderDescription description);

    /// <summary>
    /// Creates a new <see cref="CommandBuffer"/>.
    /// </summary>
    /// <returns>A new <see cref="CommandBuffer"/>.</returns>
    public CommandBuffer CreateCommandList() => CreateCommandList(new CommandBufferDescription());
    /// <summary>
    /// Creates a new <see cref="CommandBuffer"/>.
    /// </summary>
    /// <param name="description">The desired properties of the created object.</param>
    /// <returns>A new <see cref="CommandBuffer"/>.</returns>
    public CommandBuffer CreateCommandList(CommandBufferDescription description) => CreateCommandList(ref description);
    /// <summary>
    /// Creates a new <see cref="CommandBuffer"/>.
    /// </summary>
    /// <param name="description">The desired properties of the created object.</param>
    /// <returns>A new <see cref="CommandBuffer"/>.</returns>
    public abstract CommandBuffer CreateCommandList(ref CommandBufferDescription description);

    /// <summary>
    /// Creates a new <see cref="ResourceLayout"/>.
    /// </summary>
    /// <param name="description">The desired properties of the created object.</param>
    /// <returns>A new <see cref="ResourceLayout"/>.</returns>
    public ResourceLayout CreateResourceLayout(ResourceLayoutDescription description) => CreateResourceLayout(ref description);
    /// <summary>
    /// Creates a new <see cref="ResourceLayout"/>.
    /// </summary>
    /// <param name="description">The desired properties of the created object.</param>
    /// <returns>A new <see cref="ResourceLayout"/>.</returns>
    public abstract ResourceLayout CreateResourceLayout(ref ResourceLayoutDescription description);

    /// <summary>
    /// Creates a new <see cref="ResourceSet"/>.
    /// </summary>
    /// <param name="description">The desired properties of the created object.</param>
    /// <returns>A new <see cref="ResourceSet"/>.</returns>
    public ResourceSet CreateResourceSet(ResourceSetDescription description) => CreateResourceSet(ref description);
    /// <summary>
    /// Creates a new <see cref="ResourceSet"/>.
    /// </summary>
    /// <param name="description">The desired properties of the created object.</param>
    /// <returns>A new <see cref="ResourceSet"/>.</returns>
    public abstract ResourceSet CreateResourceSet(ref ResourceSetDescription description);

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
