using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;

using Prowl.Echo;
using Prowl.Editor.Importers;
using Prowl.Runtime;

namespace Prowl.Editor;

/// <summary>
/// Central asset database for the editor. Implements the runtime's IAssetDatabase interface.
/// Manages the full asset lifecycle: scanning, importing, caching, file watching.
/// </summary>
public class EditorAssetDatabase : IAssetDatabase
{
    public static EditorAssetDatabase? Instance { get; private set; }

    private readonly Project _project;
    private readonly Dictionary<Guid, AssetEntry> _guidToEntry = new();
    private readonly Dictionary<string, Guid> _pathToGuid = new(StringComparer.OrdinalIgnoreCase);
    private readonly Dictionary<Guid, EngineObject> _loadedAssets = new();
    private readonly DependencyGraph _dependencies = new();
    private AssetWatcher? _watcher;

    // Events
    public event Action<string[]>? OnAssetsImported;
    public event Action<string[]>? OnAssetsDeleted;
    public event Action<string, string>? OnAssetMoved;

    public EditorAssetDatabase(Project project)
    {
        _project = project;
    }

    // ================================================================
    //  Initialization
    // ================================================================

    public void Initialize()
    {
        ImporterRegistry.Initialize();

        // Try loading cached index for fast startup
        var cached = MetadataCache.Load(_project.MetadataDbPath);
        if (cached.Count > 0)
        {
            foreach (var (guid, entry) in cached)
            {
                _guidToEntry[guid] = entry;
                _pathToGuid[entry.Path] = guid;
            }
            Runtime.Debug.Log($"Loaded {cached.Count} entries from metadata cache.");
        }

        // Scan and reconcile with actual files
        ScanAssets();

        // Import anything that needs it
        ImportDirty();

        // Save updated cache
        MetadataCache.Save(_project.MetadataDbPath, _guidToEntry.Values);

        // Start file watching
        _watcher = new AssetWatcher();
        _watcher.Start(_project.AssetsPath);

        // Register as the active database
        Instance = this;
        Runtime.AssetDatabase.Current = this;

        Runtime.Debug.Log($"Asset database initialized: {_guidToEntry.Count} assets tracked.");
    }

    // ================================================================
    //  IAssetDatabase
    // ================================================================

    public EngineObject? Get(Guid assetId)
    {
        if (assetId == Guid.Empty) return null;

        // Check in-memory cache
        if (_loadedAssets.TryGetValue(assetId, out var loaded))
            return loaded;

        // Try loading from disk cache
        string cachePath = GetCachePath(assetId);
        if (File.Exists(cachePath))
        {
            try
            {
                var echo = EchoObject.ReadFromBinary(new FileInfo(cachePath));
                var ctx = new SerializationContext();
                Runtime.AssetDatabase.ConfigureContext(ctx);

                // Get the target type from the entry
                var entry = GetEntry(assetId);
                var targetType = entry?.MainAssetType ?? typeof(EngineObject);

                var obj = Serializer.Deserialize(echo, targetType, ctx) as EngineObject;
                if (obj != null)
                {
                    obj.AssetID = assetId;
                    if (entry != null) obj.AssetPath = entry.Path;
                    _loadedAssets[assetId] = obj;
                    return obj;
                }
            }
            catch (Exception ex)
            {
                Runtime.Debug.LogWarning($"Failed to load cached asset {assetId}: {ex.Message}");
            }
        }

        // Cache miss — try importing on demand
        if (_guidToEntry.TryGetValue(assetId, out var e) && !string.IsNullOrEmpty(e.Path))
        {
            RunImport(e);
            return _loadedAssets.GetValueOrDefault(assetId);
        }

        return null;
    }

    // ================================================================
    //  Scanning
    // ================================================================

