// This file is part of the Prowl Game Engine
// Licensed under the MIT License. See the LICENSE file in the project root for details.

using System;
using System.Collections;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Linq.Expressions;
using System.Reflection;
using System.Runtime.CompilerServices;
using System.Threading;
using System.Threading.Tasks;

using Prowl.Echo;
using Prowl.Ember;
using Prowl.Runtime.Resources;

namespace Prowl.Runtime;

/// <summary>What the database knows about one asset's residency, for diagnostics.</summary>
public readonly record struct AssetResidency(
    AssetState State, long Bytes, bool Reached, string? ReachedBy,
    IReadOnlyList<object> HeldBy, TimeSpan SinceReached, int LoadCount);

/// <summary>
/// Every asset by GUID, one object each for the whole session. Assets stay loaded while something reaches them:
/// a live scene, an object kept with <see cref="DontDestroyOnLoad"/>, a hold, a root, or a load group. A walk from
/// those roots runs about once a second, and an asset nothing reached for <see cref="GracePeriod"/> has its payload
/// freed, sooner when over <see cref="MemoryBudget"/>. Reading an unloaded asset loads it again.
/// </summary>
public static class AssetDatabase
{
    private static readonly ConcurrentDictionary<Guid, Asset> s_assets = new();

    [ModuleInitializer]
    internal static void InstallReferenceRule() => Serializer.ReferenceRule = new AssetReferenceRule();

    /// <summary>The project's or the player's own content. Mounted sources are asked before it, so they can add to it or override it.</summary>
    public static AssetBackend? Backend { get; set; }

    private static AssetBackend[] s_mounts = [];
    private static readonly object s_mountLock = new();

    /// <summary>Extra sources of assets, newest first: content packs, mods, files on disk or on a web server.</summary>
    public static IReadOnlyList<AssetBackend> Mounts => s_mounts;

    /// <summary>
    /// Adds a source of assets for the rest of the session or until <see cref="Unmount"/>. A GUID or load path that
    /// several sources know comes from the one mounted last, so a patch or a content pack can replace what came before.
    /// </summary>
    public static void Mount(AssetBackend source)
    {
        lock (s_mountLock)
        {
            if (Array.IndexOf(s_mounts, source) >= 0) return;
            s_mounts = [source, .. s_mounts];
        }
        RefreshAfterMountChange();
    }

    /// <summary>
    /// Removes a mounted source. Its assets that no other source provides become missing, keeping their GUIDs so
    /// whatever refers to them finds them again if the source comes back.
    /// </summary>
    public static void Unmount(AssetBackend source)
    {
        lock (s_mountLock)
        {
            if (Array.IndexOf(s_mounts, source) < 0) return;
            s_mounts = s_mounts.Where(mount => mount != source).ToArray();
        }
        RefreshAfterMountChange();
    }

    // Whatever now resolves to a different source, or to none, reads its content again on next use.
    private static void RefreshAfterMountChange()
    {
        s_indexedResources = null;
        foreach (Asset asset in s_assets.Values)
        {
            if (BuiltInAssets.IsBuiltIn(asset.AssetID)) continue;
            if (GetAssetType(asset.AssetID) == null) MarkMissing(asset.AssetID);
            else if (asset.State is AssetState.Missing or AssetState.Failed) Refill(asset, ReloadReason.Reimport);
            else if (asset.IsLoaded) Refill(asset, ReloadReason.Reimport);
        }
    }

    /// <summary>The source an asset's content comes from: the newest mount that knows the GUID, then <see cref="Backend"/>.</summary>
    public static AssetBackend? SourceOf(Guid assetId)
    {
        foreach (AssetBackend mount in s_mounts)
            if (mount.GetAssetType(assetId) != null) return mount;
        return Backend;
    }

    private static bool HasAnySource => Backend != null || s_mounts.Length > 0;

    public static event Action<Asset>? Loaded;
    public static event Action<Asset, ReloadReason>? Reloaded;
    public static event Action<Asset>? Unloaded;

    #region Identity

    /// <summary>The asset with this GUID, loaded or not. Never loads. Null for a GUID nothing knows.</summary>
    public static Asset? Get(Guid assetId)
    {
        if (assetId == Guid.Empty) return null;
        if (s_assets.TryGetValue(assetId, out Asset? existing)) return existing;

        Type? type = GetAssetType(assetId);
        return type == null ? null : GetOrCreate(assetId, type, AssetState.Unloaded);
    }

    /// <summary>The asset with this GUID as <typeparamref name="T"/>. Never loads. A GUID nothing knows gives a missing <typeparamref name="T"/>.</summary>
    public static T? Get<T>(Guid assetId) where T : Asset => GetOrMissing(assetId, typeof(T)) as T;

    /// <summary>The asset with this GUID, loaded. Blocks.</summary>
    public static T? Load<T>(Guid assetId) where T : Asset
    {
        T? asset = Get<T>(assetId);
        asset?.Load();
        return asset;
    }

    public static async Task<T?> LoadAsync<T>(Guid assetId, CancellationToken cancel = default) where T : Asset
    {
        T? asset = Get<T>(assetId);
        if (asset != null) await asset.LoadAsync(cancel);
        return asset;
    }

    /// <summary>
    /// Resolves a GUID read from a stub. A GUID nothing knows becomes a missing asset of the declared type, so the
    /// reference survives being saved again. Null when the type cannot be built or the asset is of another type.
    /// </summary>
    internal static Asset? GetOrMissing(Guid assetId, Type declaredType)
    {
        if (assetId == Guid.Empty) return null;

        Asset? asset = s_assets.TryGetValue(assetId, out Asset? existing) ? existing : null;
        if (asset == null)
        {
            Type? type = GetAssetType(assetId);
            if (type != null) asset = GetOrCreate(assetId, type, AssetState.Unloaded);
            else if (IsConstructible(declaredType)) asset = GetOrCreate(assetId, declaredType, AssetState.Missing);
            else
            {
                Debug.LogWarningOnce($"AssetDatabase.Unknown.{assetId}", $"No asset has the GUID {assetId}, and a missing {declaredType.Name} can not be made to stand in for it.");
                return null;
            }
        }

        if (declaredType.IsInstanceOfType(asset)) return asset;

        Debug.LogWarningOnce($"AssetDatabase.WrongType.{assetId}.{declaredType.Name}",
            $"{asset.GetType().Name} '{asset.Name}' ({assetId}) was read into a {declaredType.Name} field, which it does not fit.");
        return null;
    }

