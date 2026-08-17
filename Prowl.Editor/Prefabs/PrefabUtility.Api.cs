// This file is part of the Prowl Game Engine
// Licensed under the MIT License. See the LICENSE file in the project root for details.

using System;
using System.Collections.Generic;
using System.Linq;

using Prowl.Editor.Core;
using Prowl.Editor.GUI.SceneView;
using Prowl.Runtime;
using Prowl.Runtime.Resources;

namespace Prowl.Editor.Prefabs;

/// <summary>
/// Queries and operations tools and custom editors need, which until now every caller worked out for
/// itself from the instance data.
/// </summary>
public static partial class PrefabUtility
{
    #region Events

    /// <summary>Raised after a prefab asset is written, with the asset's guid.</summary>
    public static event Action<Guid>? OnPrefabSaved;

    /// <summary>
    /// Raised after an instance has been brought back into line with its prefab. The objects are the
    /// same ones as before, so anything holding a reference to them stays valid and only needs to
    /// re-read what it displays.
    /// </summary>
    public static event Action<GameObject>? OnPrefabInstanceUpdated;

    /// <summary>Raised after a prefab is instantiated into the scene through the editor.</summary>
    public static event Action<GameObject>? OnPrefabInstantiated;

    internal static void RaisePrefabSaved(Guid prefabGuid) => OnPrefabSaved?.Invoke(prefabGuid);
    internal static void RaiseInstanceUpdated(GameObject instanceRoot) => OnPrefabInstanceUpdated?.Invoke(instanceRoot);
    internal static void RaiseInstantiated(GameObject instanceRoot) => OnPrefabInstantiated?.Invoke(instanceRoot);

    #endregion

    #region Queries

    /// <summary>
    /// True when this object belongs to a prefab instance, whether it is the root of one or sits
    /// inside it. Distinct from <see cref="GameObject.IsPrefabInstance"/>, which is per object.
    /// </summary>
    public static bool IsPartOfPrefabInstance(GameObject go)
        => go.IsValid() && GetPrefabInstanceRoot(go).IsValid();

    /// <summary>
    /// True when this object belongs to the prefab currently open for editing, rather than to an
    /// instance of one placed in a scene.
    /// </summary>
    public static bool IsPartOfPrefabAsset(GameObject go)
    {
        if (go.IsNotValid() || !PrefabEditingMode.IsEditing) return false;

        var scene = go.Scene;
        return scene.IsValid() && ReferenceEquals(scene, Scene.Current);
    }

    /// <summary>Every instance root of a prefab in the current scene.</summary>
    public static List<GameObject> FindInstancesOf(Guid prefabGuid)
    {
        var scene = Scene.Current;
        return scene == null ? [] : FindInstancesOf(prefabGuid, scene);
    }

    /// <summary>Every instance root of a prefab in a given scene, open or not.</summary>
    public static List<GameObject> FindInstancesOf(Guid prefabGuid, Scene scene)
    {
        if (scene.IsNotValid() || prefabGuid == Guid.Empty) return [];

        return scene.AllObjects
            .Where(go => go.PrefabAssetId == prefabGuid && IsInstanceRoot(go))
            .ToList();
    }

    /// <summary>
    /// The object in the prefab that this one came from, or null when it is not part of an instance or
    /// the prefab no longer provides it.
    /// <para/>
    /// This is the primitive the rest of the prefab operations are expressed in: an override path, a
    /// structural comparison and a revert all start by asking which source object something is.
    /// </summary>
    public static GameObject? GetCorrespondingObjectFromSource(GameObject go)
    {
        if (go.IsNotValid()) return null;

        GameObject? instanceRoot = GetPrefabInstanceRoot(go);
        if (instanceRoot == null) return null;

        GameObject? source = GetCachedPrefabSource(instanceRoot.PrefabAssetId);
        if (source == null) return null;

        Guid sourceId = go.SourceIdentifier;
        return sourceId == Guid.Empty ? null : FindBySourceIdentifier(source, sourceId, source.PrefabAssetId);
    }

    /// <inheritdoc cref="GetCorrespondingObjectFromSource(GameObject)"/>
    public static MonoBehaviour? GetCorrespondingObjectFromSource(MonoBehaviour component)
    {
        if (component.IsNotValid()) return null;

        GameObject owner = component.GameObject;
        if (owner.IsNotValid()) return null;

        Guid sourceId = owner.GetComponentSourceIdentifier(component);
        if (sourceId == Guid.Empty) return null;

        GameObject? sourceObject = GetCorrespondingObjectFromSource(owner);
        if (sourceObject == null) return null;

        return sourceObject.GetComponents<MonoBehaviour>()
            .FirstOrDefault(c => sourceObject.GetComponentSourceIdentifier(c) == sourceId);
    }

    #endregion

    #region The override table

    /// <summary>
    /// What this instance overrides, as a copy. Overrides live on the instance root, so this reports
    /// the whole instance's set whichever of its objects it is handed.
    /// </summary>
    public static List<PropertyOverride> GetPropertyModifications(GameObject go)
    {
        GameObject? instanceRoot = GetPrefabInstanceRoot(go);
        return instanceRoot == null ? [] : instanceRoot.PrefabOverrides.ToList();
    }