    private void ScanAssets()
    {
        var assetsPath = _project.AssetsPath;
        if (!Directory.Exists(assetsPath)) return;

        // Track which paths we find on disk
        var foundPaths = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        foreach (var file in Directory.EnumerateFiles(assetsPath, "*", SearchOption.AllDirectories))
        {
            // Skip .meta files and hidden files
            if (file.EndsWith(".meta", StringComparison.OrdinalIgnoreCase)) continue;
            string fileName = Path.GetFileName(file);
            if (fileName.StartsWith('.')) continue;

            string relativePath = Path.GetRelativePath(assetsPath, file).Replace('\\', '/');
            foundPaths.Add(relativePath);

            // Determine importer
            string ext = Path.GetExtension(file);
            string importerName = ImporterRegistry.GetImporterTypeName(ext);

            // Ensure .meta exists
            var meta = MetaFile.EnsureMeta(file, importerName);

            if (_pathToGuid.TryGetValue(relativePath, out var existingGuid))
            {
                // Already tracked — check if needs reimport
                var entry = _guidToEntry[existingGuid];
                long currentTicks = File.GetLastWriteTimeUtc(file).Ticks;

                if (entry.LastModifiedTicks != currentTicks || !File.Exists(GetCachePath(existingGuid)))
                    entry.NeedsReimport = true;

                // Update GUID if meta was regenerated with different GUID
                if (meta.Guid != existingGuid)
                {
                    _guidToEntry.Remove(existingGuid);
                    _pathToGuid.Remove(relativePath);
                    entry.Guid = meta.Guid;
                    _guidToEntry[meta.Guid] = entry;
                    _pathToGuid[relativePath] = meta.Guid;
                    entry.NeedsReimport = true;
                }
            }
            else if (_guidToEntry.ContainsKey(meta.Guid))
            {
                // GUID exists but at a different path — the file was moved
                var entry = _guidToEntry[meta.Guid];
                string oldPath = entry.Path;
                _pathToGuid.Remove(oldPath);
                entry.Path = relativePath;
                _pathToGuid[relativePath] = meta.Guid;
            }
            else
            {
                // New asset
                var entry = new AssetEntry
                {
                    Guid = meta.Guid,
                    Path = relativePath,
                    ImporterType = importerName,
                    ImporterVersion = meta.ImporterVersion,
                    NeedsReimport = true
                };
                _guidToEntry[meta.Guid] = entry;
                _pathToGuid[relativePath] = meta.Guid;
            }
        }

        // Also scan for directories (they need .meta files too for folder GUIDs)
        foreach (var dir in Directory.EnumerateDirectories(assetsPath, "*", SearchOption.AllDirectories))
        {
            string dirName = Path.GetFileName(dir);
            if (dirName.StartsWith('.')) continue;
            // Ensure folder .meta exists (for stable folder GUIDs in version control)
            MetaFile.EnsureMeta(dir, "DefaultImporter");
        }

        // Remove entries for files that no longer exist
        var toRemove = _guidToEntry.Where(kv => !foundPaths.Contains(kv.Value.Path))
            .Select(kv => kv.Key).ToList();

        foreach (var guid in toRemove)
        {
            var entry = _guidToEntry[guid];
            _pathToGuid.Remove(entry.Path);
            _guidToEntry.Remove(guid);
            _dependencies.RemoveAsset(guid);
            _loadedAssets.Remove(guid);

            // Clean cache file
            string cachePath = GetCachePath(guid);
            if (File.Exists(cachePath))
                try { File.Delete(cachePath); } catch { }
        }

        if (toRemove.Count > 0)
            Runtime.Debug.Log($"Removed {toRemove.Count} stale entries.");
    }

    // ================================================================
    //  Importing
    // ================================================================

    private void ImportDirty()
    {
        var dirty = _guidToEntry.Values.Where(e => e.NeedsReimport).ToList();
        if (dirty.Count == 0) return;

        Runtime.Debug.Log($"Importing {dirty.Count} assets...");
        int success = 0;

        foreach (var entry in dirty)
        {
            if (RunImport(entry))
                success++;
        }

        Runtime.Debug.Log($"Import complete: {success}/{dirty.Count} succeeded.");

        if (success > 0)
            OnAssetsImported?.Invoke(dirty.Where(e => !e.NeedsReimport).Select(e => e.Path).ToArray());
    }