    private static bool IsConstructible(Type type)
        => typeof(Asset).IsAssignableFrom(type) && !type.IsAbstract && type.GetConstructor(BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic, Type.EmptyTypes) != null;

    private static Asset GetOrCreate(Guid assetId, Type type, AssetState state)
    {
        if (s_assets.TryGetValue(assetId, out Asset? existing)) return existing;

        // Two threads can build one at once. Only the one that goes in stays registered.
        Asset created = CreateStable(assetId, type, state);
        if (s_assets.TryAdd(assetId, created)) return created;
        created.Registered = false;
        return s_assets[assetId];
    }

    private static Asset CreateStable(Guid assetId, Type type, AssetState state)
    {
        Asset asset = CreateShell(type);
        string path = GetAssetPath(assetId) ?? string.Empty;
        asset.SetIdentity(assetId, path);
        if (path.Length > 0) asset.Name = BuiltInAssets.Entries.TryGetValue(assetId, out var builtIn) ? builtIn.Name : AssetNames.FromPath(path);
        asset.SetState(state);
        asset.Registered = true;
        return asset;
    }

    /// <summary>An empty object of an asset type, made with its parameterless constructor, which must touch nothing.</summary>
    public static Asset CreateShell(Type type)
        => (Asset)(Activator.CreateInstance(type, nonPublic: true) ?? throw new InvalidOperationException($"Could not create a {type.Name}."));

    /// <summary>
    /// Makes <paramref name="asset"/>, already built in memory, the stable object for a GUID the backend has just
    /// learned about. Whatever held the GUID before becomes this object.
    /// </summary>
    public static void Register(Asset asset, Guid assetId, string path)
    {
        if (asset.Registered && asset.AssetID == assetId) return;
        if (asset.Registered) throw new InvalidOperationException($"'{asset.Name}' is already the asset {asset.AssetID}.");

        asset.SetIdentity(assetId, path);
        asset.Registered = true;
        asset.SetState(AssetState.Loaded);
        if (s_assets.TryGetValue(assetId, out Asset? previous) && previous != asset)
            Unregister(previous);
        s_assets[assetId] = asset;
    }

    /// <summary>Hands a GUID's registration to the object hot reload made in place of the old one.</summary>
    internal static void Replace(Asset old, Asset replacement)
    {
        if (!s_assets.TryGetValue(old.AssetID, out Asset? current) || !ReferenceEquals(current, old) && !ReferenceEquals(current, replacement)) return;
        replacement.SetIdentity(old.AssetID, old.AssetPath);
        replacement.Registered = true;
        s_assets[old.AssetID] = replacement;
        if (!ReferenceEquals(old, replacement)) old.Registered = false;
    }

    /// <summary>Whether a GUID has a stable object yet, without making one.</summary>
    public static bool TryGetExisting(Guid assetId, out Asset asset) => s_assets.TryGetValue(assetId, out asset!);

    /// <summary>Every stable object made so far this session.</summary>
    public static IEnumerable<Asset> All => s_assets.Values;

    public static Type? GetAssetType(Guid assetId)
        => BuiltInAssets.Entries.TryGetValue(assetId, out var builtIn) ? builtIn.AssetType : SourceOf(assetId)?.GetAssetType(assetId);

    private static string? GetAssetPath(Guid assetId)
        => BuiltInAssets.Entries.TryGetValue(assetId, out var builtIn) ? builtIn.Path : SourceOf(assetId)?.GetAssetPath(assetId);

    public static bool IsBuiltIn(Asset asset) => BuiltInAssets.IsBuiltIn(asset.AssetID);

    #endregion

    #region Loading and refilling

    internal static bool NeedsMainThread(Guid assetId)
        => !BuiltInAssets.IsBuiltIn(assetId) && SourceOf(assetId) is { } source && source.NeedsMainThread(assetId);

    internal static bool ReadContent(Guid assetId, Asset staging, bool mayImport = true)
    {
        if (BuiltInAssets.Entries.ContainsKey(assetId))
            return BuiltInAssets.ReadContent(assetId, staging);

        AssetBackend? backend = SourceOf(assetId);
        if (backend == null) return false;
        if (mayImport && backend.NeedsMainThread(assetId)) backend.PrepareOnMainThread(assetId);

        var context = new SerializationContext();
        return backend.ReadContent(assetId, staging, context);
    }

    internal static void Publish(Asset asset, Asset staging, ReloadReason reason)
    {
        bool first = asset.LoadCount == 0 || asset.WasEvicted;
        asset.WasEvicted = false;
        asset.Fill(staging);
        asset.LastReached = Stopwatch.GetTimestamp();
        asset.UnreachedWalks = 0;

        if (reason == ReloadReason.Load && first) Raise(Loaded, asset);
        else RaiseReloaded(asset, reason);
    }

    internal static void PublishFailed(Asset asset)
    {
        if (GetAssetType(asset.AssetID) == null)
        {
            asset.MarkMissing();
            return;
        }
        asset.MarkFailed();
        Debug.LogWarningOnce($"AssetDatabase.Failed.{asset.AssetID}", $"{asset.GetType().Name} '{asset.Name}' ({asset.AssetID}) could not be loaded.");
    }

    /// <summary>
    /// Fills a loaded asset again from its source, after a reimport, a save or a revert. An asset that is not
    /// loaded is left as it is, and reads the new content when it next loads.
    /// </summary>
    public static void Refill(Asset asset, ReloadReason reason)
    {
        // A read already in flight has the old content, so it reads again when it publishes.
        if (asset.State == AssetState.Loading)
        {
            Interlocked.Increment(ref asset.ReadGeneration);
            return;
        }

        // Its source exists again, or imports now, so the next use or walk loads it.
        if (asset.State is AssetState.Missing or AssetState.Failed)
        {
            asset.ReportedEmpty = false;
            asset.SetState(AssetState.Unloaded);
            return;
        }
        if (asset.State != AssetState.Loaded) return;

        // Reading may import first, and the import refills this asset too. The read here already has the result.
        if (!(t_refilling ??= new(ReferenceEqualityComparer.Instance)).Add(asset)) return;
        try
        {
            Asset staging = CreateShell(asset.GetType());
            if (!ReadContent(asset.AssetID, staging))
            {
                Debug.LogWarning($"Could not read {asset.GetType().Name} '{asset.Name}' again, it keeps its old content.");
                return;
            }
            asset.Fill(staging);
        }
        finally
        {
            t_refilling.Remove(asset);
        }
        RaiseReloaded(asset, reason);
    }

