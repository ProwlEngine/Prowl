using Silk.NET.Vulkan;
using Silk.NET.Vulkan.Extensions.KHR;
using static Veldrid.Vk.VulkanUtil;
using System;

namespace Veldrid.Vk
{
    internal static unsafe class VkSurfaceUtil
    {
        internal static SurfaceKHR CreateSurface(VkGraphicsDevice gd, Instance instance, SwapchainSource swapchainSource)
        {
            // TODO a null GD is passed from VkSurfaceSource.CreateSurface for compatibility
            //      when VkSurfaceInfo is removed we do not have to handle gd == null anymore
            var doCheck = gd != null;

            if (doCheck && !gd.HasSurfaceExtension(CommonStrings.VK_KHR_SURFACE_EXTENSION_NAME))
                throw new VeldridException($"The required instance extension was not available: {CommonStrings.VK_KHR_SURFACE_EXTENSION_NAME}");

            switch (swapchainSource)
            {
                case XlibSwapchainSource xlibSource:
                    if (doCheck && !gd.HasSurfaceExtension(CommonStrings.VK_KHR_XLIB_SURFACE_EXTENSION_NAME))
                    {
                        throw new VeldridException($"The required instance extension was not available: {CommonStrings.VK_KHR_XLIB_SURFACE_EXTENSION_NAME}");
                    }
                    return CreateXlib(gd, instance, xlibSource);
                case WaylandSwapchainSource waylandSource:
                    if (doCheck && !gd.HasSurfaceExtension(CommonStrings.VK_KHR_WAYLAND_SURFACE_EXTENSION_NAME))
                    {
                        throw new VeldridException($"The required instance extension was not available: {CommonStrings.VK_KHR_WAYLAND_SURFACE_EXTENSION_NAME}");
                    }
                    return CreateWayland(gd, instance, waylandSource);
                case Win32SwapchainSource win32Source:
                    if (doCheck && !gd.HasSurfaceExtension(CommonStrings.VK_KHR_WIN32_SURFACE_EXTENSION_NAME))
                    {
                        throw new VeldridException($"The required instance extension was not available: {CommonStrings.VK_KHR_WIN32_SURFACE_EXTENSION_NAME}");
                    }
                    return CreateWin32(gd, instance, win32Source);
                case AndroidSurfaceSwapchainSource androidSource:
                    if (doCheck && !gd.HasSurfaceExtension(CommonStrings.VK_KHR_ANDROID_SURFACE_EXTENSION_NAME))
                    {
                        throw new VeldridException($"The required instance extension was not available: {CommonStrings.VK_KHR_ANDROID_SURFACE_EXTENSION_NAME}");
                    }
                    return CreateAndroidSurface(gd, instance, androidSource);
                case NSWindowSwapchainSource nsWindowSource:
                    if (doCheck)
                    {
                        bool hasMetalExtension = gd.HasSurfaceExtension(CommonStrings.VK_EXT_METAL_SURFACE_EXTENSION_NAME);
                        if (hasMetalExtension || gd.HasSurfaceExtension(CommonStrings.VK_MVK_MACOS_SURFACE_EXTENSION_NAME))
                        {
                            return CreateNSWindowSurface(gd, instance, nsWindowSource, hasMetalExtension);
                        }
                        else
                        {
                            throw new VeldridException($"Neither macOS surface extension was available: " +
                                $"{CommonStrings.VK_MVK_MACOS_SURFACE_EXTENSION_NAME}, {CommonStrings.VK_EXT_METAL_SURFACE_EXTENSION_NAME}");
                        }
                    }

                    return CreateNSWindowSurface(gd, instance, nsWindowSource, false);
                case NSViewSwapchainSource nsViewSource:
                    if (doCheck)
                    {
                        bool hasMetalExtension = gd.HasSurfaceExtension(CommonStrings.VK_EXT_METAL_SURFACE_EXTENSION_NAME);
                        if (hasMetalExtension || gd.HasSurfaceExtension(CommonStrings.VK_MVK_MACOS_SURFACE_EXTENSION_NAME))
                        {
                            return CreateNSViewSurface(gd, instance, nsViewSource, hasMetalExtension);
                        }
                        else
                        {
                            throw new VeldridException($"Neither macOS surface extension was available: " +
                                $"{CommonStrings.VK_MVK_MACOS_SURFACE_EXTENSION_NAME}, {CommonStrings.VK_EXT_METAL_SURFACE_EXTENSION_NAME}");
                        }
                    }

                    return CreateNSViewSurface(gd, instance, nsViewSource, false);
                case UIViewSwapchainSource uiViewSource:
                    if (doCheck)
                    {
                        bool hasMetalExtension = gd.HasSurfaceExtension(CommonStrings.VK_EXT_METAL_SURFACE_EXTENSION_NAME);
                        if (hasMetalExtension || gd.HasSurfaceExtension(CommonStrings.VK_MVK_IOS_SURFACE_EXTENSION_NAME))
                        {
                            return CreateUIViewSurface(gd, instance, uiViewSource, hasMetalExtension);
                        }
                        else
                        {
                            throw new VeldridException($"Neither macOS surface extension was available: " +
                                $"{CommonStrings.VK_MVK_MACOS_SURFACE_EXTENSION_NAME}, {CommonStrings.VK_MVK_IOS_SURFACE_EXTENSION_NAME}");
                        }
                    }

                    return CreateUIViewSurface(gd, instance, uiViewSource, false);
                default:
                    throw new VeldridException($"The provided SwapchainSource cannot be used to create a Vulkan surface.");
            }
        }

