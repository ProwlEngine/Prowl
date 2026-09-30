// This file is part of the Prowl Game Engine
// Licensed under the MIT License. See the LICENSE file in the project root for details.

#if WINDOWS

using System;
using System.ComponentModel;
using System.Runtime.InteropServices;

namespace Prowl.Runtime.Core.Platforms;

/// <summary>
/// Windows platform utilities.
/// </summary>
public static partial class WindowsPlatform
{
    /// <summary>
    /// File operation types accepted by SHFILEOPSTRUCT.wFunc.
    /// </summary>
    public enum FileOperationType : uint
    {
        /// <summary>Moves the files specified in pFrom to the location specified in pTo.</summary>
        FO_MOVE = 0x0001,

        /// <summary>Copies the files specified in pFrom to the location specified in pTo.</summary>
        FO_COPY = 0x0002,

        /// <summary>Deletes the files specified in pFrom.</summary>
        FO_DELETE = 0x0003,

        /// <summary>Renames the file specified in pFrom to the name specified in pTo.</summary>
        FO_RENAME = 0x0004
    }

    /// <summary>
    /// Flags that control the behavior of SHFileOperation (SHFILEOPSTRUCT.fFlags).
    /// </summary>
    [Flags]
    public enum FileOperationFlags : ushort
    {
        /// <summary>No special flags set.</summary>
        None = 0x0000,

        /// <summary>
        /// Preserves undo information, if possible.
        /// If this flag is not set, a delete operation permanently deletes the file instead of recycling it.
        /// </summary>
        FOF_ALLOWUNDO = 0x0040,

        /// <summary>
        /// Performs the operation on files only if a wildcard file name (*.*) is specified.
        /// </summary>
        FOF_FILESONLY = 0x0080,

        /// <summary>
        /// Responds with "Yes to All" for any dialog box that is displayed.
        /// </summary>
        FOF_NOCONFIRMATION = 0x0010,

        /// <summary>
        /// Does not confirm the creation of a new directory if the operation requires one to be created.
        /// </summary>
        FOF_NOCONFIRMMKDIR = 0x0200,

        /// <summary>
        /// Do not display a user interface if an error occurs.
        /// </summary>
        FOF_NOERRORUI = 0x0400,

        /// <summary>
        /// Do not copy the security attributes of the file. The file receives the security attributes of its new folder.
        /// </summary>
        FOF_NOCOPYSECURITYATTRIBS = 0x0800,

        /// <summary>
        /// Only operate in the local directory. Do not operate recursively into subdirectories.
        /// </summary>
        FOF_NORECURSION = 0x1000,

        /// <summary>
        /// Do not move connected files as a group. Only move the specified files.
        /// </summary>
        FOF_NO_CONNECTED_ELEMENTS = 0x2000,

        /// <summary>
        /// Send a warning if a file is being permanently destroyed during a delete operation rather than recycled.
        /// </summary>
        FOF_WANTNUKEWARNING = 0x4000,

        /// <summary>
        /// Treat reparse points as objects rather than containers.
        /// </summary>
        FOF_NORECURSEREPARSE = 0x8000,

        /// <summary>
        /// Give the file being operated on a new name in a collision situation ("Copy of...").
        /// </summary>
        FOF_RENAMEONCOLLISION = 0x0008,

        /// <summary>
        /// Does not display a progress dialog box to the user.
        /// </summary>
        FOF_SILENT = 0x0004,

        /// <summary>
        /// Displays a progress dialog box but does not show the file names.
        /// </summary>
        FOF_SIMPLEPROGRESS = 0x0100,

        /// <summary>
        /// If FOF_RENAMEONCOLLISION is specified, causes hNameMappings to receive an array of mapping objects.
        /// </summary>
        FOF_WANTMAPPINGHANDLE = 0x0020
    }

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    private struct SHFILEOPSTRUCT
    {
        public IntPtr hwnd;
        public FileOperationType wFunc;
        public IntPtr pFrom;
        public IntPtr pTo;
        public FileOperationFlags fFlags;
        [MarshalAs(UnmanagedType.Bool)]
        public bool fAnyOperationsAborted;
        public IntPtr hNameMappings;
        public IntPtr lpszProgressTitle;
    }

    [LibraryImport("shell32.dll", EntryPoint = "SHFileOperationW")]
    private static partial int SHFileOperation(in SHFILEOPSTRUCT lpFileOp);

    /// <summary>
    /// Sends a file to the recycle bin. Works with Directories recursively.
    /// </summary>
    /// <param name="fullPath">The path to delete.</param>
    public static void DeleteSafe(string fullPath)
    {
        // SHFileOperation requires double null-terminated string (\0\0)
        string doubleNullTerminatedPath = fullPath + "\0\0";
        IntPtr pFrom = Marshal.StringToHGlobalUni(doubleNullTerminatedPath);

        try
        {
            var fileOp = new SHFILEOPSTRUCT
            {
                wFunc = FileOperationType.FO_DELETE,
                pFrom = pFrom,
                pTo = IntPtr.Zero,
                fFlags = FileOperationFlags.FOF_ALLOWUNDO | FileOperationFlags.FOF_NOCONFIRMATION | FileOperationFlags.FOF_SILENT,
                fAnyOperationsAborted = false,
                hNameMappings = IntPtr.Zero,
                lpszProgressTitle = IntPtr.Zero
            };

            int result = SHFileOperation(in fileOp);

            if (result != 0)
            {
                throw new Win32Exception(result, $"SHFileOperation failed to send '{fullPath}' to Recycle Bin.");
            }

            if (fileOp.fAnyOperationsAborted)
            {
                throw new OperationCanceledException($"Operation to recycle '{fullPath}' was aborted.");
            }
        }
        finally
        {
            Marshal.FreeHGlobal(pFrom);
        }
    }
}

#endif