    [ThreadStatic] private static HashSet<Asset>? t_refilling;

    /// <summary>
    /// Frees an asset's payload and reads it again, so nothing built on its old content, such as a listener on one of its
    /// objects, carries on. For leaving play mode.
    /// </summary>
    public static void Reload(Asset asset)
    {
        if (!asset.IsLoaded) return;
        asset.Unload();
        asset.SetState(AssetState.Unloaded);
        asset.Load();
    }

    /// <summary>Fills an asset from a tree written earlier with the asset as its root, for undo and play mode revert.</summary>
    public static void Refill(Asset asset, EchoObject content, ReloadReason reason)
    {
        Asset staging = CreateShell(asset.GetType());
        Serializer.DeserializeInto(content, staging, new SerializationContext());
        asset.Fill(staging);
        RaiseReloaded(asset, reason);
    }

    /// <summary>Frees an asset's payload for a GUID that no longer exists. It keeps its GUID, so references to it survive.</summary>
    public static void MarkMissing(Guid assetId)
    {
        if (!s_assets.TryGetValue(assetId, out Asset? asset) || asset.State == AssetState.Missing) return;
        AssetLoader.Cancel(asset);
        bool wasLoaded = asset.IsLoaded;
        asset.MarkMissing();
        if (wasLoaded) Raise(Unloaded, asset);
    }

    /// <summary>Takes a GUID's object out of the registry, as missing, so the next lookup makes a new one. For an asset whose type changed.</summary>
    public static void Retire(Guid assetId)
    {
        if (s_assets.TryRemove(assetId, out Asset? asset)) Retire(asset);
    }

    private static void Retire(Asset asset)
    {
        Unregister(asset);
        Debug.LogWarning($"{asset.GetType().Name} '{asset.Name}' ({asset.AssetID}) is now a different type of asset. Whatever still holds the old object sees it as missing until reloaded.");
    }

    // Whatever still holds the object sees it as missing.
    private static void Unregister(Asset asset)
    {
        AssetLoader.Cancel(asset);
        asset.MarkMissing();
        asset.Registered = false;
    }

    private static void Raise(Action<Asset>? handler, Asset asset)
    {
        try { handler?.Invoke(asset); }
        catch (Exception ex) { Debug.LogError($"An asset event handler threw for '{asset.Name}': {ex}"); }
    }

    private static void RaiseReloaded(Asset asset, ReloadReason reason)
    {
        try { Reloaded?.Invoke(asset, reason); }
        catch (Exception ex) { Debug.LogError($"An asset event handler threw for '{asset.Name}': {ex}"); }
    }

    /// <summary>Loads a set of assets, and by default everything they depend on, holding them all until the group is disposed.</summary>
    public static AssetGroup LoadGroup(IEnumerable<Asset> assets, bool withDependencies = true)
        => new(withDependencies ? WithDependencies(assets) : assets.Distinct().ToList());

    /// <summary>Loads a scene's stored data and everything it depends on, without instantiating it.</summary>
    public static AssetGroup Preload(SceneAsset scene) => LoadGroup([scene]);

    private static List<Asset> WithDependencies(IEnumerable<Asset> assets)
    {
        var result = new List<Asset>();
        var seen = new HashSet<Guid>();
        var pending = new Stack<Asset>(assets);
        while (pending.Count > 0)
        {
            Asset asset = pending.Pop();
            if (!asset.IsFromDatabase)
            {
                result.Add(asset);
                continue;
            }
            if (!seen.Add(asset.AssetID)) continue;
            result.Add(asset);

            foreach (Guid dependency in SourceOf(asset.AssetID)?.GetHardDependencies(asset.AssetID) ?? [])
                if (Get(dependency) is { } found) pending.Push(found);
        }
        return result;
    }

    /// <summary>Publishes finished background loads. Called at the start of every frame.</summary>
    public static void Pump() => AssetLoader.Pump();

    /// <summary>Stops loading and frees every payload while graphics and audio are still up.</summary>
    public static void Shutdown()
    {
        AssetLoader.Stop();
        foreach (Asset asset in s_assets.Values)
            if (asset.IsLoaded && !asset.IsDisposed)
            {
                asset.Unload();
                asset.SetState(AssetState.Unloaded);
            }
    }

    #endregion

    #region Finding by load path

    private const string ResourcesFolder = "Resources";
    private const char SubAssetSeparator = '#';

    private static IReadOnlyList<ResourceEntry>? s_indexedResources;
    private static Dictionary<string, List<ResourceEntry>> s_resourcesByPath = new(StringComparer.OrdinalIgnoreCase);

    /// <summary>
    /// The asset at a load path, loaded. A load path is the asset's path below its nearest Resources folder without
    /// the extension, with "#Name" for a sub asset. A project path inside a Resources folder works too. Blocks.
    /// </summary>
    public static T? FindResource<T>(string loadPath) where T : Asset
    {
        T? asset = FindUnloaded<T>(loadPath);
        if (!AssetLoader.IsLoaderThread) asset?.Load();
        return asset;
    }

    public static async Task<T?> FindResourceAsync<T>(string loadPath, CancellationToken cancel = default) where T : Asset
    {
        T? asset = FindUnloaded<T>(loadPath);
        if (asset != null) await asset.LoadAsync(cancel);
        return asset;
    }

    /// <summary>Every asset of type <typeparamref name="T"/> under a Resources folder, or under a folder below one, loaded.</summary>
    public static List<T> FindAllResources<T>(string folder = "") where T : Asset
    {
        string? key = ToLoadPath(folder);
        var found = new List<T>();
        foreach (ResourceEntry entry in Resources)
            if ((key == null || IsAtOrBelow(entry.LoadPath, key)) && IsOfType<T>(entry) && Get(entry.Guid) is T asset)
                found.Add(asset);

        using AssetGroup group = LoadGroup(found, withDependencies: false);
        group.Wait();
        return found;
    }

