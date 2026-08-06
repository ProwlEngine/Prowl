using System;
using System.IO;

using Prowl.Echo;
using Prowl.Editor.Importers;

using Prowl.Runtime;
using Prowl.Runtime.Resources;
using Prowl.Vector;
using Prowl.Editor.GUI.Panels;
using Prowl.Editor.Projects.Settings;
using Prowl.Editor.Core;
using Prowl.Editor.Projects;

namespace Prowl.Editor.GUI.SceneView;

/// <summary>
/// Manages scene loading, saving, and tracking for the editor.
/// </summary>
public static class EditorSceneManager
{
    /// <summary>Path to the currently open scene file (relative to Assets/). Null for unsaved scenes.</summary>
    public static string? CurrentScenePath { get; internal set; }

    /// <summary>Whether the current scene has unsaved changes.</summary>
    public static bool IsDirty { get; set; }

    /// <summary>Marks the current scene as having unsaved changes.</summary>
    public static void MarkDirty() => IsDirty = true;

    /// <summary>Fired after the scene is saved. Use for auto-saving dependent assets.</summary>
    public static event Action? OnSceneSaved;

    /// <summary>
    /// Create and load a new empty default scene.
    /// </summary>
    public static void NewScene()
    {
        if (Application.IsPlaying) { Debug.LogWarning("Cannot create new scene during play mode."); return; }
        CreateAndLoadDefaultScene();
        CurrentScenePath = null;
        IsDirty = false;
        Undo.Clear();
        Selection.Clear(); // drop references to the now-unloaded scene's objects
        SaveLastScenePath(null);
    }

    /// <summary>
    /// Build a new default scene with camera, light, floor, and cubes.
    /// </summary>
    public static Scene CreateDefaultScene()
    {
        var scene = new Scene();
        scene.Name = "Untitled Scene";

        var defaultMat = new AssetRef<Material>(BuiltInAssets.GuidFor(DefaultMaterial.Standard));
        var cubeMesh = new AssetRef<Mesh>(BuiltInAssets.GuidForMesh(DefaultModel.Cube));
        var planeMesh = new AssetRef<Mesh>(BuiltInAssets.GuidForMesh(DefaultModel.Plane));

        var camGo = new GameObject("Main Camera");
        camGo.Tag = "Main Camera";
        camGo.Transform.Position = new Float3(0, 5, -15);
        camGo.Transform.LocalEulerAngles = new Float3(15, 0, 0);
        var cam = camGo.AddComponent<Camera>();
        cam.Depth = -1;
        cam.HDR = true;
        scene.Add(camGo);

        var lightGo = new GameObject("Directional Light");
        lightGo.Transform.LocalEulerAngles = new Float3(-45, 45, 0);
        var light = lightGo.AddComponent<DirectionalLight>();
        light.Intensity = 1f;
        scene.Add(lightGo);

        var floorGo = new GameObject("Floor");
        floorGo.Transform.Position = new Float3(0, 0, 0);
        floorGo.Transform.LocalScale = new Float3(1, 1, 1);
        var floorRenderer = floorGo.AddComponent<MeshRenderer>();
        floorRenderer.Mesh = planeMesh;
        floorRenderer.Material = defaultMat;
        scene.Add(floorGo);

        var cube1 = new GameObject("Cube");
        cube1.Transform.Position = new Float3(0, 0.5f, 0);
        var cube1Renderer = cube1.AddComponent<MeshRenderer>();
        cube1Renderer.Mesh = cubeMesh;
        cube1Renderer.Material = defaultMat;
        scene.Add(cube1);

        var cube2 = new GameObject("Cube (1)");
        cube2.Transform.Position = new Float3(2, 0.5f, 1);
        var cube2Renderer = cube2.AddComponent<MeshRenderer>();
        cube2Renderer.Mesh = cubeMesh;
        cube2Renderer.Material = defaultMat;
        scene.Add(cube2);

        return scene;
    }

    /// <summary>
    /// Create a default scene and load it as the current scene.
    /// </summary>
    public static void CreateAndLoadDefaultScene()
    {
        Scene.Load(CreateDefaultScene());
        Undo.Clear();
        Debug.Log("Created default scene.");
    }

