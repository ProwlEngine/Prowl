namespace Prowl.Graphite;

/// <summary>
/// Resource types tracked by the allocation bins. Used for the per-frame
/// <see cref="ProfileSnapshot.Allocated"/> / <see cref="ProfileSnapshot.Freed"/> flows
/// and the persistent <see cref="ProfileSnapshot.Live"/> gauge.
/// </summary>
public enum AllocBin
{
    /// <summary>
    /// Bin index for <see cref="Graphite.DeviceBuffer"/>.
    /// </summary>
    DeviceBuffer,

    /// <summary>
    /// Bin index for <see cref="Graphite.Texture"/>.
    /// </summary>
    Texture,

    /// <summary>
    /// Bin index for <see cref="Graphite.TextureView"/>.
    /// </summary>
    TextureView,

    /// <summary>
    /// Bin index for <see cref="Graphite.Sampler"/>.
    /// </summary>
    Sampler,

    /// <summary>
    /// Bin index for <see cref="Graphite.Framebuffer"/>.
    /// </summary>
    Framebuffer,

    /// <summary>
    /// Bin index for Vulkan pipelines; unused on D3D11, so counts will be 0
    /// </summary>
    Pipeline,

    /// <summary>
    /// Bin index for <see cref="ShaderProgram"/>.
    /// </summary>
    Shader,

    /// <summary>
    /// Bin index for Vulkan resource layouts; unused on D3D11, so counts will be 0
    /// </summary>
    ResourceLayout,

    /// <summary>
    /// Bin index Vulkan descriptor sets; unused on D3D11, so counts will be 0
    /// </summary>
    ResourceSet,

    /// <summary>
    /// Bin index for <see cref="Graphite.CommandBuffer"/>.
    /// </summary>
    CommandBuffer
}

/// <summary>
/// Buffer data-transfer operations tracked per frame by <see cref="ProfileSnapshot.BufferOps"/>.
/// </summary>
public enum BufferOpBin
{
    /// <summary>
    /// Bin index for all <see cref="GraphicsDevice.Map(MappableResource, MapMode)"/> operations.
    /// </summary>
    Map,

    /// <summary>
    /// Bin index for all <see cref="GraphicsDevice.Unmap(MappableResource)"/> operations.
    /// </summary>
    Unmap,

    /// <summary>
    /// Bin index for all <see cref="CommandBuffer.UpdateBuffer"/> operations.
    /// </summary>
    Update,

    /// <summary>
    /// Bin index for all <see cref="CommandBuffer.CopyBuffer"/> operations.
    /// </summary>
    Copy
}

/// <summary>
/// Swapchain events tracked per frame by <see cref="ProfileSnapshot.Swaps"/>.
/// </summary>
public enum SwapBin
{
    /// <summary>
    /// A swapchain present event, such as <see cref="GraphicsDevice.SwapBuffers()"/>
    /// </summary>
    Present,

    /// <summary>
    /// A swapchain resize event, such as <see cref="Swapchain.Resize(uint, uint)"/>
    /// </summary>
    Resize,

    /// <summary>
    /// A swapchain acquire event. Mostly relevant for multiple Vulkan present mode types.
    /// </summary>
    Acquire
}

/// <summary>
/// Buffer roles tracked by the persistent <see cref="ProfileSnapshot.BufferMem"/> gauge,
/// reporting resident bytes per usage.
/// </summary>
public enum BufferRoleBin
{
    /// <summary>
    /// Bin index for buffers created with <see cref="BufferUsage.VertexBuffer"/>
    /// </summary>
    Vertex,

    /// <summary>
    /// Bin index for buffers created with <see cref="BufferUsage.IndexBuffer"/>
    /// </summary>
    Index,

    /// <summary>
    /// Bin index for buffers created with <see cref="BufferUsage.UniformBuffer"/>
    /// </summary>
    Uniform,

    /// <summary>
    /// Bin index for buffers created with <see cref="BufferUsage.StructuredBufferReadOnly"/>
    /// </summary>
    StructuredReadOnly,

    /// <summary>
    /// Bin index for buffers created with <see cref="BufferUsage.StructuredBufferReadWrite"/>
    /// </summary>
    StructuredReadWrite,

    /// <summary>
    /// Bin index for buffers created with <see cref="BufferUsage.IndirectBuffer"/>
    /// </summary>
    Indirect,

    /// <summary>
    /// Bin index for buffers created with <see cref="BufferUsage.Staging"/>
    /// </summary>
    Staging,

    /// <summary>
    /// Bin index for buffers created with <see cref="BufferUsage.Dynamic"/>
    /// </summary>
    Dynamic
}
