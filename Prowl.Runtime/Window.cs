// This file is part of the Prowl Game Engine
// Licensed under the MIT License. See the LICENSE file in the project root for details.

using System;
using System.Runtime.InteropServices;

using Prowl.Graphite;

using Silk.NET.Input;
using Silk.NET.Input.Sdl;
using Silk.NET.Maths;
using Silk.NET.SDL;
using Silk.NET.Windowing;
using Silk.NET.Windowing.Sdl;

namespace Prowl.Runtime;

public static class Window
{

    public static IWindow InternalWindow { get; internal set; }
    public static IInputContext InternalInput { get; internal set; }

    /// <summary>
    /// Graphics backend the device is created with. Set before <see cref="InitWindow"/> to
    /// override. Drives the windowing platform (SDL for Vulkan on macOS) and the context API.
    /// </summary>
    public static GraphicsBackend Backend { get; set; } = GraphicsBackend.Vulkan;

    private static GraphicsDeviceOptions s_deviceOptions;

    // --- Per-monitor DPI awareness on Windows ----------------------------------------------
    // [DllImport("user32.dll")]
    // private static extern bool SetProcessDpiAwarenessContext(nint dpiContext);

    // [DllImport("shcore.dll")]
    // private static extern int SetProcessDpiAwareness(int level);

    // [DllImport("user32.dll")]
    // private static extern bool SetProcessDPIAware();

    // private static readonly nint DPI_AWARENESS_CONTEXT_PER_MONITOR_AWARE_V2 = -4;

    // private static void EnsureDpiAwareOnWindows()
    // {
    //     if (!RuntimeInformation.IsOSPlatform(OSPlatform.Windows)) return;
    //     try { if (SetProcessDpiAwarenessContext(DPI_AWARENESS_CONTEXT_PER_MONITOR_AWARE_V2)) return; } catch { }
    //     try { if (SetProcessDpiAwareness(2) == 0) return; } catch { }
    //     try { SetProcessDPIAware(); } catch { }
    // }

    public static event Action? Load;
    public static event Action<float>? Update;
    public static event Action<float>? Render;
    public static event Action<float>? PostRender;
    public static event Action<bool>? FocusChanged;
    public static event Action<Vector2D<int>>? Resize;
    public static event Action<Vector2D<int>>? FramebufferResize;
    public static event Action? Closing;

    public static event Action<Vector2D<int>>? Move;
    public static event Action<WindowState>? StateChanged;
    public static event Action<string[]>? FileDrop;

    private static nint s_contentScaleProc;
    private static bool s_contentScaleResolved;

