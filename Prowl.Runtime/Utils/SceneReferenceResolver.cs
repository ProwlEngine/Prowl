// This file is part of the Prowl Game Engine
// Licensed under the MIT License. See the LICENSE file in the project root for details.

using System;
using System.Collections.Generic;

using Prowl.Echo;
using Prowl.Runtime;
using Prowl.Runtime.Resources;
using Prowl.Vector;

namespace Prowl.Runtime;

/// <summary>A tree written for an in-memory copy, and the runtime assets it links, which it keeps alive.</summary>
public sealed record MemoryCopy(EchoObject Data, IReadOnlyList<Asset> LinkedAssets);

/// <summary>
/// Echo external-reference resolver for scene objects. The objects passed to the constructor - the
/// copy selection, or the tree being written to an asset - serialize by value; every other
/// GameObject / Component / Transform reference is linked by its persistence id and resolved back
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
    public SceneInstance? ResolveIn { get; init; }

    /// <summary>
    /// Detached content a link resolves against instead of a world, as an identifier map from
    /// <see cref="SceneContent.IdentifierMap"/>. Set for content read off disk that is never built into a world.
    /// </summary>
    public IReadOnlyDictionary<Guid, EngineObject>? ResolveInContent { get; init; }

    /// <summary>Context for writing a tree out by value, linking everything it references beyond itself.</summary>
    public static SerializationContext ContextForTree(GameObject root)
        => new() { ExternalReferences = ForTree(root) };

    /// <summary>
    /// As <see cref="ContextForTree"/>, for a copy that never leaves memory: runtime assets are linked for the
    /// session too, so pasting or undoing shares a runtime material instead of making a second one.
    /// </summary>
    internal static DependencySerializationContext ContextForMemoryCopy(GameObject root)
        => new() { ExternalReferences = ForTree(root), LinkRuntimeAssets = true };

    /// <summary>Writes a tree for an in-memory copy, holding the runtime assets it links so they live as long as the copy.</summary>
    public static MemoryCopy WriteMemoryCopy(GameObject root)
    {
        DependencySerializationContext context = ContextForMemoryCopy(root);
        return new MemoryCopy(Serializer.Serialize(typeof(object), root, context), context.LinkedAssets);
    }

    /// <summary>
    /// Context for reading data back, binding every link to the live object it names.
    /// </summary>
    /// <param name="scene">The scene to resolve in, or null for the open one.</param>
    public static SerializationContext ContextForLinking(SceneInstance? scene = null)
        => new() { ExternalReferences = new SceneReferenceResolver { ResolveIn = scene } };

    /// <summary>Context for reading data back, binding every link to the object it names in detached content.</summary>
    /// <param name="content">The content's identifier map, from <see cref="SceneContent.IdentifierMap"/>.</param>
    public static SerializationContext ContextForLinking(IReadOnlyDictionary<Guid, EngineObject> content)
        => new() { ExternalReferences = new SceneReferenceResolver { ResolveInContent = content } };

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
        return new SceneReferenceResolver([.. objects]);

        void Collect(GameObject go)
        {
            objects.Add(go);
            objects.Add(go.Transform);
            foreach (Component component in go.GetComponents<Component>())
                objects.Add(component);
            foreach (GameObject child in go.Children)
                Collect(child);
        }
    }

    private sealed class NoReferences : IExternalReferenceResolver
    {
        public object? GetReferenceKey(object value) => null;
        public object? ResolveReference(object key, Type targetType) => null;
    }

    /// <summary>
    /// A resolver for capturing a world's content: the listed objects, children included, serialize by value, and a
    /// reference to any other object, world or a world itself is written as null and logged, so saving never copies
    /// a foreign object into the content.
    /// </summary>
    internal static IExternalReferenceResolver ForCapture(IEnumerable<GameObject> objects) => new CaptureReferences(objects);

    private sealed class CaptureReferences : IExternalReferenceResolver
    {
        private readonly HashSet<object> _captured = new(ReferenceEqualityComparer.Instance);

        public CaptureReferences(IEnumerable<GameObject> objects)
        {
            foreach (GameObject go in objects)
                Collect(go);
        }

        // Children are written inside their parent, so every one of them is part of the content.
        private void Collect(GameObject go)
        {
            if (!_captured.Add(go)) return;
            _captured.Add(go.Transform);
            foreach (Component component in go.GetComponents<Component>())
                _captured.Add(component);
            foreach (GameObject child in go.Children)
                Collect(child);
        }

        public object? GetReferenceKey(object value)
        {
            string? name = value switch
            {
                GameObject go => go.Name,
                Component component => component.GameObject.IsValid() ? $"{component.GameObject.Name}/{component.GetType().Name}" : component.GetType().Name,
                Transform transform => transform.GameObject.IsValid() ? transform.GameObject.Name : "Transform",
                SceneInstance world => world.Name,
                _ => null,
            };
            if (name == null || _captured.Contains(value)) return null;

            Debug.LogWarning($"A reference to '{name}', which is not part of the content being saved, was saved as null.");
            return ExternalReference.Null;
        }

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
            Component mb => mb.Identifier,
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
        if (ResolveInContent != null)
        {
            if (key is not Guid contentId || !ResolveInContent.TryGetValue(contentId, out EngineObject? found)) return null;
            if (typeof(Transform).IsAssignableFrom(targetType))
                return found is GameObject owner && owner.IsValid() ? owner.Transform : null;
            return found;
        }

        SceneInstance? target = ResolveIn.IsValid() ? ResolveIn : SceneInstance.HasMain ? SceneInstance.Main : null;
        if (key is not Guid id || target is not { } scene) return null;

        // GameObject and Component share the FindObjectByIdentifier lookup; a Transform key is
        // its GameObject's id, so resolve the GameObject and hand back its Transform.
        if (typeof(Transform).IsAssignableFrom(targetType))
        {
            GameObject owner = scene.FindObject<GameObject>(id)!;
            return owner.IsValid() ? owner.Transform : null;
        }

        return scene.FindObject<EngineObject>(id);
    }
}
