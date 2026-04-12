using System;
using System.Diagnostics;
using System.Runtime.InteropServices;
using Silk.NET.SDL;
using NeoVeldrid.Sdl2;

namespace NeoVeldrid.StartupUtilities
{
    public static unsafe class NeoVeldridStartup
    {
        private static Sdl Sdl => Sdl2Window.SdlInstance;

        public static void CreateWindowAndGraphicsDevice(
            WindowCreateInfo windowCI,
            out Sdl2Window window,
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
            out Sdl2Window window,
            out GraphicsDevice gd)
            => CreateWindowAndGraphicsDevice(windowCI, deviceOptions, GetPlatformDefaultBackend(), out window, out gd);

        public static void CreateWindowAndGraphicsDevice(
            WindowCreateInfo windowCI,
            GraphicsDeviceOptions deviceOptions,
            GraphicsBackend preferredBackend,
            out Sdl2Window window,
            out GraphicsDevice gd)
        {
            Sdl.Init(Silk.NET.SDL.Sdl.InitVideo);

#if !EXCLUDE_OPENGL_BACKEND
            if (preferredBackend == GraphicsBackend.OpenGL || preferredBackend == GraphicsBackend.OpenGLES)
            {
                SetSDLGLContextAttributes(deviceOptions, preferredBackend);
            }
#endif

            window = CreateWindow(ref windowCI);
            gd = CreateGraphicsDevice(window, deviceOptions, preferredBackend);
        }

        public static Sdl2Window CreateWindow(WindowCreateInfo windowCI) => CreateWindow(ref windowCI);

        public static Sdl2Window CreateWindow(ref WindowCreateInfo windowCI)
        {
            SDL_WindowFlags flags = SDL_WindowFlags.OpenGL | SDL_WindowFlags.Resizable
                    | GetWindowFlags(windowCI.WindowInitialState);
            if (windowCI.WindowInitialState != WindowState.Hidden)
            {
                flags |= SDL_WindowFlags.Shown;
            }

            Sdl2Window window = new Sdl2Window(
                windowCI.WindowTitle ?? "NeoVeldrid",
                windowCI.X,
                windowCI.Y,
                windowCI.WindowWidth > 0 ? windowCI.WindowWidth : 960,
                windowCI.WindowHeight > 0 ? windowCI.WindowHeight : 540,
                flags,
                false);

            return window;
        }

        private static SDL_WindowFlags GetWindowFlags(WindowState state)
        {
            switch (state)
            {
                case WindowState.Normal:
                    return 0;
                case WindowState.FullScreen:
                    return SDL_WindowFlags.Fullscreen;
                case WindowState.Maximized:
                    return SDL_WindowFlags.Maximized;
                case WindowState.Minimized:
                    return SDL_WindowFlags.Minimized;
                case WindowState.BorderlessFullScreen:
                    return SDL_WindowFlags.FullScreenDesktop;
                case WindowState.Hidden:
                    return SDL_WindowFlags.Hidden;
                default:
                    throw new NeoVeldridException("Invalid WindowState: " + state);
            }
        }

        public static GraphicsDevice CreateGraphicsDevice(Sdl2Window window)
            => CreateGraphicsDevice(window, new GraphicsDeviceOptions(), GetPlatformDefaultBackend());
        public static GraphicsDevice CreateGraphicsDevice(Sdl2Window window, GraphicsDeviceOptions options)
            => CreateGraphicsDevice(window, options, GetPlatformDefaultBackend());
        public static GraphicsDevice CreateGraphicsDevice(Sdl2Window window, GraphicsBackend preferredBackend)
            => CreateGraphicsDevice(window, new GraphicsDeviceOptions(), preferredBackend);
        public static GraphicsDevice CreateGraphicsDevice(
            Sdl2Window window,
            GraphicsDeviceOptions options,
            GraphicsBackend preferredBackend)
        {
            switch (preferredBackend)
            {
                case GraphicsBackend.Direct3D11:
#if !EXCLUDE_D3D11_BACKEND
                    return CreateDefaultD3D11GraphicsDevice(options, window);
#else
                    throw new NeoVeldridException("D3D11 support has not been included in this configuration of NeoVeldrid");
#endif
                case GraphicsBackend.Vulkan:
#if !EXCLUDE_VULKAN_BACKEND
                    return CreateVulkanGraphicsDevice(options, window);
#else
                    throw new NeoVeldridException("Vulkan support has not been included in this configuration of NeoVeldrid");
#endif
                case GraphicsBackend.OpenGL:
#if !EXCLUDE_OPENGL_BACKEND
                    return CreateDefaultOpenGLGraphicsDevice(options, window, preferredBackend);
#else
                    throw new NeoVeldridException("OpenGL support has not been included in this configuration of NeoVeldrid");
#endif
                case GraphicsBackend.OpenGLES:
#if !EXCLUDE_OPENGL_BACKEND
                    return CreateDefaultOpenGLGraphicsDevice(options, window, preferredBackend);
#else
                    throw new NeoVeldridException("OpenGL support has not been included in this configuration of NeoVeldrid");
#endif
                default:
                    throw new NeoVeldridException("Invalid GraphicsBackend: " + preferredBackend);
            }
        }