    private bool RunImport(AssetEntry entry)
    {
        string absolutePath = Path.Combine(_project.AssetsPath, entry.Path);
        if (!File.Exists(absolutePath))
        {
            entry.NeedsReimport = false;
            return false;
        }

        var importer = ImporterRegistry.CreateByTypeName(entry.ImporterType) ?? new DefaultImporter();

        // Read settings from .meta
        EchoObject? settings = null;
        string metaPath = MetaFile.GetMetaPath(absolutePath);
        if (File.Exists(metaPath))
        {
            try
            {
                var meta = MetaFile.Read(metaPath);
                settings = meta.Settings;
            }
            catch { }
        }

        try
        {
            var result = importer.Import(absolutePath, settings);

            if (result.MainAsset != null)
            {
                // Assign identity
                result.MainAsset.AssetID = entry.Guid;
                result.MainAsset.AssetPath = entry.Path;
                if (string.IsNullOrEmpty(result.MainAsset.Name))
                    result.MainAsset.Name = Path.GetFileNameWithoutExtension(entry.Path);

                // Serialize to cache
                SerializeToCache(entry.Guid, result.MainAsset);

                // Cache in memory
                _loadedAssets[entry.Guid] = result.MainAsset;

                // Update entry metadata
                entry.MainAssetType = result.MainAsset.GetType();
                entry.Dependencies = result.Dependencies.ToArray();
            }

            // Update timestamps
            entry.LastModifiedTicks = File.GetLastWriteTimeUtc(absolutePath).Ticks;
            entry.ImporterVersion = importer.Version;
            entry.NeedsReimport = false;

            // Update dependency graph
            _dependencies.SetDependencies(entry.Guid, result.Dependencies);

            return true;
        }
        catch (Exception ex)
        {
            Runtime.Debug.LogError($"Import failed for '{entry.Path}': {ex.Message}");
            entry.NeedsReimport = false; // Don't keep retrying
            return false;
        }
    }

    private void SerializeToCache(Guid guid, EngineObject obj)
    {
        string cachePath = GetCachePath(guid);
        Directory.CreateDirectory(Path.GetDirectoryName(cachePath)!);

        try
        {
            var ctx = new SerializationContext();
            Runtime.AssetDatabase.ConfigureContext(ctx);

            var echo = Serializer.Serialize(obj, ctx);
            if (echo != null)
                echo.WriteToBinary(new FileInfo(cachePath));
        }
        catch (Exception ex)
        {
            Runtime.Debug.LogWarning($"Failed to cache asset {guid}: {ex.Message}");
        }
    }

    // ================================================================
    //  Query API
    // ================================================================

    public AssetEntry? GetEntry(Guid guid)
        => _guidToEntry.GetValueOrDefault(guid);

    public AssetEntry? GetEntry(string relativePath)
        => _pathToGuid.TryGetValue(relativePath, out var guid) ? _guidToEntry.GetValueOrDefault(guid) : null;

    public Guid PathToGuid(string relativePath)
        => _pathToGuid.GetValueOrDefault(relativePath);

    public string? GuidToPath(Guid guid)
        => _guidToEntry.TryGetValue(guid, out var entry) ? entry.Path : null;

    public IEnumerable<AssetEntry> GetAllEntries() => _guidToEntry.Values;

    public IEnumerable<AssetEntry> FindAssetsOfType<T>() where T : EngineObject
        => _guidToEntry.Values.Where(e => e.MainAssetType != null && typeof(T).IsAssignableFrom(e.MainAssetType));

    public IEnumerable<AssetEntry> FindAssetsOfType(Type type)
        => _guidToEntry.Values.Where(e => e.MainAssetType != null && type.IsAssignableFrom(e.MainAssetType));

    public string[] GetAllAssetPaths()
        => _pathToGuid.Keys.ToArray();

    public DependencyGraph Dependencies => _dependencies;

    // ================================================================
    //  Asset CRUD
    // ================================================================

