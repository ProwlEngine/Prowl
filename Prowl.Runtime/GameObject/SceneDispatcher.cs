// This file is part of the Prowl Game Engine
// Licensed under the MIT License. See the LICENSE file in the project root for details.

using System;
using System.Collections.Generic;
using System.Runtime.CompilerServices;

using Prowl.Ember;
using Prowl.PaperUI;
using Prowl.Runtime.Rendering;

namespace Prowl.Runtime;

/// <summary>
/// Which optional <see cref="MonoBehaviour"/> callbacks a component actually overrides. Resolved once per
/// instance and cached on it, so neither the per-frame loops nor the physics events pay a type lookup.
/// </summary>
[Flags]
internal enum SceneCallbacks
{
    None = 0,

    Start = 1 << 0,
    Update = 1 << 1,
    LateUpdate = 1 << 2,
    FixedUpdate = 1 << 3,
    RenderCollect = 1 << 4,
    DrawGizmos = 1 << 5,
    OnGui = 1 << 6,

    CollisionBegin = 1 << 7,
    CollisionEnd = 1 << 8,
    TriggerEnter = 1 << 9,
    TriggerStay = 1 << 10,
    TriggerExit = 1 << 11,

    /// <summary>Everything the per-frame loops dispatch. A component with none of these is never registered.</summary>
    AnyFrame = Start | Update | LateUpdate | FixedUpdate | RenderCollect | DrawGizmos | OnGui,

    /// <summary>
    /// Set once this instance's set has been worked out. The unresolved state is deliberately the default
    /// value rather than a sentinel: a component's fields can arrive zeroed after a hot reload without the
    /// engine assembly's IL being available to replay an initializer, and a sentinel that depended on that
    /// would silently decay into "overrides nothing".
    /// </summary>
    Resolved = 1 << 30,
}

/// <summary>
/// The scene's single dispatch point for component callbacks: the per-frame loops (Start, Update, LateUpdate,
/// FixedUpdate, OnRenderCollect, DrawGizmos, OnGui) and the sparse physics events (collision and trigger).
/// Both used to be separate types doing the same job at different granularities, and the physics half paid a
/// type lookup per component per event, which is the wrong cost to carry on a contact callback.
/// </summary>
/// <remarks>
/// Three things keep this cheap. A component's callback set is resolved once and cached on the instance, so a
/// dispatch test is a field read and a mask AND rather than a dictionary probe. Registration is a dense array
/// with swap-back removal, so enabling or destroying a component is constant time instead of a scan per
/// channel. And a channel's execution-ordered array is rebuilt at most once per frame no matter how many
/// components joined or left, rather than re-sorted on every membership change.
///
/// Across a hot reload the walk migrates the component references in place, then <see cref="Reset"/> drops
/// every derived answer so membership and ordering are re-derived from the new types: a component that newly
/// overrides a callback starts dispatching, and one that dropped it stops.
/// </remarks>
internal sealed class SceneDispatcher
{
    // ---- per-instance callback resolution ------------------------------------------------------------

    // Which callbacks a concrete component type overrides. One entry per type, cleared on hot reload.
    private static readonly ReloadCache<Type, SceneCallbacks> s_byType = new(Compute);

