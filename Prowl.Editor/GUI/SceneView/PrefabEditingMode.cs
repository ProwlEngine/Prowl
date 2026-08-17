using System;
using System.IO;
using System.Linq;

using Prowl.Echo;
using Prowl.Editor.Core;
using Prowl.Editor.GUI.Panels;
using Prowl.Editor.Prefabs;
using Prowl.Editor.Projects;
using Prowl.OrigamiUI;
using Prowl.Rosetta;
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
    // The scene's own dirty flag, parked while the prefab session borrows it. Without this the
    // prefab and the scene share one flag, so each makes the other look unsaved.
    private static bool _savedSceneDirty;
    // The scene's undo history, parked the same way. The session's own steps address objects that
    // stop existing when it ends, so the two histories cannot be one.
    private static object? _savedSceneUndo;
    // Tracked so Save() can serialize the prefab root specifically, skipping the
    // editor-only camera/light/etc. that we add for visibility.
    private static GameObject? _editingRoot;

    /// <summary>
    /// Enter prefab editing mode. If another prefab is already being edited with unsaved
    /// changes, prompts to save before switching rather than silently discarding them.
    /// </summary>
    public static void Enter(Guid prefabGuid)
    {
        if (Application.IsPlaying)
        {
            Debug.LogWarning("[Prefab] Cannot open a prefab for editing during play mode.");
            return;
        }

        if (IsEditing)
        {
            if (EditorSceneManager.IsDirty)
            {
                string name = EditingPrefabPath != null ? Path.GetFileNameWithoutExtension(EditingPrefabPath) : "prefab";
                Origami.Confirm(
                    Loc.Get("dialog.unsaved_prefab"),
                    Loc.Get("dialog.unsaved_prefab_body", new { name }),
                    onYes: () => { SaveAndExit(); EnterInternal(prefabGuid); },
                    onNo: () => { Exit(); EnterInternal(prefabGuid); });
                return;
            }

            Exit();
        }

        EnterInternal(prefabGuid);
    }

    private static void EnterInternal(Guid prefabGuid)
    {
        var prefab = AssetDatabase.Get(prefabGuid) as PrefabAsset;
        if (prefab == null)
        {
            Debug.LogWarning("[Prefab] Cannot edit prefab asset not found.");
            return;
        }

        // Saving writes the edited tree back over the asset's own file, which for an imported prefab
        // is the model it came from.
        if (!PrefabUtility.IsEditablePrefab(prefabGuid))
        {
            Debug.LogWarning("[Prefab] Cannot edit an imported prefab; it is generated from its source file.");
            return;
        }

        var db = EditorAssetBackend.Instance;
        var entry = db?.GetEntry(prefabGuid);
        EditingPrefabPath = entry?.Path;

        // Save current scene. Reconciled first: the session ends by restoring this snapshot and
        // refreshing its instances, which would otherwise drop any edit nothing had recorded yet.
        var currentScene = Scene.Current;
        if (currentScene != null)
        {
            PrefabUtility.ReconcileOpenScene();
            OriginalSceneName = currentScene.Name;
            _savedSceneState = Serializer.Serialize(currentScene);
            _savedScenePath = EditorSceneManager.CurrentScenePath;
        }
        _savedSceneDirty = EditorSceneManager.IsDirty;

        // Instantiate prefab into isolated scene
        var editScene = new Scene();
        editScene.Name = $"Editing: {prefab.Name}";

        var go = GameObject.InstantiateDetached(prefab);
        if (go == null)
        {
            Debug.LogWarning("[Prefab] Failed to instantiate prefab for editing.");
            return;
        }

        // We're editing the source, not an instance, so drop this prefab's own instance data. Nested
        // instances of other prefabs keep theirs, otherwise saving would flatten them permanently.
        // The objects keep their record of which source object each one is, which is what Save writes
        // back and what instances match against.
        PrefabUtility.StripInstanceDataForEditing(go, prefabGuid);

        // Adopt the identifiers the asset is written with for the whole session, so the ids that undo
        // records and that Save writes back are the ones instances already match against.
        PrefabUtility.StabilizeSourceIdentifiers(go);

        editScene.Add(go);
        _editingRoot = go;

        // Editor-only viewing aids. Hidden from gizmos and marked DontSave so they don't end
        // up in the serialized prefab file when the user hits Save.
        var camGo = new GameObject("PrefabEdit Camera");
        camGo.Tag = "Main Camera";
        camGo.HideFlags = HideFlags.HideAndDontSave | HideFlags.NoGizmos;
        camGo.Transform.Position = FramingPositionFor(go);
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
        _savedSceneUndo = Undo.PushContext();

        EditingPrefabGuid = prefabGuid;
        IsEditing = true;
        EditorSceneManager.CurrentScenePath = null;
        EditorSceneManager.IsDirty = false; // the prefab session starts clean

        Debug.Log($"[Prefab] Entered editing mode: {prefab.Name}");
    }

    /// <summary>
    /// Where to put the editing camera so the prefab fills the view: back off along its own tilt by
    /// enough to take in its bounds. A prefab that draws nothing keeps the old fixed spot, which is
    /// as good a guess as any for something with no extent.
    /// </summary>
    private static Float3 FramingPositionFor(GameObject go)
    {
        if (!Panels.SceneViewPanel.TryGetWorldBounds(go, out Float3 min, out Float3 max))
            return new Float3(0, 2, -5);

        Float3 center = (min + max) * 0.5f;
        Float3 extents = (max - min) * 0.5f;

        // Far enough that the largest axis is comfortably inside the view, with a floor so a tiny
        // object is not framed from a millimetre away.
        float radius = MathF.Max(MathF.Max((float)extents.X, (float)extents.Y), (float)extents.Z);
        float distance = MathF.Max(radius * 3.5f, 1.5f);

        return center + new Float3(0, distance * 0.35f, -distance);
    }

    /// <summary>
    /// Save the prefab being edited. Returns true if the prefab was written to disk.
    /// </summary>
    public static bool Save()
    {
        if (!IsEditing) return false;
        if (Application.IsPlaying)
        {
            Debug.LogWarning("[Prefab] Cannot save a prefab during play mode.");
            return false;
        }

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

        // A prefab has exactly one root, so anything else the user created at the top level is not
        // going to be saved. Say so rather than dropping it silently.
        var strays = scene.RootObjects
            .Where(go => !go.HideFlags.HasFlag(HideFlags.HideAndDontSave) && go != root)
            .Select(go => go.Name)
            .ToList();
        if (strays.Count > 0)
        {
            Debug.LogWarning($"[Prefab] Only '{root.Name}' is saved into the prefab. " +
                $"Parent these under it to keep them: {string.Join(", ", strays)}");
        }

        // Serialize to .prefab file. The editor-only camera and light live in this scene too, so
        // anything the prefab references outside itself is linked rather than copied into the asset.
        var writeContext = PrefabUtility.TreeValueContext(root);
        var echo = Serializer.Serialize(typeof(object), root, writeContext);
        if (echo == null) return false;

        PrefabUtility.ReportDroppedSceneReferences(writeContext, "Saving this prefab");

        // Prefabs do not nest, and the importer drops any link inside the asset on the way in. Doing
        // it here as well is what stops the file on disk from claiming something the asset built from
        // it does not. The links come off the copy being written, not off the session's own objects,
        // which keep theirs for as long as the session lasts.
        Importers.ImportHelper.FlattenNestedPrefabLinks(echo, insideInstance: true);

        if (EditingPrefabPath != null && Project.Current != null)
        {
            string absolutePath = Path.Combine(Project.Current.AssetsPath, EditingPrefabPath);
            if (!PrefabUtility.TryWriteFile(absolutePath, echo.WriteToString()))
                return false;

            PrefabUtility.RaisePrefabSaved(EditingPrefabGuid);
            EditorAssetBackend.Instance?.Reimport(EditingPrefabGuid);

            EditorSceneManager.IsDirty = false;
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
        if (Application.IsPlaying)
        {
            Debug.LogWarning("[Prefab] Cannot save a prefab during play mode.");
            return;
        }

        Save();
        var prefabGuid = EditingPrefabGuid;

        // Restore original scene
        bool restored = RestoreScene();

        // The restore only queues the swap, so refresh instances once that scene is actually current.
        Action? onLoaded = null;
        onLoaded = () =>
        {
            Scene.OnSceneLoaded -= onLoaded;
            PrefabUtility.RefreshAllInstances(prefabGuid);
        };
        Scene.OnSceneLoaded += onLoaded;

        Cleanup(restored);
        Debug.Log("[Prefab] Saved and exited editing mode.");
    }

    /// <summary>
    /// Leave prefab editing mode, prompting first if the prefab has unsaved changes.
    /// This is the entry point for user-driven exits; <see cref="Exit"/> discards without asking.
    /// </summary>
    public static void RequestExit()
    {
        if (!IsEditing) return;

        if (EditorSceneManager.IsDirty)
        {
            string name = EditingPrefabPath != null ? Path.GetFileNameWithoutExtension(EditingPrefabPath) : "prefab";
            Origami.Confirm(
                Loc.Get("dialog.unsaved_prefab"),
                Loc.Get("dialog.unsaved_prefab_body", new { name }),
                onYes: SaveAndExit,
                onNo: Exit);
            return;
        }

        Exit();
    }

    /// <summary>
    /// Exit prefab editing mode without saving. Restores the original scene as-is.
    /// Instance overrides are preserved exactly as they were before entering.
    /// </summary>
    public static void Exit()
    {
        if (!IsEditing) return;

        Cleanup(RestoreScene());

        Debug.Log("[Prefab] Exited editing mode.");
    }

    /// <summary>Puts the scene back, reporting whether it is the one that was there before.</summary>
    private static bool RestoreScene()
    {
        if (_savedSceneState != null)
        {
            var restoredScene = Serializer.Deserialize<Scene>(_savedSceneState);
            if (restoredScene != null)
            {
                Scene.Load(restoredScene);
                EditorSceneManager.CurrentScenePath = _savedScenePath;
                return true;
            }

            Debug.LogWarning("[Prefab] Failed to restore scene. Creating default.");
        }

        EditorSceneManager.CreateAndLoadDefaultScene();
        return false;
    }

    private static void Cleanup(bool sceneRestored)
    {
        IsEditing = false;
        EditingPrefabGuid = Guid.Empty;
        EditingPrefabPath = null;
        OriginalSceneName = null;
        _savedSceneState = null;
        _savedScenePath = null;
        _editingRoot = null;

        // The scene's own history addresses its objects by identifier, and the restore brings those
        // back, so the steps still resolve. If the scene could not be restored they address nothing.
        if (sceneRestored)
            Undo.PopContext(_savedSceneUndo);
        else
            Undo.Clear();
        _savedSceneUndo = null;

        EditorSceneManager.IsDirty = _savedSceneDirty;
        _savedSceneDirty = false;
    }
}
