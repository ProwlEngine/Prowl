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

    public static SwapchainSource CreateWin32(IntPtr hwnd, IntPtr hinstance) => new Win32SwapchainSource(hwnd, hinstance);

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


    internal unsafe SurfaceKHR GetSurface(Vk.VkGraphicsDevice device, Instance instance)
    {
        byte** strings = VkSurface.GetRequiredExtensions(out uint count);

        for (int i = 0; i < count; i++)
        {
            Vk.FixedUtf8String required = new(strings[i]);

            if (!device.HasSurfaceExtension(required))
                throw new RenderException($"Vk Instance does not have required swapchain extension: {required}");
        }

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
