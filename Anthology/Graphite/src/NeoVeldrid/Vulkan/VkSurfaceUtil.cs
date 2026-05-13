using Silk.NET.Vulkan;
using Silk.NET.Vulkan.Extensions.EXT;
using Silk.NET.Vulkan.Extensions.KHR;
using static NeoVeldrid.Vk.VulkanUtil;
using System;
using System.Runtime.InteropServices;

namespace NeoVeldrid.Vk
{
    internal static unsafe class VkSurfaceUtil
    {
        internal static SurfaceKHR CreateSurface(VkGraphicsDevice gd, Instance instance, SwapchainSource swapchainSource)
        {
            // TODO a null GD is passed from VkSurfaceSource.CreateSurface for compatibility
            //      when VkSurfaceInfo is removed we do not have to handle gd == null anymore
            var doCheck = gd != null;

            if (doCheck && !gd.HasSurfaceExtension(CommonStrings.VK_KHR_SURFACE_EXTENSION_NAME))
                throw new NeoVeldridException($"The required instance extension was not available: {CommonStrings.VK_KHR_SURFACE_EXTENSION_NAME}");

            switch (swapchainSource)
            {
                case XlibSwapchainSource xlibSource:
                    if (doCheck && !gd.HasSurfaceExtension(CommonStrings.VK_KHR_XLIB_SURFACE_EXTENSION_NAME))
                    {
                        throw new NeoVeldridException($"The required instance extension was not available: {CommonStrings.VK_KHR_XLIB_SURFACE_EXTENSION_NAME}");
                    }
                    return CreateXlib(gd, instance, xlibSource);
                case WaylandSwapchainSource waylandSource:
                    if (doCheck && !gd.HasSurfaceExtension(CommonStrings.VK_KHR_WAYLAND_SURFACE_EXTENSION_NAME))
                    {
                        throw new NeoVeldridException($"The required instance extension was not available: {CommonStrings.VK_KHR_WAYLAND_SURFACE_EXTENSION_NAME}");
                    }
                    return CreateWayland(gd, instance, waylandSource);
                case Win32SwapchainSource win32Source:
                    if (doCheck && !gd.HasSurfaceExtension(CommonStrings.VK_KHR_WIN32_SURFACE_EXTENSION_NAME))
                    {
                        throw new NeoVeldridException($"The required instance extension was not available: {CommonStrings.VK_KHR_WIN32_SURFACE_EXTENSION_NAME}");
                    }
                    return CreateWin32(gd, instance, win32Source);
                case AndroidSurfaceSwapchainSource androidSource:
                    if (doCheck && !gd.HasSurfaceExtension(CommonStrings.VK_KHR_ANDROID_SURFACE_EXTENSION_NAME))
                    {
                        throw new NeoVeldridException($"The required instance extension was not available: {CommonStrings.VK_KHR_ANDROID_SURFACE_EXTENSION_NAME}");
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
                            throw new NeoVeldridException($"Neither macOS surface extension was available: " +
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
                            throw new NeoVeldridException($"Neither macOS surface extension was available: " +
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
                            throw new NeoVeldridException($"Neither iOS surface extension was available: " +
                                $"{CommonStrings.VK_EXT_METAL_SURFACE_EXTENSION_NAME}, {CommonStrings.VK_MVK_IOS_SURFACE_EXTENSION_NAME}");
                        }
                    }

                    return CreateUIViewSurface(gd, instance, uiViewSource, false);
                default:
                    throw new NeoVeldridException($"The provided SwapchainSource cannot be used to create a Vulkan surface.");
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
                throw new NeoVeldridException("VK_KHR_win32_surface extension not available.");
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
                throw new NeoVeldridException("VK_KHR_xlib_surface extension not available.");
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
                throw new NeoVeldridException("VK_KHR_wayland_surface extension not available.");
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
            IntPtr contentView = ObjC.MsgSend(nsWindowSource.NSWindow, ObjC.Sel("contentView"));
            return CreateNSViewSurface(gd, instance, new NSViewSwapchainSource(contentView), hasExtMetalSurface);
        }

        private static unsafe SurfaceKHR CreateNSViewSurface(VkGraphicsDevice gd, Instance instance, NSViewSwapchainSource nsViewSource, bool hasExtMetalSurface)
        {
            IntPtr metalLayer = GetOrCreateMetalLayer(nsViewSource.NSView);

            if (hasExtMetalSurface)
            {
                MetalSurfaceCreateInfoEXT surfaceCI = new MetalSurfaceCreateInfoEXT
                {
                    SType = StructureType.MetalSurfaceCreateInfoExt,
                    PLayer = (nint*)metalLayer
                };

                if (!gd.Vk.TryGetInstanceExtension(instance, out ExtMetalSurface extMetalSurface))
                {
                    throw new NeoVeldridException("VK_EXT_metal_surface extension not available.");
                }

                SurfaceKHR surface;
                Result result = extMetalSurface.CreateMetalSurface(instance, in surfaceCI, null, out surface);
                CheckResult(result);
                return surface;
            }
            else
            {
                // Legacy path: VK_MVK_macos_surface
                MacOSSurfaceCreateInfoMVK surfaceCI = new MacOSSurfaceCreateInfoMVK
                {
                    SType = StructureType.MacosSurfaceCreateInfoMvk,
                    PView = nsViewSource.NSView.ToPointer()
                };

                var createMacOSSurface = gd.GetInstanceProcAddr<vkCreateMacOSSurfaceMVK_t>("vkCreateMacOSSurfaceMVK");
                if (createMacOSSurface == null)
                {
                    throw new NeoVeldridException("vkCreateMacOSSurfaceMVK function not found.");
                }

                SurfaceKHR surface;
                Result result = createMacOSSurface(instance, &surfaceCI, null, &surface);
                CheckResult(result);
                return surface;
            }
        }

        private static IntPtr GetOrCreateMetalLayer(IntPtr nsView)
        {
            // Ensure the view is layer-backed
            ObjC.MsgSendBool(nsView, ObjC.Sel("setWantsLayer:"), 1);

            IntPtr layer = ObjC.MsgSend(nsView, ObjC.Sel("layer"));
            IntPtr caMetalLayerClass = ObjC.GetClass("CAMetalLayer");

            if (layer != IntPtr.Zero && ObjC.MsgSendBool_Ret(layer, ObjC.Sel("isKindOfClass:"), caMetalLayerClass))
            {
                return layer;
            }

            // Create a new CAMetalLayer and set it on the view
            IntPtr metalLayer = ObjC.MsgSend(caMetalLayerClass, ObjC.Sel("alloc"));
            metalLayer = ObjC.MsgSend(metalLayer, ObjC.Sel("init"));
            ObjC.MsgSendPtr(nsView, ObjC.Sel("setLayer:"), metalLayer);
            return metalLayer;
        }

        private static SurfaceKHR CreateUIViewSurface(VkGraphicsDevice gd, Instance instance, UIViewSwapchainSource uiViewSource, bool hasExtMetalSurface)
        {
            throw new PlatformNotSupportedException("iOS Vulkan surface creation is not yet supported in the Silk.NET port.");
        }

        // Minimal ObjC runtime P/Invoke for macOS surface creation.
        // Replaces the former NeoVeldrid.MetalBindings library (only the calls needed here).
        private static class ObjC
        {
            private const string Lib = "/usr/lib/libobjc.A.dylib";

            [DllImport(Lib, EntryPoint = "sel_registerName")]
            public static extern IntPtr Sel(string name);

            [DllImport(Lib, EntryPoint = "objc_getClass")]
            public static extern IntPtr GetClass(string name);

            [DllImport(Lib, EntryPoint = "objc_msgSend")]
            public static extern IntPtr MsgSend(IntPtr receiver, IntPtr selector);

            [DllImport(Lib, EntryPoint = "objc_msgSend")]
            public static extern void MsgSendPtr(IntPtr receiver, IntPtr selector, IntPtr arg);

            [DllImport(Lib, EntryPoint = "objc_msgSend")]
            public static extern void MsgSendBool(IntPtr receiver, IntPtr selector, byte arg);

            [DllImport(Lib, EntryPoint = "objc_msgSend")]
            [return: MarshalAs(UnmanagedType.U1)]
            public static extern bool MsgSendBool_Ret(IntPtr receiver, IntPtr selector, IntPtr arg);
        }
    }
}
