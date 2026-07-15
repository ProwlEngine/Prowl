// This file is part of the Prowl Game Engine
// Licensed under the MIT License. See the LICENSE file in the project root for details.

using System;
using System.Collections.Concurrent;
using System.Collections.Generic;

using Prowl.Runtime.Resources;

namespace Prowl.Runtime;

/// <summary>
/// Interface for resolving engine objects by their asset ID.
/// Implement this to provide asset storage and retrieval (e.g., from disk, from a content pipeline, etc.).
/// </summary>
public interface IAssetDatabase
{
    /// <summary>
    /// Retrieves an <see cref="EngineObject"/> by its asset ID. May block while the asset
    /// is deserialized (and, in the editor, imported on demand). Called on the
    /// <see cref="AssetLoader"/> background thread for async loads, and directly on the
    /// calling thread when async loading is disabled. Returns null if not found.
    /// </summary>
    EngineObject? Get(Guid assetId);

    /// <summary>
    /// Non-blocking cache peek: returns the already-loaded instance for <paramref name="assetId"/>
    /// or null if it isn't loaded yet. MUST NOT deserialize, import, or block. Safe to call
    /// from any thread.
    /// </summary>
    EngineObject? GetCached(Guid assetId);
}

/// <summary>
/// Global asset access: resolving assets by GUID, tracking activity for idle-timeout eviction,
/// and pinning assets that must stay resident despite not being touched.
/// </summary>
public static class AssetDatabase
{
    #region Resolution

    /// <summary>The current asset database implementation. Set this before serializing/deserializing
    /// objects that contain asset references.</summary>
    public static IAssetDatabase? Current { get; set; }

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
