// This file is part of the Prowl Game Engine
// Licensed under the MIT License. See the LICENSE file in the project root for details.

using System;
using System.IO;
using System.Threading.Tasks;

using Prowl.Editor.Core;
using Prowl.Editor.GUI;
using Prowl.Editor.GUI.SceneView;
using Prowl.Editor.Projects;
using Prowl.Editor.Theming;
using Prowl.OrigamiUI;
using Prowl.PaperUI;
using Prowl.Runtime;
using Prowl.Runtime.Navigation;
using Prowl.Runtime.Resources;

namespace Prowl.Editor.Inspector;

/// <summary>
/// Inspector for <see cref="NavMeshSurface"/>. Baking is editor-only: the Bake button collects
/// geometry from the surface's own scene (on the main thread - the scene and its components aren't
/// safe to touch from anywhere else), runs the actual Recast pipeline on a thread pool thread via
/// <see cref="NavMeshBakeJob"/>, and once <see cref="NavMeshBakePump"/> picks up the finished result on
/// a later editor frame, writes it to a <c>.navmesh</c> asset next to the scene file and points the
/// surface at it.
/// </summary>
[CustomEditor(typeof(NavMeshSurface))]
public class NavMeshSurfaceEditor : CustomEditor
{
    /// <summary>Draws the agent-type dropdown, the default inspector, a stale-bake warning, and the
    /// Bake button (or a "Baking..." label while one is in flight).</summary>
    public override void OnGUI(Paper paper, string id, object target)
    {
        var surface = (NavMeshSurface)target;
        var font = EditorTheme.DefaultFont;
        if (font == null) return;

        NavMeshAgentTypeDropdown.Draw(paper, $"{id}_type", font, surface.AgentTypeId, v =>
        {
            Undo.Snapshot(surface);
            surface.AgentTypeId = v;
        });

        DrawDefaultInspector(paper, $"{id}_def", target);

        paper.Box($"{id}_sp").Height(6);

        bool baking = NavMeshBakePump.IsBaking(surface);

        if (!baking && surface.IsStale)
        {
            paper.Box($"{id}_stale").Height(EditorTheme.RowHeight)
                .Text("Baked settings differ from current settings - rebake to apply changes.", font)
                .TextColor(EditorTheme.Amber500).FontSize(EditorTheme.FontSize)
                .Alignment(TextAlignment.MiddleLeft);
        }

        if (baking)
        {
            paper.Box($"{id}_baking").Height(EditorTheme.RowHeight)
                .Text("Baking...", font).FontSize(EditorTheme.FontSize).Alignment(TextAlignment.MiddleLeft);
        }
        else
        {
            Origami.Button(paper, $"{id}_bake", $"{EditorIcons.WandMagicSparkles}  Bake", () => StartBake(surface)).Show();
        }
    }

    /// <summary>Collects geometry and snapshots settings on the main thread, then kicks off the actual
    /// bake in the background via <see cref="NavMeshBakeJob"/>, registering its completion with
    /// <see cref="NavMeshBakePump"/>.</summary>
    private static void StartBake(NavMeshSurface surface)
    {
        Scene? scene = surface.Scene;
        if (scene.IsNotValid())
        {
            Debug.LogError("[NavMeshSurface] Cannot bake: this component is not in an active scene.");
            return;
        }

        // Both of these read live, mutable state (the scene's components, the agent-type/area
        // registries) and must happen here, on the main thread, before any of it crosses onto the
        // background thread NavMeshBakeJob runs on.
        NavMeshBuildInput input = NavMeshGeometryCollector.Collect(scene, surface);
        NavMeshBakeSettings settings = surface.EffectiveBakeSettings;
        int agentTypeId = surface.AgentTypeId;

        Task<NavMeshBuildResult> task = NavMeshBakeJob.RunAsync(input, settings);
        NavMeshBakePump.Enqueue(surface, task, result => FinishBake(surface, result, settings, agentTypeId));
    }

    /// <summary>Runs on the main thread once <see cref="NavMeshBakePump"/> observes the background
    /// bake has finished: writes the result to a <c>.navmesh</c> asset and points <paramref name="surface"/>
    /// at it.</summary>
    private static void FinishBake(NavMeshSurface surface, NavMeshBuildResult result, NavMeshBakeSettings settings, int agentTypeId)
    {
        if (surface.IsNotValid()) return; // destroyed while its bake was in flight

        if (!result.Success)
        {
            Debug.LogError($"[NavMeshSurface] Bake failed: {result.Error}");
            return;
        }

        string? relativePath = ResolveTargetPath(surface);
        if (relativePath == null)
        {
            Debug.LogError("[NavMeshSurface] Cannot bake: save the scene first so the navmesh has somewhere to live next to it.");
            return;
        }

        // Tagged with the settings the bake actually ran with, not whatever the surface's settings
        // happen to be now - they could have changed while this bake was in flight on another thread.
        var asset = new NavMesh { Name = Path.GetFileNameWithoutExtension(relativePath) };
        asset.Apply(result, settings, agentTypeId);

        Echo.EchoObject? echo = Echo.Serializer.Serialize(typeof(object), asset);
        if (echo == null)
        {
            Debug.LogError("[NavMeshSurface] Bake failed: could not serialize the baked navmesh.");
            return;
        }

        string absolutePath = Path.Combine(Project.Current!.AssetsPath, relativePath);
        File.WriteAllText(absolutePath, echo.WriteToString());

        Guid guid = EditorAssetBackend.Instance!.ImportFile(relativePath);
        if (guid == Guid.Empty)
        {
            Debug.LogError("[NavMeshSurface] Bake succeeded but the resulting asset could not be imported.");
            return;
        }

        Undo.Snapshot(surface);
        surface.NavMeshAsset = new AssetRef<NavMesh>(guid);
        surface.RebuildQuery();
        EditorSceneManager.MarkDirty();

        Debug.Log($"[NavMeshSurface] Baked {result.TileCacheData!.Layers.Count} tile(s) to {relativePath}.");
    }

    /// <summary>Reuses the path of an already-assigned navmesh asset (so a rebake overwrites it in
    /// place), otherwise picks a fresh name in the current scene's folder.</summary>
    private static string? ResolveTargetPath(NavMeshSurface surface)
    {
        if (surface.NavMeshAsset.AssetID != Guid.Empty)
        {
            string? existingPath = EditorAssetBackend.Instance?.GuidToPath(surface.NavMeshAsset.AssetID);
            if (existingPath != null)
                return existingPath;
        }

        string? scenePath = EditorSceneManager.CurrentScenePath;
        if (string.IsNullOrEmpty(scenePath))
            return null;

        string folder = Path.GetDirectoryName(scenePath.Replace('\\', '/'))?.Replace('\\', '/') ?? "";
        string baseName = surface.GameObject.Name;

        string candidate = string.IsNullOrEmpty(folder) ? $"{baseName}.navmesh" : $"{folder}/{baseName}.navmesh";
        int suffix = 1;
        while (File.Exists(Path.Combine(Project.Current!.AssetsPath, candidate)))
        {
            candidate = string.IsNullOrEmpty(folder) ? $"{baseName} {suffix}.navmesh" : $"{folder}/{baseName} {suffix}.navmesh";
            suffix++;
        }

        return candidate;
    }
}
