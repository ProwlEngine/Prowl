// This file is part of the Prowl Game Engine
// Licensed under the MIT License. See the LICENSE file in the project root for details.

using System;
using System.IO;

using Prowl.Echo;
using Prowl.Editor.Core;
using Prowl.Editor.GUI;
using Prowl.Editor.GUI.SceneView;
using Prowl.Editor.Projects;
using Prowl.Editor.Theming;
using Prowl.OrigamiUI;
using Prowl.PaperUI;
using Prowl.Runtime;

namespace Prowl.Editor.Inspector;

/// <summary>
/// Inspector for <see cref="NavMeshSurface"/>: the default property grid plus an editor bake
/// that saves the result as a <c>.navmesh</c> asset and assigns it to the surface, so the baked
/// navmesh survives scene reloads and ships with the project.
/// </summary>
[CustomEditor(typeof(NavMeshSurface))]
public class NavMeshSurfaceEditor : CustomEditor
{
    public override void OnGUI(Paper paper, string id, object target)
    {
        var surface = (NavMeshSurface)target;

        // Pre-snapshot: captures entire component state before any widget mutates it, so
        // default-grid edits, the Advanced fields, and the Reset button all undo uniformly.
        Undo.Snapshot(surface);

        // Basic fields (agent type, collection, layers, geometry, data ref) via the default
        // grid; DefaultArea and the build overrides are [HideInInspector] and drawn below —
        // the same basic/advanced split Unity's NavMeshSurface uses.
        DrawDefaultInspector(paper, $"{id}_def", target);

        paper.Box($"{id}_adv_sp").Height(4);
        Origami.Foldout(paper, $"{id}_adv", "Advanced").Body(() =>
        {
            NavMeshAreaAttributeHandler.DrawAreaField(paper, $"{id}_adv_area", "Default Area", surface.DefaultArea, v =>
            {
                surface.DefaultArea = v;
                EditorSceneManager.MarkDirty();
            });

            PropertyGridUtils.Draw(paper, $"{id}_adv_ovr", surface.BuildOverrides,
                _ => EditorSceneManager.MarkDirty());

            paper.Box($"{id}_adv_reset_sp").Height(4);
            Origami.Button(paper, $"{id}_adv_reset", "Reset Advanced To Defaults", () =>
            {
                surface.BuildOverrides = new NavMeshBuildOverrides();
                surface.DefaultArea = NavMeshAreas.Walkable;
                EditorSceneManager.MarkDirty();
            }).Show();
        });

        paper.Box($"{id}_sp").Height(6);
        Origami.Header(paper, $"{id}_bake_hdr", $"{EditorIcons.Map}  Baking").Underline().Show();

        // Only one surface per agent type registers; name the winner before play mode makes it obvious.
        NavMeshSurface? rival = FindRival(surface);
        bool shadowed = rival != null && surface.Instance == null && rival.Instance != null;
        string? notice = RegistrationNotice(surface, rival, shadowed);
        if (notice != null)
            Origami.Label(paper, $"{id}_rival", notice).Warning().Show();

        // One Bake button (Unity-style). In edit mode it bakes to a .navmesh asset so the
        // result persists; during play it does an in-memory bake (baking to disk mid-play is
        // rarely intended). The code-only runtime API remains NavMeshSurface.BuildNavMesh().
        // Enabled even on an ignored surface: the bake produces its asset, which is worth having
        // before the agent type that makes it live is assigned.
        Origami.Button(paper, $"{id}_bake", "Bake NavMesh", () =>
        {
            if (Application.IsPlaying) surface.BuildNavMesh();
            else BakeToAsset(surface);
        }).Show();

        var data = surface.NavMeshData.Res;
        if (data.IsValid() && data!.HasTiles)
        {
            Origami.Button(paper, $"{id}_clear", $"{EditorIcons.Trash}  Clear", () => Clear(surface)).Show();

            paper.Box($"{id}_sp2").Height(4);
            Origami.Label(paper, $"{id}_stats",
                $"{data.CacheLayers.Count} cache layers · agent r={data.Settings.AgentRadius:0.##} h={data.Settings.AgentHeight:0.##} · voxel {data.Settings.EffectiveVoxelSize:0.###} · tile {data.Settings.EffectiveTileSize}")
                .Show();
        }
    }

    // The bake's agent type, not the field's: the field can be edited after baking, and
    // registration keys off the data.
    private static int RegistrationKey(NavMeshSurface surface)
    {
        Runtime.NavMeshData? data = surface.NavMeshData.Res;
        return data.IsValid() ? data!.Settings.AgentTypeId : surface.AgentTypeId;
    }

    /// <summary>Another enabled surface that would register under the same agent type. Disabled
    /// surfaces are excluded: they have no registration to lose.</summary>
    private static NavMeshSurface? FindRival(NavMeshSurface surface)
    {
        var scene = surface.Scene;
        if (!scene.IsValid() || !surface.EnabledInHierarchy) return null;

        int key = RegistrationKey(surface);
        foreach (NavMeshSurface other in scene!.Navigation.Surfaces)
            if (!ReferenceEquals(other, surface) && RegistrationKey(other) == key)
                return other;
        return null;
    }

