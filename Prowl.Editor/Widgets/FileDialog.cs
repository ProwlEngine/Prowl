// This file is part of the Prowl Game Engine
// Licensed under the MIT License. See the LICENSE file in the project root for details.
//
// Thin wrapper around OrigamiFileDialog that wires in editor-specific config.

using System;
using System.IO;
using System.Linq;

using Prowl.OrigamiUI;
using Prowl.PaperUI;

namespace Prowl.Editor.Widgets;

public static class FileDialog
{
    private static FileDialogConfig? s_config;

    public static bool IsOpen => OrigamiFileDialog.IsOpen;

    public static void Open(FileDialogMode mode, Action<string?> onComplete,
        string? startPath = null, string[]? filters = null, string[]? filterLabels = null)
    {
        s_config ??= BuildEditorConfig();
        OrigamiFileDialog.Open(mode, onComplete, startPath, filters, filterLabels, s_config);
    }

    public static void Close(string? result = null) => OrigamiFileDialog.Close(result);

    private static FileDialogConfig BuildEditorConfig() => new()
    {
        GetIcon = (ext, isDir) => isDir ? EditorIcons.Folder : FileIconRegistry.GetIconForFile("file" + ext),
        QuickAccess =
        [
            ("Desktop", EditorIcons.Desktop, Environment.GetFolderPath(Environment.SpecialFolder.Desktop)),
            ("Documents", EditorIcons.FolderOpen, Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments)),
            ("Downloads", EditorIcons.Download, Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), "Downloads")),
            ("User", EditorIcons.User, Environment.GetFolderPath(Environment.SpecialFolder.UserProfile)),
        ],
        GetDrives = () => DriveInfo.GetDrives().Where(d => d.IsReady).Select(d => (d.Name, d.Name)).ToArray(),
    };
}