    private static SceneCallbacks Compute(Type type)
    {
        SceneCallbacks callbacks = SceneCallbacks.None;

        if (Overrides(type, nameof(MonoBehaviour.Start))) callbacks |= SceneCallbacks.Start;
        if (Overrides(type, nameof(MonoBehaviour.Update))) callbacks |= SceneCallbacks.Update;
        if (Overrides(type, nameof(MonoBehaviour.LateUpdate))) callbacks |= SceneCallbacks.LateUpdate;
        if (Overrides(type, nameof(MonoBehaviour.FixedUpdate))) callbacks |= SceneCallbacks.FixedUpdate;
        if (Overrides(type, nameof(MonoBehaviour.OnRenderCollect))) callbacks |= SceneCallbacks.RenderCollect;
        if (Overrides(type, nameof(MonoBehaviour.DrawGizmos))) callbacks |= SceneCallbacks.DrawGizmos;
        if (Overrides(type, nameof(MonoBehaviour.OnGui))) callbacks |= SceneCallbacks.OnGui;

        if (Overrides(type, nameof(MonoBehaviour.OnCollisionBegin))) callbacks |= SceneCallbacks.CollisionBegin;
        if (Overrides(type, nameof(MonoBehaviour.OnCollisionEnd))) callbacks |= SceneCallbacks.CollisionEnd;
        if (Overrides(type, nameof(MonoBehaviour.OnTriggerEnter))) callbacks |= SceneCallbacks.TriggerEnter;
        if (Overrides(type, nameof(MonoBehaviour.OnTriggerStay))) callbacks |= SceneCallbacks.TriggerStay;
        if (Overrides(type, nameof(MonoBehaviour.OnTriggerExit))) callbacks |= SceneCallbacks.TriggerExit;

        return callbacks;
    }

    private static bool Overrides(Type type, string method)
        => RuntimeUtils.OverridesVirtual(type, method, typeof(MonoBehaviour));

    /// <summary>
    /// The component's callback set, resolved from its type the first time it is asked for and cached on the
    /// instance. The cache field is opted out of hot reload, so a replaced instance arrives unresolved and
    /// picks up whatever its new type overrides.
    /// </summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    internal static SceneCallbacks CallbacksOf(MonoBehaviour c)
    {
        SceneCallbacks cached = c._callbacks;
        return (cached & SceneCallbacks.Resolved) != 0 ? cached : Resolve(c);
    }

    [MethodImpl(MethodImplOptions.NoInlining)]
    private static SceneCallbacks Resolve(MonoBehaviour c)
        => c._callbacks = s_byType[c.GetType()] | SceneCallbacks.Resolved;

    // ---- registration --------------------------------------------------------------------------------

    private MonoBehaviour[] _registered = new MonoBehaviour[64];
    private int _count;
    private int _sequence;

    private readonly Channel _start = new(SceneCallbacks.Start);
    private readonly Channel _update = new(SceneCallbacks.Update);
    private readonly Channel _lateUpdate = new(SceneCallbacks.LateUpdate);
    private readonly Channel _fixedUpdate = new(SceneCallbacks.FixedUpdate);
    private readonly Channel _renderCollect = new(SceneCallbacks.RenderCollect);
    private readonly Channel _drawGizmos = new(SceneCallbacks.DrawGizmos);
    private readonly Channel _onGui = new(SceneCallbacks.OnGui);

    /// <summary>
    /// Starts dispatching a component's per-frame callbacks. Called whenever it becomes enabled in an active
    /// scene; the per-tick gameplay gate decides whether they actually run.
    /// </summary>
    public void Register(MonoBehaviour c)
    {
        if (c._dispatchSlot != 0) return;

        SceneCallbacks callbacks = CallbacksOf(c);

        // A component with no per-frame callback is never in the arrays at all, so it costs nothing to have
        // and never lengthens a channel rebuild. Its physics callbacks still dispatch, from the mask alone.
        if ((callbacks & SceneCallbacks.AnyFrame) == 0) return;

        if (_count == _registered.Length)
            Array.Resize(ref _registered, _registered.Length * 2);

        c._dispatchOrder = RuntimeUtils.GetExecutionOrder(c);
        c._dispatchSequence = ++_sequence;
        c._dispatchSlot = _count + 1;
        _registered[_count++] = c;

        MarkDirty(callbacks);
    }

    /// <summary>Stops dispatching a component's per-frame callbacks. Constant time.</summary>
    public void Unregister(MonoBehaviour c)
    {
        int slot = c._dispatchSlot;
        if (slot == 0) return;

        // Swap the tail into the hole. Ordering is carried by the sequence number, not by array position,
        // so moving an entry cannot change dispatch order.
        int index = slot - 1;
        int last = --_count;
        MonoBehaviour moved = _registered[last];
        _registered[index] = moved;
        moved._dispatchSlot = index + 1;
        _registered[last] = null!;

        c._dispatchSlot = 0;
        MarkDirty(CallbacksOf(c));
    }

