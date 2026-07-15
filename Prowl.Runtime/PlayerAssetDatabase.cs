using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.IO;
using System.IO.Compression;
using System.Reflection;
using System.Threading;

using Prowl.Echo;

using Scene = Prowl.Runtime.Resources.Scene;

namespace Prowl.Runtime;

/// <summary>
/// Asset database for built standalone players.
/// Loads binary Echo cache files from loose files, ProwlPak archives, or embedded resources.
/// </summary>
public class PlayerAssetDatabase : IAssetDatabase
{
    private readonly AssetPackagingMode _mode;
    private readonly string _basePath;
    private readonly Dictionary<Guid, string> _guidToPath = new();
    // Strongly held: an asset stays loaded until something explicitly disposes it, or the
    // idle-timeout sweep (see MaybeSweepIdle) collects a GUID nothing has touched in a while.
    // Always go through TryGetLoaded/SetLoaded rather than touching this directly.
    private readonly ConcurrentDictionary<Guid, EngineObject> _cache = new();
    private static readonly TimeSpan IdleTimeout = TimeSpan.FromSeconds(60);
    private static readonly TimeSpan IdleSweepInterval = TimeSpan.FromSeconds(5);
    private long _lastIdleSweepTicks;
    private readonly object _loadLock = new();
    // Per-thread re-entrancy guard that breaks dependency cycles during deserialization.
    [ThreadStatic] private static HashSet<Guid>? _loadingStack;
    private readonly List<ZipArchive> _pakArchives = new();

    public Dictionary<string, Guid> ResourcesMap { get; } = new(StringComparer.OrdinalIgnoreCase);
    public Guid DefaultSceneGuid { get; private set; }

    public PlayerAssetDatabase(AssetPackagingMode mode, string basePath = "Content")
    {
        _mode = mode;
        _basePath = Path.Combine(Application.DataPath, basePath);

        switch (mode)
        {
            case AssetPackagingMode.LooseFiles:
            case AssetPackagingMode.ProwlPak:
                LoadManifestFromFile(Path.Combine(_basePath, "asset_manifest.bin"));
                if (mode == AssetPackagingMode.ProwlPak) LoadPakArchives();
                break;
            case AssetPackagingMode.Embedded:
                LoadManifestFromEmbedded();
                break;
        }
    }

    /// <summary>Resolve a loaded asset by GUID if it's present and not disposed. The only valid way
    /// to read <see cref="_cache"/> - go through this rather than the dictionary directly so every
    /// successful resolve consistently records activity.</summary>
    private bool TryGetLoaded(Guid guid, out EngineObject obj)
    {
        if (_cache.TryGetValue(guid, out var found) && found != null && !found.IsDisposed)
        {
            obj = found;
            AssetDatabase.Touch(guid);
            return true;
        }
        obj = null!;
        return false;
    }

    /// <summary>Cache a freshly resolved asset instance by GUID. Strongly held until explicitly
    /// disposed or idle-swept - see <see cref="_cache"/>.</summary>
    private void SetLoaded(Guid guid, EngineObject obj)
    {
        _cache[guid] = obj;
        AssetDatabase.Touch(guid);
    }

    /// <summary>Dispose and drop every GUID that's gone untouched for <see cref="IdleTimeout"/> and
    /// isn't locked. Gated to run at most once per <see cref="IdleSweepInterval"/>.</summary>
    private void MaybeSweepIdle()
    {
        var now = DateTime.UtcNow;
        long last = Interlocked.Read(ref _lastIdleSweepTicks);
        if (now.Ticks - last < IdleSweepInterval.Ticks) return;
        if (Interlocked.CompareExchange(ref _lastIdleSweepTicks, now.Ticks, last) != last) return;

        foreach (var kv in _cache)
        {
            Guid guid = kv.Key;
            if (AssetDatabase.IsLocked(guid)) continue;
            if (!AssetDatabase.IsIdle(guid, IdleTimeout)) continue;

            if (_cache.TryRemove(guid, out var obj))
            {
                try { obj.Dispose(); } catch (Exception ex) { Debug.LogWarning($"[PlayerAssetDatabase] Error disposing idle asset {guid}: {ex.Message}"); }
                AssetDatabase.Forget(guid);
            }
        }
    }

