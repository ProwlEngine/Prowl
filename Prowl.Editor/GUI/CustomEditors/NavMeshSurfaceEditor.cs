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
/// that saves the result as a <c>.navmesh</c> asset in a SceneName_navmesh folder (mirroring
/// the lightmapper's SceneName_lightmaps convention) and assigns it to the surface, so the
/// baked navmesh survives scene reloads and ships with the project.
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

        // One Bake button (Unity-style). In edit mode it bakes to a .navmesh asset so the
        // result persists; during play it does an in-memory bake (baking to disk mid-play is
        // rarely intended). The code-only runtime API remains NavMeshSurface.BuildNavMesh().
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
            // Bake in memory first (also registers with the scene while playing/valid).
            if (!surface.BuildNavMesh())
                return;

            Runtime.NavMeshData? data = surface.NavMeshData.Res;
            if (data.IsNotValid()) return;

            var db = EditorAssetBackend.Instance;
            var scene = surface.GameObject.Scene;

            // A subfolder named after the scene, next to the scene asset, holding
            // "<SceneName> NavMesh.navmesh" (e.g. Assets/Scenes/Test Scene/Test Scene NavMesh.navmesh).
            string sceneRel = scene.IsValid() && !string.IsNullOrEmpty(scene!.AssetPath) ? scene.AssetPath : "";
            string sceneDir = string.IsNullOrEmpty(sceneRel) ? "" : (Path.GetDirectoryName(sceneRel) ?? "").Replace('\\', '/');
            string sceneName = string.IsNullOrEmpty(sceneRel) ? "Scene" : Path.GetFileNameWithoutExtension(sceneRel);
            string folderRel = (string.IsNullOrEmpty(sceneDir) ? "" : sceneDir + "/") + sceneName;
            string folderAbs = Path.Combine(Project.Current!.AssetsPath, folderRel);
            Directory.CreateDirectory(folderAbs);

            string fileRel = folderRel + "/" + Sanitize(sceneName + " NavMesh") + ".navmesh";
            string fileAbs = Path.Combine(Project.Current.AssetsPath, fileRel);
            data!.Name = Path.GetFileNameWithoutExtension(fileRel);
            File.WriteAllText(fileAbs, Serializer.Serialize(typeof(object), data).WriteToString());

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

    private static string Sanitize(string name)
    {
        foreach (char c in Path.GetInvalidFileNameChars())
            name = name.Replace(c, '_');
        return string.IsNullOrEmpty(name) ? "NavMesh" : name;
    }
}
