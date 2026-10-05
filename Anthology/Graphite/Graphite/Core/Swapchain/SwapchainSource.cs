#if !EXCLUDE_VULKAN_BACKEND
using Silk.NET.Core.Contexts;
using Silk.NET.Vulkan;
#endif

namespace Prowl.Graphite;

/// <summary>
/// Platform-specific renderable surface.
/// </summary>
public sealed class SwapchainSource
{
#if !EXCLUDE_VULKAN_BACKEND
    internal IVkSurface VkSurface { get; }

    /// <summary>
    /// Create Vulkan swapchain source from Silk.NET surface.
    /// </summary>
    public SwapchainSource(IVkSurface surface)
    {
        VkSurface = surface;
    }

    /// <summary>
    /// Create Vulkan swapchain source from Silk.NET surface.
    /// </summary>
    public static SwapchainSource CreateVulkan(IVkSurface surface)
        => new(surface);

    internal unsafe SurfaceKHR GetSurface(Instance instance)
    {
        return VkSurface.Create<AllocationCallbacks>(instance.ToHandle(), null).ToSurface();
    }
#endif
}