    /// <summary>
    /// Replace what this instance overrides and bring it back into line with the result, so the
    /// objects show the values that were just set rather than whatever they held before.
    /// <para/>
    /// Only this instance is touched. The others are instances of the same prefab, not of this one,
    /// and nothing about their overrides changed.
    /// </summary>
    public static void SetPropertyModifications(GameObject go, IEnumerable<PropertyOverride> modifications)
    {
        GameObject? instanceRoot = GetPrefabInstanceRoot(go);
        if (instanceRoot == null) return;
        if (!GuardNotPlaying("change prefab overrides")) return;

        List<PropertyOverride> before = instanceRoot.PrefabOverrides.ToList();
        List<PropertyOverride> after = modifications.ToList();
        Guid rootId = instanceRoot.Identifier;

        Apply(after);

        Undo.RegisterAction("Set Prefab Overrides",
            undo: () => Apply(before),
            redo: () => Apply(after));

        EditorSceneManager.MarkDirty();

        void Apply(List<PropertyOverride> overrides)
        {
            GameObject? live = Undo.FindGO(rootId);
            if (live.IsNotValid()) return;

            live!.PrefabOverrides = overrides.ToList();
            RefreshOneInstance(live);
        }
    }

    #endregion

    #region Linking

    /// <summary>
    /// Make an object an instance of a prefab it is not currently linked to, matching its objects to
    /// the prefab's by position. This is the recovery path for an instance whose asset was replaced or
    /// whose link was broken, which otherwise has no way back.
    /// </summary>
    public static bool ConnectGameObjectToPrefab(GameObject go, Guid prefabGuid)
    {
        if (go.IsNotValid()) return false;
        if (!GuardNotPlaying("connect a prefab")) return false;

        if (AssetDatabase.Get(prefabGuid) is not PrefabAsset)
        {
            Runtime.Debug.LogWarning("[Prefab] Cannot connect there is no prefab with that id.");
            return false;
        }

        GameObject? source = GetCachedPrefabSource(prefabGuid);
        if (source == null) return false;

        var previous = CapturePrefabState(go, go.PrefabAssetId);

        var unmatched = new List<string>();
        AdoptSourceIdentities(go, source, prefabGuid, unmatched);
        ReconcileInstance(go);

        // Matching by position gets most of a tree that drifted, and none of a tree that is simply a
        // different shape. Either way the caller is told what did not line up.
        if (unmatched.Count > 0)
            Runtime.Debug.LogWarning($"[Prefab] Connected, but {unmatched.Count} part(s) had no counterpart in the prefab " +
                $"and will not track overrides: {string.Join(", ", unmatched)}");

        Undo.RegisterAction("Connect To Prefab",
            undo: () => RestorePrefabState(previous),
            redo: () => { AdoptSourceIdentities(go, source, prefabGuid, []); ReconcileInstance(go); });

        EditorSceneManager.MarkDirty();
        return true;
    }

    /// <summary>
    /// Point an unlinked tree at a prefab by walking both in step. Position is all there is to go on:
    /// an object with no link has no record of where it came from.
    /// </summary>
    /// <param name="unmatched">
    /// Collects what could not be paired up. A relink that matched only part of the tree still leaves
    /// a working instance, but the parts it skipped can never record or revert an override, so the
    /// caller has something to say rather than reporting a clean success.
    /// </param>
    private static void AdoptSourceIdentities(GameObject go, GameObject source, Guid prefabGuid, List<string> unmatched)
    {
        PrefabLink link = go.EnsurePrefabLink();
        link.AssetId = prefabGuid;
        link.SourceIdentifier = source.SourceIdentifier;
        link.ComponentSources.Clear();

        var components = go.GetComponents<MonoBehaviour>().ToList();
        var sourceComponents = source.GetComponents<MonoBehaviour>().ToList();

        for (int i = 0; i < components.Count; i++)
        {
            if (i >= sourceComponents.Count || components[i].GetType() != sourceComponents[i].GetType())
            {
                unmatched.Add($"{go.Name} > {components[i].GetType().Name}");
                continue;
            }

            link.ComponentSources[components[i].Identifier] = source.GetComponentSourceIdentifier(sourceComponents[i]);
        }

        for (int i = 0; i < go.Children.Count; i++)
        {
            if (i >= source.Children.Count)
            {
                unmatched.Add(go.Children[i].Name);
                continue;
            }

            AdoptSourceIdentities(go.Children[i], source.Children[i], prefabGuid, unmatched);
        }
    }

    #endregion

    #region Saving

    /// <summary>
    /// Write a GameObject out as a prefab asset without turning it into an instance of what was just
    /// written. <see cref="CreatePrefab"/> does both, which is what an author usually wants and is
    /// wrong for a tool generating assets from objects it then discards.
    /// </summary>
    public static bool SaveAsPrefabAsset(GameObject source, string relativeSavePath, bool overwrite = false)
    {
        var previous = source.IsValid() ? CapturePrefabState(source, source.PrefabAssetId) : null;

        if (!SaveAsPrefabAssetAndConnect(source, relativeSavePath, overwrite)) return false;

        // CreatePrefab links its argument to the asset it wrote; put it back the way it was.
        if (previous != null) RestorePrefabState(previous);
        return true;
    }

    #endregion
}