    /// <summary>Test-only: run an idle sweep immediately, bypassing <see cref="IdleSweepInterval"/>'s
    /// gate. Combine with <see cref="AssetDatabase.ForceIdle"/> to test eviction deterministically
    /// without waiting out the real <see cref="IdleTimeout"/>.</summary>
    internal void ForceIdleSweep()
    {
        Interlocked.Exchange(ref _lastIdleSweepTicks, 0);
        MaybeSweepIdle();
    }

    /// <summary>
    /// Give the idle sweep a chance to run. Called once per frame from the player's update loop -
    /// cheap to call that often since MaybeSweepIdle's own gate means the actual scan only happens
    /// once per IdleSweepInterval regardless of how often this is called.
    /// </summary>
    public void TickIdleSweep() => MaybeSweepIdle();

    public EngineObject? Get(Guid assetId)
    {
        if (assetId == Guid.Empty) return null;
        if (TryGetLoaded(assetId, out var cached))
            return cached;

        // Break dependency cycles on the loading thread.
        var stack = _loadingStack ??= new HashSet<Guid>();
        if (!stack.Add(assetId))
            return null;

        try
        {
            // Serialize the deserialize so concurrent callers (e.g. loader + render thread in
            // sync mode) don't double-load; the second caller picks up the cached result.
            lock (_loadLock)
            {
                if (TryGetLoaded(assetId, out cached))
                    return cached;

                byte[]? data = LoadRawAsset(assetId);
                if (data == null) return null;

                var echo = ReadEchoBinary(data);
                if (echo == null) return null;

                var obj = Serializer.Deserialize<EngineObject>(echo);
                if (obj != null)
                {
                    obj.AssetID = assetId;
                    SetLoaded(assetId, obj);
                }
                return obj;
            }
        }
        catch (Exception ex)
        {
            Debug.LogError($"[PlayerAssetDatabase] Failed to load asset {assetId}: {ex.Message}");
            return null;
        }
        finally
        {
            stack.Remove(assetId);
        }
    }

    /// <summary>Non-blocking cache peek (no deserialize). Safe from any thread.</summary>
    public EngineObject? GetCached(Guid assetId)
    {
        if (assetId == Guid.Empty) return null;
        return TryGetLoaded(assetId, out var c) ? c : null;
    }

    /// <summary>Load a scene by GUID.</summary>
    public Scene? LoadScene(Guid sceneGuid)
    {
        byte[]? data = LoadRawAsset(sceneGuid);
        if (data == null) { Debug.LogError($"[PlayerAssetDatabase] Scene not found: {sceneGuid}"); return null; }

        try
        {
            var echo = ReadEchoBinary(data);
            if (echo == null) return null;

            return Serializer.Deserialize<Scene>(echo);
        }
        catch (Exception ex)
        {
            Debug.LogError($"[PlayerAssetDatabase] Failed to load scene {sceneGuid}: {ex.Message}");
            return null;
        }
    }

    // ================================================================
    //  Raw asset loading
    // ================================================================

    private byte[]? LoadRawAsset(Guid guid)
    {
        string fileName = $"{guid}.asset";

        return _mode switch
        {
            AssetPackagingMode.LooseFiles => LoadFromFile(Path.Combine(_basePath, fileName)),
            AssetPackagingMode.ProwlPak => LoadFromPak(fileName),
            AssetPackagingMode.Embedded => LoadFromEmbedded($"Assets.{fileName}"),
            _ => null,
        };
    }

    private static byte[]? LoadFromFile(string path)
        => File.Exists(path) ? File.ReadAllBytes(path) : null;

