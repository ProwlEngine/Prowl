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

    /// <summary>
    /// Physical-to-logical pixel ratio for the window's current display.
    /// Computed as <c>FramebufferSize / Size</c> — the only value that is
    /// always correct regardless of platform or DPI-awareness mode.
    /// <para>
    /// On macOS Retina this is 2 (3024 fb / 1512 logical points).
    /// On a 1× display or a DPI-unaware Windows process (where the OS virtualises
    /// the framebuffer so fb == win) this is 1, even if the system DPI is higher.
    /// On a DPI-aware Windows process at 150% this is 1.5 (1800 fb / 1200 points).
    /// </para>
    /// </summary>
    public static float ContentScale
    {
        get
        {
            if (InternalWindow == null) return 1f;
            var fb = InternalWindow.FramebufferSize;
            var win = InternalWindow.Size;
            return win.X > 0 ? (float)fb.X / win.X : 1f;
        }
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
