// This file is part of the Prowl Game Engine
// Licensed under the MIT License. See the LICENSE file in the project root for details.

using System;

using Prowl.Echo;
using Prowl.Echo.Cloning;
using Prowl.Runtime.Resources;
using Prowl.Vector;

namespace Prowl.Runtime;

/// <summary>
/// Spawning, from a prefab or from another GameObject. Every path that builds a GameObject from
/// serialized data goes through here.
/// <para/>
/// The overloads that take a scene put the object into it and let its lifecycle run, which is what
/// game code wants. <see cref="InstantiateDetached"/> is the lower level, for callers that need to
/// configure the hierarchy before it comes alive or that are not spawning into a scene at all.
/// </summary>
public partial class GameObject
{
    /// <summary>
    /// Build a prefab's hierarchy without putting it in a scene. The result is linked to the prefab,
    /// so the editor treats it as an instance, but nothing about it is live yet: no lifecycle
    /// callback has run and it belongs to no scene.
    /// </summary>
    public static GameObject? InstantiateDetached(PrefabAsset prefab)
    {
        if (prefab.IsNotValid())
        {
            Debug.LogError("[Instantiate] No prefab to instantiate.");
            return null;
        }

        if (prefab.GameObjectData == null)
        {
            Debug.LogWarning($"[Instantiate] Prefab '{prefab.Name}' has no GameObject data.");
            return null;
        }

        GameObject? clone;
        try
        {
            // A prefab cannot reference objects outside itself, so anything the editor recorded as an
            // external reference comes back null rather than as an empty object built from the stub.
            clone = Serializer.Deserialize<GameObject>(prefab.GameObjectData,
                new SerializationContext { ExternalReferences = SceneReferenceResolver.None });
        }
        catch (Exception ex)
        {
            Debug.LogError($"[Instantiate] Failed to instantiate prefab '{prefab.Name}': {ex.Message}");
            return null;
        }

        if (clone == null)
        {
            Debug.LogError($"[Instantiate] Prefab '{prefab.Name}' did not deserialize into a GameObject.");
            return null;
        }

        StampPrefabId(clone, prefab.AssetID, prefab.GameObjectData);
        return clone;
    }

    /// <summary>
    /// Marks a freshly built tree as an instance of the prefab it came from. Stops at objects that
    /// already belong to a different prefab, which are nested instances with their own link.
    /// <para/>
    /// The asset's own data is walked alongside the tree to record which source object each new one
    /// came from. Identifiers are handed out fresh on load, so that correspondence cannot be read off
    /// the objects afterwards, and it is what the editor matches overrides against.
    /// </summary>
    private static void StampPrefabId(GameObject go, Guid prefabAssetId, EchoObject? data)
    {
        if (go.IsPrefabInstance && go.PrefabAssetId != prefabAssetId)
            return; // a nested instance, with a link of its own

        var link = go.EnsurePrefabLink();
        link.AssetId = prefabAssetId;

        if (data != null && Guid.TryParse(data.Get("Identifier")?.StringValue, out Guid sourceId))
            link.SourceIdentifier = sourceId;

        var componentData = data?.Get("Components")?.List;
        for (int i = 0; i < go._components.Count; i++)
        {
            if (componentData == null || i >= componentData.Count) break;
            if (Guid.TryParse(componentData[i].Get("_identifier")?.StringValue, out Guid sourceComponentId))
                link.ComponentSources[go._components[i].Identifier] = sourceComponentId;
        }

        // Only the editor reads these, and they are what its structural rules are enforced from. A
        // player would be walking the tree on every spawn to fill in state nothing consults.
        if (Application.IsEditor)
        {
            link.SourceComponentCount = go._components.Count;
            link.SourceChildCount = go.Children.Count;
        }

        var childData = data?.Get("Children")?.List;
        for (int i = 0; i < go.Children.Count; i++)
            StampPrefabId(go.Children[i], prefabAssetId, childData != null && i < childData.Count ? childData[i] : null);
    }

    /// <summary>Spawn a prefab into the current scene.</summary>
    public static GameObject? Instantiate(PrefabAsset prefab) => Instantiate(prefab, Scene.Current);

