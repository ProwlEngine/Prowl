// This file is part of the Prowl Game Engine
// Licensed under the MIT License. See the LICENSE file in the project root for details.

using System;
using System.Buffers;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Reflection;
using System.Threading.Tasks;

namespace Prowl.Runtime.Events;

public class Event<T> where T : struct, Enum
{
    /// <summary>
    /// JIT-time cache: <c>true</c> when <typeparamref name="TArgs"/> implements
    /// <see cref="ICancellable"/>. Evaluated once per closed generic and stored in a
    /// static field, so the hot-path <see cref="Invoke{TArgs}"/> never boxes value-type
    /// args just to check the interface.
    /// </summary>
    private static class CancellableCheck<TArgs>
    {
        public static readonly bool IsCancellable = typeof(ICancellable).IsAssignableFrom(typeof(TArgs));
    }

#if EVENT_DEBUG
    /// <summary>
    /// Configurable threshold in milliseconds. Handlers exceeding this duration
    /// will be logged as warnings when EVENT_DEBUG is defined. Set to 0 to disable.
    /// </summary>
    public static double SlowHandlerThresholdMs { get; set; } = 200.0;
#endif

    private readonly T _eventType;
    public T EventType => _eventType;

    private readonly EventManager<T> _eventManager;
    public EventManager<T> EventManager => _eventManager;

    private readonly List<EventDelegateContainer<T>> _delegates = [];

    private readonly object _lock = new();

    /// <summary>
    /// Copy-on-write snapshot: a flat, priority-sorted array of all delegates.
    /// Rebuilt only when the subscriber list changes (Add/Remove), never on Invoke.
    /// Used by EVENT_DEBUG diagnostics to detect type-mismatch handlers.
    /// </summary>
    private EventDelegateContainer<T>[] _cachedSnapshot = [];

    /// <summary>
    /// Actual valid length of <see cref="_cachedSnapshot"/> when rented from ArrayPool.
    /// The rented array may be larger than needed; use this length for iteration.
    /// </summary>
    private int _cachedSnapshotLength = 0;

    /// <summary>
    /// Per-<c>TArgs</c> typed COW snapshots keyed by <see cref="Type"/>.
    /// Each value is a <c>EventDelegateContainer&lt;T, TArgs&gt;[]</c> stored as
    /// <see cref="object"/>.
    /// <para>
    /// <see cref="Invoke{TArgs}"/> retrieves the matching typed array with a single
    /// dictionary lookup and one array-reference cast — <b>no per-element type check</b>.
    /// </para>
    /// </summary>
    private readonly ConcurrentDictionary<Type, object> _typedSnapshots = new();

    /// <summary>
    /// Per-<c>TArgs</c> actual lengths of typed snapshots. Since typed arrays
    /// are rented from ArrayPool, they may be larger than needed.
    /// </summary>
    private readonly ConcurrentDictionary<Type, int> _typedSnapshotLengths = new();

    private volatile bool _enabled = true;
    public bool Enabled
    {
        get => _enabled;
        set => _enabled = value;
    }

    /// <summary>
    /// When greater than zero, snapshot rebuilds are deferred until the batch
    /// count returns to zero. Incremented by <see cref="BeginBatch"/> and
    /// decremented by <see cref="EndBatch"/>. Must only be accessed under <see cref="_lock"/>.
    /// </summary>
    private int _batchDepth;

    /// <summary>
    /// Tracks whether any Add/Remove occurred while batching was active,
    /// so that <see cref="EndBatch"/> knows whether a rebuild is needed.
    /// </summary>
    private bool _batchDirty;

    public Event(EventManager<T> eventManager, T eventType)
    {
        this._eventType = eventType;
        this._eventManager = eventManager;
    }

    /// <summary>
    /// Begins a batch operation. While batched, <see cref="Add"/> and <see cref="Remove"/>
    /// will not rebuild snapshots. Call <see cref="EndBatch"/> when finished to rebuild once.
    /// Calls may be nested; only the outermost <see cref="EndBatch"/> triggers the rebuild.
    /// </summary>
    public void BeginBatch()
    {
        lock (_lock)
        {
            _batchDepth++;
        }
    }

    /// <summary>
    /// Ends a batch operation. If this is the outermost batch and any mutations
    /// occurred, the COW snapshot is rebuilt exactly once.
    /// </summary>
    public void EndBatch()
    {
        lock (_lock)
        {
            if (_batchDepth > 0)
                _batchDepth--;

            if (_batchDepth == 0 && _batchDirty)
            {
                _batchDirty = false;
                RebuildSnapshot();
            }
        }
    }


