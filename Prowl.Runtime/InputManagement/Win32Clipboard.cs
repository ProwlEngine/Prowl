// This file is part of the Prowl Game Engine
// Licensed under the MIT License. See the LICENSE file in the project root for details.

using System;
using System.Runtime.InteropServices;
using System.Runtime.Versioning;

namespace Prowl.Runtime;

/// <summary>
/// Reads the Windows clipboard directly, so it is always closed again.
/// </summary>
/// <remarks>
/// GLFW reports clipboard failures through its error callback, which Silk raises as a managed
/// exception from inside native code. Unwinding out of the callback skips the CloseClipboard that
/// follows it, leaving the clipboard open and owned by this process and locking every other
/// application out of it. So we handle reading clipboard manually to circumvent that issue.
/// </remarks>
[SupportedOSPlatform("windows")]
internal static partial class Win32Clipboard
{
    private const uint CF_UNICODETEXT = 13;

    /// <summary>Clipboard text, or null when there is none to read.</summary>
    public static string? ReadText()
    {
        if (!IsClipboardFormatAvailable(CF_UNICODETEXT)) return null;
        if (!OpenClipboard(IntPtr.Zero)) return null;

        try
        {
            IntPtr handle = GetClipboardData(CF_UNICODETEXT);
            if (handle == IntPtr.Zero) return null;

            IntPtr text = GlobalLock(handle);
            if (text == IntPtr.Zero) return null;

            try { return Marshal.PtrToStringUni(text); }
            finally { GlobalUnlock(handle); }
        }
        finally { CloseClipboard(); }
    }

    [LibraryImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static partial bool IsClipboardFormatAvailable(uint format);

    [LibraryImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static partial bool OpenClipboard(IntPtr newOwner);

    [LibraryImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static partial bool CloseClipboard();

    [LibraryImport("user32.dll")]
    private static partial IntPtr GetClipboardData(uint format);

    [LibraryImport("kernel32.dll")]
    private static partial IntPtr GlobalLock(IntPtr handle);

    [LibraryImport("kernel32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static partial bool GlobalUnlock(IntPtr handle);
}
