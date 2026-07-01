using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;

using Prowl.Echo;
using Prowl.Editor.Thumbnails;
using Prowl.Editor.Importers;
using Prowl.Runtime;
using Prowl.Editor.GUI.Panels;
using Prowl.Editor.Projects.Scripting;
using Prowl.Editor.Projects;

namespace Prowl.Editor;

/// <summary>
/// Central asset database for the editor. Implements the runtime's IAssetDatabase interface.
/// Manages the full asset lifecycle: scanning, importing, caching, file watching.
/// </summary>
public class EditorAssetDatabase : IAssetDatabase
{
    public static EditorAssetDatabase? Instance { get; private set; }

    private readonly Project _project;
    // Concurrent so the background AssetLoader thread can read entries / write loaded assets
    // while the main thread imports/scans. (_pathToGuid is main-thread-only — the loader never touches it.)
    private readonly ConcurrentDictionary<Guid, AssetEntry> _guidToEntry = new();
    private readonly Dictionary<string, Guid> _pathToGuid = new(StringComparer.OrdinalIgnoreCase);
    private readonly ConcurrentDictionary<Guid, EngineObject> _loadedAssets = new();
    private readonly ConcurrentDictionary<Guid, (Guid parentGuid, int index)> _subAssetIndex = new();
    // Per-thread re-entrancy guard that breaks dependency/sub-asset cycles during deserialize.
    [ThreadStatic] private static HashSet<Guid>? _loadingStack;
    // Serializes the deserialize body so concurrent loads of the same asset collapse onto one.
    private readonly object _loadLock = new();
    // Importing (and the file writes / GPU work it implies) stays on the main thread; the
    // background loader only deserializes already-imported on-disk cache files.
    private int _mainThreadId = -1;
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
        _mainThreadId = Thread.CurrentThread.ManagedThreadId;

        ImporterRegistry.Initialize();

        // Try loading cached index for fast startup
        var cached = MetadataCache.Load(_project.MetadataDbPath);
        if (cached.Count > 0)
        {
            foreach (var (guid, entry) in cached)
            {
                _guidToEntry[guid] = entry;
                _pathToGuid[entry.Path] = guid;

                // Seed the dependency graph from persisted dependencies. Without this, unchanged
                // assets (not reimported on startup) have no graph edges after a reopen, so
                // GetDependents/GetDependencies return empty until something is reimported.
                if (entry.Dependencies is { Length: > 0 })
                    _dependencies.SetDependencies(guid, entry.Dependencies);
            }
            Runtime.Debug.Log($"Loaded {cached.Count} entries from metadata cache.");
        }

        // Rebuild sub-asset index
        RebuildSubAssetIndex();

        // Materialize engine default assets onto disk so the scan below can import them
        ExtractDefaultAssets();

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

