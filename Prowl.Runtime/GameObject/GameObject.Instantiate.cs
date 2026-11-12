// This file is part of the Prowl Game Engine
// Licensed under the MIT License. See the LICENSE file in the project root for details.

using System;

using Prowl.Echo;
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

        if (clone.IsNotValid())
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

        PrefabLink link = go.EnsurePrefabLink();
        link.AssetId = prefabAssetId;
        link.SourceIdentifier = go.Identifier;
        go.SetIdentifier(Guid.NewGuid());

        foreach (Component component in go._components)
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

    /// <summary>Runs a read with stored identifiers kept, for anything restored that must come back as the same object.</summary>
    internal static T PreservingIdentifiersWhile<T>(Func<T> read)
    {
        bool previous = PreservingIdentifiers;
        PreservingIdentifiers = true;
        try { return read(); }
        finally { PreservingIdentifiers = previous; }
    }

    /// <summary>
    /// Set while <see cref="DeserializePreservingIdentifiers"/> runs. Thread-local, so a load on one
    /// thread cannot change what another thread's load does.
    /// </summary>
    [ThreadStatic]
    internal static bool PreservingIdentifiers;

    /// <summary>Spawn a prefab into <see cref="Resources.SceneInstance.Active"/>, the scene whose code is running.</summary>
    public static GameObject? Instantiate(PrefabAsset prefab) => Instantiate(prefab, Resources.SceneInstance.Active);

    /// <summary>Spawn a prefab into <see cref="Resources.SceneInstance.Active"/> at a world position and rotation.</summary>
    public static GameObject? Instantiate(PrefabAsset prefab, Float3 position, Quaternion rotation)
        => Instantiate(prefab, Resources.SceneInstance.Active, position, rotation);

    /// <summary>
    /// Spawn a prefab as a child of <paramref name="parent"/>, in that parent's scene.
    /// </summary>
    /// <param name="prefab">The prefab to spawn.</param>
    /// <param name="parent">The object the spawned prefab is parented to. Null spawns it at the scene root.</param>
    /// <param name="worldPositionStays">Keep the prefab's own transform as a world transform rather
    /// than as an offset from the parent. Off by default, so the prefab lands where the parent is.</param>
    public static GameObject? Instantiate(PrefabAsset prefab, GameObject? parent, bool worldPositionStays = false)
    {
        if (parent.IsNotValid()) return Instantiate(prefab, Resources.SceneInstance.Active);
        GameObject? instance = InstantiateDetached(prefab);
        return instance.IsNotValid() ? null : AddUnder(instance!, parent!, worldPositionStays);
    }

    /// <summary>Spawn a prefab into a specific scene.</summary>
    public static GameObject? Instantiate(PrefabAsset prefab, SceneInstance? scene)
    {
        GameObject? instance = InstantiateDetached(prefab);
        if (instance.IsNotValid()) return null; // InstantiateDetached already reported why

        return AddToScene(instance, scene);
    }

    /// <summary>Spawn a prefab into a specific scene at a world position and rotation.</summary>
    public static GameObject? Instantiate(PrefabAsset prefab, SceneInstance? scene, Float3 position, Quaternion rotation)
    {
        GameObject? instance = InstantiateDetached(prefab);
        if (instance.IsNotValid()) return null;

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
        GameObject? clone = Clone(original);
        if (clone.IsNotValid()) return null;

        clone.Transform.Position = position;
        clone.Transform.Rotation = rotation;

        SceneInstance? scene = original.Scene;
        return scene.IsValid() ? AddToScene(clone, scene, original.Section) : AddToScene(clone, Resources.SceneInstance.Active);
    }

    /// <inheritdoc cref="Instantiate(PrefabAsset, GameObject?, bool)"/>
    public static GameObject? Instantiate(GameObject original, GameObject? parent, bool worldPositionStays = false)
    {
        if (parent.IsNotValid()) return Instantiate(original, original.IsValid() ? original.Scene : null);
        GameObject? clone = Clone(original);
        return clone.IsNotValid() ? null : AddUnder(clone!, parent!, worldPositionStays);
    }

    /// <summary>Copy an existing GameObject into a specific world, into the original's section when it is the original's world.</summary>
    public static GameObject? Instantiate(GameObject original, SceneInstance? scene)
    {
        GameObject? clone = Clone(original);
        if (clone.IsNotValid()) return null;
        return AddToScene(clone, scene, ReferenceEquals(scene, original.Scene) ? original.Section : null);
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
            return ObjectCopy.Clone(original);
        }
        catch (Exception ex)
        {
            Debug.LogError($"[Instantiate] Failed to copy '{original.Name}': {ex.Message}");
            return null;
        }
    }

    // Parented while still detached, so it joins the parent's world and section once, with its parent already set.
    private static GameObject? AddUnder(GameObject instance, GameObject parent, bool worldPositionStays)
    {
        if (!CanSpawnInto(instance, parent.Scene)) return null;
        instance.SetParent(parent, worldPositionStays);
        return instance;
    }

    private static GameObject? AddToScene(GameObject instance, SceneInstance? scene, SceneSection? section = null)
    {
        if (!CanSpawnInto(instance, scene)) return null;
        scene!.Add(instance, section != null && section.IsLoaded ? section : null);
        return instance;
    }

    private static bool CanSpawnInto(GameObject instance, SceneInstance? scene)
    {
        if (scene.IsNotValid() || scene!.State == SceneState.Destroying)
        {
            // Handing back a live-looking object that is in no world, gets no callbacks and never renders would be
            // worse than saying so. A death effect spawned during teardown lands here too.
            Debug.LogError(scene.IsValid()
                ? $"[Instantiate] '{scene!.Name}' is being destroyed, so '{instance.Name}' was not spawned."
                : $"[Instantiate] There is no world to spawn '{instance.Name}' into.");
            instance.Dispose();
            return false;
        }
        return true;
    }
}
