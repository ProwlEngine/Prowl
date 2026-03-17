using System;
using System.Runtime.InteropServices;
using Silk.NET.Windowing;

namespace Veldrid.StartupUtilities
{
    public static class VeldridStartup
    {
        public static void CreateWindowAndGraphicsDevice(
            WindowCreateInfo windowCI,
            out VeldridWindow window,
            out GraphicsDevice gd)
            => CreateWindowAndGraphicsDevice(
                windowCI,
                new GraphicsDeviceOptions(),
                GetPlatformDefaultBackend(),
                out window,
                out gd);

        public static void CreateWindowAndGraphicsDevice(
            WindowCreateInfo windowCI,
            GraphicsDeviceOptions deviceOptions,
            out VeldridWindow window,
            out GraphicsDevice gd)
            => CreateWindowAndGraphicsDevice(windowCI, deviceOptions, GetPlatformDefaultBackend(), out window, out gd);

        public static void CreateWindowAndGraphicsDevice(
            WindowCreateInfo windowCI,
            GraphicsDeviceOptions deviceOptions,
            GraphicsBackend preferredBackend,
            out VeldridWindow window,
            out GraphicsDevice gd)
        {
            GraphicsAPI api = GraphicsAPI.None;

#if !EXCLUDE_OPENGL_BACKEND
            if (preferredBackend == GraphicsBackend.OpenGL || preferredBackend == GraphicsBackend.OpenGLES)
            {
                api = GetOpenGLGraphicsAPI(deviceOptions, preferredBackend);
            }
#endif

            window = new VeldridWindow(windowCI, api, deviceOptions);
            gd = CreateGraphicsDevice(window, deviceOptions, preferredBackend);
        }

        public static VeldridWindow CreateWindow(WindowCreateInfo windowCI) => CreateWindow(ref windowCI);

        public static VeldridWindow CreateWindow(ref WindowCreateInfo windowCI)
        {
            return new VeldridWindow(windowCI, default);
        }

        public static GraphicsDevice CreateGraphicsDevice(VeldridWindow window)
            => CreateGraphicsDevice(window, new GraphicsDeviceOptions(), GetPlatformDefaultBackend());
        public static GraphicsDevice CreateGraphicsDevice(VeldridWindow window, GraphicsDeviceOptions options)
            => CreateGraphicsDevice(window, options, GetPlatformDefaultBackend());
        public static GraphicsDevice CreateGraphicsDevice(VeldridWindow window, GraphicsBackend preferredBackend)
            => CreateGraphicsDevice(window, new GraphicsDeviceOptions(), preferredBackend);
        public static GraphicsDevice CreateGraphicsDevice(
            VeldridWindow window,
            GraphicsDeviceOptions options,
            GraphicsBackend preferredBackend)
        {
            switch (preferredBackend)
            {
                case GraphicsBackend.Direct3D11:
#if !EXCLUDE_D3D11_BACKEND
                    return CreateDefaultD3D11GraphicsDevice(options, window);
#else
                    throw new VeldridException("D3D11 support has not been included in this configuration of Veldrid");
#endif
                case GraphicsBackend.Vulkan:
#if !EXCLUDE_VULKAN_BACKEND
                    return CreateVulkanGraphicsDevice(options, window);
#else
                    throw new VeldridException("Vulkan support has not been included in this configuration of Veldrid");
#endif
                case GraphicsBackend.OpenGL:
#if !EXCLUDE_OPENGL_BACKEND
                    return CreateDefaultOpenGLGraphicsDevice(options, window, preferredBackend);
#else
                    throw new VeldridException("OpenGL support has not been included in this configuration of Veldrid");
#endif
                case GraphicsBackend.OpenGLES:
#if !EXCLUDE_OPENGL_BACKEND
                    return CreateDefaultOpenGLGraphicsDevice(options, window, preferredBackend);
#else
                    throw new VeldridException("OpenGL support has not been included in this configuration of Veldrid");
#endif
                default:
                    throw new VeldridException("Invalid GraphicsBackend: " + preferredBackend);
            }
        }

        public static unsafe SwapchainSource GetSwapchainSource(VeldridWindow window)
        {
            var native = window.SilkWindow.Native;
            if (native == null)
                throw new VeldridException("Unable to get native window handles.");

            if (native.Win32.HasValue)
            {
                var (hwnd, _, hinstance) = native.Win32.Value;
                return SwapchainSource.CreateWin32(hwnd, hinstance);
            }

            if (native.X11.HasValue)
            {
                var (display, xwindow) = native.X11.Value;
                return SwapchainSource.CreateXlib(display, (nint)xwindow);
            }

            if (native.Wayland.HasValue)
            {
                var (display, surface) = native.Wayland.Value;
                return SwapchainSource.CreateWayland(display, surface);
            }

            if (native.Cocoa.HasValue)
            {
                return SwapchainSource.CreateNSWindow(native.Cocoa.Value);
            }

            throw new PlatformNotSupportedException("Cannot create a SwapchainSource for the current platform.");
        }

