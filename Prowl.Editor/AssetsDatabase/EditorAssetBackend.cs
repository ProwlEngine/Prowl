using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;

using Prowl.Echo;
using Prowl.Editor.Importers;
using Prowl.Editor.Prefabs;
using Prowl.Editor.Projects;
using Prowl.Editor.Projects.Scripting;
using Prowl.Editor.Thumbnails;
using Prowl.Runtime;

namespace Prowl.Editor;

/// <summary>
/// Central asset database for the editor. Manages the full asset lifecycle: scanning, importing,
/// caching, file watching. The runtime's <see cref="AssetDatabase"/> owns the asset objects, and this
/// answers what a GUID is and reads its import cache when one loads.
/// </summary>
public class EditorAssetBackend : AssetBackend
{
    public static EditorAssetBackend? Instance { get; private set; }

    private readonly Project _project;
    // Concurrent so the loader and build threads can read entries while the main thread imports and scans.
    private readonly ConcurrentDictionary<Guid, AssetEntry> _guidToEntry = new();
    private readonly ConcurrentDictionary<string, Guid> _pathToGuid = new(StringComparer.OrdinalIgnoreCase);
    private readonly ConcurrentDictionary<Guid, (Guid parentGuid, int index)> _subAssetIndex = new();
    // GPU-uploaded thumbnail cache. Main-thread-only (texture creation isn't thread-safe), same
    // as _pathToGuid every UI that shows asset thumbnails shares this instead of keeping its own.
    private readonly Dictionary<Guid, Runtime.Resources.Texture2D?> _thumbnailTextures = new();
    // Importing (and the file writes / GPU work it implies) stays on the main thread; the
    // background loader only deserializes already-imported on-disk cache files.
    private int _mainThreadId = -1;
    // Held only around reading or replacing a cache file, so the loader never reads one mid-replace.
    private readonly object _cacheFileLock = new();
    private IReadOnlyList<ResourceEntry> _resources = [];
    private readonly DependencyGraph _dependencies = new();
    private AssetWatcher? _watcher;
    private FileSystemWatcher? _buildPropsWatcher;

    // Cached folder/file structure - the single source of truth the Project Panel reads instead of
    // hitting Directory.GetDirectories/GetFiles every frame. Rebuilt from one tree walk whenever the
    // watcher reports a structural change (see ProcessFileChanges), otherwise served from memory.
    // Main-thread-only (the panel and ProcessFileChanges both run there), so no locking is needed.
    private sealed class FolderContents
    {
        public readonly List<FolderRecord> SubFolders = new();
        public readonly List<FileRecord> Files = new();
    }
    private readonly Dictionary<string, FolderContents> _folderIndex = new(StringComparer.OrdinalIgnoreCase);
    private bool _folderIndexDirty = true;

    // Events
    /// <summary> Raised after one or more assets have been imported. The string array contains the relative paths of the imported assets. </summary>
    public event Action<string[]>? OnAssetsImported;
    /// <summary> Raised after one or more assets have been deleted. The string array contains the relative paths of the deleted assets. </summary>
    public event Action<string[]>? OnAssetsDeleted;
    /// <summary> Raised after an asset is moved or renamed. Provides the old and new relative paths. </summary>
    public event Action<string, string>? OnAssetMoved;

    public EditorAssetBackend(Project project)
    {
        _project = project;
    }

    // ================================================================
    //  Initialization
    // ================================================================

    /// <summary> Initialize the asset database: set up the instance, register event hooks, load the metadata cache, scan and import assets, start file watchers, and build the shader menu catalog. Idempotent. </summary>
    public void Initialize()
    {
        _mainThreadId = Thread.CurrentThread.ManagedThreadId;

        Instance = this;
        AssetDatabase.Backend = this;
        AssetLoader.SetMainThread();

        AssetDatabase.Loaded -= EnqueueThumbnailIfMissing;
        AssetDatabase.Loaded += EnqueueThumbnailIfMissing;

        // Importers (and every other EditorRegistries scanner) must be ready before anything below
        // tries to import a file - idempotent, so this is cheap on every call after the first.
        EditorRegistries.Initialize();

        // A prefab that changed on disk - edited elsewhere, pulled from source control, or a model
        // whose import settings were changed - has to reach the instances already in the open scene.
        // Hooked here rather than at each construction site so every backend, including the ones
        // tests build, behaves the same. Detached first: Initialize is documented as idempotent, and
        // subscribing twice would refresh every instance twice per import.
        OnAssetsImported -= PrefabUtility.OnAssetsImported;
        OnAssetsImported += PrefabUtility.OnAssetsImported;
        AssetDatabase.Reloaded -= PrefabUtility.OnAssetReloaded;
        AssetDatabase.Reloaded += PrefabUtility.OnAssetReloaded;

        // A deleted prefab must stop being the baseline instances are compared against, or an edit to
        // an instance keeps recording overrides against contents that no longer exist anywhere.
        OnAssetsDeleted -= PrefabUtility.OnAssetsDeleted;
        OnAssetsDeleted += PrefabUtility.OnAssetsDeleted;

        // The notification above only reaches the scene that was open when the import happened, so a
        // scene opened afterwards has to catch up on whatever changed while it was closed.
        Runtime.Resources.Scene.OnSceneLoaded -= PrefabUtility.OnSceneLoaded;
        Runtime.Resources.Scene.OnSceneLoaded += PrefabUtility.OnSceneLoaded;

        // Remove any ".meta.tmp" files left behind by a crash/power-loss mid-write before
        // ScanAssets runs, so it doesn't pick them up and import them as real asset files.
        CleanupOrphanedMetaTempFiles();

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
                if (entry.Dependencies.Length + entry.SoftDependencies.Length > 0)
                    _dependencies.SetDependencies(guid, entry.Dependencies.Concat(entry.SoftDependencies));

                // Sub-assets have their own dependency-graph entry too (see RunImport) - needs the
                // same seeding or a later DependenciesOnly build silently drops what they reference.
                if (entry.SubAssets is { Length: > 0 })
                {
                    foreach (var sub in entry.SubAssets)
                        if (sub.Dependencies.Length + sub.SoftDependencies.Length > 0)
                            _dependencies.SetDependencies(sub.Guid, sub.Dependencies.Concat(sub.SoftDependencies));
                }
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

        // Watch Directory.Build.props (project root, outside Assets) so adding a NuGet package there
        // triggers a recompile / package restore, the same as editing the Packages UI used to.
        StartBuildPropsWatcher();

        // Read every tracked shader's declared menu path now that the scan and import have settled,
        // so the material inspector's shader picker opens against a warm catalog.
        RebuildShaderMenuPaths();

        Runtime.Debug.Log($"Asset database initialized: {_guidToEntry.Count} assets tracked.");

        // What FindResource resolves in play mode.
        RefreshResourcesMap();
    }

    /// <summary>
    /// Watch the project's Directory.Build.props (which lives at the root, outside Assets/, so the
    /// AssetWatcher never sees it) and request a recompile when it changes. That is what makes an
    /// added NuGet PackageReference take effect without the user manually rebuilding.
    /// </summary>
    private void StartBuildPropsWatcher()
    {
        if (!Directory.Exists(_project.RootPath)) return;

        _buildPropsWatcher = new FileSystemWatcher(_project.RootPath, "Directory.Build.props")
        {
            IncludeSubdirectories = false,
            NotifyFilter = NotifyFilters.LastWrite | NotifyFilters.FileName | NotifyFilters.Size,
        };
        void OnPropsChanged(object? _, FileSystemEventArgs __)
            => ScriptAssemblyManager.RequestRecompile();

        _buildPropsWatcher.Changed += OnPropsChanged;
        _buildPropsWatcher.Created += OnPropsChanged;
        _buildPropsWatcher.Deleted += OnPropsChanged;
        _buildPropsWatcher.Renamed += OnPropsChanged;
        _buildPropsWatcher.EnableRaisingEvents = true;
    }

    /// <summary>
    /// Delete leftover temp files from a previous session's write that never got renamed into place
    /// (e.g. the editor crashed mid-write). These are always incomplete data never valid on their
    /// own so ScanAssets must not pick them up as real asset files.
    /// </summary>
    private void CleanupOrphanedMetaTempFiles()
    {
        var assetsPath = _project.AssetsPath;
        if (!Directory.Exists(assetsPath)) return;

        foreach (string pattern in new[] { "*.meta.tmp", "*.prefab.tmp" })
            foreach (var tmp in Directory.EnumerateFiles(assetsPath, pattern, SearchOption.AllDirectories))
            {
                try { File.Delete(tmp); }
                catch (Exception ex) { Runtime.Debug.LogWarning($"Failed to delete orphaned temp file '{tmp}': {ex.Message}"); }
            }
    }

    /// <summary>Every asset under a Resources folder, for <see cref="AssetDatabase.FindResource{T}"/>. Rebuilt after any change to the index.</summary>
    public override IReadOnlyList<ResourceEntry> Resources
    {
        get
        {
            // The index belongs to the main thread, so another thread reads the last map built.
            if (_resourcesIndexVersion != IndexVersion && AssetLoader.IsMainThread && !AssetLoader.IsLoaderThread) RefreshResourcesMap();
            return _resources;
        }
    }

    private int _resourcesIndexVersion = -1;

    /// <summary>Scan all assets under Resources/ folders and rebuild what <see cref="AssetDatabase.FindResource{T}"/> resolves.</summary>
    public void RefreshResourcesMap()
    {
        var resources = new List<ResourceEntry>();
        foreach (var entry in _guidToEntry.Values.OrderBy(e => e.Path, StringComparer.OrdinalIgnoreCase))
            AddResourcePaths(resources, entry);
        _resources = resources;
        _resourcesIndexVersion = IndexVersion;
    }

