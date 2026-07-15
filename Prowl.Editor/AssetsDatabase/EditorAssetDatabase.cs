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
    // while the main thread imports/scans. (_pathToGuid is main-thread-only the loader never touches it.)
    private readonly ConcurrentDictionary<Guid, AssetEntry> _guidToEntry = new();
    private readonly Dictionary<string, Guid> _pathToGuid = new(StringComparer.OrdinalIgnoreCase);
    // Strongly held: an asset stays loaded until something explicitly disposes it - either direct
    // user code, or the idle-timeout sweep (see MaybeSweepIdle) collecting a GUID nothing has
    // touched (via AssetRef.Res/.Touch, AssetDatabase.Touch, or EngineObject.EnsureNotDisposed) in
    // a while. Always go through TryGetLoaded/SetLoaded rather than touching this directly, so every
    // access point consistently records activity.
    private readonly ConcurrentDictionary<Guid, EngineObject> _loadedAssets = new();
    private readonly ConcurrentDictionary<Guid, (Guid parentGuid, int index)> _subAssetIndex = new();
    // How long a GUID can go untouched before the idle sweep disposes it, and how often the sweep
    // runs (see TickIdleSweep, called once per frame from EditorApplication). A locked GUID is
    // never swept regardless of idle time.
    public static readonly TimeSpan IdleTimeout = TimeSpan.FromSeconds(60);
    public static readonly TimeSpan IdleSweepInterval = TimeSpan.FromSeconds(5);
    private long _lastIdleSweepTicks;
    // GPU-uploaded thumbnail cache. Main-thread-only (texture creation isn't thread-safe), same
    // as _pathToGuid every UI that shows asset thumbnails shares this instead of keeping its own.
    private readonly Dictionary<Guid, Runtime.Resources.Texture2D?> _thumbnailTextures = new();
    // Per-thread re-entrancy guard that breaks dependency/sub-asset cycles during deserialize.
    [ThreadStatic] private static HashSet<Guid>? _loadingStack;
    // Serializes the deserialize body so concurrent loads of the same asset collapse onto one.
    private readonly object _loadLock = new();
    // Importing (and the file writes / GPU work it implies) stays on the main thread; the
    // background loader only deserializes already-imported on-disk cache files.
    private int _mainThreadId = -1;
    private readonly DependencyGraph _dependencies = new();
    private AssetWatcher? _watcher;

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

        // A sub-asset never loads except as a side effect of loading its parent (see GetInternal), so
        // resolve it to its parent's GUID for touch/idle/lock purposes too - the whole family is one
        // atomic unit.
        AssetDatabase.ResolveFamily = guid =>
            _subAssetIndex.TryGetValue(guid, out var info) ? info.parentGuid : guid;

        // Importers (and every other EditorRegistries scanner) must be ready before anything below
        // tries to import a file - idempotent, so this is cheap on every call after the first.
        EditorRegistries.Initialize();

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
                if (entry.Dependencies is { Length: > 0 })
                    _dependencies.SetDependencies(guid, entry.Dependencies);

                // Sub-assets have their own dependency-graph entry too (see RunImport) - needs the
                // same seeding or a later DependenciesOnly build silently drops what they reference.
                if (entry.SubAssets is { Length: > 0 })
                {
                    foreach (var sub in entry.SubAssets)
                        if (sub.Dependencies is { Length: > 0 })
                            _dependencies.SetDependencies(sub.Guid, sub.Dependencies);
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

        // Register as the active database
        Instance = this;
        Runtime.AssetDatabase.Current = this;

        Runtime.Debug.Log($"Asset database initialized: {_guidToEntry.Count} assets tracked.");

        // Initialize GameResources mapping for editor play mode
        RefreshResourcesMap();
    }

    /// <summary>
    /// Delete leftover ".meta.tmp" files from a previous session's write that never got
    /// renamed into place (e.g. the editor crashed mid-write). These are always incomplete
    /// data never valid on their own so ScanAssets must not pick them up as real asset files.
    /// </summary>
    private void CleanupOrphanedMetaTempFiles()
    {
        var assetsPath = _project.AssetsPath;
        if (!Directory.Exists(assetsPath)) return;

        foreach (var tmp in Directory.EnumerateFiles(assetsPath, "*.meta.tmp", SearchOption.AllDirectories))
        {
            try { File.Delete(tmp); }
            catch (Exception ex) { Runtime.Debug.LogWarning($"Failed to delete orphaned meta temp file '{tmp}': {ex.Message}"); }
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

    /// <summary>Resolve a loaded asset by GUID if it's present and not disposed. The only valid way
    /// to read <see cref="_loadedAssets"/> - go through this rather than the dictionary directly so
    /// every successful resolve consistently records activity.</summary>
    private bool TryGetLoaded(Guid guid, out EngineObject obj)
    {
        if (_loadedAssets.TryGetValue(guid, out var found) && found != null && !found.IsDisposed)
        {
            obj = found;
            AssetDatabase.Touch(guid);
            return true;
        }
        obj = null!;
        return false;
    }

    /// <summary>Cache a freshly resolved asset instance by GUID. Strongly held until explicitly
    /// disposed or idle-swept - see <see cref="_loadedAssets"/>.</summary>
    private void SetLoaded(Guid guid, EngineObject obj)
    {
        _loadedAssets[guid] = obj;
        AssetDatabase.Touch(guid);
    }

    /// <summary>UTC time the idle sweep last actually ran (not merely checked its gate). Diagnostic-only.</summary>
    public DateTime LastSweepUtc => new(Interlocked.Read(ref _lastIdleSweepTicks), DateTimeKind.Utc);

    /// <summary>Dispose and drop every GUID that's gone untouched for <see cref="IdleTimeout"/> and
    /// isn't locked. Gated to run at most once per <see cref="IdleSweepInterval"/>.</summary>
    private void MaybeSweepIdle()
    {
        var now = DateTime.UtcNow;
        long last = Interlocked.Read(ref _lastIdleSweepTicks);
        if (now.Ticks - last < IdleSweepInterval.Ticks) return;
        if (Interlocked.CompareExchange(ref _lastIdleSweepTicks, now.Ticks, last) != last) return;

        foreach (var kv in _loadedAssets)
        {
            Guid guid = kv.Key;
            if (AssetDatabase.IsLocked(guid)) continue;
            if (!AssetDatabase.IsIdle(guid, IdleTimeout)) continue;

            if (_loadedAssets.TryRemove(guid, out var obj))
            {
                try { obj.Dispose(); } catch (Exception ex) { Runtime.Debug.LogWarning($"Error disposing idle asset {guid}: {ex.Message}"); }
                AssetDatabase.Forget(guid);
            }
        }
    }

    /// <summary>Run an idle sweep immediately, bypassing <see cref="IdleSweepInterval"/>'s gate - used
    /// by the Asset Database panel's manual "Sweep Now" action, and by tests via
    /// <see cref="AssetDatabase.ForceIdle"/>.</summary>
    public void ForceIdleSweep()
    {
        Interlocked.Exchange(ref _lastIdleSweepTicks, 0);
        MaybeSweepIdle();
    }

    /// <summary>
    /// Give the idle sweep a chance to run. Called once per frame from EditorApplication - cheap to
    /// call that often since MaybeSweepIdle's own gate means the actual scan only happens once per
    /// IdleSweepInterval regardless of how often this is called.
    /// </summary>
    public void TickIdleSweep() => MaybeSweepIdle();

    public EngineObject? Get(Guid assetId)
    {
        if (assetId == Guid.Empty) return null;

        // Check in-memory cache (covers both main assets and sub-assets). Lock-free.
        if (TryGetLoaded(assetId, out var loaded))
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
                if (TryGetLoaded(assetId, out loaded))
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
        return TryGetLoaded(assetId, out var c) ? c : null;
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
            if (TryGetLoaded(assetId, out var loadedSub))
                return loadedSub;

            // Parent was already alive (Get returned early without touching LoadSubAssetsIntoCache),
            // but this sub-asset's own weak entry died independently of its parent - a sub-asset and
            // its parent are no longer collected as a unit, so "parent alive" no longer implies "every
            // sub-asset is still alive". Reload subs explicitly; already-alive ones are skipped.
            var parentEntry = GetEntry(subInfo.parentGuid);
            if (parentEntry?.SubAssets != null)
                LoadSubAssetsIntoCache(parentEntry);

            return TryGetLoaded(assetId, out var reloadedSub) ? reloadedSub : null;
        }

        // Try loading main asset from disk cache
        string cachePath = GetCachePath(assetId);
        var entry = GetEntry(assetId);

        // Validate cache check importer version matches current importer
        if (onMainThread && entry != null && !string.IsNullOrEmpty(entry.ImporterType))
        {
            var importer = EditorRegistries.CreateImporterByName(entry.ImporterType);
            if (importer != null && importer.Version != entry.ImporterVersion)
            {
                // Cache is stale importer was updated since last import
                Runtime.Debug.Log($"Cache stale for '{entry.Path}': importer v{entry.ImporterVersion} -> v{importer.Version}. Reimporting.");
                entry.NeedsReimport = true;
                RunImport(entry);
                return TryGetLoaded(assetId, out var reimported) ? reimported : null;
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
                    SetLoaded(assetId, obj);
                    if (onMainThread) EnqueueThumbnailIfMissing(assetId, obj);

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
            return TryGetLoaded(assetId, out var justImported) ? justImported : null;
        }

        return null;
    }

    private void LoadSubAssetsIntoCache(AssetEntry parentEntry)
    {
        bool onMainThread = Thread.CurrentThread.ManagedThreadId == _mainThreadId;

        foreach (var sub in parentEntry.SubAssets)
        {
            // Must be a liveness check, not a bare key-presence check: a collected sub-asset's dict
            // entry can still exist (dead weak ref), and skipping it here would leave it permanently
            // unresolvable even though its cache file is right there ready to reload.
            if (TryGetLoaded(sub.Guid, out _)) continue;

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
                    SetLoaded(sub.Guid, obj);
                    if (onMainThread) EnqueueThumbnailIfMissing(sub.Guid, obj);
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

    /// <summary>Lazily backfill a thumbnail the first time an asset is actually loaded (import/reimport
    /// already enqueue directly - this only catches the gap where a cache-file deserialize resolves an
    /// asset whose thumbnail is missing, e.g. a fresh checkout where thumbnails aren't checked into
    /// source control, or a manually deleted thumbnail file). Main-thread only: ThumbnailGenerator's
    /// queue isn't thread-safe, and this runs from the same disk-deserialize path the background
    /// loader also uses.</summary>
    private void EnqueueThumbnailIfMissing(Guid guid, EngineObject obj)
    {
        if (File.Exists(ThumbnailGenerator.GetThumbnailPath(guid, _project.ThumbnailsPath)))
            return;

        string? sourceFile = obj is Runtime.Resources.Texture2D && !_subAssetIndex.ContainsKey(guid)
            ? Path.Combine(_project.AssetsPath, obj.AssetPath)
            : null;
        ThumbnailGenerator.Enqueue(guid, obj, sourceFile);
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
            // Ensure folder .meta exists (for stable folder GUIDs in version control)
            MetaFile.EnsureMeta(dir, "DefaultImporter");
        }

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
                    _dependencies.RemoveAsset(sub.Guid);

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

            // Reimport if the file changed, its cache is missing, OR the importer's version was
            // bumped (a new editor build with changed import logic must re-run stale caches).
            if (entry.LastModifiedTicks != currentTicks
                || !File.Exists(GetCachePath(existingGuid))
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
                _pathToGuid.Remove(relativePath);
                DisposeAndRemove(existingGuid);
                _dependencies.RemoveAsset(existingGuid);

                // Remove old sub-asset index entries
                if (entry.SubAssets != null)
                {
                    foreach (var sub in entry.SubAssets)
                    {
                        _subAssetIndex.TryRemove(sub.Guid, out _);
                        DisposeAndRemove(sub.Guid);
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

        foreach (var entry in dirty)
        {
            if (RunImport(entry))
                succeeded.Add(entry.Path);
        }

        Runtime.Debug.Log($"Import complete: {succeeded.Count}/{dirty.Count} succeeded.");

        if (succeeded.Count > 0)
            OnAssetsImported?.Invoke(succeeded.ToArray());
    }

    private bool RunImport(AssetEntry entry)
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

            SetLoaded(entry.Guid, ctx.MainAsset);
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

                    // A sub-asset (e.g. a Sprite) can hold AssetRef fields of its own - track those
                    // under its own GUID, or a DependenciesOnly build's walk stops at the sub-asset.
                    var subCtx = new DependencySerializationContext();
                    SerializeToCache(sub.AssetID, sub, subCtx);
                    _dependencies.SetDependencies(sub.AssetID, subCtx.Dependencies);

                    // Populate the sub-asset index BEFORE SetLoaded (which touches the activity
                    // tracker) - ResolveFamily looks this up to route the touch to the parent, so it
                    // must already resolve correctly on this very first touch.
                    _subAssetIndex[sub.AssetID] = (entry.Guid, i);
                    newSubGuids.Add(sub.AssetID);
                    SetLoaded(sub.AssetID, sub);

                    subEntries.Add(new SubAssetEntry
                    {
                        Guid = sub.AssetID,
                        Name = sub.Name,
                        Type = sub.GetType(),
                        // Persisted so the graph can be re-seeded on next startup (see Initialize()).
                        Dependencies = subCtx.Dependencies.ToArray()
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
                _subAssetIndex.TryRemove(oldGuid, out _);
                if (_loadedAssets.TryRemove(oldGuid, out var oldObj)) oldObj?.Dispose();
                AssetDatabase.Forget(oldGuid);
                _dependencies.RemoveAsset(oldGuid);
                ThumbnailGenerator.DeleteThumbnail(oldGuid, _project.ThumbnailsPath);
                InvalidateThumbnailTexture(oldGuid);
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

            // Queue thumbnail generation (lazy, one per frame) and drop any cached GPU texture for
            // the old content so the next access rebuilds it from the freshly generated thumbnail.
            {
                string? sourceFile = ctx.MainAsset is Runtime.Resources.Texture2D ? absolutePath : null;
                ThumbnailGenerator.Enqueue(entry.Guid, ctx.MainAsset, sourceFile);
                InvalidateThumbnailTexture(entry.Guid);
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

    private void SerializeToCache(Guid guid, EngineObject obj, SerializationContext? context = null)
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
            var echo = context != null
                ? Serializer.Serialize(typeof(object), obj, context)
                : Serializer.Serialize(typeof(object), obj);
            obj.AssetID = savedId;

            if (echo != null)
            {
                // Write to a temp file and rename into place (matching MetaFile.Write) so a
                // crash/power-loss mid-write can't leave a truncated cache file behind.
                string tempPath = cachePath + ".tmp";
                echo.WriteToBinary(new FileInfo(tempPath));
                File.Move(tempPath, cachePath, overwrite: true);
            }
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

    /// <summary>Force the folder/file index to rebuild on next query. Driven by the asset watcher.</summary>
    public void InvalidateFolderIndex() => _folderIndexDirty = true;

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

    public DependencyGraph Dependencies => _dependencies;
    public string ThumbnailsPath => _project.ThumbnailsPath;

    /// <summary>Get an already-loaded asset from memory without triggering import. Returns null if not loaded.</summary>
    public EngineObject? GetLoadedAsset(Guid guid) => TryGetLoaded(guid, out var obj) ? obj : null;

    /// <summary>Snapshot of every currently-resident (main + sub-asset) GUID and its loaded instance,
    /// for diagnostics (e.g. the Asset Database panel) - not a live view, and reading it does not
    /// itself count as activity (unlike Get/GetCached).</summary>
    public IEnumerable<(Guid Guid, EngineObject Asset)> GetLoadedAssets()
    {
        foreach (var kv in _loadedAssets)
            if (kv.Value != null && !kv.Value.IsDisposed)
                yield return (kv.Key, kv.Value);
    }

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
            var asset = kv.Value;
            if (asset is null) continue;

            bool sensitive = asset is Runtime.Resources.Scene
                          || asset is Runtime.Resources.PrefabAsset
                          || asset.GetType().Assembly.IsCollectible;

            if (!sensitive) continue;

            if (db._loadedAssets.TryRemove(kv.Key, out _))
            {
                AssetDatabase.Forget(kv.Key);
                try
                {
                    asset.Dispose();
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
            tex?.Dispose();
            _thumbnailTextures.Remove(guid);
        }
    }

    /// <summary>Dispose and clear every cached thumbnail texture (e.g. after the thumbnail size setting changes).</summary>
    public void ClearThumbnailTextureCache()
    {
        foreach (var tex in _thumbnailTextures.Values)
            tex?.Dispose();
        _thumbnailTextures.Clear();
    }

    // ================================================================
    //  Asset CRUD
    // ================================================================

    /// <summary>
    /// Create a new asset file on disk from an EngineObject.
    /// </summary>
    public void CreateAsset(EngineObject obj, string relativePath)
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
        _folderIndexDirty = true;

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
        _folderIndexDirty = true;
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
            InvalidateThumbnailTexture(guid);
            if (entry?.SubAssets != null)
                foreach (var sub in entry.SubAssets)
                {
                    DisposeAndRemove(sub.Guid);
                    _subAssetIndex.TryRemove(sub.Guid, out _);
                    _dependencies.RemoveAsset(sub.Guid);
                    ThumbnailGenerator.DeleteThumbnail(sub.Guid, _project.ThumbnailsPath);
                    InvalidateThumbnailTexture(sub.Guid);

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
        _folderIndexDirty = true;

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
            _pathToGuid.Remove(oldRelativePath);
            _pathToGuid[newRelativePath] = guid;
            _guidToEntry[guid].Path = newRelativePath;

            if (TryGetLoaded(guid, out var obj))
                obj.AssetPath = newRelativePath;

            // Update sub-asset AssetPaths (they use "parent/path#SubName" format)
            var movedEntry = _guidToEntry.GetValueOrDefault(guid);
            if (movedEntry?.SubAssets != null)
            {
                foreach (var sub in movedEntry.SubAssets)
                {
                    if (TryGetLoaded(sub.Guid, out var subObj))
                        subObj.AssetPath = $"{newRelativePath}#{sub.Name}";
                }
            }
        }

        MetadataCache.Save(_project.MetadataDbPath, _guidToEntry.Values);
        OnAssetMoved?.Invoke(oldRelativePath, newRelativePath);
        _folderIndexDirty = true;
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
                        if (TryGetLoaded(sub.Guid, out var subObj))
                            subObj.AssetPath = $"{newPath}#{sub.Name}";
                    }
                }
            }
            if (TryGetLoaded(guid, out var obj))
                obj.AssetPath = newPath;

            OnAssetMoved?.Invoke(oldPath, newPath);
        }

        MetadataCache.Save(_project.MetadataDbPath, _guidToEntry.Values);
        _folderIndexDirty = true;
        return true;
    }

    /// <summary>
    /// Force reimport a specific asset.
    /// </summary>
    /// <summary>Dispose and remove a cached asset. Triggers AssetRef re-resolve on next access.</summary>
    private void DisposeAndRemove(Guid guid)
    {
        if (TryGetLoaded(guid, out var old))
        {
            try { old.Dispose(); } catch { }
        }
        _loadedAssets.TryRemove(guid, out _);
        AssetDatabase.Forget(guid);
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

    /// <summary>Import a newly created or modified file, tracking it if it isn't already.</summary>
    private void ImportFileChange(string absolutePath, string relativePath, List<string> imported)
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
    }

    public void ProcessFileChanges()
    {
        if (_watcher == null) return;

        var events = _watcher.DrainEvents();
        if (events.Count == 0) return;

        var imported = new List<string>();
        var deleted = new List<string>();

        foreach (var evt in events)
        {
            // Any change to a real (non-.meta) file or folder can add/remove/rename an entry or
            // change a file's size/date, so the cached folder index the Project Panel reads is stale.
            // .meta files are ours and don't affect the displayed structure.
            if (!evt.Path.EndsWith(".meta", StringComparison.OrdinalIgnoreCase))
                _folderIndexDirty = true;

            // Skip directory events - ScanAssets handles directory .meta creation
            if (Directory.Exists(evt.Path)) continue;

            string relativePath = ToRelativePath(evt.Path);

            // Skip .meta files we manage them
            if (relativePath.EndsWith(".meta", StringComparison.OrdinalIgnoreCase)) continue;

            switch (evt.Type)
            {
                case FileEventType.Created:
                case FileEventType.Modified:
                    ImportFileChange(evt.Path, relativePath, imported);
                    break;

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
                                _dependencies.RemoveAsset(sub.Guid);

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
                        if (!_pathToGuid.TryGetValue(oldRelative, out var guid))
                        {
                            // The old path was never tracked e.g. the "write-to-temp-then-rename-
                            // into-place" atomic-save pattern collapses Created+Renamed within the
                            // debounce window before the temp file is ever imported. Treat the
                            // destination as a brand-new file instead of silently dropping it until
                            // the next full rescan.
                            ImportFileChange(evt.Path, relativePath, imported);
                        }
                        else
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

                            if (TryGetLoaded(guid, out var obj))
                                obj.AssetPath = relativePath;

                            // Update sub-asset AssetPaths
                            if (renamedEntry.SubAssets != null)
                            {
                                foreach (var sub in renamedEntry.SubAssets)
                                {
                                    if (TryGetLoaded(sub.Guid, out var subObj))
                                        subObj.AssetPath = $"{relativePath}#{sub.Name}";
                                }
                            }

                            // If extension changed, update importer and trigger reimport
                            string oldExt = Path.GetExtension(evt.OldPath);
                            string newExt = Path.GetExtension(evt.Path);
                            if (!string.Equals(oldExt, newExt, StringComparison.OrdinalIgnoreCase))
                            {
                                string newImporterName = EditorRegistries.GetImporterTypeName(newExt);
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

    public void Dispose()
    {
        _watcher?.Dispose();
        _watcher = null;

        ClearThumbnailTextureCache();

        // Clear the global registrations if they still point at this instance, so a torn-down
        // database (e.g. between tests) doesn't leave dangling statics behind.
        if (Instance == this) Instance = null;
        if (Runtime.AssetDatabase.Current == this) Runtime.AssetDatabase.Current = null;
        if (Instance == null) AssetDatabase.ResolveFamily = null;
    }
}
