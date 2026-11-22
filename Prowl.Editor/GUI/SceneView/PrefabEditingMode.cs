using System;
using System.IO;
using System.Linq;

using Prowl.Echo;
using Prowl.Editor.Core;
using Prowl.Editor.Prefabs;
using Prowl.Editor.Projects;
using Prowl.Rosetta;
using Prowl.Runtime;
using Prowl.Runtime.Rendering;
using Prowl.Runtime.Resources;
using Prowl.Vector;

namespace Prowl.Editor.GUI.SceneView;

/// <summary>
/// Isolated prefab editing mode. The prefab is edited in its own world while the edit world sleeps underneath,
/// and the edit world wakes again as it was when the session ends. The session runs through <see cref="IsolatedWorld"/>.
/// </summary>
public static class PrefabEditingMode
{
    private static PrefabSession? Session => IsolatedWorld.Current as PrefabSession;

    public static bool IsEditing => Session != null;

    /// <summary>Whether the prefab being edited has unsaved changes. Its own flag, so the scene's is left as it was.</summary>
    public static bool IsDirty
    {
        get => Session?.IsDirty == true;
        internal set
        {
            if (Session is { } session) session.IsDirty = value;
        }
    }

    /// <summary>The save toast label for the prefab being edited.</summary>
    public static string SaveLabel => Loc.Get("save.prefab", new { name = Path.GetFileNameWithoutExtension(EditingPrefabPath) });
    public static Guid EditingPrefabGuid => Session?.Guid ?? Guid.Empty;
    public static string? EditingPrefabPath => Session?.Path;
    public static string? OriginalSceneName => IsEditing ? IsolatedWorld.OriginalSceneName : null;

    /// <summary>The world the prefab is edited in, with its viewing camera and light, while a session runs.</summary>
    public static Scene? World => IsEditing ? IsolatedWorld.World : null;

    /// <summary>
    /// Enter prefab editing mode. If another session is open with unsaved changes, prompts to save before switching
    /// rather than silently discarding them.
    /// </summary>
    public static void Enter(Guid prefabGuid)
    {
        if (Application.IsPlaying)
        {
            Debug.LogWarning("[Prefab] Cannot open a prefab for editing during play mode.");
            return;
        }

        IsolatedWorld.Replace(() => EnterInternal(prefabGuid));
    }

    private static void EnterInternal(Guid prefabGuid)
    {
        PrefabAsset? prefab = PrefabUtility.LoadPrefab(prefabGuid);
        if (prefab.IsNotValid())
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

        EditorAssetBackend? db = EditorAssetBackend.Instance;
        AssetEntry? entry = db?.GetEntry(prefabGuid);

        var go = GameObject.InstantiateDetached(prefab);
        if (go.IsNotValid())
        {
            Debug.LogWarning("[Prefab] Failed to instantiate prefab for editing.");
            return;
        }

        // Reconciled first: the edit world refreshes its instances when the session saves, which would otherwise
        // drop any edit nothing had recorded yet.
        Scene edit = EditorSceneManager.EditWorld;
        EditorSceneManager.ApplyPendingNow();
        PrefabUtility.ReconcileOpenScene();

        Scene editScene = Scene.Create($"Editing: {prefab.Name}", SceneMode.Edit);
        try
        {
            Build(editScene, go, edit, prefabGuid);
        }
        catch (Exception e)
        {
            // Nothing of the session is left behind: the edit world wakes as it was.
            Debug.LogException("[Prefab] Entering prefab editing threw", e);
            if (editScene.IsValid()) editScene.Dispose();
            if (!go.IsDisposed) go.Dispose();
            if (edit.State == SceneState.Dormant) edit.Activate();
            return;
        }

        IsolatedWorld.Begin(new PrefabSession(prefabGuid, entry?.Path, go), editScene, edit);
        Debug.Log($"[Prefab] Entered editing mode: {prefab.Name}");
    }