    /// <summary>The GUID at a load path, or empty. Nothing is loaded.</summary>
    public static Guid FindResourceGuid<T>(string loadPath) where T : Asset
    {
        foreach (ResourceEntry entry in Candidates<T>(loadPath))
            return entry.Guid;
        return Guid.Empty;
    }

    private static T? FindUnloaded<T>(string loadPath) where T : Asset
    {
        foreach (ResourceEntry entry in Candidates<T>(loadPath))
            if (Get(entry.Guid) is T asset)
                return asset;
        return null;
    }

    private static IReadOnlyList<ResourceEntry>[] s_mergedFrom = [];
    private static IReadOnlyList<ResourceEntry> s_merged = [];

    // Every source's resources in one list, newest mount first, rebuilt only when a source's own list changes.
    private static IReadOnlyList<ResourceEntry> Resources
    {
        get
        {
            AssetBackend[] mounts = s_mounts;
            if (mounts.Length == 0) return Backend?.Resources ?? [];

            var lists = new IReadOnlyList<ResourceEntry>[mounts.Length + 1];
            for (int i = 0; i < mounts.Length; i++) lists[i] = mounts[i].Resources;
            lists[mounts.Length] = Backend?.Resources ?? [];

            IReadOnlyList<ResourceEntry>[] previous = s_mergedFrom;
            bool same = previous.Length == lists.Length;
            for (int i = 0; same && i < lists.Length; i++) same = ReferenceEquals(previous[i], lists[i]);
            if (same) return s_merged;

            var merged = new List<ResourceEntry>();
            foreach (IReadOnlyList<ResourceEntry> list in lists) merged.AddRange(list);
            s_merged = merged;
            s_mergedFrom = lists;
            return merged;
        }
    }

    /// <summary>The GUID at a load path for an asset of <paramref name="type"/>, or empty. Nothing is loaded.</summary>
    public static Guid FindResourceGuid(string loadPath, Type type)
    {
        foreach (ResourceEntry entry in Candidates(loadPath, type))
            return entry.Guid;
        return Guid.Empty;
    }

    private static IEnumerable<ResourceEntry> Candidates<T>(string loadPath) => Candidates(loadPath, typeof(T));

    private static IEnumerable<ResourceEntry> Candidates(string loadPath, Type type)
    {
        IReadOnlyList<ResourceEntry> resources = Resources;
        if (!ReferenceEquals(resources, s_indexedResources))
        {
            var byPath = new Dictionary<string, List<ResourceEntry>>(StringComparer.OrdinalIgnoreCase);
            foreach (ResourceEntry entry in resources)
            {
                if (!byPath.TryGetValue(entry.LoadPath, out var list))
                    byPath[entry.LoadPath] = list = [];
                list.Add(entry);
            }
            s_resourcesByPath = byPath;
            s_indexedResources = resources;
        }

        string? key = ToLoadPath(loadPath);
        if (key == null || !s_resourcesByPath.TryGetValue(key, out var entries)) return [];
        return entries.Where(entry => IsOfType(entry, type));
    }

    // An entry whose type can't be resolved is still a candidate, and is checked once loaded.
    private static bool IsOfType<T>(ResourceEntry entry) => IsOfType(entry, typeof(T));

    private static bool IsOfType(ResourceEntry entry, Type wanted)
    {
        Type? type = RuntimeUtils.ResolveType(entry.TypeName);
        return type == null || wanted.IsAssignableFrom(type);
    }

    private static bool IsAtOrBelow(string loadPath, string folder)
    {
        if (!loadPath.StartsWith(folder, StringComparison.OrdinalIgnoreCase)) return false;
        return loadPath.Length == folder.Length || loadPath[folder.Length] is '/' or SubAssetSeparator;
    }

    /// <summary>
    /// The load path of an asset at <paramref name="assetPath"/> (relative to Assets), or of its sub asset named
    /// <paramref name="subAssetName"/>. Null when the asset is not inside a Resources folder.
    /// </summary>
    public static string? GetLoadPath(string assetPath, string? subAssetName = null)
    {
        if (string.IsNullOrEmpty(assetPath)) return null;

        string[] segments = assetPath.Replace('\\', '/').Trim('/').Split('/');

        // The nearest Resources folder, so nested Resources folders load relative to the innermost one.
        // The last segment is the file itself and never counts as the folder.
        int resources = -1;
        for (int i = segments.Length - 2; i >= 0 && resources < 0; i--)
        {
            if (segments[i].Equals(ResourcesFolder, StringComparison.OrdinalIgnoreCase))
                resources = i;
        }
        if (resources < 0) return null;

        string loadPath = string.Join("/", segments, resources + 1, segments.Length - resources - 1);
        loadPath = StripExtension(loadPath);
        if (loadPath.Length == 0) return null;

        return string.IsNullOrEmpty(subAssetName) ? loadPath : loadPath + SubAssetSeparator + subAssetName;
    }

    // A path inside a Resources folder is a project path and is cut down to its load path, anything else is taken as a load path already.
    private static string? ToLoadPath(string path)
    {
        if (string.IsNullOrEmpty(path)) return null;

        path = path.Replace('\\', '/').Trim('/');

        string? subAsset = null;
        int separator = path.IndexOf(SubAssetSeparator);
        if (separator >= 0)
        {
            subAsset = path[(separator + 1)..];
            path = path[..separator];
        }

        string? projectLoadPath = GetLoadPath(path, subAsset);
        if (projectLoadPath != null) return projectLoadPath;

        path = StripExtension(path);
        if (path.Length == 0) return null;
        return string.IsNullOrEmpty(subAsset) ? path : path + SubAssetSeparator + subAsset;
    }

    // Only the file name carries an extension, so a dot in a folder name is left alone.
    private static string StripExtension(string path)
    {
        int slash = path.LastIndexOf('/');
        int dot = path.LastIndexOf('.');
        return dot > slash + 1 ? path[..dot] : path;
    }

    #endregion

    #region Holding

    private static readonly ConditionalWeakTable<object, HashSet<Asset>> s_holds = new();
    private static readonly List<WeakReference<object>> s_roots = new();
    private static readonly object s_holdLock = new();

    /// <summary>Keeps an asset loaded while <paramref name="owner"/> is alive, or until released. Holding twice is one hold.</summary>
    public static void Hold(Asset asset, object owner)
    {
        if (!asset.IsFromDatabase) return;
        lock (s_holdLock) s_holds.GetOrCreateValue(owner).Add(asset);
        if (!asset.IsLoaded) AssetLoader.Request(asset);
    }

