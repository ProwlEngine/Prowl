// This file is part of the Prowl Game Engine
// Licensed under the MIT License. See the LICENSE file in the project root for details.

using System;
using System.Collections.Generic;
using System.IO;
using System.IO.Compression;
using System.Linq;

namespace Prowl.Runtime.Utils;

public sealed class AssetBundle : IDisposable
{
    private readonly Stream _stream;
    private readonly ZipArchive _zipArchive;

    readonly Dictionary<Guid, string> _guidToPath = new();
    readonly Dictionary<Guid, ZipArchiveEntry> _guidToEntry = new();
    readonly Dictionary<string, Guid> _pathToGuid = new(StringComparer.OrdinalIgnoreCase);
    readonly Dictionary<string, ZipArchiveEntry> _pathToEntry = new(StringComparer.OrdinalIgnoreCase);

    public AssetBundle(Stream stream, ZipArchiveMode mode)
    {
        _stream = stream;
        _zipArchive = new ZipArchive(stream, mode);

        if (mode == ZipArchiveMode.Read)
        {
            // Load all asset paths and guids into memory for faster access
            foreach (var entry in _zipArchive.Entries)
            {
                using var entryStream = entry.Open();
                string guidS = entry.Comment;
                if (Guid.TryParse(guidS, out Guid guid))
                {
                    _guidToPath[guid] = entry.FullName;
                    _guidToEntry[guid] = entry;
                    _pathToGuid[entry.FullName] = guid;
                    _pathToEntry[entry.FullName] = entry;
                }
            }
        }
    }

    public static AssetBundle CreateNew(FileInfo path)
    {
        if (File.Exists(path.FullName))
            throw new ArgumentException("File already exists.", nameof(path));

        return new AssetBundle(path.Open(FileMode.Create, FileAccess.ReadWrite), ZipArchiveMode.Update);
    }

    #region Assets
    public long byteCount;
    public float SizeInGB => byteCount / 1024f / 1024f / 1024f;
    public bool AddAsset(string assetPath, Guid guid, SerializedAsset asset)
    {
        assetPath = NormalizePath(assetPath);

        if (_pathToEntry.ContainsKey(assetPath))
        {
            // Entry already exists, update GUID and content
            return false;
        }

        var entry = _zipArchive.CreateEntry(assetPath);
        //using (var entryStream = entry.Open())
        // I guess we dont dispose these streams? Throws an error if we do
        var entryStream = entry.Open();
        entry.Comment = guid.ToString();
        asset.SaveToStream(entryStream);

        //byteCount += entryStream.Length;


        // Update mappings
        _guidToPath[guid] = assetPath;
        _guidToEntry[guid] = entry;
        _pathToGuid[assetPath] = guid;
        _pathToEntry[assetPath] = entry;

        return true;
    }

    public bool RemoveAsset(Guid guid) => _guidToPath.TryGetValue(guid, out var path) && RemoveAsset(path);

    public bool RemoveAsset(string assetPath)
    {
        assetPath = NormalizePath(assetPath);

        if (_pathToEntry.TryGetValue(assetPath, out var entry))
        {
            // Remove entry from archive
            entry.Delete();

            // Remove mappings
            var guid = _pathToGuid[assetPath];
            _guidToPath.Remove(guid);
            _guidToEntry.Remove(guid);
            _pathToGuid.Remove(assetPath);
            _pathToEntry.Remove(assetPath);

            return true;
        }

        return false;
    }

    public bool MoveFile(string assetPath, string newAssetPath)
    {
        assetPath = NormalizePath(assetPath);
        newAssetPath = NormalizePath(newAssetPath);

        if (_pathToEntry.TryGetValue(assetPath, out var entry) && !_pathToEntry.ContainsKey(newAssetPath))
        {
            // Remove existing entry from mappings
            var guid = _pathToGuid[assetPath];
            _guidToPath.Remove(guid);
            _guidToEntry.Remove(guid);
            _pathToGuid.Remove(assetPath);
            _pathToEntry.Remove(assetPath);

            // Create a new entry with the new file path
            var newEntry = _zipArchive.CreateEntry(newAssetPath);
            CopyEntryContents(entry, newEntry);

            // Update mappings with the new file path
            _guidToPath[guid] = newAssetPath;
            _guidToEntry[guid] = newEntry;
            _pathToGuid[newAssetPath] = guid;
            _pathToEntry[newAssetPath] = newEntry;

            // Remove the old entry from the archive
            entry.Delete();

            return true;
        }

        return false;
    }

    public bool RenameFile(string assetPath, string newName)
    {
        assetPath = NormalizePath(assetPath);

        // Ensure the new file path does not exist in the archive
        string newFilePath = Path.Combine(Path.GetDirectoryName(assetPath)!, newName);
        if (!_pathToEntry.ContainsKey(newFilePath))
        {
            // Move the file to the new path
            return MoveFile(assetPath, newFilePath);
        }

        return false;
    }