    private byte[]? LoadFromPak(string entryName)
    {
        // ZipArchive is not thread-safe. Serialize all pak reads on the same lock Get() uses (it is
        // re-entrant, so Get -> LoadRawAsset -> LoadFromPak is fine) so a background Get and a
        // main-thread LoadScene can't read the shared archive/stream concurrently.
        lock (_loadLock)
        {
            foreach (var pak in _pakArchives)
            {
                var entry = pak.GetEntry(entryName);
                if (entry != null)
                {
                    using var stream = entry.Open();
                    using var ms = new MemoryStream();
                    stream.CopyTo(ms);
                    return ms.ToArray();
                }
            }
            return null;
        }
    }

    private static byte[]? LoadFromEmbedded(string resourceName)
    {
        var assembly = Assembly.GetEntryAssembly() ?? Assembly.GetExecutingAssembly();
        using var stream = assembly.GetManifestResourceStream(resourceName);
        if (stream == null) return null;
        using var ms = new MemoryStream();
        stream.CopyTo(ms);
        return ms.ToArray();
    }

    // ================================================================
    //  Manifest loading
    // ================================================================

    private void LoadManifestFromFile(string manifestPath)
    {
        if (!File.Exists(manifestPath))
        {
            Debug.LogError($"[PlayerAssetDatabase] Manifest not found: {manifestPath}");
            return;
        }

        try
        {
            var echo = EchoObject.ReadFromBinary(new FileInfo(manifestPath));
            ParseManifest(echo);
        }
        catch (Exception ex)
        {
            Debug.LogError($"[PlayerAssetDatabase] Failed to parse manifest: {ex.Message}");
        }
    }

    private void LoadPakArchives()
    {
        if (!Directory.Exists(_basePath)) return;

        foreach (var pakFile in Directory.GetFiles(_basePath, "*.prowlpak"))
        {
            try
            {
                _pakArchives.Add(ZipFile.OpenRead(pakFile));
            }
            catch (Exception ex)
            {
                Debug.LogError($"[PlayerAssetDatabase] Failed to open pak {pakFile}: {ex.Message}");
            }
        }
    }

    private void LoadManifestFromEmbedded()
    {
        var assembly = Assembly.GetEntryAssembly() ?? Assembly.GetExecutingAssembly();
        using var stream = assembly.GetManifestResourceStream("Assets._manifest.bin");
        if (stream == null) { Debug.LogError("[PlayerAssetDatabase] Embedded manifest not found."); return; }

        using var ms = new MemoryStream();
        stream.CopyTo(ms);
        ParseManifest(ReadEchoBinary(ms.ToArray()));
    }

    private void ParseManifest(EchoObject? echo)
    {
        if (echo == null) return;

        if (echo.TryGet("defaultScene", out var dsTag) && Guid.TryParse(dsTag.StringValue, out var ds))
            DefaultSceneGuid = ds;

        if (echo.TryGet("assets", out var assetsTag) && assetsTag.TagType == EchoType.Compound)
            foreach (var kvp in assetsTag.Tags)
                if (Guid.TryParse(kvp.Key, out var guid))
                    _guidToPath[guid] = kvp.Value.StringValue;

        if (echo.TryGet("resources", out var resTag) && resTag.TagType == EchoType.Compound)
            foreach (var kvp in resTag.Tags)
                if (Guid.TryParse(kvp.Value.StringValue, out var guid))
                    ResourcesMap[kvp.Key] = guid;
    }

    /// <summary>Read Echo binary from byte array.</summary>
    private static EchoObject? ReadEchoBinary(byte[] data)
    {
        using var ms = new MemoryStream(data);
        using var reader = new BinaryReader(ms);
        return EchoObject.ReadFromBinary(reader);
    }

    public void Dispose()
    {
        foreach (var pak in _pakArchives) pak.Dispose();
        _pakArchives.Clear();
        _cache.Clear();
    }
}
