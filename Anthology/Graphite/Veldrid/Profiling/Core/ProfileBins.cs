namespace Prowl.Veldrid;

/// <summary>
/// Resource types tracked by the allocation bins. Used for the per-frame
/// <see cref="ProfileSnapshot.Allocated"/> / <see cref="ProfileSnapshot.Freed"/> flows
/// and the persistent <see cref="ProfileSnapshot.Live"/> gauge.
/// </summary>
public enum AllocBin
{
    DeviceBuffer,
    Texture,
    TextureView,
    Sampler,
    Framebuffer,
    Pipeline,
    Shader,
    ResourceLayout,

    // Effectively a Vulkan descriptor-set concept; stays at zero on D3D11/OpenGL.
    PropertySet
}

/// <summary>
/// Buffer data-transfer operations tracked per frame by <see cref="ProfileSnapshot.BufferOps"/>.
/// </summary>
public enum BufferOpBin
{
    Map,
    Unmap,
    Update,
    Copy
}

/// <summary>
/// Swapchain events tracked per frame by <see cref="ProfileSnapshot.Swaps"/>.
/// </summary>
public enum SwapBin
{
    Present,
    Resize,
    Acquire
}

/// <summary>
/// Buffer roles tracked by the persistent <see cref="ProfileSnapshot.BufferMem"/> gauge,
/// reporting resident bytes per usage.
/// </summary>
public enum BufferRoleBin
{
    Vertex,
    Index,
    Uniform,
    StructuredReadOnly,
    StructuredReadWrite,
    Indirect,
    Staging,
    Dynamic
}