    public void Invoke<TArgs>(TArgs args)
    {
        if (!Enabled) return;

            EventDelegateContainer<T, TArgs>[] typedSnapshot;
            int typedLength;
#if EVENT_DEBUG
            EventDelegateContainer<T>[] fullSnapshot;
            int fullLength;
#endif
            lock (_lock)
            {
                if (_typedSnapshots.TryGetValue(typeof(TArgs), out object? obj))
                {
                    typedSnapshot = (EventDelegateContainer<T, TArgs>[])obj;
                    typedLength = _typedSnapshotLengths[typeof(TArgs)];
                }
                else
                {
                    typedSnapshot = [];
                    typedLength = 0;
                }
#if EVENT_DEBUG
                fullSnapshot = _cachedSnapshot;
                fullLength = _cachedSnapshotLength;
#endif
            }

#if EVENT_DEBUG
            // Warn about handlers on this event registered with a different TArgs.
            if (fullLength > typedLength)
            {
                for (int j = 0; j < fullLength; j++)
                {
                    if (fullSnapshot[j] is not EventDelegateContainer<T, TArgs>)
                        WarnTypeMismatch<TArgs>(fullSnapshot[j]);
                }
            }

            double threshold = SlowHandlerThresholdMs;
            Stopwatch? sw = threshold > 0 ? Stopwatch.StartNew() : null;
#endif
            var span = typedSnapshot.AsSpan(0, typedLength);
            for (int j = 0; j < span.Length; j++)
            {
#if EVENT_DEBUG
                sw?.Restart();
#endif
                try
                {
                    span[j].Invoke(args);
                }
                catch (Exception ex)
                {
                    Console.WriteLine();
                }

#if EVENT_DEBUG
                if (sw is not null)
                {
                    sw.Stop();
                    double elapsed = sw.Elapsed.TotalMilliseconds;
                    if (elapsed > threshold)
                    {
                        EventSystemDiagnostics.LogWarning?.Invoke(
                            $"[EventSystem] Slow handler on {typeof(T).Name}.{_eventType}: " +
                            $"{elapsed:F2}ms (threshold {threshold:F1}ms). " +
                            $"Handler: {typedSnapshot[j].SourceDescription}");
                    }
                }
#endif

                if (CancellableCheck<TArgs>.IsCancellable && args is ICancellable { Cancelled: true })
                    break;
            }


    }

    /// <summary>
    /// Asynchronously invokes all handlers for the given <typeparamref name="TArgs"/>,
    /// awaiting async handlers (<see cref="IAsyncInvocable{TArgs}"/>) sequentially while
    /// calling synchronous handlers inline. Priority ordering and cancellation semantics
    /// are identical to <see cref="Invoke{TArgs}"/>.
    /// <para>
    /// Intended for editor/tool events where handlers legitimately need to perform I/O.
    /// Not recommended for the game-loop hot path — use <see cref="Invoke{TArgs}"/> instead.
    /// </para>
    /// </summary>
    public async Task InvokeAsync<TArgs>(TArgs args)
    {
        if (!Enabled) return;

        EventDelegateContainer<T, TArgs>[] typedSnapshot;
        int typedLength;
#if EVENT_DEBUG
        EventDelegateContainer<T>[] fullSnapshot;
        int fullLength;
#endif
        lock (_lock)
        {
            if (_typedSnapshots.TryGetValue(typeof(TArgs), out object? obj))
            {
                typedSnapshot = (EventDelegateContainer<T, TArgs>[])obj;
                typedLength = _typedSnapshotLengths[typeof(TArgs)];
            }
            else
            {
                typedSnapshot = [];
                typedLength = 0;
            }
#if EVENT_DEBUG
            fullSnapshot = _cachedSnapshot;
            fullLength = _cachedSnapshotLength;
#endif
        }

#if EVENT_DEBUG
        // Warn about handlers on this event registered with a different TArgs.
        if (fullLength > typedLength)
        {
            for (int j = 0; j < fullLength; j++)
            {
                if (fullSnapshot[j] is not EventDelegateContainer<T, TArgs>)
                    WarnTypeMismatch<TArgs>(fullSnapshot[j]);
            }
        }

        double threshold = SlowHandlerThresholdMs;
        Stopwatch? sw = threshold > 0 ? Stopwatch.StartNew() : null;
#endif

        for (int j = 0; j < typedLength; j++)
        {
#if EVENT_DEBUG
            sw?.Restart();
#endif
            try
            {
                if (typedSnapshot[j] is IAsyncInvocable<TArgs> asyncHandler)
                    await asyncHandler.InvokeAsync(args).ConfigureAwait(false);
                else
                    typedSnapshot[j].Invoke(args);
            }
            catch (Exception ex)
            {
                Console.WriteLine(ex.Message);
            }

#if EVENT_DEBUG
            if (sw is not null)
            {
                sw.Stop();
                double elapsed = sw.Elapsed.TotalMilliseconds;
                if (elapsed > threshold)
                {
                    EventSystemDiagnostics.LogWarning?.Invoke(
                        $"[EventSystem] Slow handler on {typeof(T).Name}.{_eventType}: " +
                        $"{elapsed:F2}ms (threshold {threshold:F1}ms). " +
                        $"Handler: {typedSnapshot[j].SourceDescription}");
                }
            }
#endif

            if (CancellableCheck<TArgs>.IsCancellable && args is ICancellable { Cancelled: true })
                break;
        }

    }

