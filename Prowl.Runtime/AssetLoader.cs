// This file is part of the Prowl Game Engine
// Licensed under the MIT License. See the LICENSE file in the project root for details.

using System;
using System.Collections.Generic;
using System.Threading;

namespace Prowl.Runtime;

/// <summary>
/// Single dedicated background thread that deserializes assets off the main/render thread,
/// so a scene can appear immediately while its meshes/textures stream in.
///
/// <para>Mirrors the render-thread design: one long-lived background thread draining a queue.
/// Requests are de-duplicated and split into a High-priority queue (blocking
/// <see cref="AssetRef{T}.EnsureLoaded"/> calls) drained before the Normal queue
/// (fire-and-forget <see cref="AssetRef{T}.Res"/> resolves).</para>
///
/// <para>The actual deserialize runs via <see cref="AssetDatabase.Get"/> on this thread.
/// GPU resources created during load are safe because <c>Graphics.Submit</c> is thread-safe
/// and the render thread drains continuously.</para>
/// </summary>
public static class AssetLoader
{
    private static readonly object _lock = new();
    private static readonly Queue<Guid> _high = new();
    private static readonly Queue<Guid> _normal = new();

    // Ids currently queued or in-flight (dedup so an asset is only loaded once at a time).
    private static readonly HashSet<Guid> _queued = new();

    // Completion events for callers blocked in LoadBlocking, keyed by asset id.
    private static readonly Dictionary<Guid, ManualResetEventSlim> _waiters = new();

    // Counts pending items so the loop sleeps when idle instead of spinning.
    private static readonly SemaphoreSlim _signal = new(0);

    private static readonly object _startLock = new();
    private static Thread? _thread;
    private static volatile bool _running;

    // True only on the loader thread itself. Lets LoadBlocking detect a re-entrant blocking
    // load (e.g. a deserialize that calls EnsureLoaded on a dependency) and load it inline
    // instead of enqueueing and waiting on the single loader thread which would deadlock.
    [ThreadStatic] private static bool _isLoaderThread;

    /// <summary>Queue a non-blocking background load. No-op if already cached/queued/in-flight.</summary>
    public static void Request(Guid id)
    {
        if (id == Guid.Empty) return;
        EnsureStarted();

        lock (_lock)
        {
            if (!_queued.Add(id)) return; // already queued or in-flight
            _normal.Enqueue(id);
        }
        _signal.Release();
    }

    /// <summary>
    /// Load <paramref name="id"/> with priority and block until it finishes, returning the
    /// instance (or null if it couldn't be loaded). Used by <see cref="AssetRef{T}.EnsureLoaded"/>.
    /// </summary>
    public static EngineObject? LoadBlocking(Guid id)
    {
        if (id == Guid.Empty) return null;

        // Fast path: already loaded.
        var cached = AssetDatabase.GetCached(id);
        if (cached != null) return cached;

        // Re-entrant from the loader thread (a deserialize asked to block on a dependency):
        // load inline on this thread. Enqueueing would wait on the only thread that drains.
        if (_isLoaderThread)
            return AssetDatabase.Get(id);

        EnsureStarted();

        ManualResetEventSlim ev;
        bool enqueued = false;
        lock (_lock)
        {
            if (!_waiters.TryGetValue(id, out ev!))
            {
                ev = new ManualResetEventSlim(false);
                _waiters[id] = ev;
            }
            // Enqueue at high priority only if not already queued/in-flight. If it's already
            // queued at normal priority we still wait on the same completion event.
            if (_queued.Add(id))
            {
                _high.Enqueue(id);
                enqueued = true;
            }
        }
        if (enqueued) _signal.Release();

        ev.Wait();
        return AssetDatabase.GetCached(id);
    }

    private static void EnsureStarted()
    {
        if (_running) return;
        lock (_startLock)
        {
            if (_running) return;
            _running = true;
            _thread = new Thread(Loop)
            {
                IsBackground = true,
                Name = "Prowl Asset Loader",
            };
            _thread.Start();
        }
    }

    private static void Loop()
    {
        _isLoaderThread = true;
        while (_running)
        {
            _signal.Wait();
            if (!_running) break;

            Guid id;
            lock (_lock)
            {
                if (_high.Count > 0) id = _high.Dequeue();
                else if (_normal.Count > 0) id = _normal.Dequeue();
                else continue; // spurious wake (e.g. Stop), nothing to do
            }

            EngineObject? result = null;
            try { result = AssetDatabase.Get(id); }
            catch (Exception ex) { Debug.LogError($"[AssetLoader] Failed to load asset {id}: {ex}"); }

            // result is unused directly: AssetDatabase.Get caches it; waiters re-peek the cache.
            _ = result;

            ManualResetEventSlim? ev;
            lock (_lock)
            {
                _queued.Remove(id);
                if (_waiters.TryGetValue(id, out ev))
                    _waiters.Remove(id);
            }
            ev?.Set();
        }
    }

    /// <summary>Stop the loader thread and release any blocked callers. Called at shutdown.</summary>
    public static void Stop()
    {
        if (!_running) return;
        _running = false;
        _signal.Release(); // wake the loop so it observes _running == false

        var t = _thread;
        _thread = null;
        try { t?.Join(2000); } catch { }

        // Release anyone still blocked so they don't hang; clear pending work.
        lock (_lock)
        {
            _high.Clear();
            _normal.Clear();
            _queued.Clear();
            foreach (var ev in _waiters.Values) ev.Set();
            _waiters.Clear();
        }
    }
}
