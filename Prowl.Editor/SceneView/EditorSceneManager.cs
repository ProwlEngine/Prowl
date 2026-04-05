using System;
using System.IO;

using Prowl.Echo;
using Prowl.Editor.Importers;
using Prowl.Editor.Panels;
using Prowl.Runtime;
using Prowl.Runtime.Resources;

namespace Prowl.Editor;

/// <summary>
/// Manages scene loading, saving, and tracking for the editor.
/// </summary>
public static class EditorSceneManager
{
    /// <summary>Path to the currently open scene file (relative to Assets/). Null for unsaved scenes.</summary>
    public static string? CurrentScenePath { get; private set; }

    /// <summary>Whether the current scene has unsaved changes.</summary>
    public static bool IsDirty { get; set; }

    /// <summary>
    /// Create and load a new empty default scene.
    /// </summary>
    public static void NewScene()
    {
        SceneViewPanel.CreateAndLoadDefaultScene();
        CurrentScenePath = null;
        IsDirty = false;
    }

    /// <summary>
    /// Open a scene from a project-relative path.
    /// </summary>
    public static bool OpenScene(string relativePath)
    {
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
            return true;
        }
        return false;
    }

    /// <summary>
    /// Save the current scene to a path.
    /// </summary>
    private static bool SaveTo(string relativePath)
    {
        if (Project.Current == null || Scene.Current == null) return false;

        string absolutePath = Path.Combine(Project.Current.AssetsPath, relativePath);
        Directory.CreateDirectory(Path.GetDirectoryName(absolutePath)!);

        try
        {
            var ctx = new SerializationContext();
            Runtime.AssetDatabase.ConfigureContext(ctx);

            var echo = Serializer.Serialize(Scene.Current, ctx);
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

            Debug.Log($"Saved scene: {relativePath}");
            return true;
        }
        catch (Exception ex)
        {
            Debug.LogError($"Failed to save scene: {ex.Message}");
            return false;
        }
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
            default:
                return false;
        }
    }
}
