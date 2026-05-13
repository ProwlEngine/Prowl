using System;
using Silk.NET.Vulkan;
using static NeoVeldrid.Vk.VulkanUtil;

namespace NeoVeldrid.Vk
{
    /// <summary>
    /// An object which can be used to create a VkSurfaceKHR.
    /// </summary>
    public abstract class VkSurfaceSource
    {
        internal VkSurfaceSource() { }

        /// <summary>
        /// Creates a new VkSurfaceKHR attached to this source.
        /// </summary>
        /// <param name="instance">The VkInstance to use.</param>
        /// <returns>A new VkSurfaceKHR.</returns>
        public abstract SurfaceKHR CreateSurface(Instance instance);

        /// <summary>
        /// Creates a new <see cref="VkSurfaceSource"/> from the given Win32 instance and window handle.
        /// </summary>
        /// <param name="hinstance">The Win32 instance handle.</param>
        /// <param name="hwnd">The Win32 window handle.</param>
        /// <returns>A new VkSurfaceSource.</returns>
        public static VkSurfaceSource CreateWin32(IntPtr hinstance, IntPtr hwnd) => new Win32VkSurfaceInfo(hinstance, hwnd);
        /// <summary>
        /// Creates a new VkSurfaceSource from the given Xlib information.
        /// </summary>
        /// <param name="display">A pointer to the Xlib Display.</param>
        /// <param name="window">An Xlib window handle.</param>
        /// <returns>A new VkSurfaceSource.</returns>
        public static VkSurfaceSource CreateXlib(IntPtr display, IntPtr window) => new XlibVkSurfaceInfo(display, window);

        internal abstract SwapchainSource GetSurfaceSource();
    }

    internal class Win32VkSurfaceInfo : VkSurfaceSource
    {
        private readonly IntPtr _hinstance;
        private readonly IntPtr _hwnd;

        public Win32VkSurfaceInfo(IntPtr hinstance, IntPtr hwnd)
        {
            _hinstance = hinstance;
            _hwnd = hwnd;
        }

        public unsafe override SurfaceKHR CreateSurface(Instance instance)
        {
            return VkSurfaceUtil.CreateSurface(null, instance, GetSurfaceSource());
        }

        internal override SwapchainSource GetSurfaceSource()
        {
            return new Win32SwapchainSource(_hwnd, _hinstance);
        }
    }

    internal class XlibVkSurfaceInfo : VkSurfaceSource
    {
        private readonly IntPtr _display;
        private readonly IntPtr _window;

        public XlibVkSurfaceInfo(IntPtr display, IntPtr window)
        {
            _display = display;
            _window = window;
        }

        public unsafe override SurfaceKHR CreateSurface(Instance instance)
        {
            return VkSurfaceUtil.CreateSurface(null, instance, GetSurfaceSource());
        }

        internal unsafe override SwapchainSource GetSurfaceSource()
        {
            return new XlibSwapchainSource(_display, _window);
        }
    }
}
