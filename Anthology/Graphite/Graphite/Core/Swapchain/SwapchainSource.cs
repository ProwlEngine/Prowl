using Silk.NET.Core.Contexts;
using Silk.NET.Vulkan;

namespace Prowl.Graphite;

/// <summary>
/// Platform-specific renderable surface; use static factory methods to create.
/// </summary>
public abstract class SwapchainSource
{
    internal SwapchainSource() { }

    /// <summary>
    /// Create Vulkan swapchain source from Silk.NET surface.
    /// </summary>
    public static SwapchainSource CreateVulkan(IVkSurface surface)
        => new VkSurfaceSwapchainSource(surface);
}


internal class VkSurfaceSwapchainSource : SwapchainSource
{
    public IVkSurface VkSurface { get; }


    public VkSurfaceSwapchainSource(IVkSurface surface)
    {
        VkSurface = surface;
    }


    internal unsafe SurfaceKHR GetSurface(Instance instance)
    {
        return VkSurface.Create<AllocationCallbacks>(instance.ToHandle(), null).ToSurface();
    }
}
