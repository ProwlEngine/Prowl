// This file is part of the Prowl Game Engine
// Licensed under the MIT License. See the LICENSE file in the project root for details.

using System;
using System.Runtime.InteropServices;

using Silk.NET.Input;
using Silk.NET.Maths;
using Silk.NET.Windowing;

namespace Prowl.Runtime;

public static class Window
{

    public static IWindow InternalWindow { get; internal set; }
    public static IInputContext InternalInput { get; internal set; }

    // --- Per-monitor DPI awareness on Windows ----------------------------------------------
    [DllImport("user32.dll")]
    private static extern bool SetProcessDpiAwarenessContext(nint dpiContext);

    [DllImport("shcore.dll")]
    private static extern int SetProcessDpiAwareness(int level);

    [DllImport("user32.dll")]
    private static extern bool SetProcessDPIAware();

    private static readonly nint DPI_AWARENESS_CONTEXT_PER_MONITOR_AWARE_V2 = -4;

    private static void EnsureDpiAwareOnWindows()
    {
        if (!RuntimeInformation.IsOSPlatform(OSPlatform.Windows)) return;
        try { if (SetProcessDpiAwarenessContext(DPI_AWARENESS_CONTEXT_PER_MONITOR_AWARE_V2)) return; } catch { }
        try { if (SetProcessDpiAwareness(2) == 0) return; } catch { }
        try { SetProcessDPIAware(); } catch { }
    }

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
    /// System content scale factor (1.0 = 100%, 1.25 = 125%, 1.5 = 150%, 2.0 = retina, etc.).
    /// <para>
    /// Silk.NET.GLFW 2.22 does not expose <c>glfwGetWindowContentScale</c> as a managed method,
    /// so this resolves the symbol via <see cref="INativeContext.GetProcAddress"/> and calls it
    /// directly. If that fails (non-GLFW backend, older GLFW, or the symbol isn't reachable),
    /// we fall back to the <c>FramebufferSize / Size</c> ratio which is also the correct
    /// value on macOS retina and on DPI-aware Windows (FB in physical pixels, Size in points).
    /// </para>
    /// </summary>
    public static unsafe float ContentScale
    {
        get
        {
            if (InternalWindow == null) return 1f;

            nint? nativeGlfw = InternalWindow.Native?.Glfw;
            if (nativeGlfw.HasValue && nativeGlfw.Value != 0 && TryGetContentScaleViaProc(nativeGlfw.Value, out float scale))
                return scale;

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
                // Silk.NET's INativeContext GetProcAddress falls through to the GLFW shared
                // library's own symbol table when glfwGetProcAddress returns null (i.e. for
                // non-GL symbols). Works on Windows in our testing; on macOS / Linux this path
                // may return 0 and we'll use the FB/Size fallback in the caller.
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
        get { return InternalWindow.VSync; }
        set { InternalWindow.VSync = value; }
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
        WindowOptions options = WindowOptions.Default;
        options.Title = title;
        options.Size = new Vector2D<int>(width, height);
        options.WindowState = startState;
        options.VSync = VSync;
        var api = new GraphicsAPI(ContextAPI.OpenGL, ContextProfile.Core, ContextFlags.ForwardCompatible, new APIVersion(4, 1));
        options.API = api;
        InternalWindow = Silk.NET.Windowing.Window.Create(options);

        InternalWindow.Load += OnLoad;
        InternalWindow.Update += OnUpdate;
        InternalWindow.Render += OnRender;
        InternalWindow.FocusChanged += OnFocusChanged;
        InternalWindow.Resize += OnResize;
        InternalWindow.FramebufferResize += OnFramebufferResize;
        InternalWindow.Move += OnMove;
        InternalWindow.Closing += OnClose;

        InternalWindow.StateChanged += (state) => { StateChanged?.Invoke(state); };
        InternalWindow.FileDrop += (files) => { FileDrop?.Invoke(files); };

        InternalWindow.FocusChanged += (focused) => { isFocused = focused; };
    }

    private static void OnMove(Vector2D<int> d) => Move?.Invoke(d);
    public static void Start() => InternalWindow.Run();
    public static void Stop() => InternalWindow.Close();

    public static void OnLoad()
    {
        InternalInput = InternalWindow.CreateInput();
        WindowInputHandler = new DefaultInputHandler(InternalInput);
        Graphics.Initialize(false);

        // Push Default Handler
        Input.PushHandler(WindowInputHandler);
        Load?.Invoke();
    }

    public static void OnRender(double delta)
    {
        Render?.Invoke((float)delta);
        PostRender?.Invoke((float)delta);
    }

    public static void OnFocusChanged(bool focused)
    {
        FocusChanged?.Invoke(focused);
    }

    public static void OnResize(Vector2D<int> size)
    {
        Resize?.Invoke(size);
    }

    public static void OnFramebufferResize(Vector2D<int> size)
    {
        FramebufferResize?.Invoke(size);
    }

    public static void OnUpdate(double delta)
    {
        Update?.Invoke((float)delta);
        WindowInputHandler.LateUpdate();
    }

    public static void OnClose()
    {
        Closing?.Invoke();
        WindowInputHandler.Dispose();
        Input.PopHandler();
        Graphics.Dispose();
    }

}
