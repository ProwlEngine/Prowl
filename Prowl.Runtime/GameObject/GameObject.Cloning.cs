// This file is part of the Prowl Game Engine
// Licensed under the MIT License. See the LICENSE file in the project root for details.

using System;

using Prowl.Echo.Cloning;

namespace Prowl.Runtime;

/// <summary>
/// A GameObject claims its own components and children when it is cloned, because neither can simply
/// be allocated: a component has to be registered with the object that owns it, and a child has to be
/// parented. Everything else about the object is left to the ordinary field walk.
/// </summary>
public partial class GameObject : ICloneExplicit
{
    void ICloneExplicit.SetupCloneTargets(object targetObject, ICloneSetup setup)
    {
        var target = (GameObject)targetObject;

        setup.HandleObject(this, target);

        for (int i = 0; i < _components.Count; i++)
        {
            MonoBehaviour component = _components[i];
            if (component.IsNotValid()) continue;

            setup.HandleObject(component, ClaimComponent(target, component, i, setup), CloneBehavior.ChildObject);
        }

        for (int i = 0; i < Children.Count; i++)
        {
            GameObject child = Children[i];
            if (child.IsNotValid()) continue;

            setup.HandleObject(child, ClaimChild(target, child, i, setup), CloneBehavior.ChildObject);
        }
    }

    void ICloneExplicit.CopyCloneTo(object targetObject, ICloneOperation operation)
    {
        var target = (GameObject)targetObject;

        operation.HandleObject(this, target);

        foreach (MonoBehaviour component in _components)
        {
            if (component.IsNotValid()) continue;
            operation.HandleObject(component, operation.GetTarget(component));
        }

        foreach (GameObject child in Children)
        {
            if (child.IsNotValid()) continue;
            operation.HandleObject(child, operation.GetTarget(child));
        }

        RemapComponentSources(target, operation);
    }

    /// <summary>
    /// The prefab link records which prefab component each of this object's components came from,
    /// keyed by component identifier. A copy's components have identifiers of their own, so the keys
    /// are rewritten against them rather than carried over pointing at nothing.
    /// </summary>
    private void RemapComponentSources(GameObject target, ICloneOperation operation)
    {
        PrefabLink? sourceLink = PrefabLink;
        PrefabLink? targetLink = target.PrefabLink;
        if (sourceLink == null || targetLink == null) return;

        targetLink.ComponentSources.Clear();

        foreach (MonoBehaviour component in _components)
        {
            if (component.IsNotValid()) continue;
            if (!sourceLink.ComponentSources.TryGetValue(component.Identifier, out Guid sourceId)) continue;

            if (operation.GetTarget(component) is MonoBehaviour copy)
                targetLink.ComponentSources[copy.Identifier] = sourceId;
        }
    }

    /// <summary>
    /// The target's counterpart of one of this object's components: the one a caller already paired it
    /// with, the one standing in the same place if it is the same type, or a new one.
    /// </summary>
    private static MonoBehaviour ClaimComponent(GameObject target, MonoBehaviour component, int index, ICloneSetup setup)
    {
        if (setup.Context.TryGetTarget(component, out object? paired) && paired is MonoBehaviour claimed)
            return claimed;

        if (index < target._components.Count)
        {
            MonoBehaviour existing = target._components[index];
            if (existing.IsValid() && existing.GetType() == component.GetType())
                return existing;
        }

        return target.AttachClonedComponent(component.GetType());
    }

    private static GameObject ClaimChild(GameObject target, GameObject child, int index, ICloneSetup setup)
    {
        if (setup.Context.TryGetTarget(child, out object? paired) && paired is GameObject claimed)
            return claimed;

        if (index < target.Children.Count && target.Children[index].IsValid())
            return target.Children[index];

        var created = new GameObject(child.Name);
        created.SetParent(target, worldPositionStays: false);
        return created;
    }

    /// <summary>
    /// Attaches a bare component of the given type. Unlike <see cref="AddComponent(Type)"/> this does
    /// not pull in required components, since the object being copied already has whatever it needs.
    /// </summary>
    internal MonoBehaviour AttachClonedComponent(Type type)
    {
        var component = (MonoBehaviour)Activator.CreateInstance(type, nonPublic: true)!;

        component.AttachToGameObject(this);
        _components.Add(component);
        _componentCache.Add(type, component);
        NotifyComponentAddedToScene(component);

        return component;
    }
}
