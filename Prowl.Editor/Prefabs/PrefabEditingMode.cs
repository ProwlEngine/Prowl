using System;
using System.IO;
using System.Linq;

using Prowl.Echo;
using Prowl.Runtime;
using Prowl.Runtime.Resources;

namespace Prowl.Editor.Prefabs;

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

    /// <summary>
    /// Enter prefab editing mode.
    /// </summary>
    public static void Enter(Guid prefabGuid)
    {
        if (IsEditing) Exit();

        var prefab = AssetDatabase.Get(prefabGuid) as PrefabAsset;
        if (prefab == null)
        {
            Debug.LogWarning("[Prefab] Cannot edit — prefab asset not found.");
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

        // Clear prefab instance data — we're editing the source, not an instance
        go.ClearPrefabDataRecursive();

        editScene.Add(go);
        Scene.Load(editScene);

        EditingPrefabGuid = prefabGuid;
        IsEditing = true;
        EditorSceneManager.CurrentScenePath = null;

        Debug.Log($"[Prefab] Entered editing mode: {prefab.Name}");
    }

    /// <summary>
    /// Save the prefab being edited.
    /// </summary>
    public static void Save()
    {
        if (!IsEditing) return;

        var scene = Scene.Current;
        if (scene == null) return;

        // Get the root GO
        var roots = scene.RootObjects.ToList();
        if (roots.Count == 0) return;
        var root = roots[0];

        // Serialize to .prefab file
        var echo = Serializer.Serialize(typeof(object), root);
        if (echo == null) return;

        if (EditingPrefabPath != null && Project.Current != null)
        {
            string absolutePath = Path.Combine(Project.Current.AssetsPath, EditingPrefabPath);
            File.WriteAllText(absolutePath, echo.WriteToString());
            EditorAssetDatabase.Instance?.Reimport(EditingPrefabGuid);

            Debug.Log($"[Prefab] Saved prefab: {EditingPrefabPath}");
        }
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
                EditorSceneManager.CurrentScenePath = _savedScenePath;
            }
            else
            {
                Debug.LogWarning("[Prefab] Failed to restore scene. Creating default.");
                Panels.SceneViewPanel.CreateAndLoadDefaultScene();
            }
        }
        else
        {
            Panels.SceneViewPanel.CreateAndLoadDefaultScene();
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
    }
}