    /// <summary>
    /// Create a new asset file on disk from an EngineObject.
    /// </summary>
    public void CreateAsset(EngineObject obj, string relativePath)
    {
        string absolutePath = Path.Combine(_project.AssetsPath, relativePath);
        Directory.CreateDirectory(Path.GetDirectoryName(absolutePath)!);

        // Serialize to the file
        var ctx = new SerializationContext();
        Runtime.AssetDatabase.ConfigureContext(ctx);
        var echo = Serializer.Serialize(obj, ctx);
        if (echo != null)
            File.WriteAllText(absolutePath, echo.WriteToString());

        // Create meta
        string ext = Path.GetExtension(relativePath);
        string importerName = ImporterRegistry.GetImporterTypeName(ext);
        var meta = MetaFile.CreateNew(importerName);
        MetaFile.Write(MetaFile.GetMetaPath(absolutePath), meta);

        // Add to index and import
        var entry = new AssetEntry
        {
            Guid = meta.Guid,
            Path = relativePath,
            ImporterType = importerName,
            NeedsReimport = true
        };
        _guidToEntry[meta.Guid] = entry;
        _pathToGuid[relativePath] = meta.Guid;

        RunImport(entry);
        MetadataCache.Save(_project.MetadataDbPath, _guidToEntry.Values);
    }

    /// <summary>
    /// Re-serialize an in-memory asset back to its source file.
    /// </summary>
    public void SaveAsset(EngineObject obj)
    {
        if (obj.AssetID == Guid.Empty || string.IsNullOrEmpty(obj.AssetPath)) return;

        string absolutePath = Path.Combine(_project.AssetsPath, obj.AssetPath);
        var ctx = new SerializationContext();
        Runtime.AssetDatabase.ConfigureContext(ctx);
        var echo = Serializer.Serialize(obj, ctx);
        if (echo != null)
            File.WriteAllText(absolutePath, echo.WriteToString());

        // Reimport to update cache
        if (_guidToEntry.TryGetValue(obj.AssetID, out var entry))
        {
            entry.NeedsReimport = true;
            RunImport(entry);
            MetadataCache.Save(_project.MetadataDbPath, _guidToEntry.Values);
        }
    }

    /// <summary>
    /// Delete an asset and its .meta file.
    /// </summary>
    public void DeleteAsset(string relativePath)
    {
        string absolutePath = Path.Combine(_project.AssetsPath, relativePath);

        if (_pathToGuid.TryGetValue(relativePath, out var guid))
        {
            _guidToEntry.Remove(guid);
            _pathToGuid.Remove(relativePath);
            _loadedAssets.Remove(guid);
            _dependencies.RemoveAsset(guid);

            // Clean cache
            string cachePath = GetCachePath(guid);
            if (File.Exists(cachePath))
                try { File.Delete(cachePath); } catch { }
        }

        // Delete files
        if (File.Exists(absolutePath))
            File.Delete(absolutePath);
        string metaPath = MetaFile.GetMetaPath(absolutePath);
        if (File.Exists(metaPath))
            File.Delete(metaPath);

        MetadataCache.Save(_project.MetadataDbPath, _guidToEntry.Values);
        OnAssetsDeleted?.Invoke(new[] { relativePath });
    }

    /// <summary>
    /// Move/rename an asset. The GUID stays the same.
    /// </summary>
    public void MoveAsset(string oldRelativePath, string newRelativePath)
    {
        string oldAbsolute = Path.Combine(_project.AssetsPath, oldRelativePath);
        string newAbsolute = Path.Combine(_project.AssetsPath, newRelativePath);

        if (!File.Exists(oldAbsolute)) return;

        Directory.CreateDirectory(Path.GetDirectoryName(newAbsolute)!);
        File.Move(oldAbsolute, newAbsolute);

        // Move .meta
        string oldMeta = MetaFile.GetMetaPath(oldAbsolute);
        string newMeta = MetaFile.GetMetaPath(newAbsolute);
        if (File.Exists(oldMeta))
            File.Move(oldMeta, newMeta);

        // Update index
        if (_pathToGuid.TryGetValue(oldRelativePath, out var guid))
        {
            _pathToGuid.Remove(oldRelativePath);
            _pathToGuid[newRelativePath] = guid;
            _guidToEntry[guid].Path = newRelativePath;

            if (_loadedAssets.TryGetValue(guid, out var obj))
                obj.AssetPath = newRelativePath;
        }

        MetadataCache.Save(_project.MetadataDbPath, _guidToEntry.Values);
        OnAssetMoved?.Invoke(oldRelativePath, newRelativePath);
    }

