// This file is part of the Prowl Game Engine
// Licensed under the MIT License. See the LICENSE file in the project root for details.

using System;
using System.Collections.Generic;

using Prowl.Echo;
using Prowl.Runtime;
using Prowl.Runtime.Resources;
using Prowl.Vector;

namespace Prowl.Editor.Core;

/// <summary>
/// Echo external-reference resolver for copy/paste of scene objects. The objects passed to the
/// constructor - the copy selection - serialize by value; every other GameObject / MonoBehaviour /
/// Transform reference is linked by its persistence id and resolved back against the current scene
/// on paste, so a copied object keeps its references to the rest of the scene instead of deep-cloning
/// them into orphans. References that can't be resolved (different scene, deleted object) become null.
///
/// Shared across the various copy/paste flows (components today, GameObjects/prefabs later): each
/// passes the roots it's serializing by value, and everything outside that set is linked.
/// </summary>
public sealed class SceneReferenceResolver : IExternalReferenceResolver
{
    private readonly HashSet<object> _copied;

    /// <param name="copied">
    /// The objects being serialized by value. Anything they reference that isn't in this set is
    /// linked by id instead of cloned. Pass nothing when only deserializing (paste), since the key
    /// side is never consulted then.
    /// </param>
    public SceneReferenceResolver(params object[] copied)
        => _copied = new HashSet<object>(copied, ReferenceEqualityComparer.Instance);

    public object? GetReferenceKey(object value)
    {
        if (_copied.Contains(value)) return null; // part of the copy - serialize it by value

        return value switch
        {
            GameObject go => go.Identifier,
            MonoBehaviour mb => mb.Identifier,
            // Transform has no id of its own; anchor it to its GameObject. A detached Transform can't
            // be anchored - key it with the never-assigned Guid.Empty so it's still linked (not
            // deep-copied into an orphan) and simply resolves to null on paste.
            Transform t => t.GameObject.IsValid() ? t.GameObject.Identifier : Guid.Empty,
            _ => null
        };
    }

    public object? ResolveReference(object key, Type targetType)
    {
        if (key is not Guid id || Scene.Current is not { } scene) return null;

        // GameObject and MonoBehaviour share the FindObjectByIdentifier lookup; a Transform key is
        // its GameObject's id, so resolve the GameObject and hand back its Transform.
        if (typeof(Transform).IsAssignableFrom(targetType))
            return scene.FindObjectByIdentifier<GameObject>(id)?.Transform;

        return scene.FindObjectByIdentifier<EngineObject>(id);
    }
}