    /// <summary>
    /// Adds the load paths of <paramref name="entry"/> and its sub assets to <paramref name="resources"/> when it
    /// lives inside a Resources folder. Shared with the build so the editor and a player resolve alike.
    /// Callers add entries in asset path order, which decides who wins when load paths clash.
    /// </summary>
    internal static void AddResourcePaths(List<ResourceEntry> resources, AssetEntry entry)
    {
        string? loadPath = AssetDatabase.GetLoadPath(entry.Path);
        if (loadPath == null) return;

        resources.Add(new ResourceEntry(loadPath, entry.Guid, entry.MainAssetTypeName ?? ""));
        foreach (var sub in entry.SubAssets)
            resources.Add(new ResourceEntry(AssetDatabase.GetLoadPath(entry.Path, sub.Name)!, sub.Guid, sub.TypeName));
    }

    // ================================================================
    //  Asset content
    // ================================================================

    public override Type? GetAssetType(Guid assetId)
    {
        if (_guidToEntry.TryGetValue(assetId, out var entry)) return entry.MainAssetType;
        if (_subAssetIndex.TryGetValue(assetId, out var sub) && _guidToEntry.TryGetValue(sub.parentGuid, out var parent)
            && sub.index < parent.SubAssets.Length)
            return parent.SubAssets[sub.index].Type;
        return null;
    }

    public override string? GetAssetPath(Guid assetId)
    {
        if (_guidToEntry.TryGetValue(assetId, out var entry)) return entry.Path;
        if (_subAssetIndex.TryGetValue(assetId, out var sub) && _guidToEntry.TryGetValue(sub.parentGuid, out var parent)
            && sub.index < parent.SubAssets.Length)
            return $"{parent.Path}#{parent.SubAssets[sub.index].Name}";
        return null;
    }

    public override IReadOnlyList<Guid> GetHardDependencies(Guid assetId) => GetManifestInfo(assetId).Hard;

    /// <summary>The type name, and the hard and soft dependencies, an asset ships with.</summary>
    public (string TypeName, Guid[] Hard, Guid[] Soft) GetManifestInfo(Guid assetId)
    {
        if (_guidToEntry.TryGetValue(assetId, out var entry))
            return (entry.MainAssetTypeName ?? "", entry.Dependencies, entry.SoftDependencies);
        if (_subAssetIndex.TryGetValue(assetId, out var sub) && _guidToEntry.TryGetValue(sub.parentGuid, out var parent)
            && sub.index < parent.SubAssets.Length)
        {
            SubAssetEntry subEntry = parent.SubAssets[sub.index];
            return (subEntry.TypeName, subEntry.Dependencies, subEntry.SoftDependencies);
        }
        return ("", [], []);
    }

    public override long GetEstimatedSize(Guid assetId)
    {
        try
        {
            var info = new FileInfo(GetCachePath(assetId));
            return info.Exists ? info.Length : 0;
        }
        catch { return 0; }
    }

    /// <summary>Stale or missing caches are imported first, which only the main thread may do.</summary>
    protected internal override bool NeedsMainThread(Guid assetId)
    {
        AssetEntry? entry = OwningEntry(assetId);
        if (entry == null || string.IsNullOrEmpty(entry.Path)) return false;
        if (entry.NeedsReimport || !File.Exists(GetCachePath(assetId))) return true;

        AssetImporter? importer = ImporterOf(entry);
        return (importer != null && importer.Version != entry.ImporterVersion) || IsSourceNewerThanImport(entry);
    }

    protected internal override void PrepareOnMainThread(Guid assetId)
    {
        AssetEntry? entry = OwningEntry(assetId);
        if (entry == null) return;

        Runtime.Debug.Log($"Cache stale for '{entry.Path}'. Reimporting.");
        entry.NeedsReimport = true;
        RunImport(entry);
    }

    protected internal override bool ReadContent(Guid assetId, Asset staging, SerializationContext context)
    {
        byte[] bytes;
        lock (_cacheFileLock)
        {
            string cachePath = GetCachePath(assetId);
            if (!File.Exists(cachePath)) return false;
            bytes = File.ReadAllBytes(cachePath);
        }

        using var stream = new MemoryStream(bytes);
        using var reader = new BinaryReader(stream);
        return ReadInto(EchoObject.ReadFromBinary(reader), staging, context);
    }

    // The entry a GUID imports through: its own, or its parent's for a sub-asset.
    private AssetEntry? OwningEntry(Guid assetId)
        => _subAssetIndex.TryGetValue(assetId, out var sub) ? GetEntry(sub.parentGuid) : GetEntry(assetId);

    /// <summary>Lazily backfill a thumbnail the first time an asset is actually loaded, for one whose
    /// thumbnail is missing (a fresh checkout, or a deleted thumbnail file).</summary>
    private void EnqueueThumbnailIfMissing(Asset asset)
    {
        if (!_guidToEntry.ContainsKey(asset.AssetID) && !_subAssetIndex.ContainsKey(asset.AssetID)) return;
        if (File.Exists(ThumbnailGenerator.GetThumbnailPath(asset.AssetID, _project.ThumbnailsPath)))
            return;

        string? sourceFile = asset is Runtime.Resources.Texture2D && !_subAssetIndex.ContainsKey(asset.AssetID)
            ? Path.Combine(_project.AssetsPath, asset.AssetPath)
            : null;
        ThumbnailGenerator.Enqueue(asset.AssetID, asset, sourceFile);
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

            // A single broken file (bad custom importer, corrupt .meta) must not abort scanning the
            // rest of the project - log it and move on, leaving that entry to retry on the next scan.
            try
            {
                ScanAssetFile(assetsPath, file, relativePath);
            }
            catch (Exception ex)
            {
                Runtime.Debug.LogError($"Failed to scan asset '{relativePath}': {ex.Message}");
            }
        }

        // Also scan for directories (they need .meta files too for folder GUIDs)
        foreach (var dir in Directory.EnumerateDirectories(assetsPath, "*", SearchOption.AllDirectories))
        {
            string dirName = Path.GetFileName(dir);
            if (dirName.StartsWith('.')) continue;
            // Ensure folder .meta exists (for stable folder GUIDs in version control). One unreadable
            // folder meta must not abort the rest of the scan, same as the file loop above.
            try { MetaFile.EnsureMeta(dir, "DefaultImporter"); }
            catch (Exception ex) { Runtime.Debug.LogError($"Failed to read folder meta for '{dir}': {ex.Message}"); }
        }

        // Remove entries for files that no longer exist
        var toRemove = _guidToEntry.Where(kv => !foundPaths.Contains(kv.Value.Path))
            .Select(kv => kv.Key).ToList();