        public static GraphicsBackend GetPlatformDefaultBackend()
        {
            if (RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
            {
#if !EXCLUDE_D3D11_BACKEND
                return GraphicsBackend.Direct3D11;
#elif !EXCLUDE_VULKAN_BACKEND
                return GraphicsBackend.Vulkan;
#elif !EXCLUDE_OPENGL_BACKEND
                return GraphicsBackend.OpenGL;
#else
                throw new VeldridException("No graphics backend is available. Enable at least one backend.");
#endif
            }
            else if (RuntimeInformation.IsOSPlatform(OSPlatform.OSX))
            {
#if !EXCLUDE_VULKAN_BACKEND
                return GraphicsBackend.Vulkan; // Via MoltenVK
#elif !EXCLUDE_OPENGL_BACKEND
                return GraphicsBackend.OpenGL;
#else
                throw new VeldridException("No graphics backend is available. Enable at least one backend.");
#endif
            }
            else
            {
#if !EXCLUDE_VULKAN_BACKEND
                return GraphicsDevice.IsBackendSupported(GraphicsBackend.Vulkan)
                    ? GraphicsBackend.Vulkan
                    : GraphicsBackend.OpenGL;
#elif !EXCLUDE_OPENGL_BACKEND
                return GraphicsBackend.OpenGL;
#else
                throw new VeldridException("No graphics backend is available. Enable at least one backend.");
#endif
            }
        }

#if !EXCLUDE_VULKAN_BACKEND
        public static unsafe GraphicsDevice CreateVulkanGraphicsDevice(GraphicsDeviceOptions options, VeldridWindow window)
            => CreateVulkanGraphicsDevice(options, window, false);
        public static unsafe GraphicsDevice CreateVulkanGraphicsDevice(
            GraphicsDeviceOptions options,
            VeldridWindow window,
            bool colorSrgb)
        {
            SwapchainDescription scDesc = new SwapchainDescription(
                GetSwapchainSource(window),
                (uint)window.Width,
                (uint)window.Height,
                options.SwapchainDepthFormat,
                options.SyncToVerticalBlank,
                colorSrgb);
            GraphicsDevice gd = GraphicsDevice.CreateVulkan(options, scDesc);

            return gd;
        }
#endif

#if !EXCLUDE_OPENGL_BACKEND
        public static unsafe GraphicsDevice CreateDefaultOpenGLGraphicsDevice(
            GraphicsDeviceOptions options,
            VeldridWindow window,
            GraphicsBackend backend)
        {
            var silkWindow = window.SilkWindow;
            var glContext = silkWindow.GLContext;

            glContext.MakeCurrent();

            OpenGL.OpenGLPlatformInfo platformInfo = new OpenGL.OpenGLPlatformInfo(
                glContext.Handle,
                name => glContext.GetProcAddress(name),
                ctx => glContext.MakeCurrent(),
                () => glContext.Handle,
                () => glContext.Clear(),
                ctx => { },
                () => glContext.SwapBuffers(),
                sync => glContext.SwapInterval(sync ? 1 : 0));

            return GraphicsDevice.CreateOpenGL(
                options,
                platformInfo,
                (uint)window.Width,
                (uint)window.Height);
        }

        private static GraphicsAPI GetOpenGLGraphicsAPI(GraphicsDeviceOptions options, GraphicsBackend backend)
        {
            bool gles = backend == GraphicsBackend.OpenGLES;
            if (!OpenGLVersionProbe.TryGetMaxVersion(gles, out var version))
                throw new VeldridException(
                    $"Unable to create an {(gles ? "OpenGL ES" : "OpenGL")} context. " +
                    "No supported version could be initialized on this system.");

            if (gles)
                return new GraphicsAPI(ContextAPI.OpenGLES, ContextProfile.Core, ContextFlags.Default, new APIVersion(version.Major, version.Minor));

            ContextFlags flags = options.Debug
                ? ContextFlags.Debug | ContextFlags.ForwardCompatible
                : ContextFlags.ForwardCompatible;
            return new GraphicsAPI(ContextAPI.OpenGL, ContextProfile.Core, flags, new APIVersion(version.Major, version.Minor));
        }
#endif

#if !EXCLUDE_D3D11_BACKEND
        public static GraphicsDevice CreateDefaultD3D11GraphicsDevice(
            GraphicsDeviceOptions options,
            VeldridWindow window)
        {
            SwapchainSource source = GetSwapchainSource(window);
            SwapchainDescription swapchainDesc = new SwapchainDescription(
                source,
                (uint)window.Width, (uint)window.Height,
                options.SwapchainDepthFormat,
                options.SyncToVerticalBlank,
                options.SwapchainSrgbFormat);

            return GraphicsDevice.CreateD3D11(options, swapchainDesc);
        }
#endif
    }
}