    public static void Release(Asset asset, object owner)
    {
        lock (s_holdLock)
            if (s_holds.TryGetValue(owner, out HashSet<Asset>? held))
                held.Remove(asset);
    }

    public static void ReleaseAll(object owner)
    {
        lock (s_holdLock) s_holds.Remove(owner);
    }

    /// <summary>Walks an object's fields like a scene's, for something with many asset fields that lives outside a scene.</summary>
    public static void AddRoot(object root)
    {
        lock (s_holdLock)
        {
            foreach (WeakReference<object> existing in s_roots)
                if (existing.TryGetTarget(out object? target) && ReferenceEquals(target, root))
                    return;
            s_roots.Add(new WeakReference<object>(root));
        }
    }

    public static void RemoveRoot(object root)
    {
        lock (s_holdLock)
            s_roots.RemoveAll(r => !r.TryGetTarget(out object? target) || ReferenceEquals(target, root));
    }

    private static List<object> HoldOwnersOf(Asset asset)
    {
        var owners = new List<object>();
        lock (s_holdLock)
            foreach (var (owner, held) in s_holds)
                if (held.Contains(asset)) owners.Add(owner);
        return owners;
    }

    #endregion

    #region Residency

    /// <summary>How long an asset nothing reaches stays loaded.</summary>
    public static TimeSpan GracePeriod { get; set; } = TimeSpan.FromSeconds(10);

    public static TimeSpan WalkInterval { get; set; } = TimeSpan.FromSeconds(1);

    /// <summary>Loaded bytes past which unreached assets are freed without waiting out the grace period. Zero for no budget.</summary>
    public static long MemoryBudget { get; set; }

    /// <summary>Estimated bytes held by loaded database assets, as of the last walk.</summary>
    public static long ResidentBytes { get; private set; }

    /// <summary>False to keep everything loaded, as tests that count loads do.</summary>
    public static bool EvictionEnabled { get; set; } = true;

    /// <summary>Extra objects to walk, such as the editor's windows and selection.</summary>
    public static event Action<AssetWalker>? WalkingRoots;

    /// <summary>
    /// Time the walk may take each frame. A walk that needs longer carries on over the next frames, and assets are
    /// only freed once a whole walk has finished without reaching them.
    /// </summary>
    public static TimeSpan WalkBudget { get; set; } = TimeSpan.FromMilliseconds(0.5);

    /// <summary>Whether a walk spread over frames is under way.</summary>
    public static bool IsWalking { get; private set; }

    private static int s_epoch;
    private static int s_completedEpoch;
    private static int s_completedWalks;
    private static long s_lastWalk;
    private static bool s_warnedBudget;
    private static readonly AssetWalker s_walker = new();

    /// <summary>
    /// Advances the walk by this frame's budget, starting one when it is due, and frees what nothing reached once
    /// it finishes. Called at the end of every frame.
    /// </summary>
    public static void EndFrame()
    {
        if (!IsWalking)
        {
            if (Stopwatch.GetElapsedTime(s_lastWalk, Stopwatch.GetTimestamp()) < WalkInterval) return;
            BeginWalk();
        }

        long budget = (long)(WalkBudget.TotalSeconds * Stopwatch.Frequency);
        if (!s_walker.Step(budget)) return;
        CompleteWalk();
        Evict(ignoreGrace: false);
    }

    /// <summary>Walks every root now, in one go, and marks what it reaches. Replaces a walk under way.</summary>
    public static void Walk()
    {
        BeginWalk();
        s_walker.Finish();
        CompleteWalk();
    }

    /// <summary>Drops a walk under way, so it holds nothing an assembly unload is waiting on. The next one starts fresh.</summary>
    internal static void AbandonWalk()
    {
        IsWalking = false;
        s_walker.Reset();
    }

    private static void BeginWalk()
    {
        IsWalking = true;
        s_lastWalk = Stopwatch.GetTimestamp();
        int epoch = ++s_epoch;
        s_walker.Begin(epoch, s_lastWalk);

        foreach (Scene scene in Scene.Live)
            s_walker.Visit(scene);
        foreach (GameObject preserved in Scene.Preserved)
            s_walker.Visit(preserved);

        lock (s_holdLock)
        {
            foreach (var (_, held) in s_holds)
                foreach (Asset asset in held)
                    s_walker.Visit(asset);

            s_roots.RemoveAll(r => !r.TryGetTarget(out _));
            foreach (WeakReference<object> root in s_roots)
                if (root.TryGetTarget(out object? target))
                    s_walker.Visit(target);
        }

        try { WalkingRoots?.Invoke(s_walker); }
        catch (Exception ex) { Debug.LogError($"Walking extra asset roots threw: {ex}"); }

        s_walker.VisitHeldStatics();
    }

    private static void CompleteWalk()
    {
        IsWalking = false;
        int epoch = s_epoch;
        long resident = 0;
        foreach (Asset asset in s_assets.Values)
        {
            if (asset.MarkEpoch != epoch) asset.UnreachedWalks++;
            if (asset.IsLoaded) resident += asset.EstimateBytes();

            // A missing GUID nothing refers to any more has no identity worth keeping.
            if (asset.IsMissing && asset.UnreachedWalks >= 2 && GetAssetType(asset.AssetID) == null
                && s_assets.TryRemove(new KeyValuePair<Guid, Asset>(asset.AssetID, asset)))
                asset.Registered = false;
        }
        ResidentBytes = resident;
        s_completedEpoch = epoch;
        s_completedWalks++;
    }

    /// <summary>Walks now and loads, blocking, everything the walk reached that is not loaded yet.</summary>
    public static void LoadEverythingReached()
    {
        // A loaded prefab reaches more assets on the next walk, so this repeats until nothing new turns up.
        for (int pass = 0; pass < 8; pass++)
        {
            Walk();
            var pending = s_assets.Values.Where(a => a.MarkEpoch == s_epoch && a.State is AssetState.Unloaded or AssetState.Loading).ToList();
            if (pending.Count == 0) return;
            foreach (Asset asset in pending)
                asset.Load();
        }
    }

    /// <summary>Walks now and frees every unreached asset, without waiting out the grace period. Returns how many were freed.</summary>
    public static int UnloadUnused()
    {
        Walk();
        return Evict(ignoreGrace: true);
    }