        private static SurfaceKHR CreateWin32(VkGraphicsDevice gd, Instance instance, Win32SwapchainSource win32Source)
        {
            Win32SurfaceCreateInfoKHR surfaceCI = new Win32SurfaceCreateInfoKHR
            {
                SType = StructureType.Win32SurfaceCreateInfoKhr
            };
            surfaceCI.Hwnd = win32Source.Hwnd;
            surfaceCI.Hinstance = win32Source.Hinstance;

            if (!gd.Vk.TryGetInstanceExtension(instance, out KhrWin32Surface khrWin32Surface))
            {
                throw new VeldridException("VK_KHR_win32_surface extension not available.");
            }

            SurfaceKHR surface;
            Result result = khrWin32Surface.CreateWin32Surface(instance, in surfaceCI, null, out surface);
            CheckResult(result);
            return surface;
        }

        private static SurfaceKHR CreateXlib(VkGraphicsDevice gd, Instance instance, XlibSwapchainSource xlibSource)
        {
            XlibSurfaceCreateInfoKHR xsci = new XlibSurfaceCreateInfoKHR
            {
                SType = StructureType.XlibSurfaceCreateInfoKhr
            };
            xsci.Dpy = (nint*)xlibSource.Display;
            xsci.Window = (nint)xlibSource.Window;

            if (!gd.Vk.TryGetInstanceExtension(instance, out KhrXlibSurface khrXlibSurface))
            {
                throw new VeldridException("VK_KHR_xlib_surface extension not available.");
            }

            SurfaceKHR surface;
            Result result = khrXlibSurface.CreateXlibSurface(instance, in xsci, null, out surface);
            CheckResult(result);
            return surface;
        }

        private static SurfaceKHR CreateWayland(VkGraphicsDevice gd, Instance instance, WaylandSwapchainSource waylandSource)
        {
            WaylandSurfaceCreateInfoKHR wsci = new WaylandSurfaceCreateInfoKHR
            {
                SType = StructureType.WaylandSurfaceCreateInfoKhr
            };
            wsci.Display = (nint*)waylandSource.Display;
            wsci.Surface = (nint*)waylandSource.Surface;

            if (!gd.Vk.TryGetInstanceExtension(instance, out KhrWaylandSurface khrWaylandSurface))
            {
                throw new VeldridException("VK_KHR_wayland_surface extension not available.");
            }

            SurfaceKHR surface;
            Result result = khrWaylandSurface.CreateWaylandSurface(instance, in wsci, null, out surface);
            CheckResult(result);
            return surface;
        }

        private static SurfaceKHR CreateAndroidSurface(VkGraphicsDevice gd, Instance instance, AndroidSurfaceSwapchainSource androidSource)
        {
            // TODO: Android surface creation requires platform-specific ANativeWindow bindings.
            // Will be re-enabled when Silk.NET.Windowing handles platform surfaces (Phase 2).
            throw new PlatformNotSupportedException("Android Vulkan surface creation is not yet supported in the Silk.NET port.");
        }

        private static unsafe SurfaceKHR CreateNSWindowSurface(VkGraphicsDevice gd, Instance instance, NSWindowSwapchainSource nsWindowSource, bool hasExtMetalSurface)
        {
            // TODO: macOS surface creation requires MetalBindings (NSWindow/CAMetalLayer).
            // Will be re-enabled when Silk.NET.Windowing handles platform surfaces (Phase 2).
            throw new PlatformNotSupportedException("macOS Vulkan surface creation is not yet supported in the Silk.NET port.");
        }

        private static unsafe SurfaceKHR CreateNSViewSurface(VkGraphicsDevice gd, Instance instance, NSViewSwapchainSource nsViewSource, bool hasExtMetalSurface)
        {
            throw new PlatformNotSupportedException("macOS Vulkan surface creation is not yet supported in the Silk.NET port.");
        }

        private static SurfaceKHR CreateUIViewSurface(VkGraphicsDevice gd, Instance instance, UIViewSwapchainSource uiViewSource, bool hasExtMetalSurface)
        {
            throw new PlatformNotSupportedException("iOS Vulkan surface creation is not yet supported in the Silk.NET port.");
        }
    }
}
