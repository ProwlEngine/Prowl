using System;
using System.IO;

using Prowl.Echo;
using Prowl.Editor.Importers;
using Prowl.Editor.Panels;
using Prowl.Editor.Widgets;
using System.Linq;

using Prowl.Runtime;
using Prowl.Runtime.Resources;

namespace Prowl.Editor;

/// <summary>
/// Manages scene loading, saving, and tracking for the editor.
/// </summary>
public static class EditorSceneManager
{
    /// <summary>Path to the currently open scene file (relative to Assets/). Null for unsaved scenes.</summary>
    public static string? CurrentScenePath { get; internal set; }

    /// <summary>Whether the current scene has unsaved changes.</summary>
    public static bool IsDirty { get; set; }

    /// <summary>Fired after the scene is saved. Use for auto-saving dependent assets.</summary>
    public static event Action? OnSceneSaved;

    /// <summary>
    /// Create and load a new empty default scene.
    /// </summary>
    public static void NewScene()
    {
        if (Application.IsPlaying) { Debug.LogWarning("Cannot create new scene during play mode."); return; }
        SceneViewPanel.CreateAndLoadDefaultScene();
        CurrentScenePath = null;
        IsDirty = false;
        Undo.Clear();
        SaveLastScenePath(null);
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
    /// Ensure a scene is loaded. If Scene.Current is null, restore the last scene or create a default.
    /// Called after project open.
    /// </summary>
    public static void EnsureSceneLoaded()
    {
        if (Scene.Current != null) return;

        // Try to restore last scene
        if (ProjectSettingsRegistry.Entries.Count > 0)
        {
            var general = ProjectSettingsRegistry.Get<GeneralSettings>();
            if (!string.IsNullOrEmpty(general.LastScenePath))
            {
                if (OpenScene(general.LastScenePath))
                    return;

                // Path was invalid, clear it
                general.LastScenePath = null;
                ProjectSettingsRegistry.SaveAll();
            }
        }

        // No saved scene or failed to load — create default
        NewScene();
    }

    /// <summary>
    /// Handle double-clicking an asset in the project panel.
    /// Returns true if the asset was handled.
    /// </summary>
    public static bool HandleAssetDoubleClick(string relativePath, Guid guid)
    {
        string ext = Path.GetExtension(relativePath).ToLowerInvariant();

        switch (ext)
        {
            case ".scene":
                return OpenScene(relativePath);
            case ".prefab":
                Prefabs.PrefabEditingMode.Enter(guid);
                return true;
            case ".shadergraph":
                {
                    // Resolve via AssetRef so the asset gets loaded through the standard
                    // pipeline (importer runs, sub-assets register). Main asset is the
                    // ShaderGraph itself; the compiled Shader is its sub-asset.
                    var graphRef = new AssetRef<Runtime.GraphTools.Graph>(guid);
                    var graph = graphRef.Res;
                    if (graph != null)
                    {
                        Editor.GraphTools.GraphEditorWindow.OpenFor(graph);
                        return true;
                    }
                    return false;
                }
            default:
                return false;
        }
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
            SaveBatch.Record($"Scene: {Scene.Current.Name}");
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
        if (ProjectSettingsRegistry.Entries.Count == 0) return;
        try
        {
            var general = ProjectSettingsRegistry.Get<GeneralSettings>();
            general.LastScenePath = path;
            ProjectSettingsRegistry.SaveAll();
        }
        catch { }
    }
}