    /// <summary>
    /// Returns a read-only view of the currently registered handlers for the
    /// given <typeparamref name="TArgs"/> type, sorted by priority.
    /// The span references a COW snapshot — it is safe to read after the lock
    /// is released but may become stale if handlers are added or removed.
    /// </summary>
    public ReadOnlySpan<EventDelegateContainer<T, TArgs>> GetHandlers<TArgs>()
    {
        lock (_lock)
        {
            if (_typedSnapshots.TryGetValue(typeof(TArgs), out object? obj))
            {
                var array = (EventDelegateContainer<T, TArgs>[])obj;
                int length = _typedSnapshotLengths[typeof(TArgs)];
                return array.AsSpan(0, length);
            }
            return ReadOnlySpan<EventDelegateContainer<T, TArgs>>.Empty;
        }
    }

#if EVENT_DEBUG
    /// <summary>
    /// Logs a warning when a registered handler is skipped because its TArgs
    /// does not match the invoked type. Only compiled when EVENT_DEBUG is defined.
    /// </summary>
    private void WarnTypeMismatch<TArgs>(EventDelegateContainer<T> container)
    {
        // Extract the registered TArgs from the concrete generic type.
        Type containerType = container.GetType();
        Type? registeredArgs = null;
        if (containerType.IsGenericType && containerType.GenericTypeArguments.Length == 2)
            registeredArgs = containerType.GenericTypeArguments[1];

        EventSystemDiagnostics.LogWarning?.Invoke(
            $"[EventSystem] Type mismatch on {typeof(T).Name}.{_eventType}: " +
            $"handler registered for '{registeredArgs?.Name ?? "unknown"}' " +
            $"but invoked with '{typeof(TArgs).Name}'. Handler was skipped. " +
            $"(registered at {container.SourceDescription})");
    }
#endif

    public bool Add(EventDelegateContainer<T> eventDelegate, bool allowMultiple = false)
    {
        bool added = false;

        lock (_lock)
        {
            if (allowMultiple || !ContainsDelegate(eventDelegate.GetDelegate()))
            {
                // Binary search for insertion point to maintain sorted order by priority.
                // Uses upper-bound semantics so equal priorities preserve insertion order (stable).
                int lo = 0, hi = _delegates.Count;
                while (lo < hi)
                {
                    int mid = (lo + hi) >>> 1;
                    if (_delegates[mid].Priority.CompareTo(eventDelegate.Priority) <= 0)
                        lo = mid + 1;
                    else
                        hi = mid;
                }
                _delegates.Insert(lo, eventDelegate);
                eventDelegate.Link(this);
                added = true;
            }
            if (added)
            {
                if (_batchDepth > 0)
                    _batchDirty = true;
                else
                    RebuildSnapshot();
            }
            eventDelegate.Added = added;
            return added;
        }
    }

    public bool Remove(EventDelegateContainer<T> eventDelegate)
    {
        lock (_lock)
        {
            bool result = _delegates.Remove(eventDelegate);
            if (result)
            {
                eventDelegate.Unlink();
                if (_batchDepth > 0)
                    _batchDirty = true;
                else
                    RebuildSnapshot();
            }
            return result;
        }
    }

    /// <summary>
    /// Removes the first delegate container whose wrapped handler equals the
    /// specified <paramref name="handler"/>. Used by generated <c>-=</c> event accessors.
    /// </summary>
    public bool RemoveByDelegate(Delegate handler)
    {
        lock (_lock)
        {
            for (int i = 0; i < _delegates.Count; i++)
            {
                if (_delegates[i].MatchesDelegate(handler))
                {
                    EventDelegateContainer<T> container = _delegates[i];
                    _delegates.RemoveAt(i);
                    container.Unlink();
                    if (_batchDepth > 0)
                        _batchDirty = true;
                    else
                        RebuildSnapshot();
                    return true;
                }
            }
            return false;
        }
    }

