// This file is part of the Prowl Game Engine
// Licensed under the MIT License. See the LICENSE file in the project root for details.

using System;
using System.Collections.Generic;

using Prowl.Echo;
using Prowl.Runtime;
using Prowl.Runtime.Resources;
using Prowl.Vector;

namespace Prowl.Runtime;

/// <summary>
/// Echo external-reference resolver for scene objects. The objects passed to the constructor - the
/// copy selection, or the tree being written to an asset - serialize by value; every other
/// GameObject / MonoBehaviour / Transform reference is linked by its persistence id and resolved back
/// against the current scene, so the copy keeps its references to the rest of the scene instead of
/// deep-cloning them into orphans. References that can't be resolved (different scene, deleted
/// object) become null.
///
/// Shared across the flows that serialize part of a scene: component copy/paste, GameObject
/// duplication, and prefab creation/apply. Each passes the roots it is serializing by value, and
/// everything outside that set is linked.
/// </summary>
public sealed class SceneReferenceResolver : IExternalReferenceResolver
{
    private readonly HashSet<object> _copied;

    /// <summary>
    /// Links nothing and resolves nothing, for reading content that must not bind to the scene at
    /// all - a prefab asset, which cannot legally reference scene objects. Echo only recognises a
    /// reference stub while a resolver is present, so this is what turns an external reference into
    /// null instead of an empty object rebuilt from the stub. Deserialization only: serializing with
    /// it would deep-copy every reference rather than link it.
    /// </summary>
    public static readonly IExternalReferenceResolver None = new NoReferences();

    /// <param name="copied">
    /// The objects being serialized by value. Anything they reference that isn't in this set is
    /// linked by id instead of cloned. Pass nothing when only deserializing (paste), since the key
    /// side is never consulted then.
    /// </param>
    public SceneReferenceResolver(params object[] copied)
        => _copied = new HashSet<object>(copied, ReferenceEqualityComparer.Instance);

    /// <summary>
    /// Which scene a link resolves against. Null means the open one, which is right for everything the
    /// user does directly. A caller working on a scene that is not open - a build reading one off disk -
    /// has to say so, or its references resolve against whatever scene happens to be open and bind to
    /// the wrong objects or to nothing.
    /// </summary>
    public Scene? ResolveIn { get; init; }

    /// <summary>Context for writing a tree out by value, linking everything it references beyond itself.</summary>
    public static SerializationContext ContextForTree(GameObject root)
        => new() { ExternalReferences = ForTree(root) };

    /// <summary>
    /// Context for reading data back, binding every link to the live object it names.
    /// </summary>
    /// <param name="scene">The scene to resolve in, or null for the open one.</param>
    public static SerializationContext ContextForLinking(Scene? scene = null)
        => new() { ExternalReferences = new SceneReferenceResolver { ResolveIn = scene } };

    /// <summary>
    /// A resolver for writing out a whole GameObject tree: the tree serializes by value, and anything
    /// it references from outside is linked. Transforms and components have to be listed alongside
    /// their GameObjects, or they would serialize as links themselves.
    /// </summary>
    public static SceneReferenceResolver ForTree(GameObject root) => ForTrees([root]);

    /// <summary>
    /// A resolver for writing out several trees as one operation, so a reference from one to another
    /// stays inside the data instead of being linked out or copied twice.
    /// </summary>
    public static SceneReferenceResolver ForTrees(IEnumerable<GameObject> roots)
    {
        var objects = new List<object>();
        foreach (GameObject root in roots)
            if (root.IsValid())
                Collect(root);
        return new SceneReferenceResolver(objects.ToArray());

        void Collect(GameObject go)
        {
            objects.Add(go);
            objects.Add(go.Transform);
            foreach (var component in go.GetComponents<MonoBehaviour>())
                objects.Add(component);
            foreach (var child in go.Children)
                Collect(child);
        }
    }

    private sealed class NoReferences : IExternalReferenceResolver
    {
        public object? GetReferenceKey(object value) => null;
        public object? ResolveReference(object key, Type targetType) => null;
    }

    /// <summary>
    /// The objects this resolver linked out rather than serialized, in the order it met them.
    /// <para/>
    /// Linking out is right for a copy, which lands back in the same scene. It is a loss for a prefab
    /// asset, which cannot hold a scene reference at all and will read the link back as null. The
    /// resolver is the only place that knows it happened, so it records it and lets the caller decide
    /// whether that is worth saying anything about.
    /// </summary>
    public IReadOnlyList<object> LinkedOut => _linkedOut;

    private readonly List<object> _linkedOut = [];

    public object? GetReferenceKey(object value)
    {
        if (_copied.Contains(value)) return null; // part of the copy - serialize it by value

        object? key = value switch
        {
            GameObject go => go.Identifier,
            MonoBehaviour mb => mb.Identifier,
            // Transform has no id of its own; anchor it to its GameObject. A detached Transform can't
            // be anchored - key it with the never-assigned Guid.Empty so it's still linked (not
            // deep-copied into an orphan) and simply resolves to null on paste.
            Transform t => t.GameObject.IsValid() ? t.GameObject.Identifier : Guid.Empty,
            _ => null
        };

        if (key != null) _linkedOut.Add(value);
        return key;
    }

    public object? ResolveReference(object key, Type targetType)
    {
        Scene? target = ResolveIn.IsValid() ? ResolveIn : Scene.Current;
        if (key is not Guid id || target is not { } scene) return null;

        // GameObject and MonoBehaviour share the FindObjectByIdentifier lookup; a Transform key is
        // its GameObject's id, so resolve the GameObject and hand back its Transform.
        if (typeof(Transform).IsAssignableFrom(targetType))
        {
            GameObject owner = scene.FindObjectByIdentifier<GameObject>(id)!;
            return owner.IsValid() ? owner.Transform : null;
        }

        return scene.FindObjectByIdentifier<EngineObject>(id);
    }
}