    // Fills the prefab world with the prefab and the viewing rig, then swaps it in over the edit world.
    private static void Build(Scene editScene, GameObject go, Scene edit, Guid prefabGuid)
    {
        // We're editing the source, not an instance, so drop this prefab's own instance data. Nested
        // instances of other prefabs keep theirs, otherwise saving would flatten them permanently.
        // The objects keep their record of which source object each one is, which is what Save writes
        // back and what instances match against.
        PrefabUtility.StripInstanceDataForEditing(go, prefabGuid);

        // Adopt the identifiers the asset is written with for the whole session, so the ids that undo
        // records and that Save writes back are the ones instances already match against.
        PrefabUtility.StabilizeSourceIdentifiers(go);

        editScene.Add(go);

        // Editor-only viewing aids. Hidden from gizmos and marked DontSave so they don't end
        // up in the serialized prefab file when the user hits Save.
        var camGo = new GameObject("PrefabEdit Camera");
        camGo.Tag = "Main Camera";
        camGo.HideFlags = HideFlags.HideAndDontSave | HideFlags.NoGizmos;
        camGo.Transform.Position = FramingPositionFor(go);
        camGo.Transform.LocalEulerAngles = new Float3(15, 0, 0);
        Camera cam = camGo.AddComponent<Camera>();
        cam.Depth = -1;
        cam.Profile = VolumeProfile.CreateCameraDefault("PrefabEdit Camera Profile");
        editScene.Add(camGo);

        var lightGo = new GameObject("PrefabEdit Light");
        lightGo.HideFlags = HideFlags.HideAndDontSave | HideFlags.NoGizmos;
        lightGo.Transform.LocalEulerAngles = new Float3(45, 225, 0);
        lightGo.AddComponent<DirectionalLight>();
        editScene.Add(lightGo);

        // Objects of the dormant edit world must not stay selected, where the inspector or Delete would reach them.
        Selection.Clear();
        edit.Deactivate();
        editScene.Activate();
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
    public static bool Save() => Session?.Save() ?? false;

    /// <summary>
    /// Save changes and exit prefab editing mode.
    /// Saves the prefab, restores the scene, then refreshes instances.
    /// </summary>
    public static void SaveAndExit() => Session?.SaveAndClose();

    /// <summary>
    /// Leave prefab editing mode, prompting first if the prefab has unsaved changes.
    /// This is the entry point for user-driven exits; <see cref="Exit"/> discards without asking.
    /// </summary>
    public static void RequestExit()
    {
        if (IsEditing) IsolatedWorld.RequestClose();
    }

    /// <summary>
    /// Exit prefab editing mode without saving. Restores the original scene as-is.
    /// Instance overrides are preserved exactly as they were before entering.
    /// </summary>
    public static void Exit() => Session?.Close();

    /// <summary>A prefab open in its own world: which asset, where it is written, and the root that is saved.</summary>
    private sealed class PrefabSession(Guid guid, string? path, GameObject root) : IsolatedSession
    {
        public Guid Guid { get; } = guid;
        public string? Path { get; } = path;

        // Tracked so Save can serialize the prefab root specifically, skipping the editor-only camera and light
        private readonly GameObject _root = root;

        public override string Name => Path != null ? System.IO.Path.GetFileNameWithoutExtension(Path) : Loc.Get("hierarchy.prefab_fallback");

        public override string SaveLabel => PrefabEditingMode.SaveLabel;

        public override string HistoryTitle => Loc.Get("undo_history.prefab");

        public override (string Title, string Body) UnsavedQuestion
            => (Loc.Get("dialog.unsaved_prefab"), Loc.Get("dialog.unsaved_prefab_body", new { name = Path != null ? System.IO.Path.GetFileNameWithoutExtension(Path) : "prefab" }));

        public override bool Save()
        {
            if (Application.IsPlaying)
            {
                Debug.LogWarning("[Prefab] Cannot save a prefab during play mode.");
                return false;
            }

            Scene? scene = IsolatedWorld.World;
            if (scene.IsNotValid()) return false;

            // The tracked prefab root, and only that: it is what skips the editor-only camera and light.
            GameObject? root = _root;
            if (root.IsNotValid() || root!.Scene != scene)
            {
                Debug.LogError("[Prefab] The prefab root was destroyed or moved out of the prefab world, so there is nothing to save.");
                return false;
            }

            // A prefab has exactly one root, so anything else the user created at the top level is not
            // going to be saved. Say so rather than dropping it silently.
            var strays = scene!.Roots
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
            SerializationContext writeContext = PrefabUtility.TreeValueContext(root);
            EchoObject echo = Serializer.Serialize(typeof(object), root, writeContext);
            if (echo == null) return false;

            PrefabUtility.ReportDroppedSceneReferences(writeContext, "Saving this prefab");

            // Prefabs do not nest, and the importer drops any link inside the asset on the way in. Doing
            // it here as well is what stops the file on disk from claiming something the asset built from
            // it does not. The links come off the copy being written, not off the session's own objects,
            // which keep theirs for as long as the session lasts.
            Importers.ImportHelper.FlattenNestedPrefabLinks(echo, insideInstance: true);

            if (Path != null && Project.Current != null)
            {
                string absolutePath = System.IO.Path.Combine(Project.Current.AssetsPath, Path);
                if (!PrefabUtility.TryWriteFile(absolutePath, echo.WriteToString()))
                    return false;

                PrefabUtility.RaisePrefabSaved(Guid);
                EditorAssetBackend.Instance?.Reimport(Guid);

                IsDirty = false;
                Debug.Log($"[Prefab] Saved prefab: {Path}");
                return true;
            }
            return false;
        }

        public override void SaveAndClose()
        {
            if (Application.IsPlaying)
            {
                Debug.LogWarning("[Prefab] Cannot save a prefab during play mode.");
                return;
            }

            Save();
            PrefabUtility.MarkInstancesStale(Guid);
            IsolatedWorld.CloseWorld(this);
            IsolatedWorld.End(this);
            Debug.Log("[Prefab] Saved and exited editing mode.");
        }

        public override void Close()
        {
            IsolatedWorld.CloseWorld(this);
            IsolatedWorld.End(this);
            Debug.Log("[Prefab] Exited editing mode.");
        }
    }
}
