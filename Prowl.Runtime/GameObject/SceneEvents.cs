// This file is part of the Prowl Game Engine
// Licensed under the MIT License. See the LICENSE file in the project root for details.

using System;
using System.Collections.Generic;
using System.Diagnostics.CodeAnalysis;
using System.Linq;
using System.Runtime.CompilerServices;
using System.Threading;

using Prowl.Ember;
using Prowl.Runtime.Resources;
using Prowl.Runtime.Tasks;

namespace Prowl.Runtime;

/// <summary>Which part of an object hierarchy receives a typed event.</summary>
public enum EventScope
{
    Self,
    SelfAndDescendants
}

/// <summary>Synchronous, main-thread events delivered to matching component interfaces.</summary>
public static class SceneEvents
{
    [ReloadIgnore]
    private static ConditionalWeakTable<EngineObject, Cache> s_cache = new();
    private static long s_revision;

    internal static void Invalidate() => Interlocked.Increment(ref s_revision);

    internal static void Reset()
    {
        s_cache = new();
        Invalidate();
    }

    /// <summary>
    /// Dispatches in execution order, then tree/component order. Membership is captured at entry;
    /// removed, destroyed or moved listeners are skipped. Exceptions are logged.
    /// </summary>
    public static void RunEvent<T>(this GameObject root, Action<T> action,
        EventScope scope = EventScope.SelfAndDescendants, bool includeInactive = false) where T : class
    {
        ArgumentNullException.ThrowIfNull(action);
        MainThreadContext.AssertMainThread();
        if (!Enum.IsDefined(scope))
            throw new ArgumentOutOfRangeException(nameof(scope));

        Dispatch(root, action, scope, includeInactive);
    }

    /// <summary>Dispatches in an explicit scene, independently of Scene.Current.</summary>
    public static void RunEvent<T>(this Scene scene, Action<T> action, bool includeInactive = false) where T : class
    {
        ArgumentNullException.ThrowIfNull(action);
        MainThreadContext.AssertMainThread();
        Dispatch(scene, action, EventScope.SelfAndDescendants, includeInactive);
    }

    // Destruction notifications still have to reach listeners queued for destruction with their root.
    internal static void RunLifecycle<T>(GameObject root, Action<T> action) where T : class =>
        Dispatch(root, action, EventScope.SelfAndDescendants, includeInactive: true, includeDestroyQueued: true);

    private static void Dispatch<T>(EngineObject root, Action<T> action, EventScope scope, bool includeInactive,
        bool includeDestroyQueued = false) where T : class
    {
        if (!CanReceive(root, includeDestroyQueued))
            return;

        // Hold the captured array: a nested event can rebuild the same cache after a structural edit.
        Target[] targets = GetTargets(root);
        foreach (Target target in targets)
        {
            if (!CanReceive(root, includeDestroyQueued))
                break;

            Component component = target.Component;
            if (!IsEligible(target, includeInactive, includeDestroyQueued) || component is not T listener ||
                !IsInScope(root, target.Owner, scope))
                continue;

            try
            {
                action(listener);
            }
            catch (Exception e)
            {
                Debug.LogError($"[Event] {typeof(T).Name} on {component.GetType().Name} threw: {e}");
            }
        }
    }

    [UnconditionalSuppressMessage("Trimming", "IL2026",
        Justification = "Player builds preserve component types and their execution-order attributes.")]
    private static Target[] GetTargets(EngineObject root)
    {
        Cache cache = s_cache.GetValue(root, static _ => new Cache());
        long revision = Volatile.Read(ref s_revision);
        if (cache.Revision == revision)
            return cache.Targets;

        IEnumerable<GameObject> objects = root is Scene scene ? scene.AllObjects : EnumerateTree((GameObject)root);
        cache.Targets = objects.Where(go => go.IsValid())
            .SelectMany(go => go.GetComponents())
            .Where(component => component.IsValid())
            .OrderBy(RuntimeUtils.GetExecutionOrder)
            .Select(component => new Target(component, component.GameObject, component.EventAttachmentVersion))
            .ToArray();
        cache.Revision = revision;
        return cache.Targets;
    }

    private static bool CanReceive(EngineObject target, bool includeDestroyQueued) =>
        target.IsValid() && (includeDestroyQueued || !target.IsDestroyQueued);

    private static bool IsEligible(Target target, bool includeInactive, bool includeDestroyQueued)
    {
        Component component = target.Component;
        return CanReceive(component, includeDestroyQueued) && target.Owner.IsValid() &&
            component.EventAttachmentVersion == target.Attachment &&
            ReferenceEquals(component.GameObject, target.Owner) &&
            (includeInactive || component.EnabledInHierarchy);
    }

    private static bool IsInScope(EngineObject root, GameObject owner, EventScope scope)
    {
        if (root is Scene scene)
            return owner.Scene == scene;
        if (scope == EventScope.Self)
            return owner == root;

        for (GameObject? current = owner; current.IsValid(); current = current.Parent)
            if (current == root)
                return true;
        return false;
    }

    private static IEnumerable<GameObject> EnumerateTree(GameObject root)
    {
        yield return root;
        foreach (GameObject child in root.Children)
            foreach (GameObject descendant in EnumerateTree(child))
                yield return descendant;
    }

    private sealed class Cache
    {
        public long Revision = -1;
        public Target[] Targets = [];
    }

    private readonly record struct Target(Component Component, GameObject Owner, int Attachment);
}

/// <summary>Convenience methods for custom local scene event interfaces. These do not send RPCs.</summary>
public interface ISceneEvent<T> where T : class
{
    static void Post(Action<T> action) => Scene.Current.RunEvent(action);

    static void PostToGameObject(GameObject root, Action<T> action,
        EventScope scope = EventScope.SelfAndDescendants, bool includeInactive = false) =>
        root.RunEvent(action, scope, includeInactive);
}
