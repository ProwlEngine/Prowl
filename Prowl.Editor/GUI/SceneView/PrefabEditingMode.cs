using System;
using System.IO;
using System.Linq;

using Prowl.Echo;
using Prowl.Editor.Core;
using Prowl.Editor.GUI.Panels;
using Prowl.Editor.Prefabs;
using Prowl.Editor.Projects;
using Prowl.Runtime;
using Prowl.Runtime.Resources;
using Prowl.Vector;

namespace Prowl.Editor.GUI.SceneView;

/// <summary>
/// Isolated prefab editing mode. Saves the current scene, loads the prefab
/// into a temporary scene for editing, and restores on exit.
/// </summary>
public static class PrefabEditingMode
{
    public static bool IsEditing { get; private set; }
    public static Guid EditingPrefabGuid { get; private set; }
    public static string? EditingPrefabPath { get; private set; }
    public static string? OriginalSceneName { get; private set; }

    private static EchoObject? _savedSceneState;
    private static string? _savedScenePath;
    // Tracked so Save() can serialize the prefab root specifically, skipping the
    // editor-only camera/light/etc. that we add for visibility.
    private static GameObject? _editingRoot;

    /// <summary>
    /// Enter prefab editing mode.
    /// </summary>
    public static void Enter(Guid prefabGuid)
    {
        if (IsEditing) Exit();

        var prefab = AssetDatabase.Get(prefabGuid) as PrefabAsset;
        if (prefab == null)
        {
            Debug.LogWarning("[Prefab] Cannot edit prefab asset not found.");
            return;
        }

        var db = EditorAssetDatabase.Instance;
        var entry = db?.GetEntry(prefabGuid);
        EditingPrefabPath = entry?.Path;

        // Save current scene
        var currentScene = Scene.Current;
        if (currentScene != null)
        {
            OriginalSceneName = currentScene.Name;
            _savedSceneState = Serializer.Serialize(currentScene);
            _savedScenePath = EditorSceneManager.CurrentScenePath;
        }

        // Instantiate prefab into isolated scene
        var editScene = new Scene();
        editScene.Name = $"Editing: {prefab.Name}";

        var go = prefab.Instantiate();
        if (go == null)
        {
            Debug.LogWarning("[Prefab] Failed to instantiate prefab for editing.");
            return;
        }

        // Clear prefab instance data we're editing the source, not an instance
        go.ClearPrefabDataRecursive();

        editScene.Add(go);
        _editingRoot = go;

        // Editor-only viewing aids. Hidden from gizmos and marked DontSave so they don't end
        // up in the serialized prefab file when the user hits Save.
        var camGo = new GameObject("PrefabEdit Camera");
        camGo.Tag = "Main Camera";
        camGo.HideFlags = HideFlags.HideAndDontSave | HideFlags.NoGizmos;
        camGo.Transform.Position = new Float3(0, 2, -5);
        camGo.Transform.LocalEulerAngles = new Float3(15, 0, 0);
        var cam = camGo.AddComponent<Camera>();
        cam.Depth = -1;
        cam.HDR = true;
        editScene.Add(camGo);

        var lightGo = new GameObject("PrefabEdit Light");
        lightGo.HideFlags = HideFlags.HideAndDontSave | HideFlags.NoGizmos;
        lightGo.Transform.LocalEulerAngles = new Float3(-45, 45, 0);
        var light = lightGo.AddComponent<DirectionalLight>();
        light.Intensity = 1f;
        editScene.Add(lightGo);

        Scene.Load(editScene);
        Undo.Clear();

        EditingPrefabGuid = prefabGuid;
        IsEditing = true;
        EditorSceneManager.CurrentScenePath = null;

        Debug.Log($"[Prefab] Entered editing mode: {prefab.Name}");
    }

    /// <summary>
    /// Save the prefab being edited. Returns true if the prefab was written to disk.
    /// </summary>
    public static bool Save()
    {
        if (!IsEditing) return false;

        var scene = Scene.Current;
        if (scene == null) return false;

        // Use the tracked prefab root so we skip the editor-only camera/light we added
        // to light the scene during editing. Fall back to the first non-HideAndDontSave
        // root if the tracked reference is stale.
        var root = _editingRoot;
        if (root == null || root.Scene != scene)
        {
            root = scene.RootObjects.FirstOrDefault(go => !go.HideFlags.HasFlag(HideFlags.HideAndDontSave));
        }
        if (root == null) return false;

        // Serialize to .prefab file
        var echo = Serializer.Serialize(typeof(object), root);
        if (echo == null) return false;

        if (EditingPrefabPath != null && Project.Current != null)
        {
            string absolutePath = Path.Combine(Project.Current.AssetsPath, EditingPrefabPath);
            File.WriteAllText(absolutePath, echo.WriteToString());
            EditorAssetDatabase.Instance?.Reimport(EditingPrefabGuid);

            Debug.Log($"[Prefab] Saved prefab: {EditingPrefabPath}");
            // Label reported via SaveManager.OnSave handler
            return true;
        }
        return false;
    }

    /// <summary>
    /// Save changes and exit prefab editing mode.
    /// Saves the prefab, restores the scene, then refreshes instances.
    /// </summary>
    public static void SaveAndExit()
    {
        if (!IsEditing) return;

        Save();
        var prefabGuid = EditingPrefabGuid;

        // Restore original scene
        RestoreScene();

        // Now refresh instances in the restored scene with the updated prefab
        PrefabUtility.RefreshAllInstances(prefabGuid);

        Cleanup();
        Debug.Log("[Prefab] Saved and exited editing mode.");
    }

    /// <summary>
    /// Exit prefab editing mode without saving. Restores the original scene as-is.
    /// Instance overrides are preserved exactly as they were before entering.
    /// </summary>
    public static void Exit()
    {
        if (!IsEditing) return;

        RestoreScene();
        Cleanup();

        Debug.Log("[Prefab] Exited editing mode.");
    }

    private static void RestoreScene()
    {
        if (_savedSceneState != null)
        {
            var restoredScene = Serializer.Deserialize<Scene>(_savedSceneState);
            if (restoredScene != null)
            {
                Scene.Load(restoredScene);
                Undo.Clear();
                EditorSceneManager.CurrentScenePath = _savedScenePath;
            }
            else
            {
                Debug.LogWarning("[Prefab] Failed to restore scene. Creating default.");
                SceneViewPanel.CreateAndLoadDefaultScene();
            }
        }
        else
        {
            SceneViewPanel.CreateAndLoadDefaultScene();
        }
    }

    private static void Cleanup()
    {
        IsEditing = false;
        EditingPrefabGuid = Guid.Empty;
        EditingPrefabPath = null;
        OriginalSceneName = null;
        _savedSceneState = null;
        _savedScenePath = null;
        _editingRoot = null;
    }
}
