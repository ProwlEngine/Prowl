using Silk.NET.Core.Contexts;
using Silk.NET.Vulkan;

namespace Prowl.Graphite;

/// <summary>
/// Platform-specific renderable surface. Build via the static factory methods. Used to describe a swapchain.
/// </summary>
public abstract class SwapchainSource
{
    internal SwapchainSource() { }

    /// <summary>
    /// Builds a Vulkan swapchain source from a Silk.NET surface.
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