    /// <summary>Spawn a prefab into the current scene at a world position and rotation.</summary>
    public static GameObject? Instantiate(PrefabAsset prefab, Float3 position, Quaternion rotation)
        => Instantiate(prefab, Scene.Current, position, rotation);

    /// <summary>
    /// Spawn a prefab as a child of <paramref name="parent"/>, in that parent's scene.
    /// </summary>
    /// <param name="worldPositionStays">Keep the prefab's own transform as a world transform rather
    /// than as an offset from the parent. Off by default, so the prefab lands where the parent is.</param>
    public static GameObject? Instantiate(PrefabAsset prefab, GameObject? parent, bool worldPositionStays = false)
    {
        var instance = Instantiate(prefab, parent.IsValid() ? parent!.Scene : Scene.Current);
        if (instance == null) return null;

        if (parent.IsValid())
            instance.SetParent(parent!, worldPositionStays);

        return instance;
    }

    /// <summary>Spawn a prefab into a specific scene.</summary>
    public static GameObject? Instantiate(PrefabAsset prefab, Scene? scene)
    {
        var instance = InstantiateDetached(prefab);
        if (instance == null) return null; // InstantiateDetached already reported why

        return AddToScene(instance, scene);
    }

    /// <summary>Spawn a prefab into a specific scene at a world position and rotation.</summary>
    public static GameObject? Instantiate(PrefabAsset prefab, Scene? scene, Float3 position, Quaternion rotation)
    {
        var instance = InstantiateDetached(prefab);
        if (instance == null) return null;

        // Placed before the object enters the scene, so anything reacting to OnEnable sees where it
        // actually is rather than the prefab's authored transform.
        instance.Transform.Position = position;
        instance.Transform.Rotation = rotation;

        return AddToScene(instance, scene);
    }

    /// <summary>
    /// Copy an existing GameObject, including its children and components, into the same scene.
    /// References to objects outside the copied tree are kept pointing at those same objects rather
    /// than being copied too.
    /// </summary>
    public static GameObject? Instantiate(GameObject original)
        => Instantiate(original, original.IsValid() ? original.Scene : null);

    /// <summary>Copy an existing GameObject to a world position and rotation.</summary>
    public static GameObject? Instantiate(GameObject original, Float3 position, Quaternion rotation)
    {
        var clone = Clone(original);
        if (clone == null) return null;

        clone.Transform.Position = position;
        clone.Transform.Rotation = rotation;

        var scene = original.Scene;
        return AddToScene(clone, scene.IsValid() ? scene : Scene.Current);
    }

    /// <inheritdoc cref="Instantiate(PrefabAsset, GameObject?, bool)"/>
    public static GameObject? Instantiate(GameObject original, GameObject? parent, bool worldPositionStays = false)
    {
        Scene? target = parent.IsValid() ? parent!.Scene : (original.IsValid() ? original.Scene : null);
        var clone = Instantiate(original, target);
        if (clone == null) return null;

        if (parent.IsValid())
            clone.SetParent(parent!, worldPositionStays);

        return clone;
    }

    /// <summary>Copy an existing GameObject into a specific scene.</summary>
    public static GameObject? Instantiate(GameObject original, Scene? scene)
    {
        var clone = Clone(original);
        return clone == null ? null : AddToScene(clone, scene);
    }

    /// <summary>
    /// A detached copy of a GameObject tree. References into the tree are rewritten to the copy's own
    /// objects, and references to anything outside it keep pointing at that same object.
    /// </summary>
    private static GameObject? Clone(GameObject original)
    {
        if (original.IsNotValid())
        {
            Debug.LogError("[Instantiate] No GameObject to copy.");
            return null;
        }

        try
        {
            return Cloner.Clone(original);
        }
        catch (Exception ex)
        {
            Debug.LogError($"[Instantiate] Failed to copy '{original.Name}': {ex.Message}");
            return null;
        }
    }

    private static GameObject? AddToScene(GameObject instance, Scene? scene)
    {
        if (scene == null)
        {
            // Handing back a live-looking object that is in no scene, gets no callbacks and never
            // renders would be worse than saying so.
            Debug.LogError($"[Instantiate] There is no scene to spawn '{instance.Name}' into.");
            instance.Dispose();
            return null;
        }

        scene.Add(instance);
        return instance;
    }
}
