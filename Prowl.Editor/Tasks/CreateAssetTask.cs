// This file is part of the Prowl Game Engine
// Licensed under the MIT License. See the LICENSE file in the project root for details.

using System;
using System.Collections.Generic;
using System.IO;

using Prowl.Echo;
using Prowl.Editor.GUI;
using Prowl.Editor.GUI.Popups;
using Prowl.Runtime;

using static System.Net.Mime.MediaTypeNames;
using Prowl.Editor.GUI.Panels;

namespace Prowl.Editor.Tasks;

public class CreateAssetTask : EditorTask
{

    public enum AssetType
    {
        Asset,
        Folder,
        Script,
        Shader,
    }

    public AssetType TaskType = AssetType.Asset;

    public void StartRename(ContentItem item, bool inTree = false, Action<string>? onConfirm = null, Action? onCancel = null)
    {
        string id = inTree ? $"proj_folder_{item.RelativePath}" : $"proj_asset_{item.RelativePath}";
        string editName = item.IsFolder ? item.Name : Path.GetFileNameWithoutExtension(item.Name);

        RenameOverlay.Begin(id, editName, newText =>
        {
            string newName = newText;
            if (onConfirm != null)
                onConfirm(newName);
        }, onCancel);
    }

    public async void BeginCreateTask(Prowl.Editor.CreateAssetMenuRegistry.Entry entry, string relativeFolder)
    {
        var panel = ProjectPanel.Instance;
        if (panel != null)
        {
            string newName = entry.Name;
            string? renameResult = null;
            bool finished = false;
            var item = new ContentItem()
            {
                Name = newName,
                Icon = TaskType switch
                {
                    AssetType.Asset => FileIconRegistry.GetIconForExtension(".asset"),
                    AssetType.Shader => FileIconRegistry.GetIconForExtension(".shader"),
                    AssetType.Folder => EditorIcons.Folder,
                    _ => null
                }
            };
            panel.VirtualContentItems.Add(item);

            StartRename(item, false, (n) =>
            {
                renameResult = n;
                finished = true;
            },
            () => finished = true);

            await IdleOnCondition(() => finished);

            if (!string.IsNullOrEmpty(renameResult))
            {
                var path = TaskType switch
                {
                    AssetType.Asset => CreateAsset(entry, panel.CurrentFolder, renameResult),
                    AssetType.Shader => CreateShader(renameResult, panel.CurrentFolder),
                    AssetType.Folder => CreateFolder(renameResult, panel.CurrentFolder),
                    _ => null
                };
            }

            panel.VirtualContentItems.Remove(item);
        }
    }

    public static string? CreateShader(string shaderName, string relativeFolder)
    {
        string absFolder = AssetCreateMenu.GetAbsoluteFolder(relativeFolder);
        if (!Directory.Exists(absFolder)) return null;

        string name = AssetCreateMenu.FindUniqueName(absFolder, shaderName, ".shader");
        string filePath = Path.Combine(absFolder, name);

        var stream = EditorApplication.GetEmbeddedResource("NewShader.template");

        using (StreamReader reader = new StreamReader(stream))
        {
            File.WriteAllText(filePath, reader.ReadToEnd().Replace("{[shaderName]}", shaderName));
        }

        return string.IsNullOrEmpty(relativeFolder) ? name : relativeFolder + "/" + name;
    }

    public static string? CreateFolder(string folderName, string relativeFolder)
    {
        string absFolder = AssetCreateMenu.GetAbsoluteFolder(relativeFolder);
        if (!Directory.Exists(absFolder)) return null;

        string name = AssetCreateMenu.FindUniqueName(absFolder, folderName, "");
        string newPath = Path.Combine(absFolder, name);
        Directory.CreateDirectory(newPath);
        MetaFile.EnsureMeta(newPath, "DefaultImporter");
        Debug.Log($"Created folder: {name}");
        string relPath = string.IsNullOrEmpty(relativeFolder) ? name : relativeFolder + "/" + name;
        return relPath;
    }

    /// <summary>
    /// Create an asset file on disk for the given registry entry.
    /// Returns the relative path on success, null on failure.
    /// </summary>
    private static string? CreateAsset(Prowl.Editor.CreateAssetMenuRegistry.Entry entry, string relativeFolder, string? filename = null)
    {
        string absFolder = AssetCreateMenu.GetAbsoluteFolder(relativeFolder);
        if (!Directory.Exists(absFolder)) return null;

        string name = AssetCreateMenu.FindUniqueName(absFolder, filename ?? $"New {entry.Name}", entry.Extension);
        string filePath = Path.Combine(absFolder, name);

        try
        {
            var instance = Activator.CreateInstance(entry.Type);
            var echo = Serializer.Serialize(typeof(object), instance);
            if (echo != null)
                File.WriteAllText(filePath, echo.WriteToString());

            Debug.Log($"Created {entry.Name}: {name}");
            return string.IsNullOrEmpty(relativeFolder) ? name : relativeFolder + "/" + name;
        }
        catch (Exception ex)
        {
            Debug.LogError($"Failed to create {entry.Name}: {ex.Message}");
            return null;
        }
    }

}