        public static SwapchainSource GetSwapchainSource(Sdl2Window window)
        {
            var sdl = Sdl;
            nint sdlHandle = window.SdlWindowHandle;
            SysWMInfo sysWmInfo;
            sdl.GetVersion(&sysWmInfo.Version);
            sdl.GetWindowWMInfo((Silk.NET.SDL.Window*)sdlHandle, &sysWmInfo);
            switch (sysWmInfo.Subsystem)
            {
                case SysWMType.Windows:
                    return SwapchainSource.CreateWin32(sysWmInfo.Info.Win.Hwnd, sysWmInfo.Info.Win.HInstance);
                case SysWMType.X11:
                    return SwapchainSource.CreateXlib(
                        (nint)sysWmInfo.Info.X11.Display,
                        (nint)sysWmInfo.Info.X11.Window);
                case SysWMType.Wayland:
                    return SwapchainSource.CreateWayland(
                        (nint)sysWmInfo.Info.Wayland.Display,
                        (nint)sysWmInfo.Info.Wayland.Surface);
                case SysWMType.Cocoa:
                    return SwapchainSource.CreateNSWindow((nint)sysWmInfo.Info.Cocoa.Window);
                default:
                    throw new PlatformNotSupportedException("Cannot create a SwapchainSource for " + sysWmInfo.Subsystem + ".");
            }
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
                throw new NeoVeldridException("No graphics backend is available. Enable at least one backend.");
#endif
            }
            else if (RuntimeInformation.IsOSPlatform(OSPlatform.OSX))
            {
#if !EXCLUDE_VULKAN_BACKEND
                return GraphicsBackend.Vulkan; // Via MoltenVK
#elif !EXCLUDE_OPENGL_BACKEND
                return GraphicsBackend.OpenGL;
#else
                throw new NeoVeldridException("No graphics backend is available. Enable at least one backend.");
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
                throw new NeoVeldridException("No graphics backend is available. Enable at least one backend.");
#endif
            }
        }

#if !EXCLUDE_VULKAN_BACKEND
        public static GraphicsDevice CreateVulkanGraphicsDevice(GraphicsDeviceOptions options, Sdl2Window window)
            => CreateVulkanGraphicsDevice(options, window, options.SwapchainSrgbFormat);
        public static GraphicsDevice CreateVulkanGraphicsDevice(
            GraphicsDeviceOptions options,
            Sdl2Window window,
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
        public static GraphicsDevice CreateDefaultOpenGLGraphicsDevice(
            GraphicsDeviceOptions options,
            Sdl2Window window,
            GraphicsBackend backend)
        {
            var sdl = Sdl;
            sdl.ClearError();
            var sdlWindow = (Silk.NET.SDL.Window*)window.SdlWindowHandle;

            SetSDLGLContextAttributes(options, backend);

            void* contextHandle = sdl.GLCreateContext(sdlWindow);
            string errorString = sdl.GetErrorS();
            if (!string.IsNullOrEmpty(errorString))
            {
                throw new NeoVeldridException(
                    $"Unable to create OpenGL Context: \"{errorString}\". This may indicate that the system does not support the requested OpenGL profile, version, or Swapchain format.");
            }

            int actualDepthSize;
            sdl.GLGetAttribute(GLattr.DepthSize, &actualDepthSize);
            int actualStencilSize;
            sdl.GLGetAttribute(GLattr.StencilSize, &actualStencilSize);

            sdl.GLSetSwapInterval(options.SyncToVerticalBlank ? 1 : 0);

            OpenGL.OpenGLPlatformInfo platformInfo = new OpenGL.OpenGLPlatformInfo(
                (nint)contextHandle,
                name => (nint)sdl.GLGetProcAddress(name),
                context => sdl.GLMakeCurrent(sdlWindow, (void*)context),
                () => (nint)sdl.GLGetCurrentContext(),
                () => sdl.GLMakeCurrent((Silk.NET.SDL.Window*)null, (void*)null),
                context => sdl.GLDeleteContext((void*)context),
                () => sdl.GLSwapWindow(sdlWindow),
                sync => sdl.GLSetSwapInterval(sync ? 1 : 0));

            return GraphicsDevice.CreateOpenGL(
                options,
                platformInfo,
                (uint)window.Width,
                (uint)window.Height);
        }

        public static void SetSDLGLContextAttributes(GraphicsDeviceOptions options, GraphicsBackend backend)
        {
            var sdl = Sdl;

            if (backend != GraphicsBackend.OpenGL && backend != GraphicsBackend.OpenGLES)
            {
                throw new NeoVeldridException(
                    $"{nameof(backend)} must be {nameof(GraphicsBackend.OpenGL)} or {nameof(GraphicsBackend.OpenGLES)}.");
            }

            GLcontextFlag contextFlags = options.Debug
                ? GLcontextFlag.DebugFlag | GLcontextFlag.ForwardCompatibleFlag
                : GLcontextFlag.ForwardCompatibleFlag;

            sdl.GLSetAttribute(GLattr.ContextFlags, (int)contextFlags);

            (int major, int minor) = GetMaxGLVersion(backend == GraphicsBackend.OpenGLES);

            if (backend == GraphicsBackend.OpenGL)
            {
                sdl.GLSetAttribute(GLattr.ContextProfileMask, (int)GLprofile.Core);
                sdl.GLSetAttribute(GLattr.ContextMajorVersion, major);
                sdl.GLSetAttribute(GLattr.ContextMinorVersion, minor);
            }
            else
            {
                sdl.GLSetAttribute(GLattr.ContextProfileMask, (int)GLprofile.ES);
                sdl.GLSetAttribute(GLattr.ContextMajorVersion, major);
                sdl.GLSetAttribute(GLattr.ContextMinorVersion, minor);
            }

            int depthBits = 0;
            int stencilBits = 0;
            if (options.SwapchainDepthFormat.HasValue)
            {
                switch (options.SwapchainDepthFormat)
                {
                    case PixelFormat.R16_UNorm:
                        depthBits = 16;
                        break;
                    case PixelFormat.D24_UNorm_S8_UInt:
                        depthBits = 24;
                        stencilBits = 8;
                        break;
                    case PixelFormat.R32_Float:
                        depthBits = 32;
                        break;
                    case PixelFormat.D32_Float_S8_UInt:
                        depthBits = 32;
                        stencilBits = 8;
                        break;
                    default:
                        throw new NeoVeldridException("Invalid depth format: " + options.SwapchainDepthFormat.Value);
                }
            }

            sdl.GLSetAttribute(GLattr.DepthSize, depthBits);
            sdl.GLSetAttribute(GLattr.StencilSize, stencilBits);

            sdl.GLSetAttribute(GLattr.FramebufferSrgbCapable, options.SwapchainSrgbFormat ? 1 : 0);
        }

        private static readonly object s_glVersionLock = new object();
        private static (int Major, int Minor)? s_maxSupportedGLVersion;
        private static (int Major, int Minor)? s_maxSupportedGLESVersion;

        private static (int Major, int Minor) GetMaxGLVersion(bool gles)
        {
            lock (s_glVersionLock)
            {
                (int Major, int Minor)? maxVer = gles ? s_maxSupportedGLESVersion : s_maxSupportedGLVersion;
                if (maxVer == null)
                {
                    maxVer = TestMaxVersion(gles);
                    if (gles) { s_maxSupportedGLESVersion = maxVer; }
                    else { s_maxSupportedGLVersion = maxVer; }
                }

                return maxVer.Value;
            }
        }

        private static (int Major, int Minor) TestMaxVersion(bool gles)
        {
            (int, int)[] testVersions = gles
                ? new[] { (3, 2), (3, 0) }
                : new[] { (4, 6), (4, 3), (4, 0), (3, 3), (3, 0) };

            foreach ((int major, int minor) in testVersions)
            {
                if (TestIndividualGLVersion(gles, major, minor)) { return (major, minor); }
            }

            return (0, 0);
        }

        private static bool TestIndividualGLVersion(bool gles, int major, int minor)
        {
            var sdl = Sdl;

            GLprofile profileMask = gles ? GLprofile.ES : GLprofile.Core;

            sdl.GLSetAttribute(GLattr.ContextProfileMask, (int)profileMask);
            sdl.GLSetAttribute(GLattr.ContextMajorVersion, major);
            sdl.GLSetAttribute(GLattr.ContextMinorVersion, minor);

            var window = sdl.CreateWindow(
                string.Empty,
                0, 0,
                1, 1,
                (uint)(SDL_WindowFlags.Hidden | SDL_WindowFlags.OpenGL));

            string errorString = sdl.GetErrorS();

            if (window == null || !string.IsNullOrEmpty(errorString))
            {
                sdl.ClearError();
                Debug.WriteLine($"Unable to create version {major}.{minor} {profileMask} context.");
                if (window != null) sdl.DestroyWindow(window);
                return false;
            }

            void* context = sdl.GLCreateContext(window);
            errorString = sdl.GetErrorS();
            if (!string.IsNullOrEmpty(errorString))
            {
                sdl.ClearError();
                Debug.WriteLine($"Unable to create version {major}.{minor} {profileMask} context.");
                sdl.DestroyWindow(window);
                return false;
            }

            sdl.GLDeleteContext(context);
            sdl.DestroyWindow(window);
            return true;
        }
#endif

#if !EXCLUDE_D3D11_BACKEND
        public static GraphicsDevice CreateDefaultD3D11GraphicsDevice(
            GraphicsDeviceOptions options,
            Sdl2Window window)
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
