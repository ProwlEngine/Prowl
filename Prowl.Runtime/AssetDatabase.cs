// This file is part of the Prowl Game Engine
// Licensed under the MIT License. See the LICENSE file in the project root for details.

using System;
using System.Collections.Concurrent;
using System.Collections.Generic;

using Prowl.Runtime.Resources;

namespace Prowl.Runtime;

/// <summary>
/// Global asset access: resolving assets by GUID, tracking activity for idle-timeout eviction,
/// and pinning assets that must stay resident despite not being touched.
/// </summary>
public static class AssetDatabase
{
    #region Resolution

    /// <summary>The current asset database implementation. Set this before serializing/deserializing
    /// objects that contain asset references.</summary>
    public static AssetBackendBase? Current { get; set; }

    /// <summary>Resolves an asset by ID. Checks built-in assets first, then the current database.</summary>
    public static EngineObject? Get(Guid assetId)
    {
        var builtIn = BuiltInAssets.Get(assetId);
        if (builtIn != null) return builtIn;

        return Current?.Get(assetId);
    }

    /// <summary>Non-blocking cache peek. Never deserializes, imports, or blocks.</summary>
    public static EngineObject? GetCached(Guid assetId)
    {
        var builtIn = BuiltInAssets.Get(assetId);
        if (builtIn != null) return builtIn;

        return Current?.GetCached(assetId);
    }

    #endregion

    #region Activity Tracking

    // A GUID is "idle" purely based on time since last touch - no GC involved. Touched by
    // AssetRef.Res/.Touch, AssetDatabase.Touch, and EngineObject.EnsureNotDisposed.
    private static readonly ConcurrentDictionary<Guid, DateTime> _lastTouched = new();

    // Opt-in per GUID (see the Asset Database panel's "Track" action) - capturing a stack trace on
    // every touch of every asset would be far too expensive to do unconditionally.
    private static readonly ConcurrentDictionary<Guid, byte> _stackTraceCaptureEnabled = new();
    private static readonly ConcurrentDictionary<Guid, string> _lastTouchStackTraces = new();

    /// <summary>Maps a GUID to the GUID whose lifecycle it's actually tied to (a sub-asset resolves
    /// to its parent). Set by EditorAssetDatabase.Initialize(); null elsewhere, where a GUID resolves
    /// to itself.</summary>
    internal static Func<Guid, Guid>? ResolveFamily;

    internal static Guid Resolve(Guid guid) => ResolveFamily != null ? ResolveFamily(guid) : guid;

    /// <summary>Record that a GUID's family was just used. No-op for <see cref="Guid.Empty"/>.</summary>
    public static void Touch(Guid guid)
    {
        if (guid == Guid.Empty) return;
        guid = Resolve(guid);
        _lastTouched[guid] = DateTime.UtcNow;

        if (_stackTraceCaptureEnabled.ContainsKey(guid))
            _lastTouchStackTraces[guid] = new System.Diagnostics.StackTrace(1, true).ToString();
    }

    /// <summary>Enable/disable capturing a stack trace on every future touch of a GUID's family.
    /// Disabling also drops any previously captured trace.</summary>
    internal static void SetStackTraceCapture(Guid guid, bool enabled)
    {
        guid = Resolve(guid);
        if (enabled)
            _stackTraceCaptureEnabled[guid] = 0;
        else
        {
            _stackTraceCaptureEnabled.TryRemove(guid, out _);
            _lastTouchStackTraces.TryRemove(guid, out _);
        }
    }

    internal static bool IsCapturingStackTrace(Guid guid) => _stackTraceCaptureEnabled.ContainsKey(Resolve(guid));

    internal static bool TryGetLastTouchStackTrace(Guid guid, out string trace)
        => _lastTouchStackTraces.TryGetValue(Resolve(guid), out trace!);

    /// <summary>True if a GUID's family has gone at least <paramref name="threshold"/> since its last touch.</summary>
    internal static bool IsIdle(Guid guid, TimeSpan threshold)
    {
        guid = Resolve(guid);
        return !_lastTouched.TryGetValue(guid, out var last) || DateTime.UtcNow - last >= threshold;
    }

