// This file is part of the Prowl Game Engine
// Licensed under the MIT License. See the LICENSE file in the project root for details.

using System;
using System.Runtime.InteropServices;

using Silk.NET.Input;
using Silk.NET.Maths;
using Silk.NET.Windowing;

namespace Prowl.Runtime;

/// <summary>How the game window occupies the screen, see <see cref="Window.Mode"/>.</summary>
public enum WindowMode { Windowed, Fullscreen, Borderless }

public static class Window
{

    public static IWindow InternalWindow { get; internal set; }
    public static IInputContext InternalInput { get; internal set; }

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

    /// <summary>Asked when the window is about to close. Returning false keeps it open.</summary>
    public static Func<bool>? CloseRequested;

    private static bool s_closeConfirmed;

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

    private static WindowMode s_mode = WindowMode.Windowed;
    private static Vector2D<int> s_windowedPosition;
    private static Vector2D<int> s_windowedSize;

    /// <summary>
    /// Windowed, exclusive fullscreen, or a borderless window covering its monitor. Leaving fullscreen or
    /// borderless restores the last windowed position and size. Ignored without a window (headless), and
    /// ignored with a warning during editor play mode, where the window belongs to the editor.
    /// </summary>
    public static WindowMode Mode
    {
        get => s_mode;
        set
        {
            if (InternalWindow == null || s_mode == value) return;
            if (Application.IsEditor && Application.IsPlaying)
            {
                Debug.LogWarningOnce("Window.Mode.Editor", "Window.Mode is ignored in editor play mode, the window belongs to the editor.");
                return;
            }

            if (s_mode == WindowMode.Windowed)
            {
                s_windowedPosition = InternalWindow.Position;
                s_windowedSize = InternalWindow.Size;
            }
            s_mode = value;

            switch (value)
            {
                case WindowMode.Fullscreen:
                    InternalWindow.WindowBorder = WindowBorder.Resizable;
                    InternalWindow.WindowState = WindowState.Fullscreen;
                    break;

                case WindowMode.Borderless:
                    InternalWindow.WindowState = WindowState.Normal;
                    InternalWindow.WindowBorder = WindowBorder.Hidden;
                    if (InternalWindow.Monitor is { } monitor)
                    {
                        InternalWindow.Position = monitor.Bounds.Origin;
                        InternalWindow.Size = monitor.Bounds.Size;
                    }
                    break;

                default:
                    InternalWindow.WindowState = WindowState.Normal;
                    InternalWindow.WindowBorder = WindowBorder.Resizable;
                    if (s_windowedSize.X > 0 && s_windowedSize.Y > 0)
                    {
                        InternalWindow.Size = s_windowedSize;
                        InternalWindow.Position = s_windowedPosition;
                    }
                    break;
            }
        }
    }

    public static bool IsVisible
    {
        get { return InternalWindow.IsVisible; }
        set { InternalWindow.IsVisible = value; }
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

    public static void InitWindow(string title, int width, int height, WindowState startState = WindowState.Normal, bool vsync = false)
    {
        WindowOptions options = WindowOptions.Default;
        options.Title = title;
        options.Size = new Vector2D<int>(width, height);
        options.WindowState = startState;
        s_mode = startState == WindowState.Fullscreen ? WindowMode.Fullscreen : WindowMode.Windowed;
        options.VSync = vsync;
        Application.VSync = vsync;
        options.API = new GraphicsAPI(ContextAPI.OpenGL, ContextProfile.Core, ContextFlags.ForwardCompatible, new APIVersion(4, 1));
        // Update / Render are driven manually from MainLoop SwapBuffers happens
        // on the render thread.
        options.ShouldSwapAutomatically = false;
        InternalWindow = Silk.NET.Windowing.Window.Create(options);

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

    public static void Start()
    {
        InternalWindow.Initialize();
        // Silk.NET's automatic Render path (which would apply options.VSync) doesn't run under our
        // manual loop, so the swap interval is ours to set. The render thread owns the context from
        // here on, so it applies this at the first frame end, which is the warmup frame below.
        Graphics.SetSwapInterval(Application.VSync ? 1 : 0);
        Graphics.StartRenderThread();

        // Load runs as a warmup frame so SubmitAndWait (shader compiles, FBO checks)
        // has a live render thread to drain its CBs; otherwise it would deadlock.
        Graphics.BeginFrame();
        Load?.Invoke();
        Graphics.EndFrameAndWait();

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
        while (!ShouldExit())
        {
            long now = System.Diagnostics.Stopwatch.GetTimestamp();
            float delta = (float)((now - lastTicks) / (double)freq);
            lastTicks = now;

            Graphics.BeginFrame();

            WindowInputHandler?.BeginFrame();
            InternalWindow.DoEvents();
            Update?.Invoke(delta);
            WindowInputHandler?.LateUpdate();
            Render?.Invoke(delta);
            PostRender?.Invoke(delta);

            // SwapBuffers runs on the render thread as part of the frame-end
            // sentinel no context handoff per frame.
            Graphics.EndFrameAndWait();

            Application.WaitForNextFrame();
        }
    }

    /// <summary>Closes the window. A forced stop skips <see cref="CloseRequested"/>.</summary>
    public static void Stop(bool force = false)
    {
        s_closeConfirmed |= force;
        InternalWindow.Close();
    }

    private static bool ShouldExit()
    {
        if (!InternalWindow.IsClosing) return false;
        if (s_closeConfirmed || CloseRequested == null) return true;

        bool close;
        try { close = CloseRequested(); }
        catch (Exception ex)
        {
            Debug.LogError($"A close handler threw, so the window closes anyway: {ex}");
            close = true;
        }

        if (!close) InternalWindow.IsClosing = false;
        return close;
    }

    public static void OnLoad()
    {
        InternalInput = InternalWindow.CreateInput();
        WindowInputHandler = new DefaultInputHandler(InternalInput);
        Graphics.Initialize(false);
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
        Application.TargetFrameRate = 0; // and with it the finer system timer a limit holds

        Closing?.Invoke();
        WindowInputHandler.Dispose();
        Graphics.Dispose();
    }
}