        // Initialize GameResources mapping for editor play mode
        RefreshResourcesMap();
    }

    /// <summary>
    /// Materialize the engine's embedded default assets verbatim into the project's read-only
    /// Defaults/ folder (a sibling of Assets/), so the asset scan can import them like any other
    /// asset. The folder is engine-managed: every embedded file is (over)written when missing or out
    /// of date, which is what makes it effectively read-only. Default shaders' .meta is forced onto
    /// their deterministic BuiltInAssets GUID (see <see cref="DefaultAssetGuid"/>), which is what
    /// lets Shader.LoadDefault resolve them.
    /// </summary>
    private void ExtractDefaultAssets()
    {
        string defaultsDir = _project.DefaultsPath;
        Directory.CreateDirectory(defaultsDir);

        foreach (string fileName in Runtime.Resources.EmbeddedResources.EnumerateDefaultFileNames())
        {
            byte[] embedded = Runtime.Resources.EmbeddedResources.ReadAllBytes($"Assets/Defaults/{fileName}");
            string targetPath = Path.Combine(defaultsDir, fileName);

            if (File.Exists(targetPath) && File.ReadAllBytes(targetPath).AsSpan().SequenceEqual(embedded))
                continue;

            File.WriteAllBytes(targetPath, embedded);
        }

        MigrateLegacyDefaults();
    }

    /// <summary>
    /// Older projects extracted default shaders into Assets/Defaults. Those files use the same
    /// project-relative path ("Defaults/X.shader") and the same forced GUID as the new sibling
    /// Defaults/ root, so leaving them in place would make two files claim one GUID. Remove the
    /// legacy in-Assets copy (engine-owned, regenerated) so the sibling root is the only source.
    /// </summary>
    private void MigrateLegacyDefaults()
    {
        string legacyDir = Path.Combine(_project.AssetsPath, Projects.Project.DefaultsFolder);
        if (!Directory.Exists(legacyDir)) return;

        try
        {
            Directory.Delete(legacyDir, true);
            Runtime.Debug.Log("Migrated legacy Assets/Defaults to the read-only Defaults/ sibling folder.");
        }
        catch (Exception ex)
        {
            Runtime.Debug.LogWarning($"Failed to remove legacy Assets/Defaults folder: {ex.Message}");
        }
    }

    /// <summary>Scan all assets under Resources/ folders and update GameResources mapping.</summary>
    public void RefreshResourcesMap()
    {
        var map = new Dictionary<string, Guid>(StringComparer.OrdinalIgnoreCase);
        foreach (var entry in _guidToEntry.Values)
        {
            string path = entry.Path.Replace('\\', '/');

            // Load path = everything after the last "Resources/" segment (or a leading "Resources/").
            string? afterResources = null;
            int idx = path.LastIndexOf("/Resources/", StringComparison.OrdinalIgnoreCase);
            if (idx >= 0)
                afterResources = path[(idx + "/Resources/".Length)..];
            else if (path.StartsWith("Resources/", StringComparison.OrdinalIgnoreCase))
                afterResources = path["Resources/".Length..];

            if (afterResources == null) continue;

            int dotIdx = afterResources.LastIndexOf('.');
            if (dotIdx >= 0) afterResources = afterResources[..dotIdx];
            if (string.IsNullOrEmpty(afterResources)) continue;

            if (map.ContainsKey(afterResources))
                Runtime.Debug.LogWarning($"[Resources] Duplicate load path '{afterResources}': '{entry.Path}' overrides another asset.");
            map[afterResources] = entry.Guid;
        }
        Runtime.GameResources.Initialize(map);
    }

    // ================================================================
    //  IAssetDatabase
    // ================================================================

    public EngineObject? Get(Guid assetId)
    {
        if (assetId == Guid.Empty) return null;

        // Check in-memory cache (covers both main assets and sub-assets). Lock-free.
        if (_loadedAssets.TryGetValue(assetId, out var loaded) && loaded != null && !loaded.IsDisposed)
            return loaded;

        // Per-thread re-entrancy guard: prevents infinite recursion when deserializing
        // assets that reference each other or their own sub-assets.
        var stack = _loadingStack ??= new HashSet<Guid>();
        if (!stack.Add(assetId))
            return null; // Already loading this asset higher up the call stack

        try
        {
            // Serialize the deserialize/import so concurrent callers (background loader +
            // main/render thread in sync mode) don't double-load; the loser picks up the cache.
            lock (_loadLock)
            {
                if (_loadedAssets.TryGetValue(assetId, out loaded) && loaded != null && !loaded.IsDisposed)
                    return loaded;
                return GetInternal(assetId);
            }
        }
        finally
        {
            stack.Remove(assetId);
        }
    }

    /// <summary>Non-blocking cache peek (no deserialize, no import). Safe from any thread.</summary>
    public EngineObject? GetCached(Guid assetId)
    {
        if (assetId == Guid.Empty) return null;
        return _loadedAssets.TryGetValue(assetId, out var c) && c != null && !c.IsDisposed ? c : null;
    }

    private EngineObject? GetInternal(Guid assetId)
    {
        // Importing writes files / creates GPU resources and mutates the index, so it must run
        // on the main thread. The background loader only deserializes existing cache files; if a
        // (re)import is required it bails and leaves it to the main-thread import flow.
        bool onMainThread = Thread.CurrentThread.ManagedThreadId == _mainThreadId;

        // Check if this is a sub-asset
        if (_subAssetIndex.TryGetValue(assetId, out var subInfo))
        {
            // Load the parent first this will also cache all sub-assets
            var parent = Get(subInfo.parentGuid);
            // After loading parent, sub-asset should be cached now
            return _loadedAssets.GetValueOrDefault(assetId);
        }

        // Try loading main asset from disk cache
        string cachePath = GetCachePath(assetId);
        var entry = GetEntry(assetId);

        // Validate cache check importer version matches current importer
        if (onMainThread && entry != null && !string.IsNullOrEmpty(entry.ImporterType))
        {
            var importer = Importers.ImporterRegistry.CreateByTypeName(entry.ImporterType);
            if (importer != null && importer.Version != entry.ImporterVersion)
            {
                // Cache is stale importer was updated since last import
                Runtime.Debug.Log($"Cache stale for '{entry.Path}': importer v{entry.ImporterVersion} -> v{importer.Version}. Reimporting.");
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

                var targetType = entry?.MainAssetType ?? typeof(EngineObject);

                var obj = Serializer.Deserialize(echo, targetType) as EngineObject;
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

        // Cache miss try importing on demand (main thread only the loader leaves importing
        // to the main-thread flow and simply reports the asset as not-yet-available).
        if (onMainThread && _guidToEntry.TryGetValue(assetId, out var e) && !string.IsNullOrEmpty(e.Path))
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
                var subType = sub.Type ?? typeof(EngineObject);
                var obj = Serializer.Deserialize(echo, subType) as EngineObject;
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
        // Track which paths we find on disk across all roots
        var foundPaths = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        // Primary, writable asset root.
        ScanRoot(_project.AssetsPath, "", foundPaths);
        // Read-only engine defaults, materialized as a sibling of Assets/. Stored with a
        // "Defaults/" prefix so its entries resolve via Project.ToAbsolutePath.
        ScanRoot(_project.DefaultsPath, Projects.Project.DefaultsFolder, foundPaths);

        RemoveStaleEntries(foundPaths);
    }

    /// <summary>
    /// Scan a single asset root. <paramref name="relativePrefix"/> is prepended to each discovered
    /// path ("" for the Assets/ root, "Defaults" for the read-only Defaults/ sibling) so entry
    /// paths stay project-relative and round-trip through <see cref="Projects.Project.ToAbsolutePath"/>.
    /// </summary>
    private void ScanRoot(string rootPath, string relativePrefix, HashSet<string> foundPaths)
    {
        if (!Directory.Exists(rootPath)) return;

        foreach (var file in Directory.EnumerateFiles(rootPath, "*", SearchOption.AllDirectories))
        {
            // Skip .meta files and hidden files
            if (file.EndsWith(".meta", StringComparison.OrdinalIgnoreCase)) continue;
            string fileName = Path.GetFileName(file);
            if (fileName.StartsWith('.')) continue;

            string rel = NormalizePath(Path.GetRelativePath(rootPath, file));
            string relativePath = string.IsNullOrEmpty(relativePrefix) ? rel : $"{relativePrefix}/{rel}";
            foundPaths.Add(relativePath);

            // Determine importer
            string ext = Path.GetExtension(file);
            string importerName = ImporterRegistry.GetImporterTypeName(ext);

            // Get default settings from the importer (for new meta files)
            var importer = ImporterRegistry.CreateByTypeName(importerName);
            var defaultSettings = importer?.DefaultSettings();

            // Engine default shaders are forced onto their deterministic BuiltInAssets GUID so
            // Shader.LoadDefault resolves them; everything else gets a random GUID.
            Guid? forcedGuid = DefaultAssetGuid.TryGet(relativePath, out var defaultGuid) ? defaultGuid : null;

            // Ensure .meta exists (with default settings if creating new)
            var meta = MetaFile.EnsureMeta(file, importerName, importer?.Version ?? 1, defaultSettings, forcedGuid);

            if (_pathToGuid.TryGetValue(relativePath, out var existingGuid))
            {
                // Already tracked check if needs reimport
                var entry = _guidToEntry[existingGuid];
                long currentTicks = File.GetLastWriteTimeUtc(file).Ticks;

                // Reimport if the file changed, its cache is missing, OR the importer's version was
                // bumped (a new editor build with changed import logic must re-run stale caches).
                if (entry.LastModifiedTicks != currentTicks
                    || !File.Exists(GetCachePath(existingGuid))
                    || (importer != null && entry.ImporterVersion != importer.Version))
                    entry.NeedsReimport = true;

                // Update GUID if meta was regenerated with different GUID
                if (meta.Guid != existingGuid)
                {
                    // Clean up old GUID references
                    _guidToEntry.TryRemove(existingGuid, out _);
                    _pathToGuid.Remove(relativePath);
                    _loadedAssets.TryRemove(existingGuid, out _);
                    _dependencies.RemoveAsset(existingGuid);

                    // Remove old sub-asset index entries
                    if (entry.SubAssets != null)
                    {
                        foreach (var sub in entry.SubAssets)
                        {
                            _subAssetIndex.TryRemove(sub.Guid, out _);
                            _loadedAssets.TryRemove(sub.Guid, out _);
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
                var entry = _guidToEntry[meta.Guid];
                string oldPath = entry.Path;
                bool originalStillExists = File.Exists(Path.Combine(assetsPath, oldPath))
                    && !oldPath.Equals(relativePath, StringComparison.OrdinalIgnoreCase);

                if (originalStillExists)
                {
                    // Two files share a GUID (the asset + its .meta were copied). A GUID must be unique,
                    // so mint a fresh one for the copy and rewrite its .meta.
                    var newGuid = Guid.NewGuid();
                    meta.Guid = newGuid;
                    MetaFile.Write(MetaFile.GetMetaPath(file), meta);

                    var copyEntry = new AssetEntry
                    {
                        Guid = newGuid,
                        Path = relativePath,
                        ImporterType = importerName,
                        ImporterVersion = meta.ImporterVersion,
                        NeedsReimport = true
                    };
                    _guidToEntry[newGuid] = copyEntry;
                    _pathToGuid[relativePath] = newGuid;
                }
                else
                {
                    // The original is gone: this is a move/rename - just repoint the entry.
                    _pathToGuid.Remove(oldPath);
                    entry.Path = relativePath;
                    _pathToGuid[relativePath] = meta.Guid;
                }
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
        foreach (var dir in Directory.EnumerateDirectories(rootPath, "*", SearchOption.AllDirectories))
        {
            string dirName = Path.GetFileName(dir);
            if (dirName.StartsWith('.')) continue;
            // Ensure folder .meta exists (for stable folder GUIDs in version control)
            MetaFile.EnsureMeta(dir, "DefaultImporter");
        }
    }

    /// <summary>Drop tracked entries (and their caches/sub-assets) whose files are gone from disk.</summary>
    private void RemoveStaleEntries(HashSet<string> foundPaths)
    {
        // Remove entries for files that no longer exist
        var toRemove = _guidToEntry.Where(kv => !foundPaths.Contains(kv.Value.Path))
            .Select(kv => kv.Key).ToList();

        foreach (var guid in toRemove)
        {
            var entry = _guidToEntry[guid];

            // Dispose main + sub-assets so AssetRefs detect invalidation
            DisposeAndRemove(guid);
            if (entry.SubAssets != null)
            {
                foreach (var sub in entry.SubAssets)
                {
                    DisposeAndRemove(sub.Guid);
                    _subAssetIndex.TryRemove(sub.Guid, out _);

                    // Clean sub-asset cache file
                    string subCachePath = GetCachePath(sub.Guid);
                    if (File.Exists(subCachePath))
                        try { File.Delete(subCachePath); } catch { }
                }
            }

            _pathToGuid.Remove(entry.Path);
            _guidToEntry.TryRemove(guid, out _);
            _dependencies.RemoveAsset(guid);

            // Clean main cache file
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
        string absolutePath = _project.ToAbsolutePath(entry.Path);
        if (!File.Exists(absolutePath))
        {
            entry.NeedsReimport = false;
            return false;
        }

        // If the entry's ImporterType doesn't resolve (stale entry from before an
        // importer was registered), retry with the extension-based lookup. Common
        // case: asset created before its importer existed -> stuck on DefaultImporter.
        var resolved = ImporterRegistry.CreateByTypeName(entry.ImporterType);
        if (resolved == null)
        {
            string ext = Path.GetExtension(entry.Path);
            string freshName = ImporterRegistry.GetImporterTypeName(ext);
            if (freshName != entry.ImporterType)
            {
                Runtime.Debug.Log($"[AssetDatabase] '{entry.Path}': updating stale ImporterType '{entry.ImporterType}' -> '{freshName}'");
                entry.ImporterType = freshName;
                resolved = ImporterRegistry.CreateByTypeName(freshName);
            }
        }

        var importer = resolved ?? new DefaultImporter();
        Runtime.Debug.Log($"[AssetDatabase] Importing '{entry.Path}' via {importer.GetType().Name}");

        // Read settings from .meta, merge with importer defaults for any missing keys
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

        var defaults = importer.DefaultSettings();
        if (defaults != null)
        {
            if (settings == null)
            {
                settings = defaults.Clone();
            }
            else
            {
                foreach (var kvp in defaults.Tags)
                    if (!settings.TryGet(kvp.Key, out _))
                        settings[kvp.Key] = kvp.Value.Clone();
            }
        }

        try
        {
            // Create context with entry GUID so sub-assets get correct deterministic IDs
            var ctx = new Importers.ImportContext(entry.Guid, absolutePath, settings);
            bool success = importer.Import(ctx);

            if (!success || ctx.MainAsset == null)
            {
                entry.SubAssets = Array.Empty<SubAssetEntry>();
                entry.NeedsReimport = false;
                return false;
            }

            // Main asset ID already assigned by ctx.SetMainAsset
            ctx.MainAsset.AssetPath = entry.Path;
            if (string.IsNullOrEmpty(ctx.MainAsset.Name))
                ctx.MainAsset.Name = Path.GetFileNameWithoutExtension(entry.Path);

            _loadedAssets[entry.Guid] = ctx.MainAsset;
            entry.MainAssetType = ctx.MainAsset.GetType();
            entry.Dependencies = ctx.Dependencies.ToArray();

            // Process sub-assets IDs already assigned by ctx.AddSubAsset. Remember the previous set so
            // sub-assets that disappear this import (renamed/removed) can be cleaned up below.
            var previousSubGuids = entry.SubAssets?.Select(s => s.Guid).ToHashSet() ?? new HashSet<Guid>();
            var newSubGuids = new HashSet<Guid>();

            if (ctx.SubAssets.Count > 0)
            {
                var subEntries = new List<SubAssetEntry>();
                for (int i = 0; i < ctx.SubAssets.Count; i++)
                {
                    var sub = ctx.SubAssets[i];
                    if (sub == null) continue;

                    sub.AssetPath = $"{entry.Path}#{sub.Name}";

                    SerializeToCache(sub.AssetID, sub);
                    _loadedAssets[sub.AssetID] = sub;

                    subEntries.Add(new SubAssetEntry
                    {
                        Guid = sub.AssetID,
                        Name = sub.Name,
                        Type = sub.GetType()
                    });

                    _subAssetIndex[sub.AssetID] = (entry.Guid, i);
                    newSubGuids.Add(sub.AssetID);
                }
                entry.SubAssets = subEntries.ToArray();
            }
            else
            {
                entry.SubAssets = Array.Empty<SubAssetEntry>();
            }

            // Drop index entries, cached instances, caches and thumbnails for sub-assets that no
            // longer exist (their derived GUIDs change with their name, so they'd leak otherwise).
            foreach (var oldGuid in previousSubGuids)
            {
                if (newSubGuids.Contains(oldGuid)) continue;
                _subAssetIndex.TryRemove(oldGuid, out _);
                if (_loadedAssets.TryRemove(oldGuid, out var oldObj)) oldObj?.Dispose();
                ThumbnailGenerator.DeleteThumbnail(oldGuid, _project.ThumbnailsPath);
                string oldCache = GetCachePath(oldGuid);
                if (File.Exists(oldCache)) try { File.Delete(oldCache); } catch { }
            }

            // Serialize main asset
            SerializeToCache(entry.Guid, ctx.MainAsset);

            // Update timestamps
            entry.LastModifiedTicks = File.GetLastWriteTimeUtc(absolutePath).Ticks;
            entry.ImporterVersion = importer.Version;
            entry.NeedsReimport = false;

            // Update dependency graph
            _dependencies.SetDependencies(entry.Guid, ctx.Dependencies);

            // Queue thumbnail generation (lazy, one per frame)
            {
                string? sourceFile = ctx.MainAsset is Runtime.Resources.Texture2D ? absolutePath : null;
                ThumbnailGenerator.Enqueue(entry.Guid, ctx.MainAsset, sourceFile);
            }

            return true;
        }
        catch (Exception ex)
        {
            Runtime.Debug.LogError($"Import failed for '{entry.Path}': {ex.GetType().Name}: {ex.Message}\n{ex.StackTrace}");
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
            // Temporarily clear AssetID so the serializer writes the full object
            // instead of short-circuiting to a $assetId reference
            var savedId = obj.AssetID;
            obj.AssetID = Guid.Empty;

            // Force serialize with Base Type info, Required for Builds since they dont know the Type of the Asset
            var echo = Serializer.Serialize(typeof(object), obj);
            obj.AssetID = savedId;

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

    /// <summary>
    /// Resolve a GUID to a file path, including sub-assets (returns the parent asset's path).
    /// </summary>
    public string? GuidToPathIncludingSubAssets(Guid guid)
    {
        // Try main asset first
        if (_guidToEntry.TryGetValue(guid, out var entry))
            return entry.Path;
        // Try sub-asset -> parent
        if (_subAssetIndex.TryGetValue(guid, out var subInfo))
            return _guidToEntry.TryGetValue(subInfo.parentGuid, out var parentEntry) ? parentEntry.Path : null;
        return null;
    }

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

    /// <summary>
    /// Clear the cache for <see cref="_loadedAssets"/> on assembly reload so that scenes/prefabs that might hold
    /// user-defined scripts won't stop the ALC from reloading
    /// </summary>
    [OnAssemblyUnload]
    internal static void ClearScenesAndPrefabForReload()
    {
        var db = Instance;
        if (db == null) return;

        foreach (var kv in db._loadedAssets.ToArray())
        {
            EngineObject? asset = kv.Value;
            if (asset is null) continue;

            bool sensitive = asset is Runtime.Resources.Scene
                          || asset is Runtime.Resources.PrefabAsset
                          || asset.GetType().Assembly.IsCollectible;

            if (!sensitive) continue;

            if (db._loadedAssets.TryRemove(kv.Key, out var removed))
            {
                try
                {
                    removed?.Dispose();
                }
                catch(Exception e)
                {
                    Debug.LogException(e);
                }
            }
        }
    }

    /// <summary>Load a cached thumbnail for an asset. Returns (width, height, pixels) or null.</summary>
    public (int width, int height, byte[] pixels)? LoadThumbnail(Guid guid) => ThumbnailGenerator.LoadThumbnail(guid, _project.ThumbnailsPath);

    // ================================================================
    //  Asset CRUD
    // ================================================================

    /// <summary>
    /// Create a new asset file on disk from an EngineObject.
    /// </summary>
    public void CreateAsset(EngineObject obj, string relativePath)
    {
        relativePath = NormalizePath(relativePath);
        string absolutePath = Path.Combine(_project.AssetsPath, relativePath);
        Directory.CreateDirectory(Path.GetDirectoryName(absolutePath)!);

        // Serialize to the file (typeof(object) forces $type inclusion)
        var echo = Serializer.Serialize(typeof(object), obj);
        if (echo != null)
            File.WriteAllText(absolutePath, echo.WriteToString());

        // Create meta with correct importer version
        string ext = Path.GetExtension(relativePath);
        string importerName = ImporterRegistry.GetImporterTypeName(ext);
        var importer = ImporterRegistry.CreateByTypeName(importerName);
        var meta = MetaFile.CreateNew(importerName, importer?.Version ?? 1);
        MetaFile.Write(MetaFile.GetMetaPath(absolutePath), meta);

        // Assign the GUID and path to the original instance so any existing
        // AssetRef holding this instance picks up the asset ID immediately,
        obj.AssetID = meta.Guid;
        obj.AssetPath = relativePath;

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
    /// Import a file that already exists on disk under Assets/ (e.g. a baked lightmap PNG written by
    /// the lightmapper), creating/refreshing its metadata + cache, and return its asset GUID. If the
    /// path is already tracked it's reimported in place (so a re-bake replaces the previous asset).
    /// </summary>
    public Guid ImportFile(string relativePath)
    {
        relativePath = NormalizePath(relativePath);
        string absolutePath = Path.Combine(_project.AssetsPath, relativePath);
        if (!File.Exists(absolutePath)) return Guid.Empty;

        string ext = Path.GetExtension(relativePath);
        string importerName = ImporterRegistry.GetImporterTypeName(ext);
        var importer = ImporterRegistry.CreateByTypeName(importerName);
        var meta = MetaFile.EnsureMeta(absolutePath, importerName, importer?.Version ?? 1, importer?.DefaultSettings());

        // If we still track this path under a guid that no longer matches the on-disk .meta, the file
        // was replaced out-of-band (e.g. the lightmapper deletes + rewrites its whole folder via the
        // filesystem, bypassing the database, so EnsureMeta minted a fresh guid). The .meta is the
        // authoritative source after a restart, so drop the stale mapping and re-register under
        // meta.Guid. Otherwise the reimport-in-place branch below returns the old guid, which the
        // caller persists (e.g. into the scene's lightmap refs) yet nothing resolves to it on reload.
        if (_pathToGuid.TryGetValue(relativePath, out var staleGuid) && staleGuid != meta.Guid)
        {
            DisposeAndRemove(staleGuid);
            _guidToEntry.TryRemove(staleGuid, out _);
            _pathToGuid.Remove(relativePath);
        }

        // Already tracked at this path -> reimport in place (re-bake replacement).
        if (_pathToGuid.TryGetValue(relativePath, out var existingGuid) && _guidToEntry.TryGetValue(existingGuid, out var existing))
        {
            DisposeAndRemove(existing.Guid);
            existing.NeedsReimport = true;
            RunImport(existing);
            MetadataCache.Save(_project.MetadataDbPath, _guidToEntry.Values);
            return existing.Guid;
        }

        var entry = new AssetEntry
        {
            Guid = meta.Guid,
            Path = relativePath,
            ImporterType = importerName,
            NeedsReimport = true,
        };
        _guidToEntry[meta.Guid] = entry;
        _pathToGuid[relativePath] = meta.Guid;
        RunImport(entry);
        MetadataCache.Save(_project.MetadataDbPath, _guidToEntry.Values);
        return meta.Guid;
    }

    /// <summary>
    /// Re-serialize an in-memory asset back to its source file.
    /// </summary>
    public void SaveAsset(EngineObject obj)
    {
        if (obj.AssetID == Guid.Empty || string.IsNullOrEmpty(obj.AssetPath)) return;

        // Sub-assets have paths like "Model.fbx#Mesh_0" can't save those directly
        if (obj.AssetPath.Contains('#'))
        {
            Runtime.Debug.LogWarning($"Cannot save sub-asset directly: {obj.AssetPath}");
            return;
        }

        string absolutePath = Path.Combine(_project.AssetsPath, obj.AssetPath);
        var echo = Serializer.Serialize(typeof(object), obj);
        if (echo != null)
            File.WriteAllText(absolutePath, echo.WriteToString());

        // Reimport to update cache. Dispose the previous main + sub-asset instances
        // first so any holding AssetRef sees them as invalid and re-resolves to the
        // freshly-imported instance. Without this, downstream refs (e.g. a Material
        // pointing at a shader sub-asset that was just regenerated) keep the stale
        // instance until the user manually reimports the dependent asset.
        if (_guidToEntry.TryGetValue(obj.AssetID, out var entry))
        {
            DisposeAndRemove(obj.AssetID);
            if (entry.SubAssets != null)
                foreach (var sub in entry.SubAssets)
                    DisposeAndRemove(sub.Guid);

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
        relativePath = NormalizePath(relativePath);
        string absolutePath = Path.Combine(_project.AssetsPath, relativePath);

        if (_pathToGuid.TryGetValue(relativePath, out var guid))
        {
            // Dispose main + sub-asset instances FIRST so any AssetRef holding them
            // detects IsNotValid on next access and stops returning the deleted
            // instance. Without this, materials etc. keep using the now-orphaned
            // shader (cached in _shader.instance) until the editor restarts.
            var entry = _guidToEntry.TryGetValue(guid, out var e) ? e : null;
            DisposeAndRemove(guid);
            ThumbnailGenerator.DeleteThumbnail(guid, _project.ThumbnailsPath);
            if (entry?.SubAssets != null)
                foreach (var sub in entry.SubAssets)
                {
                    DisposeAndRemove(sub.Guid);
                    _subAssetIndex.TryRemove(sub.Guid, out _);
                    ThumbnailGenerator.DeleteThumbnail(sub.Guid, _project.ThumbnailsPath);

                    // Clean sub-asset cache file
                    string subCachePath = GetCachePath(sub.Guid);
                    if (File.Exists(subCachePath))
                        try { File.Delete(subCachePath); } catch { }
                }

            _guidToEntry.TryRemove(guid, out _);
            _pathToGuid.Remove(relativePath);
            _dependencies.RemoveAsset(guid);

            // Clean main cache file
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

        // Script deleted - trigger recompile
        if (relativePath.EndsWith(".cs", StringComparison.OrdinalIgnoreCase))
            ScriptAssemblyManager.RequestRecompile();
    }

    /// <summary>
    /// Move/rename an asset. The GUID stays the same.
    /// </summary>
    public bool MoveAsset(string oldRelativePath, string newRelativePath)
    {
        oldRelativePath = NormalizePath(oldRelativePath);
        newRelativePath = NormalizePath(newRelativePath);
        string oldAbsolute = Path.Combine(_project.AssetsPath, oldRelativePath);
        string newAbsolute = Path.Combine(_project.AssetsPath, newRelativePath);

        if (!File.Exists(oldAbsolute)) return false;

        if (File.Exists(newAbsolute))
        {
            Runtime.Debug.LogWarning($"Cannot rename: a file already exists at '{newRelativePath}'.");
            return false;
        }

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
            Runtime.Debug.LogError($"Failed to move asset '{oldRelativePath}' -> '{newRelativePath}': {ex.Message}");
            return false;
        }

        // Update index
        if (_pathToGuid.TryGetValue(oldRelativePath, out var guid))
        {
            _pathToGuid.Remove(oldRelativePath);
            _pathToGuid[newRelativePath] = guid;
            _guidToEntry[guid].Path = newRelativePath;

            if (_loadedAssets.TryGetValue(guid, out var obj))
                obj.AssetPath = newRelativePath;

            // Update sub-asset AssetPaths (they use "parent/path#SubName" format)
            var movedEntry = _guidToEntry.GetValueOrDefault(guid);
            if (movedEntry?.SubAssets != null)
            {
                foreach (var sub in movedEntry.SubAssets)
                {
                    if (_loadedAssets.TryGetValue(sub.Guid, out var subObj))
                        subObj.AssetPath = $"{newRelativePath}#{sub.Name}";
                }
            }
        }

        MetadataCache.Save(_project.MetadataDbPath, _guidToEntry.Values);
        OnAssetMoved?.Invoke(oldRelativePath, newRelativePath);
        return true;
    }

    /// <summary>
    /// Move a folder and everything inside it. GUIDs are preserved the metadata index is
    /// remapped in-place, and <see cref="OnAssetMoved"/> fires for every relocated file.
    /// </summary>
    public bool MoveFolder(string oldRelativeFolder, string newRelativeFolder)
    {
        oldRelativeFolder = oldRelativeFolder.Replace('\\', '/').TrimEnd('/');
        newRelativeFolder = newRelativeFolder.Replace('\\', '/').TrimEnd('/');
        if (oldRelativeFolder == newRelativeFolder) return true;

        string oldAbs = Path.Combine(_project.AssetsPath, oldRelativeFolder);
        string newAbs = Path.Combine(_project.AssetsPath, newRelativeFolder);

        if (!Directory.Exists(oldAbs)) return false;
        if (Directory.Exists(newAbs) || File.Exists(newAbs))
        {
            Runtime.Debug.LogWarning($"Cannot move folder: '{newRelativeFolder}' already exists.");
            return false;
        }

        // Guard against moving a folder into itself or a descendant that would delete the
        // parent while its children were still mid-copy on Windows.
        string oldWithSlash = oldRelativeFolder + "/";
        if (newRelativeFolder.Equals(oldRelativeFolder, StringComparison.OrdinalIgnoreCase)
            || newRelativeFolder.StartsWith(oldWithSlash, StringComparison.OrdinalIgnoreCase))
        {
            Runtime.Debug.LogWarning($"Cannot move folder '{oldRelativeFolder}' into itself.");
            return false;
        }

        // Snapshot the set of tracked paths inside the folder before the move the files on
        // disk move atomically via Directory.Move, but the in-memory index needs per-entry
        // path rewrites afterward.
        var toRemap = new List<(string oldPath, string newPath, Guid guid)>();
        foreach (var kv in _pathToGuid)
        {
            string p = kv.Key;
            if (p.Equals(oldRelativeFolder, StringComparison.OrdinalIgnoreCase)
                || p.StartsWith(oldWithSlash, StringComparison.OrdinalIgnoreCase))
            {
                string suffix = p.Substring(oldRelativeFolder.Length);
                string newPath = newRelativeFolder + suffix;
                toRemap.Add((p, newPath, kv.Value));
            }
        }

        Directory.CreateDirectory(Path.GetDirectoryName(newAbs)!);

        try
        {
            Directory.Move(oldAbs, newAbs);
        }
        catch (Exception ex)
        {
            Runtime.Debug.LogError($"Failed to move folder '{oldRelativeFolder}' -> '{newRelativeFolder}': {ex.Message}");
            return false;
        }

        foreach (var (oldPath, newPath, guid) in toRemap)
        {
            _pathToGuid.Remove(oldPath);
            _pathToGuid[newPath] = guid;
            if (_guidToEntry.TryGetValue(guid, out var entry))
            {
                entry.Path = newPath;

                // Update sub-asset AssetPaths (they use "parent/path#SubName" format)
                if (entry.SubAssets != null)
                {
                    foreach (var sub in entry.SubAssets)
                    {
                        if (_loadedAssets.TryGetValue(sub.Guid, out var subObj))
                            subObj.AssetPath = $"{newPath}#{sub.Name}";
                    }
                }
            }
            if (_loadedAssets.TryGetValue(guid, out var obj))
                obj.AssetPath = newPath;

            OnAssetMoved?.Invoke(oldPath, newPath);
        }

        MetadataCache.Save(_project.MetadataDbPath, _guidToEntry.Values);
        return true;
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
            _loadedAssets.TryRemove(guid, out _);
        }
    }

    public void Reimport(Guid guid)
    {
        if (_guidToEntry.TryGetValue(guid, out var entry))
        {
            // Dispose and remove the old cached instance this causes any AssetRef
            // holding it to detect IsNotValid and re-resolve via AssetDatabase.Get()
            DisposeAndRemove(guid);
            if (entry.SubAssets != null)
                foreach (var sub in entry.SubAssets)
                    DisposeAndRemove(sub.Guid);

            // Clear old thumbnails and invalidate UI cache
            ThumbnailGenerator.DeleteThumbnail(guid, _project.ThumbnailsPath);
            ProjectPanel.InvalidateThumbnail(guid);
            if (entry.SubAssets != null)
                foreach (var sub in entry.SubAssets)
                {
                    ThumbnailGenerator.DeleteThumbnail(sub.Guid, _project.ThumbnailsPath);
                    ProjectPanel.InvalidateThumbnail(sub.Guid);
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
                    ? _project.ToAbsolutePath(entry.Path)
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
            // Skip directory events - ScanAssets handles directory .meta creation
            if (Directory.Exists(evt.Path)) continue;

            string relativePath = ToRelativePath(evt.Path);

            // Skip .meta files we manage them
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

                    // Dispose previous main + sub-asset instances so any holding
                    // AssetRef detects them as invalid and re-resolves to the freshly
                    // imported instance. The Reimport() entry-point already does this;
                    // the watcher path needs to match or downstream refs (e.g. a
                    // Material pointing at a regenerated shader sub-asset) keep the
                    // stale instance until the user manually reimports.
                    var existingEntry = _guidToEntry[meta.Guid];
                    DisposeAndRemove(meta.Guid);
                    if (existingEntry.SubAssets != null)
                        foreach (var sub in existingEntry.SubAssets)
                            DisposeAndRemove(sub.Guid);

                    RunImport(existingEntry);
                    imported.Add(relativePath);
                    break;
                }

                case FileEventType.Deleted:
                {
                    if (_pathToGuid.TryGetValue(relativePath, out var guid))
                    {
                        var deletedEntry = _guidToEntry.GetValueOrDefault(guid);

                        // Dispose main + sub-assets so AssetRefs detect invalidation
                        DisposeAndRemove(guid);
                        if (deletedEntry?.SubAssets != null)
                        {
                            foreach (var sub in deletedEntry.SubAssets)
                            {
                                DisposeAndRemove(sub.Guid);
                                _subAssetIndex.TryRemove(sub.Guid, out _);

                                // Clean sub-asset cache file
                                string subCachePath = GetCachePath(sub.Guid);
                                if (File.Exists(subCachePath))
                                    try { File.Delete(subCachePath); } catch { }
                            }
                        }

                        _guidToEntry.TryRemove(guid, out _);
                        _pathToGuid.Remove(relativePath);
                        _dependencies.RemoveAsset(guid);

                        // Clean main cache file
                        string cachePath = GetCachePath(guid);
                        if (File.Exists(cachePath))
                            try { File.Delete(cachePath); } catch { }

                        deleted.Add(relativePath);

                        // Script deleted trigger recompile
                        if (relativePath.EndsWith(".cs", StringComparison.OrdinalIgnoreCase))
                            ScriptAssemblyManager.RequestRecompile();
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
                            var renamedEntry = _guidToEntry[guid];
                            renamedEntry.Path = relativePath;

                            // Move .meta
                            string oldMeta = MetaFile.GetMetaPath(evt.OldPath);
                            string newMeta = MetaFile.GetMetaPath(evt.Path);
                            if (File.Exists(oldMeta) && !File.Exists(newMeta))
                                try { File.Move(oldMeta, newMeta); } catch { }

                            if (_loadedAssets.TryGetValue(guid, out var obj))
                                obj.AssetPath = relativePath;

                            // Update sub-asset AssetPaths
                            if (renamedEntry.SubAssets != null)
                            {
                                foreach (var sub in renamedEntry.SubAssets)
                                {
                                    if (_loadedAssets.TryGetValue(sub.Guid, out var subObj))
                                        subObj.AssetPath = $"{relativePath}#{sub.Name}";
                                }
                            }

                            // If extension changed, update importer and trigger reimport
                            string oldExt = Path.GetExtension(evt.OldPath);
                            string newExt = Path.GetExtension(evt.Path);
                            if (!string.Equals(oldExt, newExt, StringComparison.OrdinalIgnoreCase))
                            {
                                string newImporterName = ImporterRegistry.GetImporterTypeName(newExt);
                                renamedEntry.ImporterType = newImporterName;
                                renamedEntry.NeedsReimport = true;

                                DisposeAndRemove(guid);
                                if (renamedEntry.SubAssets != null)
                                    foreach (var sub in renamedEntry.SubAssets)
                                        DisposeAndRemove(sub.Guid);

                                RunImport(renamedEntry);
                                imported.Add(relativePath);
                            }

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

            // Refresh resources map if any changes involved Resources/ folders
            if (imported.Any(p => p.Contains("/Resources/") || p.StartsWith("Resources/"))
                || deleted.Any(p => p.Contains("/Resources/") || p.StartsWith("Resources/")))
                RefreshResourcesMap();
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

    /// <summary>Get a project-relative path from an absolute path, normalized and root-aware.</summary>
    public string ToRelativePath(string absolutePath)
        => _project.ToRelativePath(absolutePath);

    public void Dispose()
    {
        _watcher?.Dispose();
        _watcher = null;

        // Clear the global registrations if they still point at this instance, so a torn-down
        // database (e.g. between tests) doesn't leave dangling statics behind.
        if (Instance == this) Instance = null;
        if (Runtime.AssetDatabase.Current == this) Runtime.AssetDatabase.Current = null;
    }
}
