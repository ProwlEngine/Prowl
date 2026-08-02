// This file is part of the Prowl Game Engine
// Licensed under the MIT License. See the LICENSE file in the project root for details.

using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Threading;

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

    /// <summary>Mutable timestamp cell. Storing a box rather than the value itself keeps the hot path
    /// lock-free: ConcurrentDictionary's indexer setter takes the bucket's monitor on every write,
    /// whereas TryGetValue never locks, so a repeat touch becomes a lookup plus a plain store.</summary>
    private sealed class Stamp { public long Tick; }

    // A GUID is "idle" purely based on time since last touch - no GC involved. Touched by
    // AssetRef.Res/.Touch, AssetDatabase.Touch, and EngineObject.EnsureNotDisposed.
    // Timestamps are Environment.TickCount64 milliseconds, not DateTime: only elapsed time matters
    // here, and reading the tick count is several times cheaper than DateTime.UtcNow.
    private static readonly ConcurrentDictionary<Guid, Stamp> _lastTouched = new();

    // Anchors tick counts to wall-clock for TryGetLastTouched, which reports an absolute time.
    private static readonly DateTime _epochUtc = DateTime.UtcNow;
    private static readonly long _epochTick = Environment.TickCount64;

    private const long ForceIdleBackdateMs = 24L * 60 * 60 * 1000;

    // Opt-in per GUID (see the Asset Database panel's "Track" action) - capturing a stack trace on
    // every touch of every asset would be far too expensive to do unconditionally.
    private static readonly ConcurrentDictionary<Guid, byte> _stackTraceCaptureEnabled = new();
    private static readonly ConcurrentDictionary<Guid, string> _lastTouchStackTraces = new();
    // Lets Touch skip the _stackTraceCaptureEnabled lookup entirely while nothing is being tracked,
    // which is almost always. That lookup was a fifth of the cost of a touch.
    private static int _stackTraceCaptureCount;

    /// <summary>Maps a GUID to the GUID whose lifecycle it's actually tied to (a sub-asset resolves
    /// to its parent). Set by EditorAssetDatabase.Initialize(); null elsewhere, where a GUID resolves
    /// to itself.</summary>
    internal static Func<Guid, Guid>? ResolveFamily;

    internal static Guid Resolve(Guid guid) => ResolveFamily != null ? ResolveFamily(guid) : guid;

    /// <summary>Record that a GUID's family was just used. No-op for <see cref="Guid.Empty"/>.
    /// <para>Callers that read an asset repeatedly should prefer <see cref="EngineObject.TouchAsset"/>,
    /// which coalesces so a hot property getter doesn't reach this at all.</para></summary>
    public static void Touch(Guid guid)
    {
        if (guid == Guid.Empty) return;
        guid = Resolve(guid);
        GetOrAddStamp(guid).Tick = Environment.TickCount64;

        if (Volatile.Read(ref _stackTraceCaptureCount) != 0 && _stackTraceCaptureEnabled.ContainsKey(guid))
            _lastTouchStackTraces[guid] = new System.Diagnostics.StackTrace(1, true).ToString();
    }

    private static Stamp GetOrAddStamp(Guid guid)
        => _lastTouched.TryGetValue(guid, out Stamp? stamp)
            ? stamp
            : _lastTouched.GetOrAdd(guid, static _ => new Stamp());

    /// <summary>Enable/disable capturing a stack trace on every future touch of a GUID's family.
    /// Disabling also drops any previously captured trace.</summary>
    internal static void SetStackTraceCapture(Guid guid, bool enabled)
    {
        guid = Resolve(guid);
        if (enabled)
        {
            if (_stackTraceCaptureEnabled.TryAdd(guid, 0))
                Interlocked.Increment(ref _stackTraceCaptureCount);
        }
        else
        {
            if (_stackTraceCaptureEnabled.TryRemove(guid, out _))
                Interlocked.Decrement(ref _stackTraceCaptureCount);
            _lastTouchStackTraces.TryRemove(guid, out _);
        }
    }

    internal static bool IsCapturingStackTrace(Guid guid) => _stackTraceCaptureEnabled.ContainsKey(Resolve(guid));

    internal static bool TryGetLastTouchStackTrace(Guid guid, out string trace)
        => _lastTouchStackTraces.TryGetValue(Resolve(guid), out trace!);

    /// <summary>True if a GUID's family has gone at least <paramref name="threshold"/> since its last touch.</summary>
    internal static bool IsIdle(Guid guid, TimeSpan threshold)
    {
        if (!_lastTouched.TryGetValue(Resolve(guid), out Stamp? stamp)) return true;
        return Environment.TickCount64 - stamp.Tick >= (long)threshold.TotalMilliseconds;
    }

    internal static bool TryGetLastTouched(Guid guid, out DateTime lastTouched)
    {
        if (!_lastTouched.TryGetValue(Resolve(guid), out Stamp? stamp))
        {
            lastTouched = default;
            return false;
        }
        lastTouched = _epochUtc.AddMilliseconds(stamp.Tick - _epochTick);
        return true;
    }

    /// <summary>Drop tracking for a GUID's family once it's evicted/disposed.</summary>
    internal static void Forget(Guid guid) => _lastTouched.TryRemove(Resolve(guid), out _);

    /// <summary>Test-only: make a GUID's family appear idle regardless of real elapsed time.</summary>
    internal static void ForceIdle(Guid guid)
        => GetOrAddStamp(Resolve(guid)).Tick = Environment.TickCount64 - ForceIdleBackdateMs;

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
        Volatile.Write(ref _stackTraceCaptureCount, 0);
        lock (_lockGate)
        {
            _owners.Clear();
            _sceneLocks.Clear();
        }
    }

    #endregion
}
