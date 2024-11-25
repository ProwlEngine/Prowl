// This file is part of the Prowl Game Engine
// Licensed under the MIT License. See the LICENSE file in the project root for details.

using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace Prowl.Runtime.Utils;

public static class PrefabUtility
{
    public static bool IsPrefabSource(GameObject go) => go.AffectedByPrefabLink != null && go.AffectedByPrefabLink.IsSource(go);
    public static bool IsOnPrefabInstance(GameObject go) => go.AffectedByPrefabLink != null;

    /// <summary>
    /// Is true if this MonoBehaviour is a part of the prefab source, False if not.
    /// </summary>
    /// <returns> True if this MonoBehaviour is on a prefab and a part of said prefab's source, False if not.</returns>
    public static bool IsPrefabSource(MonoBehaviour comp) => comp.GameObject.AffectedByPrefabLink != null && comp.GameObject.AffectedByPrefabLink.IsSource(comp);

    /// <summary>
    /// Is true if this MonoBehaviour is a part of a prefab instance, False if not.
    /// </summary>
    /// <returns> True if this MonoBehaviour is on a prefab and a part of a instance of said prefab, False if not.</returns>
    public static bool IsOnPrefabInstance(MonoBehaviour comp) => comp.GameObject.AffectedByPrefabLink != null;

    /// <summary>
    /// Is true if this MonoBehaviour has been modified from the prefab source, False if not.
    /// </summary>
    /// <returns> True if this MonoBehaviour is on a prefab and has been modified from the source of said prefab, False if not.</returns>
    public static bool HasPrefabMod(MonoBehaviour comp) => comp.GameObject.AffectedByPrefabLink != null && comp.GameObject.AffectedByPrefabLink.HasChange(comp, null);

    /// <summary>
    /// Sets or alters a GameObject's <see cref="PrefabLink"/> to reference the specified <see cref="Prefab"/>.
    /// </summary>
    /// <param name="go">The GameObject that will be linked to the Prefab.</param>
    /// <param name="prefab">The Prefab that will be linked to.</param>
    public static void LinkToPrefab(GameObject go, AssetRef<Prefab> prefab)
    {
        if (go.PrefabLink == null)
        {
            // Not affected by another (higher) PrefabLink
            if (go.AffectedByPrefabLink == null)
            {
                go.PrefabLink = new PrefabLink(go, prefab);
                // If a nested object is already PrefabLinked, add it to the change list
                foreach (GameObject child in go.GetChildrenDeep())
                {
                    if (child.PrefabLink != null && child.PrefabLink.ParentLink == go.PrefabLink)
                    {
                        go.PrefabLink.PushChange(child, nameof(GameObject.PrefabLink), child.PrefabLink.Clone());
                    }
                }
            }
            // Already affected by another (higher) PrefabLink
            else
            {
                go.PrefabLink = new PrefabLink(go, prefab);
                go.PrefabLink.ParentLink.RelocateChanges(go.PrefabLink);
            }
        }
        else
            go.PrefabLink = go.PrefabLink.Clone(go, prefab);
    }

    /// <summary>
    /// Breaks a GameObject's <see cref="PrefabLink"/>
    /// </summary>
    public static void BreakPrefabLink(GameObject go)
    {
        go.PrefabLink = null;
    }
}
