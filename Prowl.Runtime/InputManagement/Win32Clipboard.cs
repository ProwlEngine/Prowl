// This file is part of the Prowl Game Engine
// Licensed under the MIT License. See the LICENSE file in the project root for details.

using System.Runtime.InteropServices;
using System.Runtime.Versioning;

namespace Prowl.Runtime;

/// <summary>
/// Queries what the Windows clipboard holds without opening it.
/// </summary>
[SupportedOSPlatform("windows")]
internal static partial class Win32Clipboard
{
    private const uint CF_UNICODETEXT = 13;

    /// <summary>True when the clipboard holds text, including text Windows can synthesize.</summary>
    public static bool HasText() => IsClipboardFormatAvailable(CF_UNICODETEXT);

    [LibraryImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static partial bool IsClipboardFormatAvailable(uint format);
}