    private bool ContainsDelegate(Delegate? handler)
    {
        if (handler == null) return false;
        for (int i = 0; i < _delegates.Count; i++)
        {
            if (_delegates[i].MatchesDelegate(handler)) return true;
        }
        return false;
    }

    public void EnableByTag(string tag)
    {
        lock (_lock)
        {
            foreach (var del in _delegates)
            {
                if (del.HasTag(tag)) del.Enable();
            }
        }
    }

    public void DisableByTag(string tag)
    {
        lock (_lock)
        {
            foreach (var del in _delegates)
            {
                if (del.HasTag(tag)) del.Disable();
            }
        }
    }

    public void RemoveByTag(string tag)
    {
        lock (_lock)
        {
            bool removed = false;
            for (int i = _delegates.Count - 1; i >= 0; i--)
            {
                if (_delegates[i].HasTag(tag))
                {
                    var container = _delegates[i];
                    _delegates.RemoveAt(i);
                    container.Unlink();
                    removed = true;
                }
            }

            if (removed)
            {
                if (_batchDepth > 0)
                    _batchDirty = true;
                else
                    RebuildSnapshot();
            }
        }
    }


    /// <summary>
    /// Rebuilds the flat, priority-sorted snapshot array and per-<c>TArgs</c> typed
    /// snapshot arrays from the current delegate list.
    /// Must be called under <see cref="_lock"/>.
    /// </summary>
    private void RebuildSnapshot()
    {
        int totalCount = _delegates.Count;

        if (totalCount == 0)
        {
            // Return old snapshot to pool before clearing
            if (_cachedSnapshot.Length > 0)
                ArrayPool<EventDelegateContainer<T>>.Shared.Return(_cachedSnapshot, clearArray: true);

            _cachedSnapshot = [];
            _cachedSnapshotLength = 0;
            _typedSnapshots.Clear();
            _typedSnapshotLengths.Clear();
            return;
        }

        // Return old snapshot to pool before renting new one
        if (_cachedSnapshot.Length > 0)
            ArrayPool<EventDelegateContainer<T>>.Shared.Return(_cachedSnapshot, clearArray: true);

        // Rent from ArrayPool instead of allocating
        EventDelegateContainer<T>[] snapshot = ArrayPool<EventDelegateContainer<T>>.Shared.Rent(totalCount);
        for (int i = 0; i < totalCount; i++)
            snapshot[i] = _delegates[i];
        _cachedSnapshot = snapshot;
        _cachedSnapshotLength = totalCount;

        RebuildTypedSnapshots();
    }

    /// <summary>
    /// Rebuilds per-<c>TArgs</c> typed snapshot arrays from the priority-sorted
    /// delegate buckets.  Each resulting array is a properly typed
    /// <c>EventDelegateContainer&lt;T, TArgs&gt;[]</c>, enabling
    /// <see cref="Invoke{TArgs}"/> to iterate with direct method calls and
    /// zero per-element type checks.
    /// Must be called under <see cref="_lock"/>.
    /// </summary>
    private void RebuildTypedSnapshots()
    {
        // Return all old typed snapshots to their respective pools
        foreach (var kvp in _typedSnapshots)
        {
            Type argsType = kvp.Key;
            if (!s_arrayReturnMethods.TryGetValue(argsType, out Action<object>? returnMethod))
            {
                returnMethod = CreateArrayReturnMethod(argsType);
                s_arrayReturnMethods[argsType] = returnMethod;
            }
            returnMethod(kvp.Value);
        }

        _typedSnapshots.Clear();
        _typedSnapshotLengths.Clear();

        // First pass: collect containers per ArgsType, maintaining priority order.
        Dictionary<Type, List<EventDelegateContainer<T>>>? groups = null;

        for (int i = 0; i < _delegates.Count; i++)
        {
            EventDelegateContainer<T> container = _delegates[i];
            Type argsType = container.ArgsType;

            groups ??= new Dictionary<Type, List<EventDelegateContainer<T>>>();
            if (!groups.TryGetValue(argsType, out List<EventDelegateContainer<T>>? list))
            {
                list = new List<EventDelegateContainer<T>>();
                groups[argsType] = list;
            }
            list.Add(container);
        }

        if (groups is null)
            return;

        // Second pass: create properly typed arrays via cached generic delegates,
        // avoiding Array.CreateInstance + per-element SetValue overhead.
        foreach (KeyValuePair<Type, List<EventDelegateContainer<T>>> entry in groups)
        {
            if (!s_arrayBuilders.TryGetValue(entry.Key, out Func<List<EventDelegateContainer<T>>, object>? builder))
            {
                builder = CreateArrayBuilder(entry.Key);
                s_arrayBuilders[entry.Key] = builder;
            }
            _typedSnapshots[entry.Key] = builder(entry.Value);
            _typedSnapshotLengths[entry.Key] = entry.Value.Count;
        }
    }

