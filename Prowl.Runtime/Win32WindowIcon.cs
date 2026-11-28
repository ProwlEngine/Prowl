// This file is part of the Prowl Game Engine
// Licensed under the MIT License. See the LICENSE file in the project root for details.

using System;
using System.Runtime.InteropServices;
using System.Runtime.Versioning;

namespace Prowl.Runtime;

/// <summary>
/// The taskbar shows a window's class icon, which GLFW only fills from a resource named "GLFW_ICON", so the class
/// takes copies of the icons GLFW gave the window.
/// </summary>
[SupportedOSPlatform("windows")]
internal static partial class Win32WindowIcon
{
    private const uint WM_GETICON = 0x007F;
    private const nint ICON_SMALL = 0;
    private const nint ICON_BIG = 1;
    private const int GCLP_HBRBACKGROUND = -10;
    private const int GCLP_HICON = -14;
    private const int GCLP_HICONSM = -34;
    private const int BLACK_BRUSH = 4;
    private const int DWMWA_USE_IMMERSIVE_DARK_MODE = 20;

    /// <summary>
    /// Gives the window a dark title bar and a black class background brush before it is shown, so DWM never flashes a
    /// white frame or unpainted redirection surface while latching onto the swapchain.
    /// </summary>
    public static void ApplyDarkChrome(IntPtr hwnd)
    {
        try
        {
            int darkMode = 1;
            _ = DwmSetWindowAttribute(hwnd, DWMWA_USE_IMMERSIVE_DARK_MODE, ref darkMode, sizeof(int));

            IntPtr brush = GetStockObject(BLACK_BRUSH);
            if (brush != IntPtr.Zero)
            {
                if (IntPtr.Size == 8) SetClassLongPtrW(hwnd, GCLP_HBRBACKGROUND, brush);
                else _ = SetClassLongW(hwnd, GCLP_HBRBACKGROUND, brush.ToInt32());
            }
        }
        catch { }
    }

    public static void CopyToClass(IntPtr hwnd)
    {
        CopyOne(hwnd, ICON_BIG, GCLP_HICON);
        CopyOne(hwnd, ICON_SMALL, GCLP_HICONSM);
    }

    // A copy, because GLFW destroys the window's own icons when it replaces them.
    private static void CopyOne(IntPtr hwnd, nint kind, int slot)
    {
        IntPtr icon = SendMessageW(hwnd, WM_GETICON, kind, 0);
        if (icon != IntPtr.Zero) SetClassLongPtrW(hwnd, slot, CopyIcon(icon));
    }

    [LibraryImport("dwmapi.dll")]
    private static partial int DwmSetWindowAttribute(IntPtr hwnd, int attr, ref int value, int size);

    [LibraryImport("gdi32.dll")]
    private static partial IntPtr GetStockObject(int index);

    [LibraryImport("user32.dll")]
    private static partial IntPtr SendMessageW(IntPtr hwnd, uint message, nint wParam, nint lParam);

    [LibraryImport("user32.dll")]
    private static partial int SetClassLongW(IntPtr hwnd, int index, int value);

    [LibraryImport("user32.dll")]
    private static partial IntPtr SetClassLongPtrW(IntPtr hwnd, int index, IntPtr value);

    [LibraryImport("user32.dll")]
    private static partial IntPtr CopyIcon(IntPtr icon);
}
