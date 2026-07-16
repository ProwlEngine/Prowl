// This file is part of the Prowl Game Engine
// Licensed under the MIT License. See the LICENSE file in the project root for details.

using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Threading;

namespace Prowl.Runtime;

/// <summary>
/// Base class for asset storage backends (editor project vs. built player). Owns the shared
/// loaded-asset cache and idle-timeout eviction model - subclasses only implement how an asset is
/// actually loaded from its source (disk cache, pak, import, ...) via <see cref="LoadFresh"/>.
/// </summary>
public abstract class AssetBackendBase
{
    public static readonly TimeSpan IdleTimeout = TimeSpan.FromSeconds(60);
    public static readonly TimeSpan IdleSweepInterval = TimeSpan.FromSeconds(5);

    #region Loaded Asset Cache

    // Strongly held: an asset stays loaded until explicitly disposed or idle-swept. Always go
    // through TryGetLoaded/SetLoaded rather than touching this directly.
    protected readonly ConcurrentDictionary<Guid, EngineObject> _loadedAssets = new();

    protected bool TryGetLoaded(Guid guid, out EngineObject obj)
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

    protected void SetLoaded(Guid guid, EngineObject obj)
    {
        _loadedAssets[guid] = obj;
        AssetDatabase.Touch(guid);
        _reloadCounts.AddOrUpdate(guid, 1, (_, count) => count + 1);
    }

    /// <summary>Get an already-loaded asset from memory without triggering a load. Null if not loaded.</summary>
    public EngineObject? GetLoadedAsset(Guid guid) => TryGetLoaded(guid, out var obj) ? obj : null;

    // Counts fresh loads (cache misses) per GUID for this session - a GUID reloading repeatedly
    // usually means something is reading it without keeping it in use between reads (see
    // EngineObject.EnsureNotDisposed). Diagnostic-only, for the Asset Database panel.
    private readonly ConcurrentDictionary<Guid, int> _reloadCounts = new();

    /// <summary>Number of times this GUID has been freshly loaded (not counting cache hits) since the backend started.</summary>
    public int GetReloadCount(Guid guid) => _reloadCounts.GetValueOrDefault(guid);

    /// <summary>Snapshot of every currently-resident GUID and its loaded instance, for diagnostics.
    /// Not a live view, and reading it doesn't itself count as activity.</summary>
    public IEnumerable<(Guid Guid, EngineObject Asset)> GetLoadedAssets()
    {
        foreach (var kv in _loadedAssets)
            if (kv.Value != null && !kv.Value.IsDisposed)
                yield return (kv.Key, kv.Value);
    }

    #endregion

    #region Idle Sweep

    private long _lastIdleSweepTicks;

    /// <summary>UTC time the idle sweep last actually ran (not merely checked its gate). Diagnostic-only.</summary>
    public DateTime LastSweepUtc => new(Interlocked.Read(ref _lastIdleSweepTicks), DateTimeKind.Utc);

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
                try { obj.Dispose(); } catch (Exception ex) { Debug.LogWarning($"Error disposing idle asset {guid}: {ex.Message}"); }
                AssetDatabase.Forget(guid);
            }
        }
    }

    /// <summary>Run an idle sweep immediately, bypassing <see cref="IdleSweepInterval"/>'s gate - used
    /// by manual "Sweep Now" actions, and by tests via <see cref="AssetDatabase.ForceIdle"/>.</summary>
    public void ForceIdleSweep()
    {
        Interlocked.Exchange(ref _lastIdleSweepTicks, 0);
        MaybeSweepIdle();
    }

    /// <summary>Give the idle sweep a chance to run. Cheap to call every frame - the gate above means
    /// the actual scan only happens once per <see cref="IdleSweepInterval"/>.</summary>
    public void TickIdleSweep() => MaybeSweepIdle();

    #endregion

    #region Resolution

    // Per-thread re-entrancy guard that breaks dependency cycles during deserialization.
    [ThreadStatic] private static HashSet<Guid>? _loadingStack;
    protected readonly object _loadLock = new();

    /// <summary>
    /// Retrieves an <see cref="EngineObject"/> by its asset ID. May block while the asset is
    /// deserialized (and, in the editor, imported on demand). Called on the <see cref="AssetLoader"/>
    /// background thread for async loads, and directly on the calling thread otherwise.
    /// </summary>
    public EngineObject? Get(Guid assetId)
    {
        if (assetId == Guid.Empty) return null;
        if (TryGetLoaded(assetId, out var loaded))
            return loaded;

        var stack = _loadingStack ??= new HashSet<Guid>();
        if (!stack.Add(assetId))
            return null; // already loading higher up the call stack

        try
        {
            lock (_loadLock)
            {
                if (TryGetLoaded(assetId, out loaded))
                    return loaded;
                return LoadFresh(assetId);
            }
        }
        finally
        {
            stack.Remove(assetId);
        }
    }

    /// <summary>Non-blocking cache peek: the already-loaded instance, or null. Never deserializes,
    /// imports, or blocks. Safe from any thread.</summary>
    public EngineObject? GetCached(Guid assetId)
    {
        if (assetId == Guid.Empty) return null;
        return TryGetLoaded(assetId, out var c) ? c : null;
    }

    /// <summary>Load an asset fresh from its source. Called under <see cref="_loadLock"/> after a
    /// cache miss; implementations should call <see cref="SetLoaded"/> on success.</summary>
    protected abstract EngineObject? LoadFresh(Guid assetId);

    #endregion
}