    /// <summary>
    /// Generic helper invoked through a cached delegate.  Creates a strongly typed
    /// array and populates it with simple reference casts — no <see cref="Array.SetValue(object, int)"/>
    /// overhead.
    /// </summary>
    private static object BuildTypedArray<TArgs>(List<EventDelegateContainer<T>> list)
    {
        // Rent from ArrayPool instead of allocating
        EventDelegateContainer<T, TArgs>[] result = ArrayPool<EventDelegateContainer<T, TArgs>>.Shared.Rent(list.Count);
        for (int i = 0; i < list.Count; i++)
            result[i] = (EventDelegateContainer<T, TArgs>)list[i];
        return result;
    }

    /// <summary>
    /// Generic helper invoked through a cached delegate. Returns a rented typed array
    /// to its <c>ArrayPool&lt;EventDelegateContainer&lt;T, TArgs&gt;&gt;.Shared</c>.
    /// </summary>
    private static void ReturnTypedArray<TArgs>(object array)
    {
        ArrayPool<EventDelegateContainer<T, TArgs>>.Shared.Return(
            (EventDelegateContainer<T, TArgs>[])array, clearArray: true);
    }

    /// <summary>
    /// Creates and returns a delegate that calls <see cref="BuildTypedArray{TArgs}"/>
    /// closed over the given <paramref name="argsType"/>.  The reflection cost is paid
    /// once; subsequent rebuilds reuse the cached delegate.
    /// </summary>
    private static Func<List<EventDelegateContainer<T>>, object> CreateArrayBuilder(Type argsType)
    {
        MethodInfo openMethod = typeof(Event<T>)
            .GetMethod(nameof(BuildTypedArray), BindingFlags.NonPublic | BindingFlags.Static)!;
#pragma warning disable IL3050 // MakeGenericMethod: the closed generic is only over reference-compatible types already loaded.
        MethodInfo closedMethod = openMethod.MakeGenericMethod(argsType);
#pragma warning restore IL3050
        return (Func<List<EventDelegateContainer<T>>, object>)
            Delegate.CreateDelegate(typeof(Func<List<EventDelegateContainer<T>>, object>), closedMethod);
    }

    /// <summary>
    /// Creates and returns a delegate that calls <see cref="ReturnTypedArray{TArgs}"/>
    /// closed over the given <paramref name="argsType"/>. Used to return rented arrays
    /// to the appropriate ArrayPool instance.
    /// </summary>
    private static Action<object> CreateArrayReturnMethod(Type argsType)
    {
        MethodInfo openMethod = typeof(Event<T>)
            .GetMethod(nameof(ReturnTypedArray), BindingFlags.NonPublic | BindingFlags.Static)!;
#pragma warning disable IL3050 // MakeGenericMethod: the closed generic is only over reference-compatible types already loaded.
        MethodInfo closedMethod = openMethod.MakeGenericMethod(argsType);
#pragma warning restore IL3050
        return (Action<object>)
            Delegate.CreateDelegate(typeof(Action<object>), closedMethod);
    }

    /// <summary>
    /// Per-<c>TArgs</c> cached factory delegates that build strongly typed
    /// <c>EventDelegateContainer&lt;T, TArgs&gt;[]</c> from a list of base containers.
    /// Avoids repeated <see cref="Array.CreateInstance(Type, int)"/> and per-element
    /// <see cref="Array.SetValue(object, int)"/> overhead.
    /// </summary>
    private static readonly ConcurrentDictionary<Type, Func<List<EventDelegateContainer<T>>, object>> s_arrayBuilders = new();

    /// <summary>
    /// Per-<c>TArgs</c> cached methods that return rented arrays to the appropriate
    /// <c>ArrayPool&lt;EventDelegateContainer&lt;T, TArgs&gt;&gt;.Shared</c>.
    /// </summary>
    private static readonly ConcurrentDictionary<Type, Action<object>> s_arrayReturnMethods = new();
}