    /// <summary>Frees one asset's payload now, even if something reaches it. It loads again on its next use.</summary>
    public static bool Unload(Asset asset)
    {
        if (!CanEvict(asset)) return false;
        Evict(asset);
        return true;
    }

    private static bool CanEvict(Asset asset)
        => asset.Registered && asset.IsLoaded && !BuiltInAssets.IsBuiltIn(asset.AssetID)
           && !AssetLoader.IsInFlight(asset);

    private static int Evict(bool ignoreGrace)
    {
        if (!EvictionEnabled || !HasAnySource) return 0;

        long now = Stopwatch.GetTimestamp();
        bool overBudget = MemoryBudget > 0 && ResidentBytes > MemoryBudget;

        var candidates = new List<Asset>();
        foreach (Asset asset in s_assets.Values)
        {
            if (asset.MarkEpoch == s_completedEpoch || asset.UnreachedWalks < 2 && !ignoreGrace) continue;
            if (!CanEvict(asset)) continue;
            if (!ignoreGrace && !overBudget && Stopwatch.GetElapsedTime(asset.LastReached, now) < GracePeriod) continue;
            candidates.Add(asset);
        }
        candidates.Sort((a, b) => a.LastReached.CompareTo(b.LastReached));

        int freed = 0;
        foreach (Asset asset in candidates)
        {
            bool graceOver = Stopwatch.GetElapsedTime(asset.LastReached, now) >= GracePeriod;
            if (!ignoreGrace && !graceOver && (MemoryBudget == 0 || ResidentBytes <= MemoryBudget)) break;

            long bytes = asset.EstimateBytes();
            Evict(asset);
            ResidentBytes -= bytes;
            freed++;
        }

        if (MemoryBudget > 0 && ResidentBytes > MemoryBudget && !s_warnedBudget)
        {
            s_warnedBudget = true;
            string largest = string.Join("\n", s_assets.Values.Where(a => a.IsLoaded).OrderByDescending(a => a.EstimateBytes()).Take(10)
                .Select(a => $"  {a.GetType().Name} '{a.Name}' {a.EstimateBytes() / 1024} KB"));
            Debug.LogWarning($"Assets in use need {ResidentBytes / (1024 * 1024)} MB, over the {MemoryBudget / (1024 * 1024)} MB budget. The largest:\n{largest}");
        }
        return freed;
    }

    private static void Evict(Asset asset)
    {
        asset.Unload();
        asset.WasEvicted = true;
        asset.SetState(AssetState.Unloaded);
        Raise(Unloaded, asset);
    }

    /// <summary>State, size, holders and whether the last walk reached the asset.</summary>
    public static AssetResidency Explain(Asset asset)
    {
        bool reached = asset.MarkEpoch == s_completedEpoch && s_completedWalks > 0;
        string? reachedBy = reached ? s_walker.PathTo(asset) : null;
        return new AssetResidency(asset.State, asset.IsLoaded ? asset.EstimateBytes() : 0, reached, reachedBy,
            HoldOwnersOf(asset), Stopwatch.GetElapsedTime(asset.LastReached), asset.LoadCount);
    }

    /// <summary>Records the first object that reached each asset in a walk, for <see cref="Explain"/>. Costs a little per walk.</summary>
    public static bool RecordReachPaths
    {
        get => s_walker.RecordPaths;
        set => s_walker.RecordPaths = value;
    }

    internal static void ClearForTests()
    {
        AssetLoader.Stop();
        s_assets.Clear();
        lock (s_mountLock) s_mounts = [];
        s_indexedResources = null;
        lock (s_holdLock)
        {
            s_holds.Clear();
            s_roots.Clear();
        }
        s_epoch = 0;
        s_completedEpoch = 0;
        s_completedWalks = 0;
        AbandonWalk();
        s_lastWalk = 0;
        ResidentBytes = 0;
    }

    #endregion
}

/// <summary>
/// Visits everything reachable from a root through instance fields, marking each asset it meets. It follows every
/// field whose type can hold an asset, serialized or not, except ones marked <see cref="NotHeldAttribute"/>.
/// It does not see statics, delegates or what a lambda or async method captured, so an asset held only there should
/// be held with <see cref="AssetDatabase.Hold"/>.
/// </summary>
public sealed class AssetWalker
{
    private enum Kind { Skip, Asset, Object, Enumerable }

    private sealed class Plan
    {
        public Kind Kind;
        public Func<object, object?>[] Fields = [];
    }

    private static readonly ReloadCache<Type, Plan> s_plans = new(BuildPlan);
    private static readonly ReloadCache<Type, bool> s_canHold = new();
    private static readonly string[] s_skippedAssemblies =
        ["System", "Microsoft", "Silk.NET", "Jitter2", "Prowl.Motion", "Prowl.Vector", "Prowl.Recast", "Prowl.Paper", "Prowl.Scribe", "Prowl.Echo", "Prowl.Quill", "Prowl.Ember"];

    private const int ExpandsPerClockCheck = 256;

    // Never reset, so a stamp an earlier walk left on an engine object can never match a later walk
    private static int s_stamp;

    private readonly HashSet<object> _visited = new(ReferenceEqualityComparer.Instance);
    private readonly Stack<object> _pending = new();
    private readonly Dictionary<object, object> _parents = new(ReferenceEqualityComparer.Instance);
    private int _epoch;
    private int _walkStamp;
    private long _now;
    private object? _current;

    internal bool RecordPaths;

    internal void Begin(int epoch, long now)
    {
        _epoch = epoch;
        _walkStamp = ++s_stamp;
        _now = now;
        Reset();
    }

    /// <summary>Drops everything the walk still has to visit, so it holds on to nothing.</summary>
    internal void Reset()
    {
        _visited.Clear();
        _parents.Clear();
        _pending.Clear();
        _current = null;
    }

    /// <summary>Marks what <paramref name="value"/> reaches, and it if it is an asset.</summary>
    public void Visit(object? value)
    {
        if (value == null) return;
        if (value is EngineObject engineObject)
        {
            if (engineObject.WalkStamp == _walkStamp) return;
            engineObject.WalkStamp = _walkStamp;
        }
        else if (!_visited.Add(value)) return;
        if (RecordPaths && _current != null) _parents.TryAdd(value, _current);

        if (value is Asset asset)
        {
            asset.MarkEpoch = _epoch;
            asset.LastReached = _now;
            asset.UnreachedWalks = 0;
            if (asset.State == AssetState.Unloaded) AssetLoader.Request(asset);
            if (!asset.IsLoaded) return;
        }
        else if (value is EngineObject { IsDisposed: true }) return;

        _pending.Push(value);
    }

