// This file is part of the Prowl Game Engine
// Licensed under the MIT License. See the LICENSE file in the project root for details.

using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;

using Prowl.PaperUI;
using Prowl.Runtime.Rendering;

namespace Prowl.Runtime;

/// <summary>
/// Per-scene registry of the components that actually implement a per-frame callback
/// (Start/Update/LateUpdate/FixedUpdate, plus OnRenderCollect/DrawGizmos/OnGui). Components register
/// when they become enabled in an active scene and unregister when disabled/removed, so each loop
/// iterates a short, pre-filtered, execution-ordered list instead of walking every GameObject and
/// component. This is why <see cref="GameObject"/> stores its components in insertion order - ordering
/// for callbacks is owned here, decoupled from component storage.
/// </summary>
internal sealed class SceneComponentRegistry
{
    [Flags]
    private enum Ticks
    {
        None = 0,
        Start = 1, Update = 2, LateUpdate = 4, FixedUpdate = 8,
        RenderCollect = 16, DrawGizmos = 32, OnGui = 64,
    }

    // Which callbacks a concrete component type overrides. Computed once per type.
    private static readonly Dictionary<Type, Ticks> s_typeTicks = new();

    private static Ticks GetTicks(Type type)
    {
        if (s_typeTicks.TryGetValue(type, out Ticks cached))
            return cached;

        Ticks ticks = Ticks.None;
        if (Overrides(type, nameof(MonoBehaviour.Start))) ticks |= Ticks.Start;
        if (Overrides(type, nameof(MonoBehaviour.Update))) ticks |= Ticks.Update;
        if (Overrides(type, nameof(MonoBehaviour.LateUpdate))) ticks |= Ticks.LateUpdate;
        if (Overrides(type, nameof(MonoBehaviour.FixedUpdate))) ticks |= Ticks.FixedUpdate;
        if (Overrides(type, nameof(MonoBehaviour.OnRenderCollect))) ticks |= Ticks.RenderCollect;
        if (Overrides(type, nameof(MonoBehaviour.DrawGizmos))) ticks |= Ticks.DrawGizmos;
        if (Overrides(type, nameof(MonoBehaviour.OnGui))) ticks |= Ticks.OnGui;

        s_typeTicks[type] = ticks;
        return ticks;
    }

    private static bool Overrides(Type type, string method)
    {
        // All the callbacks are public virtual on MonoBehaviour; an override's DeclaringType differs.
        MethodInfo? mi = type.GetMethods(BindingFlags.Instance | BindingFlags.Public)
            .FirstOrDefault(m => m.Name == method && m.GetBaseDefinition().DeclaringType == typeof(MonoBehaviour));
        return mi != null && mi.DeclaringType != typeof(MonoBehaviour);
    }

    /// <summary>One callback's membership: an insertion-ordered list plus an execution-order-sorted
    /// snapshot rebuilt only when membership changes. Iterating the snapshot also keeps a loop safe
    /// against components enabling/disabling others mid-callback.</summary>
    private sealed class Channel
    {
        private readonly List<MonoBehaviour> _list = new();
        private MonoBehaviour[] _sorted = Array.Empty<MonoBehaviour>();
        private bool _dirty;

        public void Add(MonoBehaviour c) { _list.Add(c); _dirty = true; }
        public void Remove(MonoBehaviour c) { if (_list.Remove(c)) _dirty = true; }

        public MonoBehaviour[] Snapshot()
        {
            if (_dirty)
            {
                // OrderBy is a stable sort, so equal execution orders keep registration order.
                _sorted = _list.OrderBy(c => RuntimeUtils.GetExecutionOrder(c) ?? 0).ToArray();
                _dirty = false;
            }
            return _sorted;
        }
    }

    private readonly Channel _start = new();
    private readonly Channel _update = new();
    private readonly Channel _lateUpdate = new();
    private readonly Channel _fixedUpdate = new();
    private readonly Channel _renderCollect = new();
    private readonly Channel _drawGizmos = new();
    private readonly Channel _onGui = new();

