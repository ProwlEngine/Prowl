using System;
using System.IO;
using System.Linq;

using Prowl.Echo;
using Prowl.Editor.Core;
using Prowl.Editor.GUI.Panels;
using Prowl.Editor.Importers;
using Prowl.Editor.Prefabs;
using Prowl.Editor.Projects;
using Prowl.Editor.Projects.Settings;
using Prowl.Runtime;
using Prowl.Runtime.Rendering;
using Prowl.Runtime.Resources;
using Prowl.Vector;

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

    /// <summary>The save toast label for the open scene or prefab.</summary>
    public static string SaveLabel => PrefabEditingMode.IsEditing
        ? Rosetta.Loc.Get("save.prefab", new { name = Path.GetFileNameWithoutExtension(PrefabEditingMode.EditingPrefabPath) })
        : Rosetta.Loc.Get("save.scene", new { name = Scene.Current.IsValid() ? Scene.Current.Name : "Untitled" });

    /// <summary>Runs <paramref name="replace"/>, which swaps out the open scene, after asking to save the scene when it has changes.</summary>
    public static void ConfirmSceneChange(Action replace)
    {
        // Play mode and prefab editing refuse the change further on, so there is nothing to ask about.
        if (!IsDirty || Application.IsPlaying || PrefabEditingMode.IsEditing)
        {
            replace();
            return;
        }

        SaveManager.AskToSave([SaveLabel], null,
            save: () =>
            {
                if (Save()) replace();
                else EditorApplication.PromptSaveAs(replace);
            },
            discard: replace);
    }

    /// <summary>Fired after the scene is saved. Use for auto-saving dependent assets.</summary>
    public static event Action? OnSceneSaved;

    /// <summary>
    /// Create and load a new empty default scene.
    /// </summary>
    /// <summary>
    /// Whether the scene can be swapped right now. A prefab editing session has borrowed the scene, the
    /// undo history and the dirty flag, and it puts the previous scene back when it ends. Loading
    /// another scene underneath it would leave the session writing whatever is in the scene over the
    /// prefab, and the swapped-in scene discarded on exit.
    /// </summary>
    private static bool GuardNotEditingPrefab(string operation)
    {
        if (!PrefabEditingMode.IsEditing) return true;
        Debug.LogWarning($"Cannot {operation} while editing a prefab. Leave prefab editing mode first.");
        return false;
    }

    /// <summary> Creates and loads a new default scene, clearing the current scene path, dirty flag, undo history, and selection. </summary>
    public static void NewScene()
    {
        if (Application.IsPlaying) { Debug.LogWarning("Cannot create new scene during play mode."); return; }
        if (!GuardNotEditingPrefab("create a new scene")) return;
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

        Material? defaultMat = AssetDatabase.Get<Material>(BuiltInAssets.GuidFor(DefaultMaterial.Standard));
        Mesh? cubeMesh = AssetDatabase.Get<Mesh>(BuiltInAssets.GuidForMesh(DefaultModel.Cube));
        Mesh? planeMesh = AssetDatabase.Get<Mesh>(BuiltInAssets.GuidForMesh(DefaultModel.Plane));

        var camGo = new GameObject("Main Camera");
        camGo.Tag = "Main Camera";
        camGo.Transform.Position = new Float3(0, 5, -15);
        camGo.Transform.LocalEulerAngles = new Float3(15, 0, 0);
        Camera cam = camGo.AddComponent<Camera>();
        cam.Depth = -1;
        cam.Profile = VolumeProfile.CreateCameraDefault("Main Camera Profile");
        scene.Add(camGo);

        var lightGo = new GameObject("Directional Light");
        lightGo.Transform.LocalEulerAngles = new Float3(45, 225, 0);
        Light light = lightGo.AddComponent<Light>();
        light.Type = LightType.Directional;
        light.Intensity = 100000f;
        scene.Add(lightGo);

        var floorGo = new GameObject("Floor");
        floorGo.Transform.Position = new Float3(0, 0, 0);
        floorGo.Transform.LocalScale = new Float3(1, 1, 1);
        MeshRenderer floorRenderer = floorGo.AddComponent<MeshRenderer>();
        floorRenderer.Mesh = planeMesh;
        floorRenderer.Material = defaultMat;
        scene.Add(floorGo);

        var cube1 = new GameObject("Cube");
        cube1.Transform.Position = new Float3(0, 0.5f, 0);
        MeshRenderer cube1Renderer = cube1.AddComponent<MeshRenderer>();
        cube1Renderer.Mesh = cubeMesh;
        cube1Renderer.Material = defaultMat;
        scene.Add(cube1);

        var cube2 = new GameObject("Cube (1)");
        cube2.Transform.Position = new Float3(2, 0.5f, 1);
        MeshRenderer cube2Renderer = cube2.AddComponent<MeshRenderer>();
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

    private static SceneAsset? SceneAssetAt(string relativePath)
    {
        Guid guid = EditorAssetBackend.Instance?.PathToGuid(relativePath) ?? Guid.Empty;
        return guid == Guid.Empty ? null : AssetDatabase.Get<SceneAsset>(guid);
    }

    /// <summary>
    /// Open a scene from a project-relative path.
    /// </summary>
    public static bool OpenScene(string relativePath)
    {
        if (Application.IsPlaying) { Debug.LogWarning("Cannot open scenes during play mode."); return false; }
        if (!GuardNotEditingPrefab("open a scene")) return false;
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

            SerializationContext ctx = ImportHelper.CreateTrackingContext(out _);
            Scene? scene = Serializer.Deserialize<Scene>(echo, ctx);

            if (scene.IsNotValid())
            {
                Debug.LogError($"Failed to deserialize scene: {relativePath}");
                return false;
            }

            scene.Name = Path.GetFileNameWithoutExtension(relativePath);
            scene.Source = SceneAssetAt(relativePath);
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
            GeneralSettings general = EditorRegistries.GetSettings<GeneralSettings>();
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

    [AssetDoubleClickHandler(".scene")]
    private static bool OpenSceneHandler(string relativePath, Guid guid)
    {
        ConfirmSceneChange(() => OpenScene(relativePath));
        return true;
    }

    [AssetDoubleClickHandler(".prefab")]
    private static bool OpenPrefabHandler(string relativePath, Guid guid)
    {
        PrefabEditingMode.Enter(guid);
        return true;
    }

    private static bool SaveTo(string relativePath)
    {
        if (Project.Current == null || Scene.Current.IsNotValid()) return false;
        // The prefab session's scene holds the prefab and the editor-only viewing rig, which is not a
        // scene anyone means to save. Its own save goes through PrefabEditingMode.
        if (!GuardNotEditingPrefab("save a scene")) return false;

        // Recognized by identity rather than by the flag, so a session that ended without its scene being
        // swapped back cannot write the prefab and its editor-only rig over the user's scene file.
        if (PrefabEditingMode.IsPrefabEditScene(Scene.Current))
        {
            Debug.LogError($"The open scene is a prefab editing scene, so it was not saved over '{relativePath}'. " +
                "Open a scene to get back to one.");
            return false;
        }

        string absolutePath = Path.Combine(Project.Current.AssetsPath, relativePath);
        Directory.CreateDirectory(Path.GetDirectoryName(absolutePath)!);

        try
        {
            // Anything edited on a prefab instance without the inspector noticing is not in its
            // override list yet. Saving without it would write a scene whose instances silently lose
            // those edits the next time they are refreshed.
            PrefabUtility.ReconcileOpenScene();

            EchoObject echo = Serializer.Serialize(typeof(object), Scene.Current);
            if (echo == null)
            {
                Debug.LogError("Failed to serialize scene.");
                return false;
            }

            File.WriteAllText(absolutePath, echo.WriteToString());

            // The stored scene takes what was written, so a build or a Scene.Load of it sees this save.
            Guid guid = EditorAssetBackend.Instance?.ImportFile(relativePath) ?? Guid.Empty;
            if (guid != Guid.Empty) Scene.Current.Source = AssetDatabase.Get<SceneAsset>(guid);

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
            GeneralSettings general = EditorRegistries.GetSettings<GeneralSettings>();
            general.LastScenePath = path;
            EditorRegistries.SaveSettings();
        }
        catch { }
    }
}