    internal void Finish() => Step(long.MaxValue);

    /// <summary>Visits what is pending until <paramref name="budgetTicks"/> of stopwatch time is spent. True once nothing is left.</summary>
    internal bool Step(long budgetTicks)
    {
        long start = Stopwatch.GetTimestamp();
        int sinceCheck = 0;
        while (_pending.Count > 0)
        {
            if (++sinceCheck > ExpandsPerClockCheck)
            {
                sinceCheck = 0;
                if (Stopwatch.GetTimestamp() - start >= budgetTicks) break;
            }

            object value = _pending.Pop();
            // Pending across frames, so it may have been destroyed since it was found
            if (value is EngineObject { IsDisposed: true }) continue;

            _current = value;
            try { Expand(value); }
            catch (Exception ex)
            {
                Debug.LogWarningOnce($"AssetWalker.Threw.{value.GetType().FullName}",
                    $"Walking a {value.GetType().Name} threw, so the assets only it holds count as unused: {ex.Message}");
            }
        }
        _current = null;
        return _pending.Count == 0;
    }

    private void Expand(object value)
    {
        if (value is IAssetWalkable custom)
        {
            custom.Walk(this);
            return;
        }

        Plan plan = s_plans[value.GetType()];
        if (plan.Kind == Kind.Skip) return;

        if (plan.Kind == Kind.Enumerable)
        {
            // Indexed where possible, since enumerating through the interface boxes an enumerator per collection
            if (value is IList list)
            {
                for (int i = 0; i < list.Count; i++)
                    if (list[i] is { } item) VisitMember(item);
            }
            else
            {
                foreach (object? item in (IEnumerable)value)
                    if (item != null) VisitMember(item);
            }
        }

        foreach (Func<object, object?> field in plan.Fields)
            if (field(value) is { } member) VisitMember(member);
    }

    // A struct is walked where it sits, since boxing gives it a new identity each time.
    private void VisitMember(object member)
    {
        if (member.GetType().IsValueType) Expand(member);
        else Visit(member);
    }

    /// <summary>The chain of objects the last walk followed to reach <paramref name="target"/>, when paths were recorded.</summary>
    internal string? PathTo(object target)
    {
        if (!RecordPaths) return null;
        var chain = new List<string>();
        for (object? current = target; current != null && chain.Count < 32; current = _parents.GetValueOrDefault(current))
            chain.Add(current switch
            {
                EngineObject engineObject => $"{current.GetType().Name} '{engineObject.Name}'",
                StaticRoot root => root.Name,
                _ => current.GetType().Name,
            });
        chain.Reverse();
        return string.Join(" > ", chain);
    }

    // Only a collection is enumerated, since enumerating anything else runs code that may be lazy or never end.
    // A base library collection is walked through its elements alone, any other through its fields as well.
    private sealed class StaticRoot(string name)
    {
        public string Name { get; } = name;
    }

    private static readonly ConditionalWeakTable<Assembly, FieldInfo[]> s_heldStatics = new();

    /// <summary>Marks what every static field with <see cref="HeldStaticAttribute"/> reaches.</summary>
    internal void VisitHeldStatics()
    {
        foreach (Assembly assembly in AppDomain.CurrentDomain.GetAssemblies())
            foreach (FieldInfo field in s_heldStatics.GetValue(assembly, FindHeldStatics))
            {
                object? value;
                try { value = field.GetValue(null); }
                catch (Exception ex)
                {
                    Debug.LogWarningOnce($"AssetWalker.Static.{field.DeclaringType?.FullName}.{field.Name}",
                        $"Reading {field.DeclaringType?.Name}.{field.Name} for the asset walk threw, so what it holds counts as unused: {ex.Message}");
                    continue;
                }
                if (value == null) continue;

                _current = RecordPaths ? new StaticRoot($"static {field.DeclaringType?.Name}.{field.Name}") : null;
                VisitMember(value);
            }
        _current = null;
    }

    // Only an assembly that references the engine can use the attribute.
    private static FieldInfo[] FindHeldStatics(Assembly assembly)
    {
        Assembly engine = typeof(Asset).Assembly;
        if (assembly.IsDynamic) return [];
        if (assembly != engine && !assembly.GetReferencedAssemblies().Any(reference => reference.Name == engine.GetName().Name)) return [];

        Type?[] types;
        try { types = assembly.GetTypes(); }
        catch (ReflectionTypeLoadException ex) { types = ex.Types; }

        var fields = new List<FieldInfo>();
        foreach (Type? type in types)
        {
            if (type == null || type.ContainsGenericParameters) continue;
            foreach (FieldInfo field in type.GetFields(BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.DeclaredOnly))
                if (field.IsDefined(typeof(HeldStaticAttribute)))
                    fields.Add(field);
        }
        return fields.ToArray();
    }

    private static Plan BuildPlan(Type type)
    {
        if (!CanHold(type)) return new Plan { Kind = Kind.Skip };
        if (type.IsArray) return new Plan { Kind = ElementCanHold(type) ? Kind.Enumerable : Kind.Skip };

        bool collection = type != typeof(string) && !typeof(EngineObject).IsAssignableFrom(type) && IsCollection(type);
        if (collection && Skipped(type)) return new Plan { Kind = ElementCanHold(type) ? Kind.Enumerable : Kind.Skip };

        var fields = new List<Func<object, object?>>();
        for (Type? current = type; current != null && current != typeof(object); current = current.BaseType)
            foreach (FieldInfo field in current.GetFields(BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.DeclaredOnly))
                if (!field.IsDefined(typeof(NotHeldAttribute)) && CanHold(field.FieldType))
                    fields.Add(Getter(field));

        Kind kind = collection && ElementCanHold(type) ? Kind.Enumerable : typeof(Asset).IsAssignableFrom(type) ? Kind.Asset : Kind.Object;
        return new Plan { Kind = kind, Fields = fields.ToArray() };
    }