    /// <summary>Null when there is nothing to warn about.</summary>
    private static string? RegistrationNotice(NavMeshSurface surface, NavMeshSurface? rival, bool shadowed)
    {
        string agentType = NavMeshAgentTypes.GetName(RegistrationKey(surface));
        if (shadowed)
            return $"'{rival!.GameObject.Name}' provides the {agentType} navmesh; this surface is ignored. Bake as a single surface, or give them separate agent types.";
        if (rival != null && surface.Instance != null)
            return $"'{rival.GameObject.Name}' also targets the {agentType} navmesh and is ignored; one surface per agent type per scene.";
        if (rival != null)
            return $"'{rival.GameObject.Name}' also targets the {agentType} navmesh; only one of these will be used.";

        // Baked, enabled, and still unregistered with no rival to blame: something else holds the
        // type, so the bake is invisible until it is redone under a type that is free.
        Runtime.NavMeshData? data = surface.NavMeshData.Res;
        if (surface.EnabledInHierarchy && surface.Instance == null && data.IsValid() && data!.HasTiles)
            return $"This bake targets {agentType}, which already has a navmesh; this surface is ignored.";
        return null;
    }

    /// <summary>Unregister and drop the surface's baked data reference. The .navmesh file (if
    /// any) is left on disk; delete it from the Assets panel to remove it fully.</summary>
    private static void Clear(NavMeshSurface surface)
    {
        surface.NavMeshData = default;
        surface.RefreshRegistration();
        EditorSceneManager.MarkDirty();
    }

    private static void BakeToAsset(NavMeshSurface surface)
    {
        try
        {
            // Bake without registering: the asset is imported and registered below, and
            // registering the in-memory copy too would mesh every tile a second time.
            Runtime.NavMeshData? data = surface.BuildNavMeshData();
            if (data.IsNotValid()) return;

            var db = EditorAssetBackend.Instance;
            string fileRel = BakePath(surface);
            string fileAbs = Path.Combine(Project.Current!.AssetsPath, fileRel);
            string? dirAbs = Path.GetDirectoryName(fileAbs);
            if (!string.IsNullOrEmpty(dirAbs))
                Directory.CreateDirectory(dirAbs);
            data!.Name = Path.GetFileNameWithoutExtension(fileRel);
            Serializer.Serialize(typeof(object), data).WriteToBinary(new FileInfo(fileAbs));

            Guid guid = db.ImportFile(fileRel);
            if (guid == Guid.Empty)
            {
                Runtime.Debug.LogError($"[Navigation] Failed to import baked navmesh at {fileRel}.");
                return;
            }

            surface.NavMeshData = new AssetRef<Runtime.NavMeshData>(guid);
            surface.RefreshRegistration();
            EditorSceneManager.MarkDirty();
            Runtime.Debug.Log($"[Navigation] Baked navmesh saved to {fileRel} ({data.CacheLayers.Count} cache layers).");
        }
        catch (Exception e)
        {
            Runtime.Debug.LogError($"[Navigation] Bake failed: {e.Message}\n{e.StackTrace}");
        }
    }

    // Reuses the assigned asset's path, found by guid, so a file the user renamed is rebaked in
    // place rather than orphaned beside a freshly named one.
    internal static string BakePath(NavMeshSurface surface)
    {
        string? assigned = EditorAssetBackend.Instance.GuidToPath(surface.NavMeshData.AssetID);
        return string.IsNullOrEmpty(assigned) ? DefaultBakePath(surface) : assigned;
    }

    // The agent type id is in the name because names alone can collide: they are deduplicated
    // case-sensitively and Sanitize folds path separators.
    private static string DefaultBakePath(NavMeshSurface surface)
    {
        var scene = surface.GameObject.Scene;
        string sceneRel = scene.IsValid() && !string.IsNullOrEmpty(scene!.AssetPath) ? scene.AssetPath : "";
        string sceneDir = string.IsNullOrEmpty(sceneRel) ? "" : (Path.GetDirectoryName(sceneRel) ?? "").Replace('\\', '/');
        string sceneName = string.IsNullOrEmpty(sceneRel) ? "Scene" : Path.GetFileNameWithoutExtension(sceneRel);
        string folderRel = (string.IsNullOrEmpty(sceneDir) ? "" : sceneDir + "/") + sceneName;
        string agentType = NavMeshAgentTypes.GetName(surface.AgentTypeId);
        return folderRel + "/" + Sanitize($"{sceneName} NavMesh ({agentType} {surface.AgentTypeId})") + ".navmesh";
    }

    private static string Sanitize(string name)
    {
        foreach (char c in Path.GetInvalidFileNameChars())
            name = name.Replace(c, '_');
        return string.IsNullOrEmpty(name) ? "NavMesh" : name;
    }
}
