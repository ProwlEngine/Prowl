namespace Prowl.Graphite;

/// <summary>
/// Resource types for Allocate/Free.
/// </summary>
public enum AllocBin
{
    /// <summary>
    /// Bin for DeviceBuffer.
    /// </summary>
    DeviceBuffer,

    /// <summary>
    /// Bin for Texture.
    /// </summary>
    Texture,

    /// <summary>
    /// Bin for TextureView.
    /// </summary>
    TextureView,

    /// <summary>
    /// Bin for Sampler.
    /// </summary>
    Sampler,

    /// <summary>
    /// Bin for Framebuffer.
    /// </summary>
    Framebuffer,

    /// <summary>
    /// Bin for Vulkan pipelines.
    /// </summary>
    Pipeline,

    /// <summary>
    /// Bin for ShaderProgram.
    /// </summary>
    Shader,

    /// <summary>
    /// Bin for Vulkan resource layouts.
    /// </summary>
    ResourceLayout,

    /// <summary>
    /// Bin for Vulkan descriptor sets.
    /// </summary>
    ResourceSet,

    /// <summary>
    /// Bin for CommandBuffer.
    /// </summary>
    CommandBuffer
}

/// <summary>
/// Buffer transfer ops for Record.
/// </summary>
public enum BufferOpBin
{
    /// <summary>
    /// Bin for all Map ops.
    /// </summary>
    Map,

    /// <summary>
    /// Bin for all Unmap ops.
    /// </summary>
    Unmap,

    /// <summary>
    /// Bin for all UpdateBuffer ops.
    /// </summary>
    Update,

    /// <summary>
    /// Bin for all CopyBuffer ops.
    /// </summary>
    Copy
}

/// <summary>
/// Swapchain events for RecordSwap.
/// </summary>
public enum SwapBin
{
    /// <summary>
    /// A present event, e.g. SwapBuffers.
    /// </summary>
    Present,

    /// <summary>
    /// A resize event, e.g. Swapchain.Resize.
    /// </summary>
    Resize,

    /// <summary>
    /// An acquire event. Mostly matters for Vulkan's multiple present modes.
    /// </summary>
    Acquire
}

/// <summary>
/// Buffer roles for AllocateMemory/FreeMemory, tracks resident bytes per usage.
/// </summary>
public enum BufferRoleBin
{
    /// <summary>
    /// Bin for VertexBuffer usage.
    /// </summary>
    Vertex,

    /// <summary>
    /// Bin for IndexBuffer usage.
    /// </summary>
    Index,

    /// <summary>
    /// Bin for UniformBuffer usage.
    /// </summary>
    Uniform,

    /// <summary>
    /// Bin for StructuredBufferReadOnly usage.
    /// </summary>
    StructuredReadOnly,

    /// <summary>
    /// Bin for StructuredBufferReadWrite usage.
    /// </summary>
    StructuredReadWrite,

    /// <summary>
    /// Bin for IndirectBuffer usage.
    /// </summary>
    Indirect,

    /// <summary>
    /// Bin for Staging usage.
    /// </summary>
    Staging,

    /// <summary>
    /// Bin for Dynamic usage.
    /// </summary>
    Dynamic
}
