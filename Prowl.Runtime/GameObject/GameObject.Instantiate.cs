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

        // Which prefab object each new one came from is what the editor matches overrides against.
        // A player never refreshes an instance, and ships its scenes with this stripped out already,
        // so outside the editor a spawn skips the whole business and pays for none of it. Gated on
        // the editor rather than on play mode so that what an instance carries never depends on
        // whether it was spawned before or after someone pressed play.
        bool trackSources = Application.IsEditor;

        GameObject? clone;
        try
        {
            // A prefab cannot reference objects outside itself, so anything the editor recorded as an
            // external reference comes back null rather than as an empty object built from the stub.
            var context = new SerializationContext { ExternalReferences = SceneReferenceResolver.None };

            // Identifiers come through intact and are traded for fresh ones by StampPrefabId, which is
            // what lets it record where each object came from without having to work out which part of
            // the serialized data produced which object. Without that to record, an ordinary load
            // hands out the fresh identifiers itself and there is nothing left to walk for.
            clone = trackSources
                ? DeserializePreservingIdentifiers(prefab.GameObjectData, context)
                : Serializer.Deserialize<GameObject>(prefab.GameObjectData, context);
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

        if (trackSources) StampPrefabId(clone, prefab.AssetID);
        else MarkAsPrefabInstance(clone, prefab.AssetID);

        return clone;
    }

    /// <summary>
    /// Says which prefab a tree came from and nothing else. What a player and a running game can
    /// observe of an instance is <see cref="IsPrefabInstance"/> and <see cref="PrefabAssetId"/>, and a
    /// scene ships with only those, so a spawn while playing records only those.
    /// </summary>
    private static void MarkAsPrefabInstance(GameObject go, Guid prefabAssetId)
    {
        if (go.IsPrefabInstance && go.PrefabAssetId != prefabAssetId)
            return; // a nested instance, with a link of its own

        go.EnsurePrefabLink().AssetId = prefabAssetId;

        foreach (GameObject child in go.Children)
            MarkAsPrefabInstance(child, prefabAssetId);
    }

    /// <summary>
    /// Marks a freshly built tree as an instance of the prefab it came from, recording which prefab
    /// object each one is. Stops at objects that already belong to a different prefab, which are
    /// nested instances with their own link.
    /// <para/>
    /// The tree still wears the prefab's own identifiers at this point, so each object is holding the
    /// answer already: it notes down what it came in as and is given a fresh identity of its own.
    /// Reading the correspondence back off the serialized data instead meant pairing objects with the
    /// nodes that produced them by position, which is only true while nothing failed to load.
    /// </summary>
    private static void StampPrefabId(GameObject go, Guid prefabAssetId)
    {
        if (go.IsPrefabInstance && go.PrefabAssetId != prefabAssetId)
            return; // a nested instance, with a link of its own

        var link = go.EnsurePrefabLink();
        link.AssetId = prefabAssetId;
        link.SourceIdentifier = go.Identifier;
        go.SetIdentifier(Guid.NewGuid());

        foreach (MonoBehaviour component in go._components)
        {
            if (component.IsNotValid()) continue;

            // The identifier the component came in under is the asset's, which is what it records as
            // where it came from before taking an identity of its own.
            component.SourceIdentifier = component.Identifier;
            component.Identifier = Guid.NewGuid();
        }

        foreach (GameObject child in go.Children)
            StampPrefabId(child, prefabAssetId);
    }

    /// <summary>
    /// Deserialize a GameObject tree with the identifiers the data carries, rather than the fresh
    /// ones a load normally hands out. For callers that need to know which serialized object each
    /// live one came from, and that take responsibility for the identities afterwards.
    /// </summary>
    internal static GameObject? DeserializePreservingIdentifiers(EchoObject data, SerializationContext? context = null)
    {
        PreservingIdentifiers = true;
        try
        {
            return context == null
                ? Serializer.Deserialize<GameObject>(data)
                : Serializer.Deserialize<GameObject>(data, context);
        }
        finally
        {
            PreservingIdentifiers = false;
        }
    }

    /// <summary>
    /// Set while <see cref="DeserializePreservingIdentifiers"/> runs. Thread-local, so a load on one
    /// thread cannot change what another thread's load does.
    /// </summary>
    [ThreadStatic]
    internal static bool PreservingIdentifiers;

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
