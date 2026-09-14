// This file is part of the Prowl Game Engine
// Licensed under the MIT License. See the LICENSE file in the project root for details.

using Prowl.Editor.Core;
using Prowl.Editor.GUI;
using Prowl.Editor.GUI.PropertyEditors;
using Prowl.Editor.GUI.SceneView;
using Prowl.Editor.Navigation;
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
        // grid; DefaultArea and the build overrides are [HideInInspector] and drawn below.
        DrawDefaultInspector(paper, $"{id}_def", target);

        paper.Box($"{id}_adv_sp").Height(4);
        Origami.Foldout(paper, $"{id}_adv", "Advanced").Body(() =>
        {
            NavMeshAreaPropertyEditor.DrawAreaField(paper, $"{id}_adv_area", "Default Area", surface.DefaultArea, v =>
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
        string? notice = RegistrationNotice(surface, rival);
        if (notice != null)
            Origami.Label(paper, $"{id}_rival", notice).Warning().Show();

        // In edit mode the bake runs in the background and saves a .navmesh asset so the result
        // persists; during play it bakes in memory. Enabled even on an ignored surface: the asset is
        // worth having before the agent type that makes it live is assigned.
        NavMeshBakeService bake = NavMeshBakeService.Instance;
        if (bake.IsBaking && ReferenceEquals(bake.TargetSurface, surface))
        {
            Origami.Label(paper, $"{id}_bake_status", $"{bake.Status} ({bake.Elapsed.TotalSeconds:0.0}s)").Show();
            Origami.Button(paper, $"{id}_bake_cancel", $"{EditorIcons.Xmark}  Cancel", bake.Cancel).Show();
            return;
        }

        Origami.Button(paper, $"{id}_bake", "Bake NavMesh", () =>
        {
            if (Application.IsPlaying) surface.BuildNavMesh();
            else if (!bake.Start(surface)) Runtime.Debug.LogWarning("[Navigation] Another navmesh is already baking.");
        }).Show();

        var data = surface.NavMeshData.Res;
        if (data.IsValid() && data!.HasTiles)
        {
            Origami.Button(paper, $"{id}_clear", $"{EditorIcons.Trash}  Clear", () => NavMeshBakeService.Clear(surface)).Show();

            paper.Box($"{id}_sp2").Height(4);
            Origami.Label(paper, $"{id}_stats",
                $"{data.CacheLayers.Count} cache layers · agent r={data.Settings.Agent.Radius:0.##} h={data.Settings.Agent.Height:0.##} · voxel {data.Settings.EffectiveVoxelSize:0.###} · tile {data.Settings.EffectiveTileSize}")
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
    private static string? RegistrationNotice(NavMeshSurface surface, NavMeshSurface? rival)
    {
        string agentType = NavMeshAgentTypes.GetName(RegistrationKey(surface));
        if (rival != null && surface.Instance == null && rival.Instance != null)
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
}
