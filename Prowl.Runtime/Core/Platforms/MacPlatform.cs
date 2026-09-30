// This file is part of the Prowl Game Engine
// Licensed under the MIT License. See the LICENSE file in the project root for details.

#if MACOS

using System;
using System.IO;
using System.Runtime.InteropServices;

namespace Prowl.Runtime.Core.Platforms;

/// <summary>
/// Mac platform utilities.
/// </summary>
public static partial class MacPlatform
{
    private const string ObjCRuntime = "libobjc";

    [LibraryImport(ObjCRuntime, EntryPoint = "objc_getClass", StringMarshalling = StringMarshalling.Utf8)]
    private static partial IntPtr objc_getClass(string className);

    [LibraryImport(ObjCRuntime, EntryPoint = "sel_registerName", StringMarshalling = StringMarshalling.Utf8)]
    private static partial IntPtr sel_registerName(string selectorName);

    [LibraryImport(ObjCRuntime, EntryPoint = "objc_msgSend")]
    private static partial IntPtr objc_msgSend(IntPtr receiver, IntPtr selector);

    [LibraryImport(ObjCRuntime, EntryPoint = "objc_msgSend")]
    private static partial IntPtr objc_msgSend(IntPtr receiver, IntPtr selector, IntPtr arg1);

    [LibraryImport(ObjCRuntime, EntryPoint = "objc_msgSend")]
    [return: MarshalAs(UnmanagedType.I1)]
    private static partial bool objc_msgSend_bool(IntPtr receiver, IntPtr selector, IntPtr arg1, out IntPtr outResultingUrl, out IntPtr outError);

    /// <summary>
    /// Sends a file to the recycle bin. Works with Directories recursively.
    /// </summary>
    /// <param name="fullPath">The path to delete.</param>
    public static void DeleteSafe(string fullPath)
    {
        // Get default NSFileManager instance
        IntPtr clsFileManager = objc_getClass("NSFileManager");
        IntPtr selDefaultManager = sel_registerName("defaultManager");
        IntPtr fileManager = objc_msgSend(clsFileManager, selDefaultManager);

        // Wrap file path as NSString
        IntPtr clsNSString = objc_getClass("NSString");
        IntPtr selStringWithUtf8 = sel_registerName("stringWithUTF8String:");
        IntPtr nativePathStr = Marshal.StringToCoTaskMemUTF8(fullPath);
        IntPtr nsPath = objc_msgSend(clsNSString, selStringWithUtf8, nativePathStr);
        Marshal.FreeCoTaskMem(nativePathStr);

        // Wrap NSString as NSURL
        IntPtr clsNSURL = objc_getClass("NSURL");
        IntPtr selFileUrlWithPath = sel_registerName("fileURLWithPath:");
        IntPtr fileUrl = objc_msgSend(clsNSURL, selFileUrlWithPath, nsPath);

        // Send to Trash
        IntPtr selTrashItem = sel_registerName("trashItemAtURL:resultingItemURL:error:");
        bool success = objc_msgSend_bool(fileManager, selTrashItem, fileUrl, out _, out IntPtr errorPtr);

        if (!success || errorPtr != IntPtr.Zero)
        {
            throw new IOException($"Failed to move '{fullPath}' to the macOS Trash.");
        }
    }
}

#endif
