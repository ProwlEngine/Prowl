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
    private readonly Dictionary<Guid, (Guid parentGuid, int index)> _subAssetIndex = new();
    private readonly HashSet<Guid> _currentlyLoading = new(); // re-entrancy guard
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

        // Rebuild sub-asset index
        RebuildSubAssetIndex();

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

        // Check in-memory cache (covers both main assets and sub-assets)
        if (_loadedAssets.TryGetValue(assetId, out var loaded))
            return loaded;

        // Re-entrancy guard — prevents infinite recursion when deserializing
        // assets that reference each other or their own sub-assets
        if (!_currentlyLoading.Add(assetId))
            return null; // Already loading this asset higher up the call stack

        try
        {
            return GetInternal(assetId);
        }
        finally
        {
            _currentlyLoading.Remove(assetId);
        }
    }

    private EngineObject? GetInternal(Guid assetId)
    {
        // Check if this is a sub-asset
        if (_subAssetIndex.TryGetValue(assetId, out var subInfo))
        {
            // Load the parent first — this will also cache all sub-assets
            var parent = Get(subInfo.parentGuid);
            // After loading parent, sub-asset should be cached now
            return _loadedAssets.GetValueOrDefault(assetId);
        }

        // Try loading main asset from disk cache
        string cachePath = GetCachePath(assetId);
        var entry = GetEntry(assetId);

        // Validate cache — check importer version matches current importer
        if (entry != null && !string.IsNullOrEmpty(entry.ImporterType))
        {
            var importer = Importers.ImporterRegistry.CreateByTypeName(entry.ImporterType);
            if (importer != null && importer.Version != entry.ImporterVersion)
            {
                // Cache is stale — importer was updated since last import
                Runtime.Debug.Log($"Cache stale for '{entry.Path}': importer v{entry.ImporterVersion} → v{importer.Version}. Reimporting.");
                entry.NeedsReimport = true;
                RunImport(entry);
                return _loadedAssets.GetValueOrDefault(assetId);
            }
        }

        if (File.Exists(cachePath))
        {
            try
            {
                var echo = EchoObject.ReadFromBinary(new FileInfo(cachePath));
                var ctx = new SerializationContext();
                Runtime.AssetDatabase.ConfigureContext(ctx);

                var targetType = entry?.MainAssetType ?? typeof(EngineObject);

                var obj = Serializer.Deserialize(echo, targetType, ctx) as EngineObject;
                if (obj != null)
                {
                    obj.AssetID = assetId;
                    if (entry != null) obj.AssetPath = entry.Path;
                    _loadedAssets[assetId] = obj;

                    // Also load and cache sub-assets
                    if (entry?.SubAssets != null)
                        LoadSubAssetsIntoCache(entry);

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

    private void LoadSubAssetsIntoCache(AssetEntry parentEntry)
    {
        foreach (var sub in parentEntry.SubAssets)
        {
            if (_loadedAssets.ContainsKey(sub.Guid)) continue;

            string subCachePath = GetCachePath(sub.Guid);
            if (!File.Exists(subCachePath))
            {
                Runtime.Debug.LogWarning($"Sub-asset cache missing for '{sub.Name}' in '{parentEntry.Path}'. Reimport the parent asset.");
                continue;
            }

            try
            {
                var echo = EchoObject.ReadFromBinary(new FileInfo(subCachePath));
                var ctx = new SerializationContext();
                Runtime.AssetDatabase.ConfigureContext(ctx);
                var subType = sub.Type ?? typeof(EngineObject);
                var obj = Serializer.Deserialize(echo, subType, ctx) as EngineObject;
                if (obj != null)
                {
                    obj.AssetID = sub.Guid;
                    obj.AssetPath = $"{parentEntry.Path}#{sub.Name}";
                    _loadedAssets[sub.Guid] = obj;
                }
                else
                {
                    Runtime.Debug.LogWarning($"Failed to deserialize sub-asset '{sub.Name}' from '{parentEntry.Path}' (type: {sub.TypeName}).");
                }
            }
            catch (Exception ex)
            {
                Runtime.Debug.LogWarning($"Error loading sub-asset '{sub.Name}' from '{parentEntry.Path}': {ex.Message}");
            }
        }
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

            string relativePath = NormalizePath(Path.GetRelativePath(assetsPath, file));
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
                    // Clean up old GUID references
                    _guidToEntry.Remove(existingGuid);
                    _pathToGuid.Remove(relativePath);
                    _loadedAssets.Remove(existingGuid);
                    _dependencies.RemoveAsset(existingGuid);

                    // Remove old sub-asset index entries
                    if (entry.SubAssets != null)
                    {
                        foreach (var sub in entry.SubAssets)
                        {
                            _subAssetIndex.Remove(sub.Guid);
                            _loadedAssets.Remove(sub.Guid);
                        }
                    }

                    // Delete old cache file
                    string oldCachePath = GetCachePath(existingGuid);
                    if (File.Exists(oldCachePath))
                        try { File.Delete(oldCachePath); } catch { }

                    entry.Guid = meta.Guid;
                    entry.SubAssets = Array.Empty<SubAssetEntry>();
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

            // Process sub-assets
            if (result.SubAssets != null && result.SubAssets.Length > 0)
            {
                var subEntries = new List<SubAssetEntry>();
                for (int i = 0; i < result.SubAssets.Length; i++)
                {
                    var sub = result.SubAssets[i];
                    if (sub == null) continue;

                    string subName = !string.IsNullOrEmpty(sub.Name) ? sub.Name : $"SubAsset_{i}";
                    Guid subGuid = AssetEntry.DeriveSubAssetGuid(entry.Guid, subName);

                    sub.AssetID = subGuid;
                    sub.AssetPath = $"{entry.Path}#{subName}";

                    // Cache sub-asset to disk
                    SerializeToCache(subGuid, sub);

                    // Cache in memory
                    _loadedAssets[subGuid] = sub;

                    // Build sub-asset entry
                    subEntries.Add(new SubAssetEntry
                    {
                        Guid = subGuid,
                        Name = subName,
                        Type = sub.GetType()
                    });

                    // Index the sub-asset
                    _subAssetIndex[subGuid] = (entry.Guid, i);
                }
                entry.SubAssets = subEntries.ToArray();
            }
            else
            {
                entry.SubAssets = Array.Empty<SubAssetEntry>();
            }

            // Update timestamps
            entry.LastModifiedTicks = File.GetLastWriteTimeUtc(absolutePath).Ticks;
            entry.ImporterVersion = importer.Version;
            entry.NeedsReimport = false;

            // Update dependency graph
            _dependencies.SetDependencies(entry.Guid, result.Dependencies);

            // Queue thumbnail generation (lazy, one per frame)
            if (result.MainAsset != null)
            {
                string? sourceFile = result.MainAsset is Runtime.Resources.Texture2D ? absolutePath : null;
                ThumbnailGenerator.Enqueue(entry.Guid, result.MainAsset, sourceFile);
            }

            return true;
        }
        catch (Exception ex)
        {
            Runtime.Debug.LogError($"Import failed for '{entry.Path}': {ex.Message}");
            // Keep NeedsReimport true so file changes will trigger a retry.
            // Only clear the timestamp so next scan detects the file as changed.
            entry.LastModifiedTicks = 0;
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
        => FindAssetsOfType(typeof(T));

    /// <summary>
    /// Find all assets (main + sub) matching the given type via inheritance.
    /// Returns (Guid, Name, ParentPath) tuples for display.
    /// </summary>
    public IEnumerable<AssetEntry> FindAssetsOfType(Type type)
        => _guidToEntry.Values.Where(e => e.MainAssetType != null && type.IsAssignableFrom(e.MainAssetType));

    /// <summary>
    /// Find all assets AND sub-assets matching the given type.
    /// Includes built-in assets (embedded in runtime).
    /// Returns tuples of (guid, displayName, parentPath, type).
    /// </summary>
    public IEnumerable<(Guid guid, string name, string parentPath, Type assetType)> FindAllOfType(Type type)
    {
        // Built-in assets first
        foreach (var item in Runtime.BuiltInAssets.FindAllOfType(type))
            yield return item;

        // Main assets
        foreach (var entry in _guidToEntry.Values)
        {
            if (entry.MainAssetType != null && type.IsAssignableFrom(entry.MainAssetType))
                yield return (entry.Guid, Path.GetFileNameWithoutExtension(entry.Path), entry.Path, entry.MainAssetType);
        }

        // Sub-assets
        foreach (var entry in _guidToEntry.Values)
        {
            if (entry.SubAssets == null) continue;
            foreach (var sub in entry.SubAssets)
            {
                var subType = sub.Type;
                if (subType != null && type.IsAssignableFrom(subType))
                    yield return (sub.Guid, sub.Name, entry.Path, subType);
            }
        }
    }

    /// <summary>Get sub-assets of a parent asset.</summary>
    public SubAssetEntry[] GetSubAssets(Guid parentGuid)
        => _guidToEntry.TryGetValue(parentGuid, out var entry) ? entry.SubAssets : Array.Empty<SubAssetEntry>();

    public string[] GetAllAssetPaths()
        => _pathToGuid.Keys.ToArray();

    public DependencyGraph Dependencies => _dependencies;
    public string ThumbnailsPath => _project.ThumbnailsPath;

    /// <summary>Get an already-loaded asset from memory without triggering import. Returns null if not loaded.</summary>
    public EngineObject? GetLoadedAsset(Guid guid) => _loadedAssets.GetValueOrDefault(guid);

    /// <summary>Load a cached thumbnail for an asset. Returns raw RGBA bytes or null.</summary>
    public byte[]? LoadThumbnail(Guid guid) => ThumbnailGenerator.LoadThumbnail(guid, _project.ThumbnailsPath);

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

        // Sub-assets have paths like "Model.fbx#Mesh_0" — can't save those directly
        if (obj.AssetPath.Contains('#'))
        {
            Runtime.Debug.LogWarning($"Cannot save sub-asset directly: {obj.AssetPath}");
            return;
        }

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

        // Move asset file + .meta atomically (with rollback on failure)
        string oldMeta = MetaFile.GetMetaPath(oldAbsolute);
        string newMeta = MetaFile.GetMetaPath(newAbsolute);
        bool assetMoved = false;

        try
        {
            File.Move(oldAbsolute, newAbsolute);
            assetMoved = true;

            if (File.Exists(oldMeta))
                File.Move(oldMeta, newMeta);
        }
        catch (Exception ex)
        {
            // Rollback: if asset moved but meta didn't, move asset back
            if (assetMoved && File.Exists(newAbsolute) && !File.Exists(oldAbsolute))
            {
                try { File.Move(newAbsolute, oldAbsolute); }
                catch { /* best effort rollback */ }
            }
            Runtime.Debug.LogError($"Failed to move asset '{oldRelativePath}' → '{newRelativePath}': {ex.Message}");
            return;
        }

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
    /// <summary>Dispose and remove a cached asset. Triggers AssetRef re-resolve on next access.</summary>
    private void DisposeAndRemove(Guid guid)
    {
        if (_loadedAssets.TryGetValue(guid, out var old))
        {
            try { old?.Dispose(); } catch { }
            _loadedAssets.Remove(guid);
        }
    }

    public void Reimport(Guid guid)
    {
        if (_guidToEntry.TryGetValue(guid, out var entry))
        {
            // Dispose and remove the old cached instance — this causes any AssetRef
            // holding it to detect IsNotValid and re-resolve via AssetDatabase.Get()
            DisposeAndRemove(guid);
            if (entry.SubAssets != null)
                foreach (var sub in entry.SubAssets)
                    DisposeAndRemove(sub.Guid);

            // Clear old thumbnails and invalidate UI cache
            ThumbnailGenerator.DeleteThumbnail(guid, _project.ThumbnailsPath);
            Panels.ProjectPanel.InvalidateThumbnail(guid);
            if (entry.SubAssets != null)
                foreach (var sub in entry.SubAssets)
                {
                    ThumbnailGenerator.DeleteThumbnail(sub.Guid, _project.ThumbnailsPath);
                    Panels.ProjectPanel.InvalidateThumbnail(sub.Guid);
                }

            entry.NeedsReimport = true;
            RunImport(entry);
            MetadataCache.Save(_project.MetadataDbPath, _guidToEntry.Values);
            OnAssetsImported?.Invoke(new[] { entry.Path });


            // Reload the asset and enqueue thumbnail regeneration
            var reloaded = Get(guid);
            if (reloaded != null)
            {
                string? sourceFile = entry.MainAssetType == typeof(Runtime.Resources.Texture2D)
                    ? System.IO.Path.Combine(_project.AssetsPath, entry.Path)
                    : null;
                ThumbnailGenerator.Enqueue(guid, reloaded, sourceFile);
            }

            // Also regenerate sub-asset thumbnails
            if (entry.SubAssets != null)
            {
                foreach (var sub in entry.SubAssets)
                {
                    var subAsset = Get(sub.Guid);
                    if (subAsset != null)
                        ThumbnailGenerator.Enqueue(sub.Guid, subAsset, null);
                }
            }
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
            string relativePath = ToRelativePath(evt.Path);

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

                        // Script deleted — trigger recompile
                        if (relativePath.EndsWith(".cs", StringComparison.OrdinalIgnoreCase))
                            Scripting.ScriptAssemblyManager.RequestRecompile();
                    }
                    break;
                }

                case FileEventType.Renamed:
                {
                    if (evt.OldPath != null)
                    {
                        string oldRelative = ToRelativePath(evt.OldPath);
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

    private void RebuildSubAssetIndex()
    {
        _subAssetIndex.Clear();
        foreach (var entry in _guidToEntry.Values)
        {
            if (entry.SubAssets == null) continue;
            for (int i = 0; i < entry.SubAssets.Length; i++)
                _subAssetIndex[entry.SubAssets[i].Guid] = (entry.Guid, i);
        }
    }

    /// <summary>Normalize a path to use forward slashes, relative to Assets/.</summary>
    public static string NormalizePath(string path)
        => path.Replace('\\', '/');

    /// <summary>Get a relative path from an absolute path, normalized.</summary>
    public string ToRelativePath(string absolutePath)
        => NormalizePath(Path.GetRelativePath(_project.AssetsPath, absolutePath));

    public void Dispose()
    {
        _watcher?.Dispose();
        _watcher = null;
    }
}
