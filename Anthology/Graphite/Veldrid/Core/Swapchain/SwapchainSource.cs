using System;

using Silk.NET.Core.Contexts;
using Silk.NET.Vulkan;

namespace Prowl.Veldrid;

/// <summary>
/// A platform-specific object representing a renderable surface.
/// A SwapchainSource can be created with one of several static factory methods.
/// A SwapchainSource is used to describe a Swapchain (see <see cref="SwapchainDescription"/>).
/// </summary>
public abstract class SwapchainSource
{
    internal SwapchainSource() { }

    /// <summary>
    /// Creates a Win32 swapchain source for Direct3D 11 from an hwnd and hinstance pointer.
    /// </summary>
    public static SwapchainSource CreateWin32(IntPtr hwnd, IntPtr hinstance) => new Win32SwapchainSource(hwnd, hinstance);

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


internal class Win32SwapchainSource : SwapchainSource
{
    public IntPtr Hwnd { get; }
    public IntPtr Hinstance { get; }

    public Win32SwapchainSource(IntPtr hwnd, IntPtr hinstance)
    {
        Hwnd = hwnd;
        Hinstance = hinstance;
    }
}
