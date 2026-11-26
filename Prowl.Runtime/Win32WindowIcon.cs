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
    private const int GCLP_HICON = -14;
    private const int GCLP_HICONSM = -34;

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

    [LibraryImport("user32.dll")]
    private static partial IntPtr SendMessageW(IntPtr hwnd, uint message, nint wParam, nint lParam);

    [LibraryImport("user32.dll")]
    private static partial IntPtr SetClassLongPtrW(IntPtr hwnd, int index, IntPtr value);

    [LibraryImport("user32.dll")]
    private static partial IntPtr CopyIcon(IntPtr icon);
}
