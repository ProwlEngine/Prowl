using System;
using System.Collections.Generic;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Security.Cryptography;
using System.Text.Json;
using System.Text.Json.Serialization;

using Prowl.Runtime;

namespace Prowl.Editor.Packages;

public class PackageAssetEntry
{
    [JsonPropertyName("path")]
    public string Path { get; set; } = "";

    [JsonPropertyName("guid")]
    public string Guid { get; set; } = "";

    [JsonPropertyName("importerType")]
    public string ImporterType { get; set; } = "";

    [JsonPropertyName("fileSize")]
    public long FileSize { get; set; }

    [JsonPropertyName("mainAssetType")]
    public string MainAssetType { get; set; } = "";
}

public class PackageManifest
{
    [JsonPropertyName("engineVersion")]
    public string EngineVersion { get; set; } = "0.0.1";

    [JsonPropertyName("exportDate")]
    public string ExportDate { get; set; } = "";

    [JsonPropertyName("containsProjectSettings")]
    public bool ContainsProjectSettings { get; set; }

    [JsonPropertyName("assets")]
    public List<PackageAssetEntry> Assets { get; set; } = new();
}

public enum ImportAction
{
    Add,     // GUID not in project - new asset
    Replace, // GUID exists but content differs
    Skip     // GUID exists and content is identical
}