    // A compiled read is many times faster than reflection. Types a hot reload can unload keep the reflection read,
    // so no generated code holds on to them.
    private static Func<object, object?> Getter(FieldInfo field)
    {
        Type owner = field.DeclaringType!;
        if (owner.Assembly.IsCollectible || field.FieldType.Assembly.IsCollectible)
            return field.GetValue;

        ParameterExpression target = Expression.Parameter(typeof(object));
        Expression read = Expression.Field(Expression.Convert(target, owner), field);
        return Expression.Lambda<Func<object, object?>>(Expression.Convert(read, typeof(object)), target).Compile();
    }

    private static bool IsCollection(Type type)
    {
        if (typeof(ICollection).IsAssignableFrom(type)) return true;
        foreach (Type iface in type.GetInterfaces())
            if (iface.IsGenericType && (iface.GetGenericTypeDefinition() == typeof(ICollection<>) || iface.GetGenericTypeDefinition() == typeof(IReadOnlyCollection<>)))
                return true;
        return false;
    }

    private static bool ElementCanHold(Type type)
    {
        if (type.IsArray) return CanHold(type.GetElementType()!);
        foreach (Type iface in type.GetInterfaces())
            if (iface.IsGenericType && iface.GetGenericTypeDefinition() == typeof(IEnumerable<>))
                return CanHold(iface.GetGenericArguments()[0]);
        return true;
    }

    /// <summary>Whether a value of this static type can lead to an asset.</summary>
    private static bool CanHold(Type type)
    {
        if (s_canHold.TryGetValue(type, out bool known)) return known;
        s_canHold.Set(type, false); // a type that contains itself is decided by its other fields
        bool result = Decide(type);
        s_canHold.Set(type, result);
        return result;
    }

    private static bool Decide(Type type)
    {
        if (type.IsPrimitive || type.IsEnum || type.IsPointer || type == typeof(string) || type == typeof(Type) || typeof(Delegate).IsAssignableFrom(type)) return false;
        if (typeof(EngineObject).IsAssignableFrom(type) || type == typeof(object)) return true;
        if (type.IsArray) return CanHold(type.GetElementType()!);

        // Collections, pairs and tuples of the base library hold whatever their type arguments can.
        if (Skipped(type)) return type.IsGenericType && type.GetGenericArguments().Any(CanHold);
        if (type.IsInterface) return true;
        if (!type.IsValueType && !type.IsSealed) return true;

        for (Type? current = type; current != null && current != typeof(object); current = current.BaseType)
            foreach (FieldInfo field in current.GetFields(BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.DeclaredOnly))
                if (!field.IsDefined(typeof(NotHeldAttribute)) && CanHold(field.FieldType))
                    return true;
        return false;
    }

    private static bool Skipped(Type type)
    {
        string name = type.Assembly.GetName().Name ?? "";
        foreach (string skipped in s_skippedAssemblies)
            if (name == skipped || name.StartsWith(skipped + ".", StringComparison.Ordinal))
                return true;
        return false;
    }
}

/// <summary>For a type that knows better than its fields what it holds, such as a prefab reaching everything its stored tree names.</summary>
public interface IAssetWalkable
{
    void Walk(AssetWalker walker);
}

/// <summary>
/// Writes a database asset that is not the root of a write as <c>{"$asset": guid}</c>, and reads that back as the
/// asset's stable object without loading it. Runtime assets are written in full.
/// </summary>
internal sealed class AssetReferenceRule : IReferenceRule
{
    public const string AssetKey = "$asset";

    public string Key => AssetKey;

    private const string RuntimePrefix = "runtime:";

    // Runtime assets linked by an in-memory copy, by instance id. Weak, so a link never keeps an asset alive.
    private static readonly ConcurrentDictionary<int, WeakReference<Asset>> s_runtimeLinks = new();
    private static int s_pruneLinksAt = 256;

    public bool TryGetReference(object value, SerializationContext context, out string reference)
    {
        reference = "";
        if (value is not Asset asset) return false;

        var tracker = context as DependencySerializationContext;
        if (tracker?.Inline?.Contains(asset) == true) return false;

        if (asset.AssetID != Guid.Empty)
        {
            tracker?.Dependencies.Add(asset.AssetID);
            reference = asset.AssetID.ToString();
            return true;
        }

        if (tracker is { LinkRuntimeAssets: true })
        {
            if (!s_runtimeLinks.ContainsKey(asset.InstanceID) && s_runtimeLinks.Count >= s_pruneLinksAt) PruneRuntimeLinks();
            s_runtimeLinks[asset.InstanceID] = new WeakReference<Asset>(asset);
            tracker.LinkedAssets.Add(asset);
            reference = RuntimePrefix + asset.InstanceID;
            return true;
        }
        return false;
    }

    private static void PruneRuntimeLinks()
    {
        foreach (var (id, link) in s_runtimeLinks)
            if (!link.TryGetTarget(out _))
                s_runtimeLinks.TryRemove(id, out _);
        s_pruneLinksAt = Math.Max(256, s_runtimeLinks.Count * 2);
    }

    public object? Resolve(string reference, Type declaredType, SerializationContext context)
    {
        if (reference.StartsWith(RuntimePrefix, StringComparison.Ordinal))
            return int.TryParse(reference.AsSpan(RuntimePrefix.Length), out int instanceId)
                   && s_runtimeLinks.TryGetValue(instanceId, out WeakReference<Asset>? link) && link.TryGetTarget(out Asset? linked)
                   && declaredType.IsInstanceOfType(linked) ? linked : null;

        Type type = typeof(Asset).IsAssignableFrom(declaredType) ? declaredType : typeof(Asset);
        if (declaredType.IsGenericType && declaredType.GetGenericTypeDefinition() == typeof(AssetRef<>))
            type = declaredType.GetGenericArguments()[0];

        // Text written by hand can name an asset by its load path, "Textures/Paint", rather than its GUID.
        if (!Guid.TryParse(reference, out Guid id))
            id = AssetDatabase.FindResourceGuid(reference, type);
        if (id == Guid.Empty) return null;
        if (context is DependencySerializationContext tracker) tracker.Dependencies.Add(id);

        // A field that became an AssetRef reads the stub as a lazy reference.
        if (declaredType.IsGenericType && declaredType.GetGenericTypeDefinition() == typeof(AssetRef<>))
            return Activator.CreateInstance(declaredType, id);

        return AssetDatabase.GetOrMissing(id, type);
    }
}