    /// <summary>
    /// Open a scene from a project-relative path.
    /// </summary>
    public static bool OpenScene(string relativePath)
    {
        if (Application.IsPlaying) { Debug.LogWarning("Cannot open scenes during play mode."); return false; }
        if (Project.Current == null) return false;

        string absolutePath = Path.Combine(Project.Current.AssetsPath, relativePath);
        if (!File.Exists(absolutePath))
        {
            Debug.LogError($"Scene file not found: {absolutePath}");
            return false;
        }

        try
        {
            string text = File.ReadAllText(absolutePath);
            var echo = EchoObject.ReadFromString(text);

            var ctx = ImportHelper.CreateTrackingContext(out _);
            var scene = Serializer.Deserialize<Scene>(echo, ctx);

            if (scene == null)
            {
                Debug.LogError($"Failed to deserialize scene: {relativePath}");
                return false;
            }

            scene.Name = Path.GetFileNameWithoutExtension(relativePath);
            Scene.Load(scene);
            CurrentScenePath = relativePath;
            IsDirty = false;
            Undo.Clear();
            Selection.Clear(); // drop references to the previous scene's (now disposed) objects

            SaveLastScenePath(relativePath);
            Debug.Log($"Opened scene: {relativePath}");
            return true;
        }
        catch (Exception ex)
        {
            Debug.LogError($"Failed to open scene: {ex.Message}");
            return false;
        }
    }

    /// <summary>
    /// Save the current scene to its existing path. Returns false if no path set (use SaveAs).
    /// </summary>
    public static bool Save()
    {
        if (Application.IsPlaying) { Debug.LogWarning("Cannot save scenes during play mode."); return false; }
        if (CurrentScenePath == null) return false;
        return SaveTo(CurrentScenePath);
    }

    /// <summary>
    /// Save the current scene to a specific path.
    /// </summary>
    public static bool SaveAs(string relativePath)
    {
        if (SaveTo(relativePath))
        {
            CurrentScenePath = relativePath;
            SaveLastScenePath(relativePath);
            return true;
        }
        return false;
    }

    /// <summary>
    /// Opens the project's last scene, or creates a default one. Called after project open, where the
    /// engine's own scene is the empty placeholder nobody has edited yet.
    /// </summary>
    public static void EnsureSceneLoaded()
    {
        // Try to restore last scene
        if (EditorRegistries.SettingsEntries.Count > 0)
        {
            var general = EditorRegistries.GetSettings<GeneralSettings>();
            if (!string.IsNullOrEmpty(general.LastScenePath))
            {
                if (OpenScene(general.LastScenePath))
                    return;

                // Path was invalid, clear it
                general.LastScenePath = null;
                EditorRegistries.SaveSettings();
            }
        }

        // No saved scene or failed to load create default
        NewScene();
    }

    /// <summary>
    /// Handle double-clicking an asset in the project panel. Dispatches to a handler
    /// registered via <see cref="AssetDoubleClickHandlerAttribute"/>. Returns true if the
    /// asset was handled.
    /// </summary>
    public static bool HandleAssetDoubleClick(string relativePath, Guid guid)
        => EditorRegistries.DispatchDoubleClick(relativePath, guid);

    [AssetDoubleClickHandler(".scene")]
    private static bool OpenSceneHandler(string relativePath, Guid guid) => OpenScene(relativePath);

    [AssetDoubleClickHandler(".prefab")]
    private static bool OpenPrefabHandler(string relativePath, Guid guid)
    {
        PrefabEditingMode.Enter(guid);
        return true;
    }

    private static bool SaveTo(string relativePath)
    {
        if (Project.Current == null || Scene.Current == null) return false;

        string absolutePath = Path.Combine(Project.Current.AssetsPath, relativePath);
        Directory.CreateDirectory(Path.GetDirectoryName(absolutePath)!);

        try
        {
            var echo = Serializer.Serialize(typeof(object), Scene.Current);
            if (echo == null)
            {
                Debug.LogError("Failed to serialize scene.");
                return false;
            }

            File.WriteAllText(absolutePath, echo.WriteToString());

            // Ensure .meta exists
            MetaFile.EnsureMeta(absolutePath, "SceneImporter");

            Scene.Current.Name = Path.GetFileNameWithoutExtension(relativePath);
            IsDirty = false;

            // Notify listeners (e.g. terrain editor saves TerrainData assets)
            OnSceneSaved?.Invoke();

            Debug.Log($"Saved scene: {relativePath}");
            // Label reported via SaveManager.OnSave handler
            return true;
        }
        catch (Exception ex)
        {
            Debug.LogError($"Failed to save scene: {ex.Message}");
            return false;
        }
    }

    private static void SaveLastScenePath(string? path)
    {
        if (EditorRegistries.SettingsEntries.Count == 0) return;
        try
        {
            var general = EditorRegistries.GetSettings<GeneralSettings>();
            general.LastScenePath = path;
            EditorRegistries.SaveSettings();
        }
        catch { }
    }
}