        foreach (var guid in toRemove)
        {
            var entry = _guidToEntry[guid];

            // Missing rather than gone, so everything referencing them keeps the GUID.
            AssetDatabase.MarkMissing(guid);
            RemoveSubAssets(entry, includeThumbnails: false);

            _pathToGuid.TryRemove(entry.Path, out _);
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

    /// <summary>Reconcile a single asset file against the index: register new assets, flag changed ones
    /// for reimport, and handle GUID collisions/regeneration. Isolated per-file so ScanAssets can skip a
    /// broken file without losing the rest of the scan.</summary>
    private void ScanAssetFile(string assetsPath, string file, string relativePath)
    {
        // Determine importer
        string ext = Path.GetExtension(file);
        string importerName = EditorRegistries.GetImporterTypeName(ext);

        // Get default settings from the importer (for new meta files)
        var importer = EditorRegistries.CreateImporterByName(importerName);
        var defaultSettings = importer?.DefaultSettings();

        // Ensure .meta exists (with default settings if creating new)
        var meta = MetaFile.EnsureMeta(file, importerName, importer?.Version ?? 1, defaultSettings);

        if (_pathToGuid.TryGetValue(relativePath, out var existingGuid))
        {
            // Already tracked check if needs reimport
            var entry = _guidToEntry[existingGuid];
            long currentTicks = File.GetLastWriteTimeUtc(file).Ticks;

            // Only assets that produce an object have a cache to go missing. A script produces none,
            // so testing for one marks every script dirty on every scan, and reimporting a script asks
            // for a recompile.
            bool cacheMissing = entry.MainAssetTypeName != null && !File.Exists(GetCachePath(existingGuid));

            // Reimport if the file changed, its cache is missing, OR the importer's version was
            // bumped (a new editor build with changed import logic must re-run stale caches).
            if (entry.LastModifiedTicks != currentTicks
                || cacheMissing
                || (importer != null && entry.ImporterVersion != importer.Version))
                entry.NeedsReimport = true;

            // Update GUID if meta was regenerated with different GUID
            if (meta.Guid != existingGuid)
            {
                // Every existing reference to this asset (and any of its sub-assets) by the old
                // GUID now silently resolves to null - warn so a lost/corrupted .meta doesn't
                // look like an unrelated mystery bug later.
                Runtime.Debug.LogWarning(
                    $"Asset '{relativePath}' has a new GUID ({existingGuid} -> {meta.Guid}), " +
                    "likely from a regenerated .meta file. Any existing references to the old " +
                    "GUID (including its sub-assets, if any) are now broken.");

                // Clean up old GUID references
                _guidToEntry.TryRemove(existingGuid, out _);
                _pathToGuid.TryRemove(relativePath, out _);
                AssetDatabase.MarkMissing(existingGuid);
                _dependencies.RemoveAsset(existingGuid);

                // Remove old sub-asset index entries
                if (entry.SubAssets != null)
                {
                    foreach (var sub in entry.SubAssets)
                    {
                        _subAssetIndex.TryRemove(sub.Guid, out _);
                        AssetDatabase.MarkMissing(sub.Guid);
                        _dependencies.RemoveAsset(sub.Guid);
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
                // Two files share a GUID (the asset + its .meta were copied). A GUID must be unique, and
                // whichever file keeps it is what every existing reference in the project resolves to -
                // so the ORIGINAL has to keep it. Picking "whichever the directory walk reached second"
                // gets that backwards half the time, silently retargeting the whole project at the copy.
                // Older file wins, path as the tiebreak, so the outcome doesn't depend on walk order.
                string claimedAbsolute = Path.Combine(assetsPath, oldPath);
                if (IsOriginalOf(claimedAbsolute, oldPath, file, relativePath))
                {
                    AssignFreshGuid(file, relativePath, importerName, meta);
                }
                else
                {
                    // The file already holding the GUID is the copy - move it off, then let this one take it.
                    var displacedMeta = MetaFile.Read(MetaFile.GetMetaPath(claimedAbsolute));
                    AssignFreshGuid(claimedAbsolute, oldPath, entry.ImporterType, displacedMeta);

                    _guidToEntry.TryRemove(meta.Guid, out _);
                    AssetDatabase.MarkMissing(meta.Guid);
                    RemoveSubAssets(entry, includeThumbnails: true);

                    _guidToEntry[meta.Guid] = new AssetEntry
                    {
                        Guid = meta.Guid,
                        Path = relativePath,
                        ImporterType = importerName,
                        ImporterVersion = meta.ImporterVersion,
                        NeedsReimport = true
                    };
                    _pathToGuid[relativePath] = meta.Guid;
                }
            }
            else
            {
                // The original is gone: this is a move/rename - just repoint the entry.
                _pathToGuid.TryRemove(oldPath, out _);
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

    /// <summary>Of two files claiming one GUID, whether the first is the original that should keep it.
    /// Creation time decides; identical timestamps fall back to path order so the answer is stable.</summary>
    private static bool IsOriginalOf(string absoluteA, string relativeA, string absoluteB, string relativeB)
    {
        static DateTime Created(string path)
        {
            try { return File.GetCreationTimeUtc(path); }
            catch { return DateTime.MinValue; }
        }

        int byAge = Created(absoluteA).CompareTo(Created(absoluteB));
        return byAge != 0 ? byAge < 0 : string.CompareOrdinal(relativeA, relativeB) < 0;
    }

    /// <summary>Move a file onto a brand new GUID, rewriting its .meta and registering it.</summary>
    private void AssignFreshGuid(string absolutePath, string relativePath, string importerName, MetaFileData meta)
    {
        var newGuid = Guid.NewGuid();
        meta.Guid = newGuid;
        MetaFile.Write(MetaFile.GetMetaPath(absolutePath), meta);

        _guidToEntry[newGuid] = new AssetEntry
        {
            Guid = newGuid,
            Path = relativePath,
            ImporterType = importerName,
            ImporterVersion = meta.ImporterVersion,
            NeedsReimport = true
        };
        _pathToGuid[relativePath] = newGuid;
    }

    // ================================================================
    //  Importing
    // ================================================================

    private void ImportDirty()
    {
        var dirty = _guidToEntry.Values.Where(e => e.NeedsReimport).ToList();
        if (dirty.Count == 0) return;

        Runtime.Debug.Log($"Importing {dirty.Count} assets...");
        // Collect successful paths directly RunImport also clears NeedsReimport on failed
        // imports (to avoid retrying every frame), so filtering dirty by !NeedsReimport
        // afterward would include failed imports too and fire OnAssetsImported for them.
        var succeeded = new List<string>();

        int sinceReclaim = 0;

        foreach (var entry in dirty)
        {
            bool ok = RunImport(entry);

            if (ok)
                succeeded.Add(entry.Path);

            // Importing keeps nothing loaded, but what it allocated on the way lingers until collected.
            // A full collection every few entries bounds peak memory across a big batch without paying
            // for one per asset.
            if (++sinceReclaim >= 5)
            {
                sinceReclaim = 0;
                ReclaimMemory();
            }
        }

        Runtime.Debug.Log($"Import complete: {succeeded.Count}/{dirty.Count} succeeded.");

        if (succeeded.Count > 0)
        {
            IndexVersion++;
            OnAssetsImported?.Invoke(succeeded.ToArray());
            ReclaimMemory();
        }
    }

    // Importing (especially models/textures) transiently allocates a lot - raw file bytes,
    // per-polygon-vertex mesh unpacking, texture decode buffers - none of which is reachable once
    // disposed, but the CLR doesn't eagerly decommit freed Large Object Heap segments back to the OS
    // on its own. Left alone, a heavy import batch's peak memory (and the page file backing it)
    // lingers for the rest of the session instead of shrinking back down once the garbage is
    // actually collectible. Force a compacting collection to reclaim it immediately instead of
    // waiting on the GC's own heuristics, which are tuned for throughput, not for promptly returning
    // memory after a one-off spike.
    private static void ReclaimMemory()
    {
        System.Runtime.GCSettings.LargeObjectHeapCompactionMode = System.Runtime.GCLargeObjectHeapCompactionMode.CompactOnce;
        GC.Collect();
        GC.WaitForPendingFinalizers();
        GC.Collect();
    }

    // Main thread only. The loader reads cache files from its own thread, which _cacheFileLock keeps apart from the
    // import replacing them.
    private bool RunImport(AssetEntry entry)
    {
        bool imported = RunImportCore(entry);

        // Every add and every reimport funnels through here, so this is the one place the shader
        // catalog has to react to a declaration having possibly changed.
        if (IsShaderPath(entry.Path))
            InvalidateShaderMenuPath(entry.Guid);

        return imported;
    }

    private bool RunImportCore(AssetEntry entry)
    {
        string absolutePath = Path.Combine(_project.AssetsPath, entry.Path);
        if (!File.Exists(absolutePath))
        {
            entry.NeedsReimport = false;
            return false;
        }

        try
        {
            // If the entry's ImporterType doesn't resolve (stale entry from before an
            // importer was registered), retry with the extension-based lookup. Common
            // case: asset created before its importer existed -> stuck on DefaultImporter.
            var resolved = EditorRegistries.CreateImporterByName(entry.ImporterType);
            if (resolved == null)
            {
                string ext = Path.GetExtension(entry.Path);
                string freshName = EditorRegistries.GetImporterTypeName(ext);
                if (freshName != entry.ImporterType)
                {
                    Runtime.Debug.Log($"[AssetDatabase] '{entry.Path}': updating stale ImporterType '{entry.ImporterType}' -> '{freshName}'");
                    entry.ImporterType = freshName;
                    resolved = EditorRegistries.CreateImporterByName(freshName);
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

            // Create context with entry GUID so sub-assets get correct deterministic IDs
            var ctx = new Importers.ImportContext(entry.Guid, absolutePath, settings);
            // Taken before reading, so a change made while the import runs still reads as newer than it.
            long sourceTicks = File.GetLastWriteTimeUtc(absolutePath).Ticks;
            bool success = importer.Import(ctx);

            if (!success || ctx.MainAsset == null)
            {
                // Clearing the list alone would strand the sub-assets in the index and their caches on
                // disk, leaving GUIDs that resolve to a parent claiming it has no sub-assets.
                RemoveSubAssets(entry, includeThumbnails: true);
                entry.SubAssets = Array.Empty<SubAssetEntry>();
                entry.NeedsReimport = false;

                // An importer that succeeded without producing an object has still done its whole job,
                // so record it as imported. Left at zero the next scan reads the file as changed, which
                // for a script means asking for a recompile every time anything triggers a scan.
                if (success)
                {
                    entry.LastModifiedTicks = sourceTicks;
                    entry.ImporterVersion = importer.Version;
                }

                return false;
            }

            if (string.IsNullOrEmpty(ctx.MainAsset.Name))
                ctx.MainAsset.Name = Path.GetFileNameWithoutExtension(entry.Path);

            entry.MainAssetType = ctx.MainAsset.GetType();

            // Process sub-assets IDs already assigned by ctx.AddSubAsset. Remember the previous set so
            // sub-assets that disappear this import (renamed/removed) can be cleaned up below.
            var previousSubGuids = entry.SubAssets?.Select(s => s.Guid).ToHashSet() ?? new HashSet<Guid>();
            var newSubGuids = new HashSet<Guid>();
            bool cachesWritten = true;

            if (ctx.SubAssets.Count > 0)
            {
                var subEntries = new List<SubAssetEntry>();
                for (int i = 0; i < ctx.SubAssets.Count; i++)
                {
                    var sub = ctx.SubAssets[i];
                    if (sub == null) continue;

                    // A sub-asset (e.g. a Sprite) can reference assets of its own - track those under its
                    // own GUID, or a DependenciesOnly build's walk stops at the sub-asset.
                    var subCtx = new DependencySerializationContext();
                    if (!SerializeToCache(sub.AssetID, sub, subCtx))
                        cachesWritten = false;
                    _dependencies.SetDependencies(sub.AssetID, ShippedEdges(subCtx));

                    _subAssetIndex[sub.AssetID] = (entry.Guid, subEntries.Count);
                    newSubGuids.Add(sub.AssetID);

                    subEntries.Add(new SubAssetEntry
                    {
                        Guid = sub.AssetID,
                        Name = sub.Name,
                        Type = sub.GetType(),
                        // Persisted so the graph can be re-seeded on next startup (see Initialize()).
                        Dependencies = subCtx.Dependencies.ToArray(),
                        SoftDependencies = subCtx.SoftDependencies.ToArray(),
                    });
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
                RemoveSubAsset(oldGuid, includeThumbnails: true);
            }

            // Serialize main asset. Whatever the write reaches is a dependency, as well as what the importer reported.
            var mainCtx = new DependencySerializationContext();
            if (!SerializeToCache(entry.Guid, ctx.MainAsset, mainCtx))
                cachesWritten = false;
            mainCtx.Dependencies.UnionWith(ctx.Dependencies);
            mainCtx.SoftDependencies.UnionWith(ctx.SoftDependencies);
            mainCtx.SoftDependencies.ExceptWith(mainCtx.Dependencies);

            // The cache file IS the imported asset - it's what loads later and what a build ships. An
            // import that couldn't write one has produced nothing, so it must not be recorded as done:
            // that would leave the previous cache in place while claiming to be current.
            if (!cachesWritten)
            {
                Runtime.Debug.LogError(
                    $"Import of '{entry.Path}' could not write its cache. Leaving it flagged for reimport.");
                entry.LastModifiedTicks = 0;
                return false;
            }

            // Update timestamps
            entry.LastModifiedTicks = sourceTicks;
            entry.ImporterVersion = importer.Version;
            entry.NeedsReimport = false;

            // Hard and soft edges both ship. Editor edges (prefab links) only drive refreshes.
            entry.Dependencies = mainCtx.Dependencies.ToArray();
            entry.SoftDependencies = mainCtx.SoftDependencies.ToArray();
            entry.EditorDependencies = ctx.EditorDependencies.ToArray();
            _dependencies.SetDependencies(entry.Guid, ShippedEdges(mainCtx));

            // Every object already standing for this file takes the new content, so whatever holds one keeps it.
            RefillFromCache(entry.Guid, entry.MainAssetType);
            foreach (SubAssetEntry sub in entry.SubAssets)
                RefillFromCache(sub.Guid, sub.Type);

            // Queue thumbnail generation (lazy, one per frame) and drop any cached GPU texture for
            // the old content so the next access rebuilds it from the freshly generated thumbnail.
            {
                string? sourceFile = ctx.MainAsset is Runtime.Resources.Texture2D ? absolutePath : null;
                ThumbnailGenerator.Enqueue(entry.Guid, ctx.MainAsset, sourceFile);
                InvalidateThumbnailTexture(entry.Guid);
            }

            // Only the caches needed the GUIDs. Left on, what the import built would be a second object claiming each one.
            foreach (Asset built in ctx.SubAssets.Append(ctx.MainAsset))
                if (built is { Registered: false }) built.SetIdentity(Guid.Empty, "");

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

    private static HashSet<Guid> ShippedEdges(DependencySerializationContext context)
    {
        var edges = new HashSet<Guid>(context.Dependencies);
        edges.UnionWith(context.SoftDependencies);
        return edges;
    }

    /// <summary>
    /// Refills the stable object for a GUID from its new cache when it is loaded, brings one that was missing back,
    /// and retires one whose type the import changed.
    /// </summary>
    private void RefillFromCache(Guid guid, Type? type)
    {
        if (!AssetDatabase.TryGetExisting(guid, out Asset stable)) return;

        if (type != null && stable.GetType() != type)
        {
            AssetDatabase.Retire(guid);
            return;
        }
        AssetDatabase.Refill(stable, _refillReason);
    }

    /// <summary>Writes the asset's cache file, reporting whether it actually landed.</summary>
    private bool SerializeToCache(Guid guid, Asset obj, SerializationContext context)
    {
        string cachePath = GetCachePath(guid);
        Directory.CreateDirectory(Path.GetDirectoryName(cachePath)!);

        try
        {
            // With its base type, since a build does not know the type of what it loads.
            var echo = Serializer.Serialize(typeof(object), obj, context);
            if (echo == null)
            {
                Runtime.Debug.LogError($"Serializing asset {guid} produced nothing.");
                return false;
            }

            // Write to a temp file and rename into place (matching MetaFile.Write) so a
            // crash/power-loss mid-write can't leave a truncated cache file behind.
            string tempPath = cachePath + ".tmp";
            echo.WriteToBinary(new FileInfo(tempPath));
            lock (_cacheFileLock) File.Move(tempPath, cachePath, overwrite: true);
            return true;
        }
        catch (Exception ex)
        {
            Runtime.Debug.LogError($"Failed to cache asset {guid}: {ex.Message}");
            return false;
        }
    }

    // ================================================================
    //  Query API
    // ================================================================

    /// <summary> Get the asset entry for a GUID, or null if not tracked. </summary>
    public AssetEntry? GetEntry(Guid guid)
        => _guidToEntry.GetValueOrDefault(guid);

    /// <summary> Get the asset entry for a relative path, or null if not tracked. </summary>
    public AssetEntry? GetEntry(string relativePath)
        => _pathToGuid.TryGetValue(relativePath, out var guid) ? _guidToEntry.GetValueOrDefault(guid) : null;

    /// <summary> Resolve a relative path to its asset GUID. Returns Guid.Empty when the path is not tracked. </summary>
    public Guid PathToGuid(string relativePath)
        => _pathToGuid.GetValueOrDefault(relativePath);

    /// <summary> Resolve a GUID to its relative asset path, or null if not tracked. </summary>
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

    /// <summary>If <paramref name="guid"/> is a sub-asset, returns its parent's GUID. False for a
    /// main asset or an unknown GUID.</summary>
    public bool TryGetParentGuid(Guid guid, out Guid parentGuid)
    {
        if (_subAssetIndex.TryGetValue(guid, out var subInfo))
        {
            parentGuid = subInfo.parentGuid;
            return true;
        }
        parentGuid = Guid.Empty;
        return false;
    }

    /// <summary> Enumerate every tracked asset entry. </summary>
    public IEnumerable<AssetEntry> GetAllEntries() => _guidToEntry.Values;

    /// <summary> Find all main asset entries whose type is assignable to T. </summary>
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

    /// <summary> Get the relative path of every tracked asset. </summary>
    public string[] GetAllAssetPaths()
        => _pathToGuid.Keys.ToArray();

    // ================================================================
    //  Shader menu paths
    // ================================================================
    // A shader's menu path is the `Shader "Some/Path"` declaration at the top of its own source,
    // which is not something the importer records anywhere. The database reads it directly and
    // keeps it beside the entry index: the built-ins are compiled in and read once, project
    // shaders are re-read whenever they import and are validated against the live entries so a
    // deleted one stops being offered.

    /// <summary>
    /// Menu-path prefix for shaders the engine drives itself (post-process, gizmos, blits) rather
    /// than ones a material would be assigned. Excluded from <see cref="GetShaderCatalog"/> by default.
    /// </summary>
    public const string HiddenShaderPrefix = "Hidden/";

    // Kept apart from the project cache below: these are compiled into the runtime assembly, so
    // they are read once and never invalidated, and crucially they are never subject to the
    // liveness check (they have no asset entry to be live against).
    private readonly Dictionary<Guid, string> _builtInShaderPaths = new();

    // Written from the import path and read from the inspector, so this carries its own lock.
    private readonly Dictionary<Guid, string> _projectShaderPaths = new();
    private readonly object _shaderMenuLock = new();

    /// <summary>One assignable shader and the menu path it declares.</summary>
    public readonly struct ShaderMenuEntry
    {
        /// <summary>Asset GUID to assign, built-in or project.</summary>
        public Guid Guid { get; init; }
        /// <summary>The declared menu path, e.g. <c>"Default/Cutout/Standard"</c>.</summary>
        public string MenuPath { get; init; }
        /// <summary>True for the shaders embedded in the runtime assembly.</summary>
        public bool IsBuiltIn { get; init; }
    }

    /// <summary>
    /// Every shader the editor can assign, built-in and project, sorted by menu path.
    /// </summary>
    public List<ShaderMenuEntry> GetShaderCatalog(bool includeHidden = false)
    {
        var result = new List<ShaderMenuEntry>();

        lock (_shaderMenuLock)
        {
            SeedBuiltInShaderPaths();

            foreach (var (guid, path) in _builtInShaderPaths)
            {
                if (!includeHidden && path.StartsWith(HiddenShaderPrefix, StringComparison.Ordinal)) continue;
                result.Add(new ShaderMenuEntry { Guid = guid, MenuPath = path, IsBuiltIn = true });
            }

            // Driven off the live entries rather than the cache, so a shader whose entry has gone
            // stops being offered whether or not its cached path has been pruned yet.
            foreach (var entry in _guidToEntry.Values)
            {
                if (!IsShaderPath(entry.Path)) continue;

                string path = GetOrReadProjectShaderPath(entry.Guid, entry.Path);
                if (!includeHidden && path.StartsWith(HiddenShaderPrefix, StringComparison.Ordinal)) continue;
                result.Add(new ShaderMenuEntry { Guid = entry.Guid, MenuPath = path, IsBuiltIn = false });
            }
        }

        result.Sort((a, b) => string.Compare(a.MenuPath, b.MenuPath, StringComparison.OrdinalIgnoreCase));
        return result;
    }

    /// <summary>
    /// The menu path a shader GUID declares, or <paramref name="fallback"/> when it is not a shader
    /// this database knows about.
    /// </summary>
    public string GetShaderMenuPath(Guid guid, string fallback)
    {
        if (guid == Guid.Empty) return fallback;

        lock (_shaderMenuLock)
        {
            SeedBuiltInShaderPaths();
            if (_builtInShaderPaths.TryGetValue(guid, out string? builtIn)) return builtIn;

            if (_guidToEntry.TryGetValue(guid, out var entry) && IsShaderPath(entry.Path))
                return GetOrReadProjectShaderPath(guid, entry.Path);

            // Not a live shader any more (deleted, or never one). Drop whatever was cached so the
            // GUID does not keep reporting a path for an asset that is gone.
            _projectShaderPaths.Remove(guid);
            return fallback;
        }
    }

    private static bool IsShaderPath(string relativePath)
        => relativePath.EndsWith(".shader", StringComparison.OrdinalIgnoreCase);

    private void SeedBuiltInShaderPaths()
    {
        if (_builtInShaderPaths.Count > 0) return;

        foreach (Runtime.Resources.DefaultShader shader in Enum.GetValues<Runtime.Resources.DefaultShader>())
        {
            string path;
            try
            {
                path = Runtime.Resources.Shader.ReadDeclaredPath(Runtime.Resources.Shader.GetDefaultSource(shader))
                       ?? shader.ToString();
            }
            catch (Exception ex)
            {
                Runtime.Debug.LogWarning($"Could not read the declared path of built-in shader '{shader}': {ex.Message}");
                path = shader.ToString();
            }

            _builtInShaderPaths[Runtime.BuiltInAssets.GuidFor(shader)] = path;
        }
    }

    /// <summary>
    /// The cached menu path for a project shader, reading it off disk the first time. Import drops
    /// the cached value, so an edited declaration is picked up on the next read.
    /// </summary>
    private string GetOrReadProjectShaderPath(Guid guid, string relativePath)
    {
        if (_projectShaderPaths.TryGetValue(guid, out string? existing)) return existing;

        string path = ReadDeclaredShaderPath(Path.Combine(_project.AssetsPath, relativePath))
            ?? Path.GetFileNameWithoutExtension(relativePath);

        _projectShaderPaths[guid] = path;
        return path;
    }

    /// <summary>
    /// Called after a shader is imported so the next catalog read picks up an edited declaration.
    /// </summary>
    private void InvalidateShaderMenuPath(Guid guid)
    {
        lock (_shaderMenuLock)
            _projectShaderPaths.Remove(guid);
    }

    /// <summary>
    /// Reads every tracked shader's declaration and drops cached paths whose asset is gone. Run once
    /// the initial scan and import have settled, so the catalog is warm before anything asks for it.
    /// </summary>
    private void RebuildShaderMenuPaths()
    {
        lock (_shaderMenuLock)
        {
            SeedBuiltInShaderPaths();

            var live = new HashSet<Guid>();
            foreach (var entry in _guidToEntry.Values)
            {
                if (!IsShaderPath(entry.Path)) continue;
                live.Add(entry.Guid);
                GetOrReadProjectShaderPath(entry.Guid, entry.Path);
            }

            foreach (Guid guid in _projectShaderPaths.Keys.ToList())
                if (!live.Contains(guid))
                    _projectShaderPaths.Remove(guid);
        }
    }

    // Only the head of the file matters - the declaration sits at the top and a shader can be tens
    // of kilobytes of GLSL below it.
    private static string? ReadDeclaredShaderPath(string absolutePath)
    {
        const int HeadChars = 8 * 1024;
        try
        {
            using var reader = new StreamReader(absolutePath);
            char[] buffer = new char[HeadChars];
            int read = reader.ReadBlock(buffer, 0, HeadChars);
            return Runtime.Resources.Shader.ReadDeclaredPath(new string(buffer, 0, read));
        }
        catch (Exception ex)
        {
            Runtime.Debug.LogWarning($"Could not read shader '{absolutePath}': {ex.Message}");
            return null;
        }
    }

    // ================================================================
    //  Folder / file structure (cached tree the Project Panel reads)
    // ================================================================

    /// <summary>An immediate child folder of some folder, relative to the Assets root.</summary>
    public readonly struct FolderRecord
    {
        public readonly string RelativePath;
        public readonly string Name;
        public FolderRecord(string relativePath, string name) { RelativePath = relativePath; Name = name; }
    }

    /// <summary>An asset file (non-.meta) within a folder, with its cached size and modified time.</summary>
    public readonly struct FileRecord
    {
        public readonly string RelativePath;
        public readonly string Name;
        public readonly long Size;
        public readonly DateTime Modified;
        public FileRecord(string relativePath, string name, long size, DateTime modified)
        { RelativePath = relativePath; Name = name; Size = size; Modified = modified; }
    }

    /// <summary>Immediate subfolders of the given folder (""=Assets root). Served from the cached index.</summary>
    public IReadOnlyList<FolderRecord> GetSubFolders(string folderRelativePath)
    {
        EnsureFolderIndex();
        return _folderIndex.TryGetValue(NormalizePath(folderRelativePath ?? ""), out var c)
            ? c.SubFolders : Array.Empty<FolderRecord>();
    }

    /// <summary>Immediate asset files of the given folder (""=Assets root). Served from the cached index.</summary>
    public IReadOnlyList<FileRecord> GetFolderFiles(string folderRelativePath)
    {
        EnsureFolderIndex();
        return _folderIndex.TryGetValue(NormalizePath(folderRelativePath ?? ""), out var c)
            ? c.Files : Array.Empty<FileRecord>();
    }

    /// <summary>
    /// Bumped every time the folder/file index is invalidated or an asset is imported, deleted or moved.
    /// Lets views that rebuild a model from the index cache it and rebuild only when this changes.
    /// </summary>
    public int IndexVersion { get; private set; }

    /// <summary>Force the folder/file index to rebuild on next query. Driven by the asset watcher.</summary>
    public void InvalidateFolderIndex()
    {
        _folderIndexDirty = true;
        IndexVersion++;
    }

    private void EnsureFolderIndex()
    {
        if (!_folderIndexDirty) return;
        _folderIndex.Clear();
        _folderIndexDirty = false;

        var assetsPath = _project.AssetsPath;
        if (!Directory.Exists(assetsPath)) return;
        BuildFolderIndex(assetsPath, "");
    }

    private void BuildFolderIndex(string absolutePath, string relativePath)
    {
        var contents = new FolderContents();
        try
        {
            foreach (var dir in Directory.EnumerateDirectories(absolutePath))
            {
                string name = Path.GetFileName(dir);
                string childRel = relativePath.Length == 0 ? name : relativePath + "/" + name;
                contents.SubFolders.Add(new FolderRecord(childRel, name));

                // List hidden folders (so the content view can show them under "Show Hidden") but
                // don't descend into them - matches the folder tree's existing '.'-prefix skip and
                // avoids walking large hidden trees like .git.
                if (!name.StartsWith('.'))
                    BuildFolderIndex(dir, childRel);
            }

            foreach (var file in Directory.EnumerateFiles(absolutePath))
            {
                string name = Path.GetFileName(file);
                if (name.EndsWith(".meta", StringComparison.OrdinalIgnoreCase)) continue;

                string childRel = relativePath.Length == 0 ? name : relativePath + "/" + name;
                long size = 0; DateTime mod = DateTime.MinValue;
                try { var fi = new FileInfo(file); size = fi.Length; mod = fi.LastWriteTimeUtc; } catch { }
                contents.Files.Add(new FileRecord(childRel, name, size, mod));
            }
        }
        catch { }

        _folderIndex[relativePath] = contents;
    }

    /// <summary> Directed dependency graph tracking which assets reference which other assets. </summary>
    public DependencyGraph Dependencies => _dependencies;
    /// <summary> Absolute path to the folder where thumbnail cache files are stored. </summary>
    public string ThumbnailsPath => _project.ThumbnailsPath;

    /// <summary>Load a cached thumbnail for an asset. Returns (width, height, pixels) or null.</summary>
    public (int width, int height, byte[] pixels)? LoadThumbnail(Guid guid) => ThumbnailGenerator.LoadThumbnail(guid, _project.ThumbnailsPath);

    /// <summary>
    /// Resolve an asset GUID to its cached GPU thumbnail texture, building it from the on-disk
    /// pixel cache (<see cref="LoadThumbnail"/>) on first use. Returns null when no thumbnail
    /// exists yet. Shared by every UI that shows asset thumbnails (Project panel, asset pickers,
    /// Terrain layer tiles, etc.) so there's one GPU copy per asset, invalidated automatically
    /// whenever this database reimports or deletes that asset.
    /// </summary>
    public Runtime.Resources.Texture2D? GetThumbnailTexture(Guid guid)
    {
        if (guid == Guid.Empty) return null;

        if (_thumbnailTextures.TryGetValue(guid, out var cached))
            return cached;

        var thumb = LoadThumbnail(guid);
        if (thumb == null) return null;

        try
        {
            var (w, h, pixels) = thumb.Value;
            var tex = new Runtime.Resources.Texture2D((uint)w, (uint)h, false, TextureImageFormat.Color4b);
            tex.SetData<byte>(pixels);
            tex.SetTextureFilters(TextureMin.Linear, TextureMag.Linear);
            _thumbnailTextures[guid] = tex;
            return tex;
        }
        catch
        {
            _thumbnailTextures[guid] = null;
            return null;
        }
    }

    /// <summary>Dispose and drop a single cached thumbnail texture so it's rebuilt from disk on next access.</summary>
    public void InvalidateThumbnailTexture(Guid guid)
    {
        if (_thumbnailTextures.TryGetValue(guid, out var tex))
        {
            if (tex.IsValid()) tex.Dispose();
            _thumbnailTextures.Remove(guid);
        }
    }

    /// <summary>Dispose and clear every cached thumbnail texture (e.g. after the thumbnail size setting changes).</summary>
    public void ClearThumbnailTextureCache()
    {
        foreach (var tex in _thumbnailTextures.Values)
            if (tex.IsValid()) tex.Dispose();
        _thumbnailTextures.Clear();
    }

    // ================================================================
    //  Asset CRUD
    // ================================================================

    /// <summary>
    /// Create a new asset file on disk from an asset built in memory, which becomes that asset's object: whatever
    /// already holds it holds the database asset.
    /// </summary>
    public void CreateAsset(Asset obj, string relativePath)
    {
        relativePath = NormalizePath(relativePath);
        if (!TryResolveAssetPath(relativePath, out string absolutePath)) return;
        Directory.CreateDirectory(Path.GetDirectoryName(absolutePath)!);

        // Serialize to the file (typeof(object) forces $type inclusion)
        var echo = Serializer.Serialize(typeof(object), obj);
        if (echo != null)
            File.WriteAllText(absolutePath, echo.WriteToString());

        // Create meta with correct importer version
        string ext = Path.GetExtension(relativePath);
        string importerName = EditorRegistries.GetImporterTypeName(ext);
        var importer = EditorRegistries.CreateImporterByName(importerName);
        var meta = MetaFile.CreateNew(importerName, importer?.Version ?? 1);
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
        InvalidateFolderIndex();

        RunImport(entry);
        MetadataCache.Save(_project.MetadataDbPath, _guidToEntry.Values);

        AssetDatabase.Register(obj, meta.Guid, relativePath);
        obj.Name = Path.GetFileNameWithoutExtension(relativePath);
    }

    /// <summary>
    /// Import a file that already exists on disk under Assets/ (e.g. a baked lightmap PNG written by
    /// the lightmapper), creating/refreshing its metadata + cache, and return its asset GUID. If the
    /// path is already tracked it's reimported in place (so a re-bake replaces the previous asset).
    /// </summary>
    public Guid ImportFile(string relativePath)
    {
        relativePath = NormalizePath(relativePath);
        if (!TryResolveAssetPath(relativePath, out string absolutePath)) return Guid.Empty;
        if (!File.Exists(absolutePath)) return Guid.Empty;

        string ext = Path.GetExtension(relativePath);
        string importerName = EditorRegistries.GetImporterTypeName(ext);
        var importer = EditorRegistries.CreateImporterByName(importerName);
        var meta = MetaFile.EnsureMeta(absolutePath, importerName, importer?.Version ?? 1, importer?.DefaultSettings());

        // If we still track this path under a guid that no longer matches the on-disk .meta, the file
        // was replaced out-of-band (e.g. the lightmapper deletes + rewrites its whole folder via the
        // filesystem, bypassing the database, so EnsureMeta minted a fresh guid). The .meta is the
        // authoritative source after a restart, so drop the stale mapping and re-register under
        // meta.Guid. Otherwise the reimport-in-place branch below returns the old guid, which the
        // caller persists (e.g. into the scene's lightmap refs) yet nothing resolves to it on reload.
        if (_pathToGuid.TryGetValue(relativePath, out var staleGuid) && staleGuid != meta.Guid)
        {
            AssetDatabase.MarkMissing(staleGuid);
            _guidToEntry.TryRemove(staleGuid, out _);
            _pathToGuid.TryRemove(relativePath, out _);
        }

        // Already tracked at this path -> reimport in place (re-bake replacement).
        if (_pathToGuid.TryGetValue(relativePath, out var existingGuid) && _guidToEntry.TryGetValue(existingGuid, out var existing))
        {
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
        InvalidateFolderIndex();
        RunImport(entry);
        MetadataCache.Save(_project.MetadataDbPath, _guidToEntry.Values);
        return meta.Guid;
    }

    /// <summary>
    /// Re-serialize an in-memory asset back to its source file, or its parent's for a sub-asset. The asset is refilled
    /// from what was written, so everything holding it sees the saved content. False when it could not be saved.
    /// </summary>
    public bool SaveAsset(Asset obj)
    {
        if (!obj.IsFromDatabase) return false;

        Asset owner = TryGetParentGuid(obj.AssetID, out Guid parentGuid) && AssetDatabase.Get(parentGuid) is { } parent ? parent : obj;
        return SerializeForSave(owner) is { } serialized && SaveAsset(owner.AssetID, serialized);
    }

    /// <summary>Whether edits to this asset can be saved: its source file, or its parent's, is the asset written by Echo.</summary>
    public bool CanSave(Guid assetId) => SourceOf(OwningEntry(assetId)) != EchoSource.None;

    private static EchoSource SourceOf(AssetEntry? entry) => entry != null && ImporterOf(entry) is { } importer ? importer.Source : EchoSource.None;

    // One per importer type, for reading what it declares. Importing makes its own.
    private static readonly ConcurrentDictionary<string, AssetImporter?> s_importers = new();

    internal static void ClearImporterCache() => s_importers.Clear();

    private static AssetImporter? ImporterOf(AssetEntry entry)
        => string.IsNullOrEmpty(entry.ImporterType) ? null : s_importers.GetOrAdd(entry.ImporterType, EditorRegistries.CreateImporterByName);

    /// <summary>
    /// An asset written the way its source file holds it: the asset itself and the sub-assets it carries in full,
    /// everything else it references by GUID. Null when a sub-asset could not be loaded, since writing it would
    /// replace its data with an empty one.
    /// </summary>
    public EchoObject? SerializeForSave(Asset asset)
    {
        asset.Load();
        if (!asset.IsLoaded)
        {
            Runtime.Debug.LogError($"Not saving '{asset.AssetPath}', it could not be loaded and would be written empty.");
            return null;
        }

        var inline = new HashSet<Asset>(ReferenceEqualityComparer.Instance);
        foreach (SubAssetEntry sub in GetSubAssets(asset.AssetID))
            if (AssetDatabase.TryGetExisting(sub.Guid, out Asset existing))
            {
                existing.Load();
                if (!existing.IsLoaded)
                {
                    Runtime.Debug.LogError($"Not saving '{asset.AssetPath}', its sub-asset '{existing.Name}' could not be loaded and would be written empty.");
                    return null;
                }
                inline.Add(existing);
            }

        return Serializer.Serialize(typeof(object), asset, new DependencySerializationContext { Inline = inline });
    }

    /// <summary>Puts an asset and its sub-assets back to what was last imported, discarding edits made in memory.</summary>
    public void RevertToSaved(Asset asset)
    {
        AssetDatabase.Refill(asset, ReloadReason.Revert);
        foreach (SubAssetEntry sub in GetSubAssets(asset.AssetID))
            if (AssetDatabase.TryGetExisting(sub.Guid, out Asset subAsset))
                AssetDatabase.Refill(subAsset, ReloadReason.Revert);
    }

    /// <summary>
    /// Writes an asset's serialized form over its source file and reimports it, refilling the loaded asset in
    /// place. False, and logged, when the write fails.
    /// </summary>
    public bool SaveAsset(Guid guid, EchoObject serialized)
    {
        if (!_guidToEntry.TryGetValue(guid, out var entry)) return false;

        EchoSource source = SourceOf(entry);
        if (source == EchoSource.None)
        {
            Runtime.Debug.LogError($"Not saving '{entry.Path}', its file is not the asset written by Echo and would be overwritten.");
            return false;
        }

        try
        {
            string path = Path.Combine(_project.AssetsPath, entry.Path);
            if (source == EchoSource.Binary) serialized.WriteToBinary(new FileInfo(path));
            else File.WriteAllText(path, serialized.WriteToString());
        }
        catch (Exception ex)
        {
            Runtime.Debug.LogError($"Failed to save '{entry.Path}': {ex.Message}");
            return false;
        }

        Reimport(guid, ReloadReason.Save);
        return true;
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
            // Missing rather than gone, so everything referencing them keeps the GUID and comes back
            // in place if the file is restored.
            var entry = _guidToEntry.TryGetValue(guid, out var e) ? e : null;
            AssetDatabase.MarkMissing(guid);
            ThumbnailGenerator.DeleteThumbnail(guid, _project.ThumbnailsPath);
            InvalidateThumbnailTexture(guid);
            if (entry != null)
                RemoveSubAssets(entry, includeThumbnails: true);

            _guidToEntry.TryRemove(guid, out _);
            _pathToGuid.TryRemove(relativePath, out _);
            _dependencies.RemoveAsset(guid);

            // Clean main cache file
            string cachePath = GetCachePath(guid);
            if (File.Exists(cachePath))
                try { File.Delete(cachePath); } catch { }
        }

        // Delete files. The index/dispose state above has already been cleared, so an
        // unhandled exception here (e.g. the file is locked by an external app) would leave
        // the database thinking the asset is gone while it still exists on disk. Catch and
        // warn instead of throwing, matching the best-effort cache/thumbnail cleanup above.
        try
        {
            if (File.Exists(absolutePath))
                File.Delete(absolutePath);
            string metaPath = MetaFile.GetMetaPath(absolutePath);
            if (File.Exists(metaPath))
                File.Delete(metaPath);
        }
        catch (Exception ex)
        {
            Runtime.Debug.LogWarning($"Failed to delete asset file '{relativePath}': {ex.Message}");
        }

        MetadataCache.Save(_project.MetadataDbPath, _guidToEntry.Values);
        OnAssetsDeleted?.Invoke(new[] { relativePath });
        InvalidateFolderIndex();

        if (AffectsCompilation(relativePath))
            ScriptAssemblyManager.RequestRecompile();
    }

    /// <summary>
    /// True for files whose path feeds script compilation: sources, assembly definitions (they own
    /// scripts by folder) and managed plugins. Moving one changes the generated csproj and which
    /// assembly a script lands in, so it has to recompile even though no file content changed.
    /// </summary>
    private static bool AffectsCompilation(string path)
    {
        string ext = Path.GetExtension(path);
        return ext.Equals(".cs", StringComparison.OrdinalIgnoreCase)
            || ext.Equals(AssemblyDefinitionDatabase.Extension, StringComparison.OrdinalIgnoreCase)
            || ext.Equals(".dll", StringComparison.OrdinalIgnoreCase);
    }

    private static bool ContainsCompilationInput(string directory)
    {
        try { return Directory.EnumerateFiles(directory, "*", SearchOption.AllDirectories).Any(AffectsCompilation); }
        catch { return false; }
    }

    /// <summary>
    /// Move/rename an asset. The GUID stays the same.
    /// </summary>
    public bool MoveAsset(string oldRelativePath, string newRelativePath)
    {
        oldRelativePath = NormalizePath(oldRelativePath);
        newRelativePath = NormalizePath(newRelativePath);
        if (!TryResolveAssetPath(oldRelativePath, out string oldAbsolute)) return false;
        if (!TryResolveAssetPath(newRelativePath, out string newAbsolute)) return false;

        if (!File.Exists(oldAbsolute)) return false;

        // On a case-insensitive filesystem, a case-only rename's target path File.Exists-matches the
        // source file itself - that's not a collision with a different file.
        bool isCaseOnlyRename = string.Equals(oldRelativePath, newRelativePath, StringComparison.OrdinalIgnoreCase);

        if (!isCaseOnlyRename && File.Exists(newAbsolute))
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
            _pathToGuid.TryRemove(oldRelativePath, out _);
            _pathToGuid[newRelativePath] = guid;
            _guidToEntry[guid].Path = newRelativePath;
            UpdateAssetPaths(_guidToEntry[guid]);
        }

        MetadataCache.Save(_project.MetadataDbPath, _guidToEntry.Values);
        OnAssetMoved?.Invoke(oldRelativePath, newRelativePath);
        InvalidateFolderIndex();

        if (AffectsCompilation(oldRelativePath) || AffectsCompilation(newRelativePath))
            ScriptAssemblyManager.RequestRecompile();
        return true;
    }

    /// <summary>
    /// Move a folder and everything inside it. GUIDs are preserved the metadata index is
    /// remapped in-place, and <see cref="OnAssetMoved"/> fires for every relocated file.
    /// </summary>
    public bool MoveFolder(string oldRelativeFolder, string newRelativeFolder)
    {
        oldRelativeFolder = NormalizePath(oldRelativeFolder);
        newRelativeFolder = NormalizePath(newRelativeFolder);
        if (oldRelativeFolder == newRelativeFolder) return true;

        if (!TryResolveAssetPath(oldRelativeFolder, out string oldAbs)) return false;
        if (!TryResolveAssetPath(newRelativeFolder, out string newAbs)) return false;

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

        bool recompile = false;
        foreach (var (oldPath, newPath, guid) in toRemap)
        {
            recompile |= AffectsCompilation(oldPath);
            _pathToGuid.TryRemove(oldPath, out _);
            _pathToGuid[newPath] = guid;
            if (_guidToEntry.TryGetValue(guid, out var entry))
            {
                entry.Path = newPath;
                UpdateAssetPaths(entry);
            }

            OnAssetMoved?.Invoke(oldPath, newPath);
        }

        MetadataCache.Save(_project.MetadataDbPath, _guidToEntry.Values);
        InvalidateFolderIndex();

        if (recompile)
            ScriptAssemblyManager.RequestRecompile();
        return true;
    }

    /// <summary> Reimport an asset by GUID: clear thumbnails, run the importer, which refills every loaded object from it, and regenerate thumbnails. </summary>
    public void Reimport(Guid guid) => Reimport(guid, ReloadReason.Reimport);

    private void Reimport(Guid guid, ReloadReason reason)
    {
        if (_guidToEntry.TryGetValue(guid, out var entry))
        {
            // Clear old thumbnails and invalidate the cached GPU texture
            ThumbnailGenerator.DeleteThumbnail(guid, _project.ThumbnailsPath);
            InvalidateThumbnailTexture(guid);
            if (entry.SubAssets != null)
                foreach (var sub in entry.SubAssets)
                {
                    ThumbnailGenerator.DeleteThumbnail(sub.Guid, _project.ThumbnailsPath);
                    InvalidateThumbnailTexture(sub.Guid);
                }

            entry.NeedsReimport = true;
            _refillReason = reason;
            try { RunImport(entry); }
            finally { _refillReason = ReloadReason.Reimport; }
            MetadataCache.Save(_project.MetadataDbPath, _guidToEntry.Values);
            IndexVersion++;
            OnAssetsImported?.Invoke(new[] { entry.Path });

            // Sub-asset thumbnails regenerate from their objects. One not loaded now is queued when it next loads.
            foreach (var sub in entry.SubAssets)
                if (AssetDatabase.TryGetExisting(sub.Guid, out Asset subAsset) && subAsset.IsLoaded)
                    ThumbnailGenerator.Enqueue(sub.Guid, subAsset, null);
        }
    }

    // What a refill during the running import is reported as: a save is a reimport the user asked for by saving.
    private ReloadReason _refillReason = ReloadReason.Reimport;

    // ================================================================
    //  File Watching (per-frame update)
    // ================================================================

    /// <summary>Import a newly created or modified file, tracking it if it isn't already.</summary>
    /// <summary>
    /// Give a changed file its .meta and its entry, and queue it for import. Registration only:
    /// an importer that resolves a sibling by path (a model looking up its textures) must find every
    /// file in the batch already tracked, whatever order the watcher delivered the events in.
    /// </summary>
    private void RegisterFileChange(string absolutePath, string relativePath, List<AssetEntry> toImport)
    {
        string ext = Path.GetExtension(absolutePath);
        string importerName = EditorRegistries.GetImporterTypeName(ext);
        var meta = MetaFile.EnsureMeta(absolutePath, importerName);

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

        var existingEntry = _guidToEntry[meta.Guid];
        if (!toImport.Contains(existingEntry))
            toImport.Add(existingEntry);
    }

    /// <summary>Import a file that was registered earlier in this batch. The import refills whatever is loaded from it.</summary>
    private void ImportRegisteredChange(AssetEntry entry, List<string> imported)
    {
        RunImport(entry);
        imported.Add(entry.Path);
    }

    /// <param name="force">Drain the watcher immediately instead of waiting out its debounce window.</param>
    public void ProcessFileChanges(bool force = false)
    {
        if (_watcher == null) return;

        var events = _watcher.DrainEvents(force);
        if (events.Count == 0) return;

        var imported = new List<string>();
        var deleted = new List<string>();
        var toImport = new List<AssetEntry>();

        // Two passes, matching what startup already does (ScanAssets before ImportDirty). Registering
        // the whole batch first means a file's importer sees every other file in the batch as a
        // tracked asset it can reference, instead of depending on which watcher event arrived first.
        foreach (var evt in events)
        {
            // One bad file (unreadable .meta, broken importer) must not drop the rest of this batch of
            // events on the floor - those changes would never be seen again until a full rescan.
            try { ProcessFileEvent(evt, toImport, deleted); }
            catch (Exception ex) { Runtime.Debug.LogError($"Failed to process change to '{evt.Path}': {ex.Message}"); }
        }

        foreach (var entry in toImport)
        {
            try { ImportRegisteredChange(entry, imported); }
            catch (Exception ex) { Runtime.Debug.LogError($"Failed to import '{entry.Path}': {ex.Message}"); }
        }

        if (imported.Count > 0 || deleted.Count > 0)
        {
            MetadataCache.Save(_project.MetadataDbPath, _guidToEntry.Values);
            IndexVersion++;
            if (imported.Count > 0) OnAssetsImported?.Invoke(imported.ToArray());
            if (deleted.Count > 0) OnAssetsDeleted?.Invoke(deleted.ToArray());
        }
    }

    private void ProcessFileEvent(FileEvent evt, List<AssetEntry> toImport, List<string> deleted)
    {
        // Any change to a real (non-.meta) file or folder can add/remove/rename an entry or
        // change a file's size/date, so the cached folder index the Project Panel reads is stale.
        // .meta files are ours and don't affect the displayed structure.
        if (!evt.Path.EndsWith(".meta", StringComparison.OrdinalIgnoreCase))
            InvalidateFolderIndex();

        // Skip directory events - ScanAssets handles directory .meta creation
        if (Directory.Exists(evt.Path))
        {
            // A renamed folder relocates everything under it without any per-file event.
            if (evt.Type == FileEventType.Renamed && ContainsCompilationInput(evt.Path))
                ScriptAssemblyManager.RequestRecompile();
            return;
        }

        string relativePath = ToRelativePath(evt.Path);

        // Skip .meta files we manage them
        if (relativePath.EndsWith(".meta", StringComparison.OrdinalIgnoreCase)) return;

        switch (evt.Type)
        {
            // Already imported as it now is, as after the editor's own save.
            case FileEventType.Modified when _pathToGuid.TryGetValue(relativePath, out Guid known)
                                          && _guidToEntry.TryGetValue(known, out AssetEntry? current)
                                          && !current.NeedsReimport && !IsSourceNewerThanImport(current):
                break;

            case FileEventType.Created:
            case FileEventType.Modified:
                RegisterFileChange(evt.Path, relativePath, toImport);
                break;

            case FileEventType.Deleted:
                {
                    if (_pathToGuid.TryGetValue(relativePath, out var guid))
                    {
                        var deletedEntry = _guidToEntry.GetValueOrDefault(guid);

                        AssetDatabase.MarkMissing(guid);
                        if (deletedEntry != null)
                            RemoveSubAssets(deletedEntry, includeThumbnails: false);

                        _guidToEntry.TryRemove(guid, out _);
                        _pathToGuid.TryRemove(relativePath, out _);
                        _dependencies.RemoveAsset(guid);

                        // Clean main cache file
                        string cachePath = GetCachePath(guid);
                        if (File.Exists(cachePath))
                            try { File.Delete(cachePath); } catch { }

                        deleted.Add(relativePath);

                        if (AffectsCompilation(relativePath))
                            ScriptAssemblyManager.RequestRecompile();
                    }
                    break;
                }

            case FileEventType.Renamed:
                {
                    if (evt.OldPath != null)
                    {
                        string oldRelative = ToRelativePath(evt.OldPath);

                        // A move keeps the file's timestamp and content, so nothing else here asks for a
                        // recompile, yet the csproj still lists the old path and asmdef ownership may
                        // have changed.
                        if (AffectsCompilation(oldRelative) || AffectsCompilation(relativePath))
                            ScriptAssemblyManager.RequestRecompile();

                        if (!_pathToGuid.TryGetValue(oldRelative, out var guid))
                        {
                            // The old path was never tracked e.g. the "write-to-temp-then-rename-
                            // into-place" atomic-save pattern collapses Created+Renamed within the
                            // debounce window before the temp file is ever imported. Treat the
                            // destination as a brand-new file instead of silently dropping it until
                            // the next full rescan.
                            RegisterFileChange(evt.Path, relativePath, toImport);
                        }
                        else
                        {
                            _pathToGuid.TryRemove(oldRelative, out _);
                            _pathToGuid[relativePath] = guid;
                            var renamedEntry = _guidToEntry[guid];
                            renamedEntry.Path = relativePath;

                            // Move .meta
                            string oldMeta = MetaFile.GetMetaPath(evt.OldPath);
                            string newMeta = MetaFile.GetMetaPath(evt.Path);
                            if (File.Exists(oldMeta) && !File.Exists(newMeta))
                                try { File.Move(oldMeta, newMeta); } catch { }

                            UpdateAssetPaths(renamedEntry);

                            // If extension changed, update importer and trigger reimport
                            string oldExt = Path.GetExtension(evt.OldPath);
                            string newExt = Path.GetExtension(evt.Path);
                            if (!string.Equals(oldExt, newExt, StringComparison.OrdinalIgnoreCase))
                            {
                                string newImporterName = EditorRegistries.GetImporterTypeName(newExt);
                                renamedEntry.ImporterType = newImporterName;
                                renamedEntry.NeedsReimport = true;

                                if (!toImport.Contains(renamedEntry))
                                    toImport.Add(renamedEntry);
                            }

                            OnAssetMoved?.Invoke(oldRelative, relativePath);
                        }
                    }
                    break;
                }
        }
    }

    /// <summary>
    /// Reconcile the whole database against what is actually on disk: pending watcher events first, then a
    /// full scan that picks up anything added, removed, or modified since the last import.
    /// <para>
    /// The watcher is best-effort - it debounces, it can drop events on buffer overflow, and it is gated
    /// behind window focus by default - so anything that must not act on stale state calls this first.
    /// Main-thread only, since it imports.
    /// </para>
    /// </summary>
    public void Refresh()
    {
        if (Thread.CurrentThread.ManagedThreadId != _mainThreadId)
        {
            Runtime.Debug.LogWarning("AssetDatabase.Refresh must run on the main thread; ignoring.");
            return;
        }

        ProcessFileChanges(force: true);
        ScanAssets();
        ImportDirty();
        MetadataCache.Save(_project.MetadataDbPath, _guidToEntry.Values);
        RefreshResourcesMap();
        InvalidateFolderIndex();
    }

    // ================================================================
    //  Helpers
    // ================================================================

    private string GetCachePath(Guid guid)
        => Path.Combine(_project.CachePath, $"{guid}.asset");

    /// <summary>
    /// The asset exactly as it was imported, straight from the cache, or null when it has never been
    /// cached or cannot be read. This is what the loaded instance was built from.
	///
	/// This was added to allow the Inspectors to track modified assets to show Apply/Revert.
	/// Comparing a live object against it reveals any change made to that object since last import, including ones made outside the
    /// inspector, which nothing else can detect. Like a user mutating a asset directly rather then copying or instantiating it.
    /// </summary>
    public EchoObject? ReadCachedEcho(Guid guid)
    {
        string cachePath = GetCachePath(guid);
        if (!File.Exists(cachePath)) return null;

        try
        {
            return EchoObject.ReadFromBinary(new FileInfo(cachePath));
        }
        catch (Exception ex)
        {
            Runtime.Debug.LogWarning($"Could not read the cached form of {guid}: {ex.Message}");
            return null;
        }
    }

    /// <summary>True when the source file has been written since the entry was last imported, i.e. the
    /// cached import no longer represents what's on disk.</summary>
    private bool IsSourceNewerThanImport(AssetEntry entry)
    {
        string absolutePath = Path.Combine(_project.AssetsPath, entry.Path);
        if (!File.Exists(absolutePath)) return false;
        return File.GetLastWriteTimeUtc(absolutePath).Ticks != entry.LastModifiedTicks;
    }

    /// <summary>
    /// Reimport <paramref name="guid"/> if its cache file is missing or its source file has changed since
    /// the last import, and report whether it did. Sub-asset GUIDs resolve to their parent, since only the
    /// parent can be imported.
    /// <para>
    /// The file watcher normally keeps caches current, but it debounces and can miss changes outright
    /// (buffer overflow, a save immediately before the work that reads the cache), and until now nothing
    /// but a full editor restart reconciled that. Anything that reads caches straight off disk rather than
    /// through the asset database - a build, above all - has to check first or it ships whatever the
    /// asset used to be.
    /// </para>
    /// </summary>
    public bool EnsureCacheUpToDate(Guid guid)
    {
        Guid parentGuid = _subAssetIndex.TryGetValue(guid, out var subInfo) ? subInfo.parentGuid : guid;

        var entry = GetEntry(parentGuid);
        if (entry == null) return false;

        bool cacheMissing = !File.Exists(GetCachePath(guid)) || !File.Exists(GetCachePath(parentGuid));
        if (!cacheMissing && !IsSourceNewerThanImport(entry)) return false;

        Reimport(parentGuid);
        return true;
    }

    private void RemoveSubAsset(Guid subGuid, bool includeThumbnails)
    {
        AssetDatabase.MarkMissing(subGuid);
        _subAssetIndex.TryRemove(subGuid, out _);
        _dependencies.RemoveAsset(subGuid);
        if (includeThumbnails)
        {
            ThumbnailGenerator.DeleteThumbnail(subGuid, _project.ThumbnailsPath);
            InvalidateThumbnailTexture(subGuid);
        }
        string subCachePath = GetCachePath(subGuid);
        if (File.Exists(subCachePath))
            try { File.Delete(subCachePath); } catch { }
    }

    private void RemoveSubAssets(AssetEntry entry, bool includeThumbnails)
    {
        if (entry.SubAssets == null) return;
        foreach (var sub in entry.SubAssets)
            RemoveSubAsset(sub.Guid, includeThumbnails);
    }

    // A rename changes the path and the name of the asset and its sub-assets, loaded or not.
    private static void UpdateAssetPaths(AssetEntry entry)
    {
        if (AssetDatabase.TryGetExisting(entry.Guid, out Asset main))
        {
            main.SetPath(entry.Path);
            main.Name = Path.GetFileNameWithoutExtension(entry.Path);
        }

        foreach (var sub in entry.SubAssets)
            if (AssetDatabase.TryGetExisting(sub.Guid, out Asset subAsset))
                subAsset.SetPath($"{entry.Path}#{sub.Name}");
    }

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

    /// <summary>Normalize a path to use forward slashes, relative to Assets/, with no trailing slash.</summary>
    public static string NormalizePath(string path)
        => path.Replace('\\', '/').TrimEnd('/');

    /// <summary>Resolve a relative path to its absolute form, rejecting any that escape the Assets
    /// folder via ".." segments or a rooted path.</summary>
    private bool TryResolveAssetPath(string relativePath, out string absolutePath)
    {
        absolutePath = Path.GetFullPath(Path.Combine(_project.AssetsPath, relativePath));
        string assetsRoot = Path.GetFullPath(_project.AssetsPath) + Path.DirectorySeparatorChar;
        if (!absolutePath.StartsWith(assetsRoot, StringComparison.OrdinalIgnoreCase))
        {
            Runtime.Debug.LogWarning($"Rejected path '{relativePath}': it resolves outside the Assets folder.");
            return false;
        }
        return true;
    }

    /// <summary>Get a relative path from an absolute path, normalized.</summary>
    public string ToRelativePath(string absolutePath)
        => NormalizePath(Path.GetRelativePath(_project.AssetsPath, absolutePath));

    /// <summary> Dispose the asset watcher, clear thumbnail textures, and unregister this instance from the global AssetDatabase.Current. </summary>
    public void Dispose()
    {
        _watcher?.Dispose();
        _watcher = null;

        _buildPropsWatcher?.Dispose();
        _buildPropsWatcher = null;

        ClearThumbnailTextureCache();
        AssetDatabase.Loaded -= EnqueueThumbnailIfMissing;
        AssetDatabase.Reloaded -= PrefabUtility.OnAssetReloaded;

        // Clear the global registrations if they still point at this instance, so a torn-down
        // database (e.g. between tests) doesn't leave dangling statics behind.
        if (Instance == this) Instance = null;
        if (AssetDatabase.Backend == this) AssetDatabase.Backend = null;
    }
}