    public void Register(MonoBehaviour c)
    {
        if (c._updateRegistered) return;
        c._updateRegistered = true;

        Ticks ticks = GetTicks(c.GetType());
        if ((ticks & Ticks.Start) != 0 && !c.HasStarted) _start.Add(c);
        if ((ticks & Ticks.Update) != 0) _update.Add(c);
        if ((ticks & Ticks.LateUpdate) != 0) _lateUpdate.Add(c);
        if ((ticks & Ticks.FixedUpdate) != 0) _fixedUpdate.Add(c);
        if ((ticks & Ticks.RenderCollect) != 0) _renderCollect.Add(c);
        if ((ticks & Ticks.DrawGizmos) != 0) _drawGizmos.Add(c);
        if ((ticks & Ticks.OnGui) != 0) _onGui.Add(c);
    }

    public void Unregister(MonoBehaviour c)
    {
        if (!c._updateRegistered) return;
        c._updateRegistered = false;

        _start.Remove(c);
        _update.Remove(c);
        _lateUpdate.Remove(c);
        _fixedUpdate.Remove(c);
        _renderCollect.Remove(c);
        _drawGizmos.Remove(c);
        _onGui.Remove(c);
    }

    // ---- Gameplay callbacks (gated per-component by ShouldExecuteGameplay inside Internal*) ----

    public void RunStart()
    {
        foreach (MonoBehaviour c in _start.Snapshot())
        {
            if (c.IsDisposed || c.HasStarted || !c.EnabledInHierarchy) continue;
            c.InternalStart();
            if (c.HasStarted) _start.Remove(c);
        }
    }

    public void RunUpdate()
    {
        foreach (MonoBehaviour c in _update.Snapshot())
            if (!c.IsDisposed && c.EnabledInHierarchy) c.InternalUpdate();
    }

    public void RunLateUpdate()
    {
        foreach (MonoBehaviour c in _lateUpdate.Snapshot())
            if (!c.IsDisposed && c.EnabledInHierarchy) c.InternalLateUpdate();
    }

    public void RunFixedUpdate()
    {
        foreach (MonoBehaviour c in _fixedUpdate.Snapshot())
            if (!c.IsDisposed && c.EnabledInHierarchy) c.InternalFixedUpdate();
    }

    // ---- Rendering / gizmo / GUI callbacks (always run, even in edit mode; not gameplay-gated) ----

    public void RunRenderCollect(Camera camera, List<IRenderable> renderables, List<IRenderableLight> lights)
    {
        foreach (MonoBehaviour c in _renderCollect.Snapshot())
        {
            if (c.IsDisposed || !c.EnabledInHierarchy) continue;
            try { c.OnRenderCollect(camera, renderables, lights); }
            catch (Exception ex) { Debug.LogError($"[{c.GameObject?.Name}/{c.GetType().Name}] OnRenderCollect threw: {ex.Message}\n{ex.StackTrace}"); }
        }
    }

    public void RunDrawGizmos()
    {
        foreach (MonoBehaviour c in _drawGizmos.Snapshot())
        {
            if (c.IsDisposed || !c.EnabledInHierarchy || c.HideFlags.HasFlag(HideFlags.NoGizmos)) continue;
            try { c.DrawGizmos(); }
            catch (Exception ex) { Debug.LogError($"[{c.GameObject?.Name}/{c.GetType().Name}] DrawGizmos threw: {ex.Message}\n{ex.StackTrace}"); }
        }
    }

    public void RunOnGui(Paper paper)
    {
        foreach (MonoBehaviour c in _onGui.Snapshot())
        {
            if (c.IsDisposed || !c.EnabledInHierarchy) continue;
            try { c.OnGui(paper); }
            catch (Exception ex) { Debug.LogError($"[{c.GameObject?.Name}/{c.GetType().Name}] OnGui threw: {ex.Message}\n{ex.StackTrace}"); }
        }
    }
}