    /// <summary>
    /// Drops every derived answer: registration, channel membership, and each component's cached callback set
    /// and ordering. The caller re-registers from the live scene afterwards.
    /// </summary>
    public void Reset()
    {
        for (int i = 0; i < _count; i++)
        {
            MonoBehaviour c = _registered[i];
            if (c is null) continue;

            c._dispatchSlot = 0;
            c._callbacks = default; // unresolved, so the new type decides membership
            _registered[i] = null!;
        }

        _count = 0;
        _sequence = 0;

        _start.Clear(); _update.Clear(); _lateUpdate.Clear(); _fixedUpdate.Clear();
        _renderCollect.Clear(); _drawGizmos.Clear(); _onGui.Clear();
    }

    private void MarkDirty(SceneCallbacks callbacks)
    {
        if ((callbacks & SceneCallbacks.Start) != 0) _start.Dirty = true;
        if ((callbacks & SceneCallbacks.Update) != 0) _update.Dirty = true;
        if ((callbacks & SceneCallbacks.LateUpdate) != 0) _lateUpdate.Dirty = true;
        if ((callbacks & SceneCallbacks.FixedUpdate) != 0) _fixedUpdate.Dirty = true;
        if ((callbacks & SceneCallbacks.RenderCollect) != 0) _renderCollect.Dirty = true;
        if ((callbacks & SceneCallbacks.DrawGizmos) != 0) _drawGizmos.Dirty = true;
        if ((callbacks & SceneCallbacks.OnGui) != 0) _onGui.Dirty = true;
    }

    // ---- one callback's execution-ordered membership -------------------------------------------------

    /// <summary>
    /// The components subscribed to one callback, in execution order. Rebuilt lazily, so a frame that enables
    /// a thousand components pays one rebuild rather than a thousand re-sorts, and iterating the rebuilt array
    /// keeps a loop safe against a callback enabling or disabling something mid-dispatch.
    /// </summary>
    private sealed class Channel
    {
        private readonly SceneCallbacks _bit;
        private MonoBehaviour[] _items = Array.Empty<MonoBehaviour>();
        private int _count;

        public Channel(SceneCallbacks bit) => _bit = bit;

        public bool Dirty = true;

        public void Clear()
        {
            Array.Clear(_items, 0, _count);
            _count = 0;
            Dirty = true;
        }

        public int Snapshot(SceneDispatcher owner, out MonoBehaviour[] items)
        {
            if (Dirty) Rebuild(owner);
            items = _items;
            return _count;
        }

        private void Rebuild(SceneDispatcher owner)
        {
            MonoBehaviour[] source = owner._registered;
            int sourceCount = owner._count;

            if (_items.Length < sourceCount)
                _items = new MonoBehaviour[Math.Max(sourceCount, 8)];

            int n = 0;
            bool skipStarted = _bit == SceneCallbacks.Start;

            for (int i = 0; i < sourceCount; i++)
            {
                MonoBehaviour c = source[i];
                if ((c._callbacks & _bit) == 0) continue;

                // A component only ever starts once, so it leaves this channel for good afterwards.
                if (skipStarted && c.HasStarted) continue;

                _items[n++] = c;
            }

            Array.Clear(_items, n, _items.Length - n);
            _count = n;

            if (n > 1) Array.Sort(_items, 0, n, DispatchOrder.Instance);
            Dirty = false;
        }
    }

    /// <summary>
    /// Execution order first, then registration order. Comparing two cached ints keeps the sort off the
    /// reflection path entirely, and the sequence tie-break makes the result independent of array position,
    /// which is what lets registration use swap-back removal.
    /// </summary>
    private sealed class DispatchOrder : IComparer<MonoBehaviour>
    {
        public static readonly DispatchOrder Instance = new();

        public int Compare(MonoBehaviour? a, MonoBehaviour? b)
        {
            int order = a!._dispatchOrder.CompareTo(b!._dispatchOrder);
            return order != 0 ? order : a._dispatchSequence.CompareTo(b._dispatchSequence);
        }
    }

