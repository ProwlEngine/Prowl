// This file is part of the Prowl Game Engine
// Licensed under the MIT License. See the LICENSE file in the project root for details.

using System;
using System.IO;

using Prowl.Editor.GUI.Panels;
using Prowl.Editor.GUI.Popups;
using Prowl.Editor.Utils;
using Prowl.Runtime;

namespace Prowl.Editor.Core.Tasks;

/// <summary>
/// Creates a new asset or folder the way every Create menu item does: a placeholder is named inline in the Project
/// panel, then the file is written under a safe, unique version of that name, imported, selected and pinged.
/// Without a Project panel the default name is used straight away.
/// </summary>
public class CreateAssetTask : EditorTask
{
    /// <summary>Writes the new file or folder at an absolute path, given its name without extension. Returns false when it could not be created.</summary>
    public delegate bool Writer(string absolutePath, string name);

    /// <summary>Starts naming and creating an asset in <paramref name="relativeFolder"/>. Extension is empty for a folder.</summary>
    public static void Begin(string relativeFolder, string defaultName, string extension, Writer write, bool isFolder = false)
        => new CreateAssetTask().Run(relativeFolder, defaultName, extension, write, isFolder);

    private async void Run(string relativeFolder, string defaultName, string extension, Writer write, bool isFolder)
    {
        string absoluteFolder = AssetCreateMenu.GetAbsoluteFolder(relativeFolder);
        if (!Directory.Exists(absoluteFolder)) return;

        string? chosen = defaultName;
        ProjectPanel? panel = ProjectPanel.Instance;
        if (panel != null)
        {
            panel.NavigateTo(relativeFolder);
            var placeholder = new ContentItem
            {
                Name = defaultName + extension,
                RelativePath = Combine(relativeFolder, defaultName + extension),
                IsFolder = isFolder,
            };
            panel.VirtualContentItems.Add(placeholder);

            chosen = null;
            bool finished = false;
            RenameOverlay.Begin($"proj_asset_{placeholder.RelativePath}", defaultName, n => { chosen = n; finished = true; }, () => finished = true);
            await IdleOnCondition(() => finished);
            panel.VirtualContentItems.Remove(placeholder);
            if (chosen == null) return;
        }

        string name = AssetCreateMenu.FindUniqueName(absoluteFolder, EditorUtils.SafeFileName(chosen, defaultName), extension);
        string absolutePath = Path.Combine(absoluteFolder, name);
        try
        {
            if (!write(absolutePath, Path.GetFileNameWithoutExtension(name))) return;
        }
        catch (Exception ex)
        {
            Debug.LogError($"Failed to create '{name}': {ex.Message}");
            return;
        }

        string relativePath = Combine(relativeFolder, name);
        EditorAssetBackend? db = EditorAssetBackend.Instance;
        if (db == null) return;
        db.InvalidateFolderIndex();
        Guid guid = isFolder ? Guid.Empty : db.ImportFile(relativePath);

        Selection.Select(new ContentItem { Name = name, RelativePath = relativePath, IsFolder = isFolder, Guid = guid });
        Selection.Ping(guid);
    }

    private static string Combine(string folder, string name) => string.IsNullOrEmpty(folder) ? name : folder + "/" + name;
}
