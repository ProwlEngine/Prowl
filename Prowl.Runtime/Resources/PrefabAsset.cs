// This file is part of the Prowl Game Engine
// Licensed under the MIT License. See the LICENSE file in the project root for details.

using System;
using System.Linq;

using Prowl.Echo;

namespace Prowl.Runtime.Resources;

/// <summary>
/// A prefab asset containing a serialized GameObject hierarchy.
/// The GO tree is stored as raw EchoObject data and instantiated on demand.
/// </summary>
public class PrefabAsset : EngineObject
{
    /// <summary>The serialized GameObject hierarchy in Echo format.</summary>
    [SerializeField]
    public EchoObject GameObjectData;

    /// <summary>
    /// Instantiate a live GameObject from this prefab.
    /// The returned GO has PrefabAssetId set to this asset's GUID.
    /// Add it to a scene to make it active.
    /// </summary>
    public GameObject? Instantiate()
    {
        EnsureNotDisposed();
        if (GameObjectData == null) return null;

        // A prefab cannot reference objects outside itself, so anything the editor recorded as an
        // external reference instantiates as null rather than as an empty object built from the stub.
        var clone = Serializer.Deserialize<GameObject>(GameObjectData,
            new SerializationContext { ExternalReferences = SceneReferenceResolver.None });
        if (clone == null) return null;

        // Stamp all GOs with this prefab's asset ID
        StampPrefabId(clone, AssetID);

        return clone;
    }

    /// <summary>
    /// Stamps PrefabAssetId on a GO and its children.
    /// Skips children that already have a different PrefabAssetId (nested prefab instances).
    /// </summary>
    private static void StampPrefabId(GameObject go, Guid prefabAssetId)
    {
        go.PrefabAssetId = prefabAssetId;

        // Only the editor reads these, and they are what the structural rules in the hierarchy and
        // inspector are enforced from. A player pays a walk per spawn for state nothing consults.
        if (Application.IsEditor)
        {
            go.PrefabComponentCount = go._components.Count;
            go.PrefabChildCount = go.Children.Count;
        }

        foreach (var child in go.Children)
        {
            // Don't overwrite nested prefab instances
            if (child.IsPrefabInstance && child.PrefabAssetId != prefabAssetId)
                continue;
            StampPrefabId(child, prefabAssetId);
        }
    }
}
