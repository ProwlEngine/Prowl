// This file is part of the Prowl Game Engine
// Licensed under the MIT License. See the LICENSE file in the project root for details.

using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;

using Prowl.Editor.GUI.Panels;
using Prowl.Runtime;

namespace Prowl.Editor.Core;

/// <summary>
/// Cut, copy, paste and duplicate for Project panel items. Copies get fresh GUIDs and keep their import settings, a cut moves the originals on paste, and the results are selected.
/// </summary>
public static class AssetClipboard
{
    // Files and sub-assets by GUID, so a rename or move before the paste still finds them. Folders have no GUID lookup, so by path
    private static readonly List<Guid> s_assets = [];
    private static readonly List<string> s_folders = [];
    private static bool s_isCut;

    // Folder paths mean nothing in another project, so what was held under an earlier backend is dropped
    private static EditorAssetBackend? s_owner;

    /// <summary>
    /// True while anything is copied or cut.
    /// </summary>
    public static bool HasContent => ReferenceEquals(s_owner, EditorAssetBackend.Instance) && (s_assets.Count > 0 || s_folders.Count > 0);

    /// <summary>
    /// Replace the clipboard with the items, to be copied on paste.
    /// </summary>
    public static void Copy(IEnumerable<ContentItem> items) => Hold(items, false);

    /// <summary>
    /// Replace the clipboard with the items, to be moved on paste. Sub-assets are skipped.
    /// </summary>
    public static void Cut(IEnumerable<ContentItem> items) => Hold(items, true);

    /// <summary>
    /// Empty the clipboard if it holds a cut.
    /// </summary>
    public static void CancelCut()
    {
        if (s_isCut) Clear();
    }

    /// <summary>
    /// True when the item is held by a cut, so the panel can dim it.
    /// </summary>
    public static bool IsCut(ContentItem item)
    {
        if (!s_isCut || !HasContent) return false;

        return item.IsFolder
            ? s_folders.Contains(item.RelativePath, StringComparer.OrdinalIgnoreCase)
            : s_assets.Contains(item.Guid);
    }

    /// <summary>
    /// Paste what is held into a folder (assets-relative; empty = root). A copy stays on the clipboard, a cut is moved and then cleared.
    /// </summary>
    public static void Paste(string destinationFolder)
    {
        EditorAssetBackend? db = EditorAssetBackend.Instance;
        if (db == null || !HasContent) return;

        var paths = new List<string>();
        var subAssets = new List<Guid>();
        foreach (Guid guid in s_assets)
        {
            if (db.GuidToPath(guid) is { } path)
                paths.Add(path);
            else if (db.TryGetParentGuid(guid, out _))
                subAssets.Add(guid);
            else
                Debug.LogWarning($"Skipped: asset '{guid}' no longer exists.");
        }

        foreach (string folder in s_folders)
        {
            if (Directory.Exists(AssetCreateMenu.GetAbsoluteFolder(folder)))
                paths.Add(folder);
            else
                Debug.LogWarning($"Skipped: folder '{folder}' no longer exists.");
        }

        paths = OutsideOtherFolders(paths);

        List<string> results;
        if (s_isCut)
        {
            results = db.MoveAssets(paths, destinationFolder);
            Clear();
        }
        else
        {
            results = db.CopyAssets([.. paths.Select(path => (path, destinationFolder))]);
            foreach (Guid subAsset in subAssets)
                if (db.ExtractSubAsset(subAsset, destinationFolder) is { } extracted)
                    results.Add(extracted);
        }

        Reveal(db, results);
    }

    /// <summary>
    /// Copy each item beside itself, without touching the clipboard.
    /// </summary>
    public static void Duplicate(IEnumerable<ContentItem> items)
    {
        EditorAssetBackend? db = EditorAssetBackend.Instance;
        if (db == null) return;

        var paths = new List<string>();
        var subAssets = new List<(Guid Guid, string Folder)>();
        foreach (ContentItem item in items)
        {
            if (!item.IsSubAsset)
                paths.Add(item.RelativePath);
            else if (db.GuidToPath(item.ParentGuid) is { } parentPath)
                subAssets.Add((item.Guid, ParentFolder(parentPath)));
        }

        List<string> results = db.CopyAssets([.. OutsideOtherFolders(paths).Select(path => (path, ParentFolder(path)))]);
        foreach ((Guid guid, string folder) in subAssets)
            if (db.ExtractSubAsset(guid, folder) is { } extracted)
                results.Add(extracted);

        Reveal(db, results);
    }

    private static void Hold(IEnumerable<ContentItem> items, bool isCut)
    {
        Clear();
        s_isCut = isCut;
        s_owner = EditorAssetBackend.Instance;

        foreach (ContentItem item in items)
        {
            if (item.IsFolder)
            {
                if (item.RelativePath.Length > 0) s_folders.Add(item.RelativePath);
                continue;
            }

            if (item.Guid == Guid.Empty) continue;

            if (isCut && item.IsSubAsset)
            {
                Debug.LogWarning($"Skipped: '{item.Name}' is a sub-asset and can't move out of its file.");
                continue;
            }

            s_assets.Add(item.Guid);
        }
    }

    private static void Clear()
    {
        s_assets.Clear();
        s_folders.Clear();
        s_isCut = false;
    }

    private static string ParentFolder(string path) => Path.GetDirectoryName(path)?.Replace('\\', '/') ?? "";

    // An item inside a folder that is also held travels with that folder, so moving or copying it too would pull it out
    private static List<string> OutsideOtherFolders(List<string> paths)
        => [.. paths.Where(path => !paths.Any(folder => path.StartsWith(folder + "/", StringComparison.OrdinalIgnoreCase)))];

    private static void Reveal(EditorAssetBackend db, List<string> paths)
    {
        if (paths.Count == 0) return;

        Selection.Clear();
        Guid first = Guid.Empty;
        foreach (string path in paths)
        {
            Guid guid = db.PathToGuid(path);
            Selection.AddToSelection(new ContentItem
            {
                Name = Path.GetFileName(path),
                RelativePath = path,
                IsFolder = Directory.Exists(AssetCreateMenu.GetAbsoluteFolder(path)),
                Guid = guid,
            });
            if (first == Guid.Empty) first = guid;
        }

        if (first != Guid.Empty) Selection.Ping(first);
    }
}