    /// <summary>
    /// Force reimport a specific asset.
    /// </summary>
    public void Reimport(Guid guid)
    {
        if (_guidToEntry.TryGetValue(guid, out var entry))
        {
            _loadedAssets.Remove(guid);
            entry.NeedsReimport = true;
            RunImport(entry);
            MetadataCache.Save(_project.MetadataDbPath, _guidToEntry.Values);
            OnAssetsImported?.Invoke(new[] { entry.Path });
        }
    }

    // ================================================================
    //  File Watching (per-frame update)
    // ================================================================

    public void ProcessFileChanges()
    {
        if (_watcher == null) return;

        var events = _watcher.DrainEvents();
        if (events.Count == 0) return;

        var imported = new List<string>();
        var deleted = new List<string>();

        foreach (var evt in events)
        {
            string relativePath = Path.GetRelativePath(_project.AssetsPath, evt.Path).Replace('\\', '/');

            // Skip .meta files — we manage them
            if (relativePath.EndsWith(".meta", StringComparison.OrdinalIgnoreCase)) continue;

            switch (evt.Type)
            {
                case FileEventType.Created:
                case FileEventType.Modified:
                {
                    string ext = Path.GetExtension(evt.Path);
                    string importerName = ImporterRegistry.GetImporterTypeName(ext);
                    var meta = MetaFile.EnsureMeta(evt.Path, importerName);

                    if (!_guidToEntry.ContainsKey(meta.Guid))
                    {
                        var entry = new AssetEntry
                        {
                            Guid = meta.Guid,
                            Path = relativePath,
                            ImporterType = importerName,
                            NeedsReimport = true
                        };
                        _guidToEntry[meta.Guid] = entry;
                        _pathToGuid[relativePath] = meta.Guid;
                    }
                    else
                    {
                        _guidToEntry[meta.Guid].NeedsReimport = true;
                    }

                    _loadedAssets.Remove(meta.Guid);
                    RunImport(_guidToEntry[meta.Guid]);
                    imported.Add(relativePath);
                    break;
                }

                case FileEventType.Deleted:
                {
                    if (_pathToGuid.TryGetValue(relativePath, out var guid))
                    {
                        _guidToEntry.Remove(guid);
                        _pathToGuid.Remove(relativePath);
                        _loadedAssets.Remove(guid);
                        _dependencies.RemoveAsset(guid);

                        string cachePath = GetCachePath(guid);
                        if (File.Exists(cachePath))
                            try { File.Delete(cachePath); } catch { }

                        deleted.Add(relativePath);
                    }
                    break;
                }

                case FileEventType.Renamed:
                {
                    if (evt.OldPath != null)
                    {
                        string oldRelative = Path.GetRelativePath(_project.AssetsPath, evt.OldPath).Replace('\\', '/');
                        if (_pathToGuid.TryGetValue(oldRelative, out var guid))
                        {
                            _pathToGuid.Remove(oldRelative);
                            _pathToGuid[relativePath] = guid;
                            _guidToEntry[guid].Path = relativePath;

                            // Move .meta
                            string oldMeta = MetaFile.GetMetaPath(evt.OldPath);
                            string newMeta = MetaFile.GetMetaPath(evt.Path);
                            if (File.Exists(oldMeta) && !File.Exists(newMeta))
                                try { File.Move(oldMeta, newMeta); } catch { }

                            if (_loadedAssets.TryGetValue(guid, out var obj))
                                obj.AssetPath = relativePath;

                            OnAssetMoved?.Invoke(oldRelative, relativePath);
                        }
                    }
                    break;
                }
            }
        }

        if (imported.Count > 0 || deleted.Count > 0)
        {
            MetadataCache.Save(_project.MetadataDbPath, _guidToEntry.Values);
            if (imported.Count > 0) OnAssetsImported?.Invoke(imported.ToArray());
            if (deleted.Count > 0) OnAssetsDeleted?.Invoke(deleted.ToArray());
        }
    }

    // ================================================================
    //  Helpers
    // ================================================================

    private string GetCachePath(Guid guid)
        => Path.Combine(_project.CachePath, $"{guid}.asset");

    public void Dispose()
    {
        _watcher?.Dispose();
        _watcher = null;
    }
}