    // ---- per-frame gameplay callbacks ----------------------------------------------------------------

    public void RunStart()
    {
        int count = _start.Snapshot(this, out MonoBehaviour[] items);
        bool anyStarted = false;

        for (int i = 0; i < count; i++)
        {
            MonoBehaviour c = items[i];
            if (c.IsDisposed || c.HasStarted || !c.EnabledInHierarchy) continue;

            c.InternalStart();
            anyStarted |= c.HasStarted;
        }

        // Started components drop out on the next rebuild. Removing them one at a time here is what made
        // bringing a scene up quadratic.
        if (anyStarted) _start.Dirty = true;
    }

    public void RunUpdate()
    {
        int count = _update.Snapshot(this, out MonoBehaviour[] items);
        for (int i = 0; i < count; i++)
        {
            MonoBehaviour c = items[i];
            if (!c.IsDisposed && c.EnabledInHierarchy) c.InternalUpdate();
        }
    }

    public void RunLateUpdate()
    {
        int count = _lateUpdate.Snapshot(this, out MonoBehaviour[] items);
        for (int i = 0; i < count; i++)
        {
            MonoBehaviour c = items[i];
            if (!c.IsDisposed && c.EnabledInHierarchy) c.InternalLateUpdate();
        }
    }

    public void RunFixedUpdate()
    {
        int count = _fixedUpdate.Snapshot(this, out MonoBehaviour[] items);
        for (int i = 0; i < count; i++)
        {
            MonoBehaviour c = items[i];
            if (!c.IsDisposed && c.EnabledInHierarchy) c.InternalFixedUpdate();
        }
    }

    // ---- rendering, gizmo and GUI callbacks (run in edit mode too, so not gameplay gated) ------------

    public void RunRenderCollect(Camera camera, List<IRenderable> renderables, List<IRenderableLight> lights)
    {
        int count = _renderCollect.Snapshot(this, out MonoBehaviour[] items);
        for (int i = 0; i < count; i++)
        {
            MonoBehaviour c = items[i];
            if (c.IsDisposed || !c.EnabledInHierarchy) continue;

            try { c.OnRenderCollect(camera, renderables, lights); }
            catch (Exception ex) { Report(c, nameof(MonoBehaviour.OnRenderCollect), ex); }
        }
    }

    public void RunDrawGizmos()
    {
        int count = _drawGizmos.Snapshot(this, out MonoBehaviour[] items);
        for (int i = 0; i < count; i++)
        {
            MonoBehaviour c = items[i];
            if (c.IsDisposed || !c.EnabledInHierarchy || (c.HideFlags & HideFlags.NoGizmos) != 0) continue;

            try { c.DrawGizmos(); }
            catch (Exception ex) { Report(c, nameof(MonoBehaviour.DrawGizmos), ex); }
        }
    }

    public void RunOnGui(Paper paper)
    {
        int count = _onGui.Snapshot(this, out MonoBehaviour[] items);
        for (int i = 0; i < count; i++)
        {
            MonoBehaviour c = items[i];
            if (c.IsDisposed || !c.EnabledInHierarchy) continue;

            try { c.OnGui(paper); }
            catch (Exception ex) { Report(c, nameof(MonoBehaviour.OnGui), ex); }
        }
    }

    private static void Report(MonoBehaviour c, string callback, Exception ex)
        => Debug.LogError($"[{(c.GameObject.IsValid() ? c.GameObject.Name : null)}/{c.GetType().Name}] {callback} threw: {ex.Message}\n{ex.StackTrace}");

    // ---- sparse physics events -----------------------------------------------------------------------
    //
    // These fire per contact, so the whole point is to get out cheaply. Recipients are resolved from the
    // GameObject's live component list rather than stored as delegates, which is also what lets them survive
    // a hot reload for free: the list is repointed to the migrated instances and the masks are re-resolved.