public static class ProwlPackage
{
    [AssetDoubleClickHandler(".prowlpackage")]
    private static bool OnDoubleClickPackage(string relativePath, Guid guid)
    {
        var project = Project.Current;
        if (project == null) return false;
        string absPath = Path.Combine(project.AssetsPath, relativePath);
        if (File.Exists(absPath))
            PackageImportDialog.Open(absPath);
        return true;
    }

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = true,
        PropertyNameCaseInsensitive = true
    };

    /// <summary>
    /// Export selected assets (and optionally project settings) into a .prowlpackage ZIP file.
    /// </summary>
    public static void Export(string outputPath, IEnumerable<string> assetRelativePaths, bool includeProjectSettings, bool includeDependencies)
    {
        var project = Project.Current;
        if (project == null) throw new InvalidOperationException("No project is open.");
        var db = EditorAssetDatabase.Instance;
        if (db == null) throw new InvalidOperationException("Asset database not initialized.");

        // Collect all asset paths (including dependencies if requested)
        var allPaths = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var queue = new Queue<string>(assetRelativePaths);

        while (queue.Count > 0)
        {
            string path = queue.Dequeue();
            if (!allPaths.Add(path)) continue;

            if (includeDependencies)
            {
                var entry = db.GetEntry(path);
                if (entry?.Dependencies != null)
                {
                    foreach (var depGuid in entry.Dependencies)
                    {
                        // Use GuidToPathIncludingSubAssets so sub-asset dependencies
                        // resolve to their parent asset's file path
                        string? depPath = db.GuidToPathIncludingSubAssets(depGuid);
                        if (depPath != null && !allPaths.Contains(depPath))
                            queue.Enqueue(depPath);
                    }
                }
            }
        }

        // Build manifest
        var manifest = new PackageManifest
        {
            ExportDate = DateTime.UtcNow.ToString("o"),
            ContainsProjectSettings = includeProjectSettings,
        };

        using var stream = File.Create(outputPath);
        using var archive = new ZipArchive(stream, ZipArchiveMode.Create);

        foreach (string relPath in allPaths)
        {
            string absPath = Path.Combine(project.AssetsPath, relPath);
            if (!File.Exists(absPath)) continue;

            var entry = db.GetEntry(relPath);

            // Add asset file
            string zipRelPath = "Assets/" + relPath.Replace('\\', '/');
            archive.CreateEntryFromFile(absPath, zipRelPath);

            // Add .meta file
            string metaPath = MetaFile.GetMetaPath(absPath);
            if (File.Exists(metaPath))
                archive.CreateEntryFromFile(metaPath, zipRelPath + ".meta");

            // Add companion files (.bin for .gltf, .mtl for .obj, etc.)
            foreach (var companion in GetCompanionFiles(absPath))
            {
                if (!File.Exists(companion)) continue;
                string companionRel = Path.GetRelativePath(project.AssetsPath, companion).Replace('\\', '/');
                string companionZip = "Assets/" + companionRel;
                // Avoid duplicates (companion might also be a tracked asset)
                if (archive.GetEntry(companionZip) == null)
                    archive.CreateEntryFromFile(companion, companionZip);
            }

            // Add thumbnail (with size header already baked in)
            if (entry != null)
            {
                string thumbPath = ThumbnailGenerator.GetThumbnailPath(entry.Guid, project.ThumbnailsPath);
                if (File.Exists(thumbPath))
                    archive.CreateEntryFromFile(thumbPath, zipRelPath + ".thumb");
            }

            // Add to manifest
            manifest.Assets.Add(new PackageAssetEntry
            {
                Path = relPath.Replace('\\', '/'),
                Guid = entry?.Guid.ToString() ?? "",
                ImporterType = entry?.ImporterType ?? "",
                FileSize = new FileInfo(absPath).Length,
                MainAssetType = entry?.MainAssetTypeName ?? ""
            });
        }

        // Project settings
        if (includeProjectSettings && Directory.Exists(project.ProjectSettingsPath))
        {
            foreach (var settingsFile in Directory.EnumerateFiles(project.ProjectSettingsPath, "*.yaml"))
            {
                string settingsRelPath = Path.GetFileName(settingsFile);
                archive.CreateEntryFromFile(settingsFile, "ProjectSettings/" + settingsRelPath);
            }
        }

        // Write manifest
        string manifestJson = JsonSerializer.Serialize(manifest, JsonOptions);
        var manifestEntry = archive.CreateEntry("manifest.json");
        using (var writer = new StreamWriter(manifestEntry.Open()))
            writer.Write(manifestJson);

        Debug.Log($"[ProwlPackage] Exported {manifest.Assets.Count} assets to '{outputPath}'");
    }

    /// <summary>
    /// Read the manifest from a .prowlpackage file without extracting.
    /// </summary>
    public static PackageManifest? ReadManifest(string packagePath)
    {
        using var stream = File.OpenRead(packagePath);
        using var archive = new ZipArchive(stream, ZipArchiveMode.Read);
        return ReadManifest(archive);
    }

    /// <summary>
    /// Read the manifest from an open ZipArchive.
    /// </summary>
    public static PackageManifest? ReadManifest(ZipArchive archive)
    {
        var entry = archive.GetEntry("manifest.json");
        if (entry == null) return null;

        using var reader = new StreamReader(entry.Open());
        string json = reader.ReadToEnd();
        return JsonSerializer.Deserialize<PackageManifest>(json, JsonOptions);
    }

    /// <summary>
    /// Read a thumbnail from a package. Returns raw .thumb file bytes (with size header).
    /// </summary>
    public static byte[]? ReadThumbnail(ZipArchive archive, string assetRelPath)
    {
        string entryPath = "Assets/" + assetRelPath.Replace('\\', '/') + ".thumb";
        var entry = archive.GetEntry(entryPath);
        if (entry == null) return null;

        using var ms = new MemoryStream();
        using (var entryStream = entry.Open())
            entryStream.CopyTo(ms);
        return ms.ToArray();
    }

    /// <summary>
    /// Extract a single asset (file + .meta) from a package to the project's Assets folder.
    /// Creates directories as needed. Returns true on success.
    /// </summary>
    public static bool ExtractAsset(ZipArchive archive, string assetRelPath, string destAssetsPath)
    {
        string zipAssetPath = "Assets/" + assetRelPath.Replace('\\', '/');
        string zipMetaPath = zipAssetPath + ".meta";

        var assetEntry = archive.GetEntry(zipAssetPath);
        if (assetEntry == null) return false;

        string destFile = Path.Combine(destAssetsPath, assetRelPath);
        Directory.CreateDirectory(Path.GetDirectoryName(destFile)!);

        // Extract the asset file
        using (var src = assetEntry.Open())
        using (var dst = File.Create(destFile))
            src.CopyTo(dst);

        // Extract the .meta file
        var metaEntry = archive.GetEntry(zipMetaPath);
        if (metaEntry != null)
        {
            string destMeta = MetaFile.GetMetaPath(destFile);
            using var src = metaEntry.Open();
            using var dst = File.Create(destMeta);
            src.CopyTo(dst);
        }

        // Extract any companion files (e.g., .bin for .gltf, .mtl for .obj)
        // They're stored in the zip at the same relative path under Assets/
        string assetDir = Path.GetDirectoryName(zipAssetPath)?.Replace('\\', '/') ?? "Assets";
        foreach (var entry in archive.Entries)
        {
            string entryPath = entry.FullName.Replace('\\', '/');
            // Skip the main asset, its meta, and its thumbnail
            if (entryPath == zipAssetPath || entryPath == zipMetaPath || entryPath.EndsWith(".thumb"))
                continue;
            // Check if this entry is a companion in the same directory
            string entryDir = Path.GetDirectoryName(entryPath)?.Replace('\\', '/') ?? "";
            if (entryDir != assetDir) continue;
            // Skip entries that are other tracked assets (they have their own .meta)
            string companionMetaPath = entryPath + ".meta";
            if (archive.GetEntry(companionMetaPath) != null) continue;

            // This is a companion file - extract it
            string companionRelPath = entryPath["Assets/".Length..];
            string companionDest = Path.Combine(destAssetsPath, companionRelPath);
            Directory.CreateDirectory(Path.GetDirectoryName(companionDest)!);
            using var csrc = entry.Open();
            using var cdst = File.Create(companionDest);
            csrc.CopyTo(cdst);
        }

        return true;
    }

    /// <summary>
    /// Extract project settings files from the package.
    /// </summary>
    public static void ExtractProjectSettings(ZipArchive archive, string destProjectSettingsPath)
    {
        Directory.CreateDirectory(destProjectSettingsPath);

        foreach (var entry in archive.Entries)
        {
            if (!entry.FullName.StartsWith("ProjectSettings/", StringComparison.OrdinalIgnoreCase))
                continue;
            if (string.IsNullOrEmpty(entry.Name)) continue; // directory entry

            string destFile = Path.Combine(destProjectSettingsPath, entry.Name);
            using var src = entry.Open();
            using var dst = File.Create(destFile);
            src.CopyTo(dst);
        }
    }

    /// <summary>
    /// Determine the import action for a package asset against the current project.
    /// </summary>
    public static ImportAction DetermineAction(ZipArchive archive, PackageAssetEntry packageAsset, string assetsPath)
    {
        // Parse the GUID from the package entry
        if (!System.Guid.TryParse(packageAsset.Guid, out var guid) || guid == System.Guid.Empty)
            return ImportAction.Add;

        var db = EditorAssetDatabase.Instance;
        if (db == null) return ImportAction.Add;

        var existing = db.GetEntry(guid);
        if (existing == null) return ImportAction.Add;

        // GUID exists in project - compare file content
        string existingAbsPath = Path.Combine(assetsPath, existing.Path);
        if (!File.Exists(existingAbsPath)) return ImportAction.Add;

        // Quick size check first
        var existingInfo = new FileInfo(existingAbsPath);
        if (existingInfo.Length != packageAsset.FileSize)
            return ImportAction.Replace;

        // Size matches - compare hashes
        string zipAssetPath = "Assets/" + packageAsset.Path.Replace('\\', '/');
        var zipEntry = archive.GetEntry(zipAssetPath);
        if (zipEntry == null) return ImportAction.Add;

        byte[] existingHash;
        using (var fs = File.OpenRead(existingAbsPath))
            existingHash = SHA256.HashData(fs);

        byte[] packageHash;
        using (var zipStream = zipEntry.Open())
        using (var ms = new MemoryStream())
        {
            zipStream.CopyTo(ms);
            ms.Position = 0;
            packageHash = SHA256.HashData(ms);
        }

        return existingHash.SequenceEqual(packageHash) ? ImportAction.Skip : ImportAction.Replace;
    }

    /// <summary>
    /// Collect all asset paths under a folder (recursive).
    /// Used when exporting a folder selection.
    /// </summary>
    /// <summary>
    /// Get companion/sidecar files that should be included alongside an asset.
    /// E.g., .gltf -> .bin, .obj -> .mtl, plus any texture files referenced by relative URI.
    /// </summary>
    private static List<string> GetCompanionFiles(string absAssetPath)
    {
        var results = new List<string>();
        string ext = Path.GetExtension(absAssetPath).ToLowerInvariant();
        string dir = Path.GetDirectoryName(absAssetPath) ?? "";
        string baseName = Path.GetFileNameWithoutExtension(absAssetPath);

        switch (ext)
        {
            case ".gltf":
                // GLTF text format can reference external .bin buffers and textures
                string gltfBin = Path.Combine(dir, baseName + ".bin");
                if (File.Exists(gltfBin)) results.Add(gltfBin);
                // Scan for URI references in the GLTF JSON
                try
                {
                    string gltfText = File.ReadAllText(absAssetPath);
                    results.AddRange(ExtractUriReferences(gltfText, dir));
                }
                catch { }
                break;

            case ".obj":
                // OBJ files reference .mtl material library files
                string mtl = Path.Combine(dir, baseName + ".mtl");
                if (File.Exists(mtl)) results.Add(mtl);
                // Scan the .obj for mtllib directive
                try
                {
                    foreach (var line in File.ReadLines(absAssetPath))
                    {
                        if (line.StartsWith("mtllib ", StringComparison.OrdinalIgnoreCase))
                        {
                            string mtlName = line["mtllib ".Length..].Trim();
                            string mtlPath = Path.Combine(dir, mtlName);
                            if (File.Exists(mtlPath)) results.Add(mtlPath);
                        }
                    }
                }
                catch { }
                break;
        }
        return results;
    }

    /// <summary>Extract relative file URIs from a GLTF JSON string (buffers and images).</summary>
    private static IEnumerable<string> ExtractUriReferences(string gltfJson, string baseDir)
    {
        // Simple regex to find "uri": "filename.ext" patterns (not data: URIs)
        var matches = System.Text.RegularExpressions.Regex.Matches(gltfJson, @"""uri""\s*:\s*""([^""]+)""");
        foreach (System.Text.RegularExpressions.Match match in matches)
        {
            string uri = match.Groups[1].Value;
            if (uri.StartsWith("data:", StringComparison.OrdinalIgnoreCase)) continue;
            string fullPath = Path.GetFullPath(Path.Combine(baseDir, Uri.UnescapeDataString(uri)));
            if (File.Exists(fullPath)) yield return fullPath;
        }
    }

    public static List<string> CollectFolderAssets(string folderRelativePath)
    {
        var project = Project.Current;
        if (project == null) return new List<string>();
        var db = EditorAssetDatabase.Instance;
        if (db == null) return new List<string>();

        string prefix = string.IsNullOrEmpty(folderRelativePath) ? "" : folderRelativePath.Replace('\\', '/') + "/";

        return db.GetAllAssetPaths()
            .Where(p => string.IsNullOrEmpty(prefix) || p.StartsWith(prefix, StringComparison.OrdinalIgnoreCase))
            .ToList();
    }
}
