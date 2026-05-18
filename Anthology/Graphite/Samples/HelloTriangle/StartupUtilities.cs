using System;

using Silk.NET.SDL;

namespace NeoVeldrid.Samples.HelloTriangle;

public enum WindowState
{
    Normal,
    FullScreen,
    Maximized,
    Minimized,
    BorderlessFullScreen,
    Hidden,
}

public struct WindowCreateInfo
{
    public int X;
    public int Y;
    public int WindowWidth;
    public int WindowHeight;
    public WindowState WindowInitialState;
    public string WindowTitle;
}

public sealed unsafe class Sdl2Window : IDisposable
{
    private readonly Sdl _sdl;
    private Window* _handle;

    internal Sdl2Window(Sdl sdl, Window* handle, int width, int height)
    {
        _sdl = sdl;
        _handle = handle;
        Width = width;
        Height = height;
    }

    public Sdl Sdl => _sdl;
    public Window* Handle => _handle;
    public nint SdlWindowHandle => (nint)_handle;
    public int Width { get; private set; }
    public int Height { get; private set; }

    public void Resized(int width, int height)
    {
        Width = width;
        Height = height;
    }

    public void Close() => Dispose();

    public void Dispose()
    {
        if (_handle != null)
        {
            _sdl.DestroyWindow(_handle);
            _handle = null;
        }
    }
}

public static unsafe class Startup
{
    private static readonly Sdl s_sdl = Sdl.GetApi();
    private static bool s_initialized;

    public static Sdl Sdl => s_sdl;

    public static void EnsureSdlInitialized()
    {
        if (!s_initialized)
        {
            s_sdl.Init(Sdl.InitVideo);
            s_initialized = true;
        }
    }

    public static Sdl2Window CreateWindow(WindowCreateInfo wci) => CreateWindow(ref wci);

    public static Sdl2Window CreateWindow(ref WindowCreateInfo wci)
    {
        EnsureSdlInitialized();

        WindowFlags flags = WindowFlags.Vulkan | WindowFlags.Opengl | WindowFlags.Resizable
            | GetWindowFlags(wci.WindowInitialState);
        if (wci.WindowInitialState != WindowState.Hidden)
        {
            flags |= WindowFlags.Shown;
        }

        int width = wci.WindowWidth > 0 ? wci.WindowWidth : 960;
        int height = wci.WindowHeight > 0 ? wci.WindowHeight : 540;

        Window* handle = s_sdl.CreateWindow(
            wci.WindowTitle ?? "NeoVeldrid",
            wci.X == 0 ? Sdl.WindowposCentered : wci.X,
            wci.Y == 0 ? Sdl.WindowposCentered : wci.Y,
            width,
            height,
            (uint)flags);

        if (handle == null)
        {
            throw new InvalidOperationException("Failed to create SDL window: " + s_sdl.GetErrorS());
        }

        return new Sdl2Window(s_sdl, handle, width, height);
    }

    private static WindowFlags GetWindowFlags(WindowState state) => state switch
    {
        WindowState.Normal => 0,
        WindowState.FullScreen => WindowFlags.Fullscreen,
        WindowState.Maximized => WindowFlags.Maximized,
        WindowState.Minimized => WindowFlags.Minimized,
        WindowState.BorderlessFullScreen => WindowFlags.FullscreenDesktop,
        WindowState.Hidden => WindowFlags.Hidden,
        _ => throw new ArgumentOutOfRangeException(nameof(state)),
    };

    public static SwapchainSource GetSwapchainSource(Sdl2Window window)
    {
        SysWMInfo info;
        s_sdl.GetVersion(&info.Version);
        if (!s_sdl.GetWindowWMInfo(window.Handle, &info))
        {
            throw new InvalidOperationException("Failed to get window WM info: " + s_sdl.GetErrorS());
        }

        return info.Subsystem switch
        {
            SysWMType.Windows => SwapchainSource.CreateWin32(info.Info.Win.Hwnd, info.Info.Win.HInstance),
            SysWMType.X11 => SwapchainSource.CreateXlib((nint)info.Info.X11.Display, (nint)info.Info.X11.Window),
            SysWMType.Wayland => SwapchainSource.CreateWayland((nint)info.Info.Wayland.Display, (nint)info.Info.Wayland.Surface),
            SysWMType.Cocoa => SwapchainSource.CreateNSWindow((nint)info.Info.Cocoa.Window),
            _ => throw new PlatformNotSupportedException("Unsupported SDL window subsystem: " + info.Subsystem),
        };
    }
}
