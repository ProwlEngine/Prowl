using Silk.NET.Core.Contexts;
using Silk.NET.Vulkan;

namespace Prowl.Graphite;

/// <summary>
/// A platform-specific object representing a renderable surface.
/// A SwapchainSource can be created with one of several static factory methods.
/// A SwapchainSource is used to describe a Swapchain (see <see cref="SwapchainDescription"/>).
/// </summary>
public abstract class SwapchainSource
{
    internal SwapchainSource() { }

    /// <summary>
    /// Creates a Vulkan swapchain source from an <see cref="IVkSurface"/> interface, typically acquired from a Silk.NET window.
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