    private void CopyEntryContents(ZipArchiveEntry sourceEntry, ZipArchiveEntry destinationEntry)
    {
        using (var sourceStream = sourceEntry.Open())
        using (var destinationStream = destinationEntry.Open())
        {
            // Copy contents from source entry to destination entry
            sourceStream.CopyTo(destinationStream);
        }
    }

    public List<string> GetAssets(string folderPath, bool includeSubdirectories = false)
    {
        folderPath = NormalizePath(folderPath);

        List<string> assets = [];
        foreach (var path in _pathToEntry.Keys)
        {
            // Check if the path is within the specified folder (or subdirectory)
            if (path.StartsWith(folderPath, StringComparison.OrdinalIgnoreCase) && !path.Equals(folderPath, StringComparison.OrdinalIgnoreCase))
            {
                // If includeSubdirectories is true, add all paths starting with folderPath
                // If includeSubdirectories is false, only add paths directly inside folderPath
                if (includeSubdirectories || !path[folderPath.Length..].Contains('/', StringComparison.OrdinalIgnoreCase))
                {
                    assets.Add(path);
                }
            }
        }
        return assets;
    }

    public List<Guid> GetAllGuids()
    {
        return _guidToEntry.Keys.ToList();
    }

    #endregion

    #region Folders

    public List<string> GetRootFolders()
    {
        HashSet<string> folders = [];

        foreach (var path in _pathToEntry.Keys)
        {
            string[] segments = path.Split('/');
            if (segments.Length > 1)
            {
                // Add the first segment (root folder) to the HashSet
                folders.Add(segments[0]);
            }
        }

        return [..folders];
    }

    public List<string> GetFolders(string folderPath)
    {
        folderPath = NormalizePath(folderPath);

        List<string> folders = [];

        foreach (var path in _pathToEntry.Keys)
        {
            // Check if the path starts with the specified folderPath
            bool isPathInFolder = path.StartsWith(folderPath);

            // Check if the path is within the specified folder (or subdirectory) and contains a single '/'
            // This ensures that we are getting immediate child directories and not files
            if (isPathInFolder && !path.Equals(folderPath) && path[folderPath.Length..].Count(f => f == '/') == 1)
            {
                // Extract the immediate child folder name and add it to the list
                string[] segments = path[(folderPath.Length + 1)..].Split('/');
                folders.Add(segments[0]);
            }
        }

        return folders;
    }

    #endregion

    #region Mapping
    public bool HasAsset(Guid assetID) => _guidToPath.ContainsKey(assetID);

    public bool TryGetGuid(string assetPath, out Guid guid) => _pathToGuid.TryGetValue(NormalizePath(assetPath), out guid);

    public bool TryGetPath(Guid guid, out string? assetPath) => _guidToPath.TryGetValue(guid, out assetPath);

    public bool TryGetAsset(Guid guid, out SerializedAsset? asset)
    {
        _guidToEntry.TryGetValue(guid, out var entry);
        if (entry != null)
        {
            using var entryStream = entry.Open();
            asset = SerializedAsset.FromStream(entryStream);
            asset.Main!.AssetID = guid;
            for (int i = 0; i < asset.SubAssets.Count; i++)
            {
                asset.SubAssets[i].AssetID = guid;
                asset.SubAssets[i].FileID = (ushort)i;
            }
            return true;
        }
        asset = null;
        return false;
    }

    public bool TryGetAsset(string assetPath, out SerializedAsset? asset)
    {
        _pathToEntry.TryGetValue(NormalizePath(assetPath), out var entry);
        if (entry != null)
        {
            using var entryStream = entry.Open();
            asset = SerializedAsset.FromStream(entryStream);
            asset.Main!.AssetID = _pathToGuid[assetPath];
            for (int i = 0; i < asset.SubAssets.Count; i++)
            {
                asset.SubAssets[i].AssetID = asset.Main!.AssetID;
                asset.SubAssets[i].FileID = (ushort)i;
            }
            return true;
        }
        asset = null;
        return false;
    }

    #endregion

    /// <summary>
    /// Formats virtual path to be uniform, so there are no identical entries but with different paths.
    /// </summary>
    private string NormalizePath(string path)
    {
        if (string.IsNullOrWhiteSpace(path))
            throw new ArgumentException("Path cannot be null or empty.");

        path = path.Replace(Path.AltDirectorySeparatorChar, Path.DirectorySeparatorChar);
        path = path.Replace(@"\\", @"\").Replace(@"\", @"/");

        // If it starts with a / remove it
        if (path.StartsWith("/")) path = path[1..];

        return path;
    }

    public void Dispose()
    {
        _zipArchive.Dispose();
        _stream.Dispose();
    }

}