    /// <summary>
    /// Physical-to-logical pixel ratio for the window's current display.
    /// <para>
    /// On Windows, resolves <c>glfwGetWindowContentScale</c> via the native GLFW context
    /// to capture the system DPI factor (e.g. 1.25 at 125%). This is needed because
    /// <c>FramebufferSize / Size</c> only reflects the ratio when the process is
    /// per-monitor DPI-aware; if DPI awareness failed at startup the OS virtualises
    /// the framebuffer (fb == win) and the ratio collapses to 1.
    /// </para>
    /// <para>
    /// On macOS and Linux the GLFW proc address is not reachable via this path, so we
    /// fall back to <c>FramebufferSize / Size</c> which is always correct on those
    /// platforms (Retina: 3024 fb / 1512 points = 2; 1× monitor: 1).
    /// </para>
    /// </summary>
    public static unsafe float ContentScale
    {
        get
        {
            if (InternalWindow == null) return 1f;

            if (RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
            {
                nint? nativeGlfw = InternalWindow.Native?.Glfw;
                if (nativeGlfw.HasValue && nativeGlfw.Value != 0 && TryGetContentScaleViaProc(nativeGlfw.Value, out float scale))
                    return scale;
            }

            var fb = InternalWindow.FramebufferSize;
            var win = InternalWindow.Size;
            return win.X > 0 ? (float)fb.X / win.X : 1f;
        }
    }

    private static unsafe bool TryGetContentScaleViaProc(nint glfwWindow, out float scale)
    {
        scale = 1f;

        if (!s_contentScaleResolved)
        {
            s_contentScaleResolved = true;
            try
            {
                s_contentScaleProc = Silk.NET.GLFW.GlfwProvider.GLFW.Value.Context.GetProcAddress("glfwGetWindowContentScale");
            }
            catch { }
        }

        if (s_contentScaleProc == 0) return false;

        float x, y;
        ((delegate* unmanaged[Cdecl]<nint, float*, float*, void>)s_contentScaleProc)(glfwWindow, &x, &y);
        if (x <= 0) return false;
        scale = x;
        return true;
    }

    public static Vector2D<int> Position
    {
        get { return InternalWindow.Position; }
        set { InternalWindow.Position = value; }
    }

    public static Vector2D<int> Size
    {
        get { return InternalWindow.Size; }
        set { InternalWindow.Size = value; }
    }

    public static bool IsVisible
    {
        get { return InternalWindow.IsVisible; }
        set { InternalWindow.IsVisible = value; }
    }

    public static bool VSync
    {
        get { return Graphics.Device.SyncToVerticalBlank; }
        set { Graphics.Device.SyncToVerticalBlank = value; }
    }

    public static float FramesPerSecond
    {
        get { return (float)InternalWindow.FramesPerSecond; }
        set { InternalWindow.FramesPerSecond = value; InternalWindow.UpdatesPerSecond = value; }
    }

    public static nint Handle
    {
        get { return InternalWindow.Handle; }
    }

    private static bool isFocused = true;
    private static DefaultInputHandler WindowInputHandler;

    public static bool IsFocused
    {
        get { return isFocused; }
    }

    public static void InitWindow(string title, int width, int height, WindowState startState = WindowState.Normal, bool VSync = true)
    {
        MoltenVKMacWorkaround(Backend);

        WindowOptions options = WindowOptions.Default;
        options.Title = title;
        options.Size = new Vector2D<int>(width, height);
        options.WindowState = startState;
        options.VSync = VSync;
        options.API = SilkApiFor(Backend);
        // Update / Render are driven manually from MainLoop, and SwapBuffers is called
        // explicitly after EndFrame, so Silk must not swap on its own.
        options.ShouldSwapAutomatically = false;
        InternalWindow = Silk.NET.Windowing.Window.Create(options);

        s_deviceOptions = new GraphicsDeviceOptions
        {
            Debug = true,
            EnableProfiling = true,
            EnableValidation = true,
            SwapchainDepthFormat = Graphite.PixelFormat.D24_UNorm_S8_UInt,
            SyncToVerticalBlank = VSync,
            PreferStandardClipSpaceYDirection = true,
            PreferDepthRangeZeroToOne = true,
        };

        InternalWindow.Load += OnLoad;
        InternalWindow.Resize += OnResize;
        InternalWindow.FramebufferResize += OnFramebufferResize;
        InternalWindow.Move += OnMove;
        InternalWindow.StateChanged += (state) => StateChanged?.Invoke(state);
        InternalWindow.FileDrop += (files) => FileDrop?.Invoke(files);
        InternalWindow.FocusChanged += (focused) =>
        {
            isFocused = focused;
            FocusChanged?.Invoke(focused);
        };
    }

    private static void OnMove(Vector2D<int> d) => Move?.Invoke(d);

    private static GraphicsAPI SilkApiFor(GraphicsBackend backend) => backend switch
    {
        GraphicsBackend.Vulkan => new GraphicsAPI(ContextAPI.Vulkan, ContextProfile.Core, ContextFlags.ForwardCompatible, new APIVersion(2, 1)),
        _ => GraphicsAPI.None,
    };

    // MoltenVK + SDL is used on Mac since GLFW doesn't provide Vk surfaces there
    // Unfortunately, the Silk.NET config for MoltenVK/SDL doesn't correctly resovle libMoltenVK.dylib so we have to load it manually.
    private static void MoltenVKMacWorkaround(GraphicsBackend backend)
    {
        if (!RuntimeInformation.IsOSPlatform(OSPlatform.OSX) || backend != GraphicsBackend.Vulkan)
            return;

        SdlWindowing.RegisterPlatform();
        SdlWindowing.Use();
        SdlInput.Use();

        Sdl sdl = Sdl.GetApi();

        if (sdl.Init(Sdl.InitVideo) != 0)
            Debug.LogError($"SDL video initialization failed: {sdl.GetErrorS()}");

        string basePath = Environment.ProcessPath != null ? AppContext.BaseDirectory :
            System.IO.Path.GetDirectoryName(Environment.ProcessPath) ?? AppContext.BaseDirectory;

        string libraryPath = System.IO.Path.Join(basePath, "runtimes/osx/native/libMoltenVK.dylib");

        if (sdl.VulkanLoadLibrary(libraryPath) != 0)
            Debug.LogError($"SDL VulkanLoadLibrary failed for '{libraryPath}': {sdl.GetErrorS()}");
    }

    public static void Start()
    {
        // Initialize() fires InternalWindow.Load -> OnLoad, which creates the Graphite
        // device and sets Graphics.Device before the public Load event below runs.
        InternalWindow.Initialize();

        Load?.Invoke();

        try { MainLoop(); }
        finally
        {
            OnClose();
            InternalWindow.Reset();
        }
    }

    private static void MainLoop()
    {
        long lastTicks = System.Diagnostics.Stopwatch.GetTimestamp();
        long freq = System.Diagnostics.Stopwatch.Frequency;

        while (!InternalWindow.IsClosing)
        {
            long now = System.Diagnostics.Stopwatch.GetTimestamp();
            float delta = (float)((now - lastTicks) / (double)freq);
            lastTicks = now;

            // Pump OS events before opening the frame so handlers that touch the device
            // (e.g. FramebufferResize -> device.ResizeMainWindow) never run while a frame is open.
            InternalWindow.DoEvents();
            Update?.Invoke(delta);
            WindowInputHandler?.LateUpdate();

            Frame frame = Graphics.Device.BeginFrame();
            Graphics.CurrentFrame = frame;

            Render?.Invoke(delta);
            PostRender?.Invoke(delta);

            Graphics.SubmitPendingMipmaps(frame);

            Graphics.Device.EndFrame(frame);
            Graphics.CurrentFrame = null;
            Graphics.Device.SwapBuffers();

            Rendering.RenderProfiler.FlushPendingCapture();
        }
    }

    public static void Stop() => InternalWindow.Close();

    public static void OnLoad()
    {
        Graphics.Device = DeviceCreateUtilities.CreateDevice(InternalWindow, s_deviceOptions, Backend);
        Graphics.Device.SyncToVerticalBlank = s_deviceOptions.SyncToVerticalBlank;
        Graphics.Device.OnMissingProperty += (shader, compute, name, expectedKind, set, bindingIndex) =>
            Debug.LogWarning($"Missing shader property '{name}' ({expectedKind}) at set {set}, binding {bindingIndex} for {(shader?.Name ?? compute?.Name ?? "<unknown>")}");

        Graphics.QueryDeviceLimits();

        InternalInput = InternalWindow.CreateInput();
        WindowInputHandler = new DefaultInputHandler(InternalInput);
        Input.PushHandler(WindowInputHandler);
        TrySetWindowIcon();
    }

    // GLFW doesn't use the executable's icon for the window/taskbar, so set it explicitly from a
    // raw-RGBA pack embedded in the entry assembly (the editor ships "Resources/prowl-icon.rgba").
    // Format: int32 count, then per image: int32 width, int32 height, width*height*4 straight-alpha RGBA.
    private static void TrySetWindowIcon()
    {
        try
        {
            var asm = System.Reflection.Assembly.GetEntryAssembly();
            string name = System.Array.Find(asm?.GetManifestResourceNames() ?? System.Array.Empty<string>(),
                n => n.EndsWith(".prowl-icon.rgba", StringComparison.Ordinal));
            if (name == null) return;

            using var stream = asm!.GetManifestResourceStream(name);
            if (stream == null) return;
            using var br = new System.IO.BinaryReader(stream);

            int count = br.ReadInt32();
            var icons = new Silk.NET.Core.RawImage[count];
            for (int i = 0; i < count; i++)
            {
                int w = br.ReadInt32();
                int h = br.ReadInt32();
                byte[] pixels = br.ReadBytes(w * h * 4);
                icons[i] = new Silk.NET.Core.RawImage(w, h, pixels);
            }
            InternalWindow.SetWindowIcon(icons);
        }
        catch { /* no icon shipped, or the platform doesn't support it - keep the default */ }
    }

    public static void OnResize(Vector2D<int> size) => Resize?.Invoke(size);
    public static void OnFramebufferResize(Vector2D<int> size) => FramebufferResize?.Invoke(size);

    public static void OnClose()
    {
        // Stop background asset loading first so no load runs during teardown (it would
        // otherwise race scene unload and try to submit GPU work after the render thread exits).
        AssetLoader.Stop();
        Closing?.Invoke();
        WindowInputHandler.Dispose();
        Graphics.Device?.Dispose();
    }
}
