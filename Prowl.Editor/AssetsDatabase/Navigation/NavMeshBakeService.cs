// This file is part of the Prowl Game Engine
// Licensed under the MIT License. See the LICENSE file in the project root for details.

using System;
using System.Diagnostics;
using System.IO;
using System.Threading;
using System.Threading.Tasks;

using Prowl.Echo;
using Prowl.Editor.GUI.SceneView;
using Prowl.Editor.Projects;
using Prowl.Runtime;

namespace Prowl.Editor.Navigation;

/// <summary>
/// Bakes a <see cref="NavMeshSurface"/> in the background and saves the result as a <c>.navmesh</c>
/// asset in a SceneName_navmesh folder beside the scene, then assigns it to the surface. One bake runs
/// at a time.
/// </summary>
public sealed class NavMeshBakeService
{
    public static NavMeshBakeService Instance { get; } = new();

    private NavMeshSurface? _surface;
    private Task<NavMeshData?>? _task;
    private CancellationTokenSource? _cancellation;
    private readonly Stopwatch _timer = new();

    public bool IsBaking => _task != null;
    public string Status { get; private set; } = "Idle";

    /// <summary>The surface the in-flight bake belongs to, or null when idle.</summary>
    public NavMeshSurface? TargetSurface => _surface;

    public TimeSpan Elapsed => _timer.Elapsed;

    /// <summary>Collect the surface's geometry and start voxelizing it off the main thread. False while another bake runs.</summary>
    public bool Start(NavMeshSurface surface)
    {
        ArgumentNullException.ThrowIfNull(surface);
        if (IsBaking) return false;

        _surface = surface;
        _cancellation = new CancellationTokenSource();
        _task = surface.BuildNavMeshAsync(_cancellation.Token);
        _timer.Restart();
        Status = "Baking";
        return true;
    }

    /// <summary>Call once per editor frame. Saves and assigns the result once the build finishes.</summary>
    public void Poll()
    {
        if (_task == null || !_task.IsCompleted) return;

        Task<NavMeshData?> task = _task;
        NavMeshSurface? surface = _surface;
        Cleanup();

        if (task.IsFaulted)
        {
            Status = "Failed";
            Runtime.Debug.LogError($"[Navigation] Bake failed: {task.Exception!.GetBaseException()}");
            return;
        }
        if (surface.IsNotValid())
        {
            Status = "Discarded";
            Runtime.Debug.LogWarning("[Navigation] The surface was destroyed while its navmesh baked; the result was discarded.");
            return;
        }
        if (task.Result == null)
        {
            Status = "Nothing to bake";
            Runtime.Debug.LogWarning($"[Navigation] Bake of '{surface.GameObject.Name}' produced no walkable geometry.");
            return;
        }

        Status = Save(surface, task.Result) ? "Done" : "Failed";
    }

    public void Cancel()
    {
        if (_task == null) return;
        _cancellation!.Cancel();
        Cleanup();
        Status = "Cancelled";
    }

    private void Cleanup()
    {
        _timer.Stop();
        _cancellation?.Dispose();
        _cancellation = null;
        _task = null;
        _surface = null;
    }

    /// <summary>Unregister and drop the surface's baked data reference. The .navmesh file is left on disk.</summary>
    public static void Clear(NavMeshSurface surface)
    {
        surface.NavMeshData = default;
        surface.RefreshRegistration();
        EditorSceneManager.MarkDirty();
    }

    /// <summary>Write <paramref name="data"/> over the surface's asset (or a new one), import it and assign it.</summary>
    internal static bool Save(NavMeshSurface surface, NavMeshData data)
    {
        try
        {
            var db = EditorAssetBackend.Instance;
            string fileRel = BakePath(surface);
            string fileAbs = Path.Combine(Project.Current!.AssetsPath, fileRel);
            string? dirAbs = Path.GetDirectoryName(fileAbs);
            if (!string.IsNullOrEmpty(dirAbs))
                Directory.CreateDirectory(dirAbs);
            data.Name = Path.GetFileNameWithoutExtension(fileRel);
            Serializer.Serialize(typeof(object), data).WriteToBinary(new FileInfo(fileAbs));

            Guid guid = db.ImportFile(fileRel);
            if (guid == Guid.Empty)
            {
                Runtime.Debug.LogError($"[Navigation] Failed to import baked navmesh at {fileRel}.");
                return false;
            }

            surface.NavMeshData = new AssetRef<NavMeshData>(guid);
            surface.RefreshRegistration();
            EditorSceneManager.MarkDirty();
            Runtime.Debug.Log($"[Navigation] Baked navmesh saved to {fileRel} ({data.CacheLayers.Count} cache layers).");
            return true;
        }
        catch (Exception e)
        {
            Runtime.Debug.LogError($"[Navigation] Saving the baked navmesh failed: {e}");
            return false;
        }
    }

    /// <summary>Reuses the assigned asset's path, found by guid, so a file the user renamed is rebaked in
    /// place rather than orphaned beside a freshly named one.</summary>
    internal static string BakePath(NavMeshSurface surface)
    {
        string? assigned = EditorAssetBackend.Instance.GuidToPath(surface.NavMeshData.AssetID);
        return string.IsNullOrEmpty(assigned) ? DefaultBakePath(surface) : assigned;
    }

    // The agent type id is in the name because names alone can collide: they are deduplicated
    // case sensitively and Sanitize folds path separators.
    private static string DefaultBakePath(NavMeshSurface surface)
    {
        var scene = surface.GameObject.Scene;
        string sceneRel = scene.IsValid() && !string.IsNullOrEmpty(scene!.AssetPath) ? scene.AssetPath : "";
        string sceneDir = string.IsNullOrEmpty(sceneRel) ? "" : (Path.GetDirectoryName(sceneRel) ?? "").Replace('\\', '/');
        string sceneName = string.IsNullOrEmpty(sceneRel) ? "Scene" : Path.GetFileNameWithoutExtension(sceneRel);
        string folderRel = (string.IsNullOrEmpty(sceneDir) ? "" : sceneDir + "/") + sceneName + "_navmesh";
        string agentType = NavMeshAgentTypes.GetName(surface.AgentTypeId);
        return folderRel + "/" + Sanitize($"{sceneName} NavMesh ({agentType} {surface.AgentTypeId.Value})") + ".navmesh";
    }

    private static string Sanitize(string name)
    {
        foreach (char c in Path.GetInvalidFileNameChars())
            name = name.Replace(c, '_');
        return string.IsNullOrEmpty(name) ? "NavMesh" : name;
    }
}