    public static void CollisionBegin(GameObject go, Rigidbody3D other, Rigidbody3D.ContactInfo contact)
    {
        if (go is null) return;

        int count = Collect(go, SceneCallbacks.CollisionBegin, out MonoBehaviour single, out MonoBehaviour[]? many);
        if (count == 0) return;
        if (count == 1) { single.InternalOnCollisionBegin(other, contact); return; }

        try { for (int i = 0; i < count; i++) many![i].InternalOnCollisionBegin(other, contact); }
        finally { Release(many!, count); }
    }

    public static void CollisionEnd(GameObject go, Rigidbody3D other)
    {
        if (go is null) return;

        int count = Collect(go, SceneCallbacks.CollisionEnd, out MonoBehaviour single, out MonoBehaviour[]? many);
        if (count == 0) return;
        if (count == 1) { single.InternalOnCollisionEnd(other); return; }

        try { for (int i = 0; i < count; i++) many![i].InternalOnCollisionEnd(other); }
        finally { Release(many!, count); }
    }

    public static void TriggerEnter(GameObject go, Rigidbody3D other)
    {
        if (go is null) return;

        int count = Collect(go, SceneCallbacks.TriggerEnter, out MonoBehaviour single, out MonoBehaviour[]? many);
        if (count == 0) return;
        if (count == 1) { single.InternalOnTriggerEnter(other); return; }

        try { for (int i = 0; i < count; i++) many![i].InternalOnTriggerEnter(other); }
        finally { Release(many!, count); }
    }

    public static void TriggerStay(GameObject go, Rigidbody3D other)
    {
        if (go is null) return;

        int count = Collect(go, SceneCallbacks.TriggerStay, out MonoBehaviour single, out MonoBehaviour[]? many);
        if (count == 0) return;
        if (count == 1) { single.InternalOnTriggerStay(other); return; }

        try { for (int i = 0; i < count; i++) many![i].InternalOnTriggerStay(other); }
        finally { Release(many!, count); }
    }

    public static void TriggerExit(GameObject go, Rigidbody3D other)
    {
        if (go is null) return;

        int count = Collect(go, SceneCallbacks.TriggerExit, out MonoBehaviour single, out MonoBehaviour[]? many);
        if (count == 0) return;
        if (count == 1) { single.InternalOnTriggerExit(other); return; }

        try { for (int i = 0; i < count; i++) many![i].InternalOnTriggerExit(other); }
        finally { Release(many!, count); }
    }

    // A handler may add or remove components, or trigger a nested event, so more than one recipient has to be
    // dispatched off a snapshot. Nothing is copied for the overwhelmingly common cases of no handler at all
    // or exactly one, which is where the contact callbacks actually spend their time.
    private static int Collect(GameObject go, SceneCallbacks which, out MonoBehaviour single, out MonoBehaviour[]? many)
    {
        single = null!;
        many = null;

        List<MonoBehaviour> components = go._components;
        int total = components.Count;
        int found = 0;

        for (int i = 0; i < total; i++)
        {
            MonoBehaviour c = components[i];
            if ((CallbacksOf(c) & which) == 0) continue;
            if (c.IsDisposed || !c.EnabledInHierarchy) continue;

            if (found == 0) single = c;
            else
            {
                many ??= Rent(total);
                if (found == 1) many[0] = single;
                many[found] = c;
            }

            found++;
        }

        return found;
    }

    [ThreadStatic] private static Stack<MonoBehaviour[]>? t_buffers;

    private static MonoBehaviour[] Rent(int minimum)
    {
        Stack<MonoBehaviour[]> pool = t_buffers ??= new Stack<MonoBehaviour[]>();

        while (pool.TryPop(out MonoBehaviour[]? buffer))
            if (buffer.Length >= minimum)
                return buffer;

        return new MonoBehaviour[Math.Max(minimum, 8)];
    }

    private static void Release(MonoBehaviour[] buffer, int used)
    {
        Array.Clear(buffer, 0, used);
        (t_buffers ??= new Stack<MonoBehaviour[]>()).Push(buffer);
    }
}
