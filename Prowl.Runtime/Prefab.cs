// This file is part of the Prowl Game Engine
// Licensed under the MIT License. See the LICENSE file in the project root for details.

using System;

using Prowl.Runtime.Resources;

namespace Prowl.Runtime;

/// <summary>
/// Static utility for runtime prefab instantiation.
/// Works in both editor and standalone builds.
/// </summary>
public static class Prefab
{
    /// <summary>
    /// Instantiate a prefab from its asset GUID.
    /// Returns a GameObject ready to be added to a scene.
    /// </summary>
    public static GameObject? Instantiate(Guid prefabAssetId)
    {
        var prefab = AssetDatabase.Get(prefabAssetId) as PrefabAsset;
        if (prefab == null)
        {
            Debug.LogWarning($"[Prefab] Failed to load prefab asset {prefabAssetId}");
            return null;
        }
        return prefab.Instantiate();
    }

    /// <summary>
    /// Instantiate a prefab from an AssetRef.
    /// </summary>
    public static GameObject? Instantiate(AssetRef<PrefabAsset> prefabRef)
    {
        var prefab = prefabRef.Res;
        return prefab?.Instantiate();
    }
}