    internal static bool TryGetLastTouched(Guid guid, out DateTime lastTouched)
        => _lastTouched.TryGetValue(Resolve(guid), out lastTouched);

    /// <summary>Drop tracking for a GUID's family once it's evicted/disposed.</summary>
    internal static void Forget(Guid guid) => _lastTouched.TryRemove(Resolve(guid), out _);

    /// <summary>Test-only: make a GUID's family appear idle regardless of real elapsed time.</summary>
    internal static void ForceIdle(Guid guid) => _lastTouched[Resolve(guid)] = DateTime.MinValue;

    #endregion

    #region Locking

    // Escape hatch for the idle-timeout sweep: pins an asset resident even though nothing is
    // actively touching it. Ownership is set membership, not a ref-count, so locking the same GUID
    // twice is idempotent and an unbalanced unlock can't leave it stuck. LockToScene releases
    // automatically when that scene disposes; LockPermanent needs an explicit Unlock.
    private static readonly object Permanent = new();
    private static readonly Dictionary<Guid, HashSet<object>> _owners = new();
    // Reverse index so a scene's disposal releases everything it locked in one pass.
    private static readonly Dictionary<Scene, HashSet<Guid>> _sceneLocks = new();
    private static readonly object _lockGate = new();

    /// <summary>Pin a GUID's family resident for as long as <paramref name="scene"/> is loaded.
    /// Released automatically when that scene disposes.</summary>
    public static void LockToScene(Guid assetId, Scene scene)
    {
        if (assetId == Guid.Empty || scene == null) return;
        assetId = Resolve(assetId);
        lock (_lockGate)
        {
            GetOrAddOwners(assetId).Add(scene);
            if (!_sceneLocks.TryGetValue(scene, out var guids))
                _sceneLocks[scene] = guids = new HashSet<Guid>();
            guids.Add(assetId);
        }
    }

    /// <summary>Pin a GUID's family resident indefinitely, until an explicit <see cref="Unlock"/>.</summary>
    public static void LockPermanent(Guid assetId)
    {
        if (assetId == Guid.Empty) return;
        assetId = Resolve(assetId);
        lock (_lockGate)
            GetOrAddOwners(assetId).Add(Permanent);
    }

    /// <summary>Release a permanent lock. Scene-scoped locks release themselves automatically.</summary>
    public static void Unlock(Guid assetId)
    {
        if (assetId == Guid.Empty) return;
        assetId = Resolve(assetId);
        lock (_lockGate)
        {
            if (_owners.TryGetValue(assetId, out var owners))
            {
                owners.Remove(Permanent);
                if (owners.Count == 0)
                    _owners.Remove(assetId);
            }
        }
    }

    /// <summary>True if anything (a scene, or a permanent lock) currently pins this GUID's family resident.</summary>
    public static bool IsLocked(Guid guid)
    {
        guid = Resolve(guid);
        lock (_lockGate)
            return _owners.TryGetValue(guid, out var owners) && owners.Count > 0;
    }

    /// <summary>Release every lock a scene holds. Called from <see cref="Scene.OnDispose"/>.</summary>
    internal static void ReleaseSceneLocks(Scene scene)
    {
        lock (_lockGate)
        {
            if (!_sceneLocks.Remove(scene, out var guids)) return;
            foreach (var guid in guids)
            {
                if (_owners.TryGetValue(guid, out var owners))
                {
                    owners.Remove(scene);
                    if (owners.Count == 0)
                        _owners.Remove(guid);
                }
            }
        }
    }

    private static HashSet<object> GetOrAddOwners(Guid guid)
    {
        if (!_owners.TryGetValue(guid, out var owners))
            _owners[guid] = owners = new HashSet<object>();
        return owners;
    }

    #endregion

    #region Test Helpers

    /// <summary>Test-only: drop all activity tracking and lock state.</summary>
    internal static void ClearForTests()
    {
        _lastTouched.Clear();
        _stackTraceCaptureEnabled.Clear();
        _lastTouchStackTraces.Clear();
        lock (_lockGate)
        {
            _owners.Clear();
            _sceneLocks.Clear();
        }
    }

    #endregion
}
