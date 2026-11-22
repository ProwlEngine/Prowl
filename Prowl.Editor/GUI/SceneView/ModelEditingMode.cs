// This file is part of the Prowl Game Engine
// Licensed under the MIT License. See the LICENSE file in the project root for details.

using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;

using Prowl.Editor.Core;
using Prowl.Editor.Modeling;
using Prowl.Editor.Projects;
using Prowl.Rosetta;
using Prowl.Runtime;
using Prowl.Runtime.Resources;
using Prowl.Vector;

namespace Prowl.Editor.GUI.SceneView;

/// <summary>
/// The model world: a <c>.pmesh</c> model edited in a world of its own, one <see cref="ModelObject"/> per object of
/// the model, while the edit world sleeps underneath. Meshes are never edited in place, only Save writes the file.
/// </summary>
public static class ModelEditingMode
{
    private static ModelSession? Session => IsolatedWorld.Current as ModelSession;

    public static bool IsEditing => Session != null;

    public static Guid EditingModelGuid => Session?.Guid ?? Guid.Empty;
    public static string? EditingModelPath => Session?.Path;

    /// <summary>The world the model is edited in, while a session runs.</summary>
    public static Scene? World => IsEditing ? IsolatedWorld.World : null;

    /// <summary>Opens a model for editing. An open session with unsaved changes is offered a save first.</summary>
    public static void Open(Guid modelGuid)
    {
        if (Application.IsPlaying)
        {
            Debug.LogWarning("[Model] Cannot open a model for editing during play mode.");
            return;
        }
        IsolatedWorld.Replace(() => OpenInternal(modelGuid));
    }

    private static void OpenInternal(Guid modelGuid)
    {
        AssetEntry? entry = EditorAssetBackend.Instance?.GetEntry(modelGuid);
        if (entry == null || Project.Current == null)
        {
            Debug.LogWarning("[Model] Cannot edit a model that is not in the project.");
            return;
        }

        string file = Path.Combine(Project.Current.AssetsPath, entry.Path);
        string name = Path.GetFileNameWithoutExtension(entry.Path);
        if (PendingRecovery(modelGuid) is { } recovery)
        {
            OrigamiUI.Origami.Confirm(Loc.Get("model.recover_title"), Loc.Get("model.recover_body", new { name }),
                onYes: () => OpenFile(recovery, modelGuid, entry.Path, name, recovered: true),
                onNo: () =>
                {
                    DeleteRecovery(modelGuid);
                    OpenFile(file, modelGuid, entry.Path, name, recovered: false);
                });
            return;
        }
        OpenFile(file, modelGuid, entry.Path, name, recovered: false);
    }

    /// <summary>Opens a model from a file, its own or a recovery copy of it, which leaves the model with unsaved changes.</summary>
    internal static void OpenFile(string file, Guid modelGuid, string? path, string name, bool recovered)
    {
        PMeshDocument document;
        try
        {
            document = PMeshDocument.Read(File.ReadAllBytes(file));
        }
        catch (Exception e)
        {
            Debug.LogException("[Model] Reading the model failed", e);
            return;
        }

        var session = new ModelSession(modelGuid, path, name);
        OpenDocument(document, session);
        if (!ReferenceEquals(IsolatedWorld.Current, session)) return;
        session.Watch();
        if (recovered) session.IsDirty = true;
    }

    /// <summary>Whether the open model's file holds something other than what the session last read or wrote, as after a pull.</summary>
    internal static bool ChangedOnDisk() => Session?.ChangedOnDisk() ?? false;

    // ================================================================
    //  Recovery copies
    // ================================================================

    /// <summary>Where a model's recovery copy is kept, outside the assets so nothing imports it.</summary>
    internal static string? RecoveryPath(Guid modelGuid)
        => Project.Current == null || modelGuid == Guid.Empty ? null : Path.Combine(Project.Current.LibraryPath, "ModelRecovery", modelGuid.ToString("N") + PMeshDocument.Extension);

    /// <summary>A recovery copy newer than the model's own file, left by a session that did not end cleanly.</summary>
    internal static string? PendingRecovery(Guid modelGuid)
    {
        if (RecoveryPath(modelGuid) is not { } recovery || !File.Exists(recovery)) return null;
        if (EditorAssetBackend.Instance?.GetEntry(modelGuid) is not { } entry || Project.Current == null) return null;
        string file = Path.Combine(Project.Current.AssetsPath, entry.Path);
        return !File.Exists(file) || File.GetLastWriteTimeUtc(recovery) > File.GetLastWriteTimeUtc(file) ? recovery : null;
    }

    internal static void DeleteRecovery(Guid modelGuid)
    {
        if (RecoveryPath(modelGuid) is { } recovery && File.Exists(recovery)) File.Delete(recovery);
    }

    /// <summary>Writes the open model's recovery copy, which is what the auto save timer does instead of saving the model.</summary>
    internal static bool WriteRecovery() => Session?.WriteRecovery() ?? false;

    /// <summary>Builds a document's model world and makes it the open session, the edit world asleep under it.</summary>
    private static void OpenDocument(PMeshDocument document, ModelSession session)
    {
        Scene edit = EditorSceneManager.EditWorld;
        EditorSceneManager.ApplyPendingNow();

        string name = session.Name;
        Scene world = Scene.Create($"Editing: {name}", SceneMode.Edit);
        try
        {
            Build(world, document);
        }
        catch (Exception e)
        {
            Debug.LogException("[Model] Opening the model threw", e);
            if (world.IsValid()) world.Dispose();
            if (edit.State == SceneState.Dormant) edit.Activate();
            return;
        }

        Selection.Clear();
        edit.Deactivate();
        world.Activate();
        IsolatedWorld.Begin(session, world, edit);
        Debug.Log($"[Model] Entered editing mode: {name}");
    }

    /// <summary>One object per model object, under its parent, with the viewing camera and light.</summary>
    internal static void Build(Scene world, PMeshDocument document)
    {
        var objects = new Dictionary<Guid, GameObject>();
        foreach (PMeshObject item in document.Objects)
        {
            var go = new GameObject(item.Name);
            ModelObject model = go.AddComponent<ModelObject>();
            model.Id = item.Id;
            model.Mesh = item.Mesh;
            model.Materials = new List<Guid>(item.Materials);
            model.SmoothingAngle = item.SmoothingAngle;
            model.Modifiers = item.Modifiers;
            objects[item.Id] = go;
        }

        GameObject? first = null;
        foreach (PMeshObject item in document.Objects)
        {
            GameObject go = objects[item.Id];
            if (objects.TryGetValue(item.Parent, out GameObject? parent)) go.SetParent(parent, false);
            go.Transform.LocalPosition = item.Position;
            go.Transform.LocalRotation = item.Rotation;
            go.Transform.LocalScale = item.Scale;
        }
        foreach (PMeshObject item in document.Objects)
        {
            GameObject go = objects[item.Id];
            go.GetComponent<ModelObject>()!.RefreshPreview();
            if (go.Parent.IsValid()) continue;
            world.Add(go);
            if (first.IsNotValid()) first = go;
        }

        IsolatedWorld.AddViewingRig(world, first, "ModelEdit");
    }

    /// <summary>The model objects of a world as a document, each parent written before its children.</summary>
    internal static PMeshDocument Gather(Scene world)
    {
        var document = new PMeshDocument();
        var ids = new HashSet<Guid>();
        foreach (GameObject root in world.Roots)
        {
            if (root.HideFlags.HasFlag(HideFlags.HideAndDontSave)) continue;
            Collect(root, document, ids);
        }
        return document;
    }

    // A duplicated object copies its original's id, so the second to be written takes a new one
    private static void Collect(GameObject go, PMeshDocument document, HashSet<Guid> ids)
    {
        if (go.GetComponent<ModelObject>() is { } model && model.IsValid())
        {
            if (!ids.Add(model.Id))
            {
                model.Id = Guid.NewGuid();
                ids.Add(model.Id);
            }
            document.Objects.Add(model.ToDocumentObject());
        }
        foreach (GameObject child in go.Children) Collect(child, document, ids);
    }

    public static bool Save() => Session?.Save() ?? false;

    public static void SaveAndExit() => Session?.SaveAndClose();

    /// <summary>Leaves the model world without saving.</summary>
    public static void Exit() => Session?.Close();

    /// <summary>Whether an object draws a mesh made only of triangles, which is what can be edited as a model.</summary>
    private static bool Drawable(GameObject go)
    {
        MeshRenderer? renderer = go.GetComponent<MeshRenderer>();
        if (renderer.IsNotValid() || renderer!.Mesh is not { } mesh || mesh.IsNotValid()) return false;
        for (int i = 0; i < mesh.SubMeshCount; i++)
            if (mesh.GetSubMesh(i).Topology != Topology.Triangles) return false;
        return true;
    }

    public static bool CanEditAsModel(IReadOnlyList<GameObject> objects)
    {
        foreach (GameObject go in objects)
            if (go.IsValid() && Drawable(go)) return true;
        return false;
    }

    /// <summary>
    /// Opens a new model made from copies of the objects that draw meshes, placed around the active one. Nothing in the
    /// scene changes, and the model has no file until its first save asks where.
    /// </summary>
    public static void EditAsModel(IReadOnlyList<GameObject> objects, GameObject? active = null)
    {
        if (Application.IsPlaying)
        {
            Debug.LogWarning("[Model] Cannot edit as a model during play mode.");
            return;
        }

        var drawn = new List<GameObject>();
        foreach (GameObject go in objects)
            if (go.IsValid() && Drawable(go)) drawn.Add(go);
        if (drawn.Count == 0) return;
        GameObject centre = active.IsValid() && drawn.Contains(active!) ? active! : drawn[0];

        PMeshDocument document = PMeshDocument.FromObjects(drawn, centre);
        IsolatedWorld.Replace(() => OpenDocument(document, new ModelSession(Guid.Empty, null, centre.Name)));
    }

    /// <summary>
    /// Merges the selected model objects into the active one, as one undo step. Their meshes move into its space, their
    /// material slots merge into its slots, and their children move under it, keeping where they are.
    /// </summary>
    public static void Join(IReadOnlyList<GameObject> selected, GameObject active)
    {
        const string Name = "Join Model Objects";
        if (!IsEditing || active.GetComponent<ModelObject>() is not { } target || target.IsNotValid()) return;

        var others = new List<ModelObject>();
        foreach (GameObject go in selected)
            if (go != active && go.GetComponent<ModelObject>() is { } model && model.IsValid()) others.Add(model);
        if (others.Count == 0) return;

        Undo.RecordObject(target, Name);
        Float4x4 toTarget = active.Transform.WorldToLocalMatrix;
        foreach (ModelObject other in others)
        {
            Operators.Join(target.Mesh, target.Materials, other.Mesh, other.Materials, toTarget * other.Transform.LocalToWorldMatrix);
            foreach (GameObject child in other.GameObject.Children.ToList())
                if (!others.Exists(o => o.GameObject == child)) Undo.SetTransformParent(child, active, true, Name);
        }
        foreach (ModelObject other in others) Undo.DestroyObjectImmediate(other.GameObject);
        target.RefreshPreview();
        Selection.Select(active);
    }

    /// <summary>Moves an object's origin to the middle of its mesh, the mesh staying where it is in the world.</summary>
    public static void OriginToGeometry(IReadOnlyList<GameObject> selected)
    {
        foreach (GameObject go in selected)
        {
            if (go.GetComponent<ModelObject>() is not { } model || model.IsNotValid() || model.Mesh.Vertices.Count == 0) continue;
            Float3 centre = Float3.Zero;
            foreach (Vector.Geometry.GeometryData.Vertex vertex in model.Mesh.Vertices) centre += vertex.Point;
            MoveOrigin(model, centre / model.Mesh.Vertices.Count, "Origin To Geometry");
        }
    }

    /// <summary>Moves a model object's origin to a point of its mesh, leaving the mesh where it is in the world.</summary>
    internal static void MoveOrigin(ModelObject model, Float3 local, string undoName)
    {
        GameObject go = model.GameObject;
        Undo.RecordObject(model, undoName);
        Undo.RecordObject(go, undoName);
        foreach (Vector.Geometry.GeometryData.Vertex vertex in model.Mesh.Vertices) vertex.Point -= local;
        model.Mesh.MarkChanged();
        go.Transform.Position = go.Transform.TransformPoint(local);
        model.RefreshPreview();
    }

    /// <summary>Whether an asset is a model document, whose instances open in the model world.</summary>
    public static bool IsModelAsset(Guid asset)
        => EditorAssetBackend.Instance?.GetEntry(asset) is { } entry && entry.Path.EndsWith(PMeshDocument.Extension, StringComparison.OrdinalIgnoreCase);

    [MenuItem("SceneView/Edit as Model", priority: 40, SelectionWide = true)]
    private static void EditSelectionAsModel()
        => EditAsModel([.. Selection.GetSelected<GameObject>()], Selection.ActiveObject as GameObject);

    [MenuItem("SceneView/Edit as Model", isValidate: true)]
    private static bool CanEditSelectionAsModel() => !IsolatedWorld.IsOpen && CanEditAsModel([.. Selection.GetSelected<GameObject>()]);

    /// <summary>Writes the open model to a path in the project, which becomes its file from then on.</summary>
    internal static bool SaveAs(string relativePath) => Session?.SaveTo(relativePath) ?? false;

    /// <summary>Whether an asset can be made into a model: a mesh, or a model's prefab.</summary>
    public static bool CanCreateModelFrom(Guid asset)
    {
        Type? type = AssetDatabase.GetAssetType(asset);
        return type == typeof(Mesh) || type == typeof(PrefabAsset) && AssetDatabase.Load<PrefabAsset>(asset) is { InstanceType: PrefabInstanceType.Model };
    }

    /// <summary>
    /// Writes a model made from a mesh or a model's objects beside it, then opens it. The source is not changed.
    /// Returns the new model, or empty when nothing could be made.
    /// </summary>
    public static Guid CreateModelFrom(Guid asset, bool open = true)
    {
        EditorAssetBackend? backend = EditorAssetBackend.Instance;
        if (backend?.GetEntry(asset) is not { } entry || Project.Current == null) return Guid.Empty;

        PMeshDocument document;
        try
        {
            document = DocumentOf(asset);
        }
        catch (Exception e)
        {
            Debug.LogException("[Model] Making a model from the asset failed", e);
            return Guid.Empty;
        }
        if (document.Objects.Count == 0)
        {
            Debug.LogWarning("[Model] The asset draws no mesh to make a model from.");
            return Guid.Empty;
        }

        string folder = Path.GetDirectoryName(entry.Path)?.Replace(Path.DirectorySeparatorChar, '/') ?? "";
        string absoluteFolder = AssetCreateMenu.GetAbsoluteFolder(folder);
        string name = AssetCreateMenu.FindUniqueName(absoluteFolder, Path.GetFileNameWithoutExtension(entry.Path), PMeshDocument.Extension);
        File.WriteAllBytes(Path.Combine(absoluteFolder, name), document.Write());
        backend.Refresh();

        string relative = folder.Length > 0 ? $"{folder}/{name}" : name;
        if (backend.GetEntry(relative) is not { } created) return Guid.Empty;
        if (open) Open(created.Guid);
        return created.Guid;
    }

    private static PMeshDocument DocumentOf(Guid asset)
    {
        if (AssetDatabase.Load<Mesh>(asset) is { } mesh && mesh.IsValid())
        {
            var item = new PMeshObject { Name = mesh.Name, Mesh = MeshGeometry.ToGeometryData(mesh, PMeshDocument.JoinDegrees) };
            Vector.Geometry.GeometryOperators.SoftenByAngle(item.Mesh, item.Mesh.Edges, item.SmoothingAngle);
            return new PMeshDocument { Objects = { item } };
        }

        PrefabAsset prefab = AssetDatabase.Load<PrefabAsset>(asset)!;
        GameObject root = GameObject.InstantiateDetached(prefab);
        try
        {
            var objects = new List<GameObject>();
            Collect(root, objects);
            return PMeshDocument.FromObjects(objects, root);
        }
        finally
        {
            root.Dispose();
        }

        static void Collect(GameObject go, List<GameObject> into)
        {
            into.Add(go);
            foreach (GameObject child in go.Children) Collect(child, into);
        }
    }

    /// <summary>The model world's Create menu: an empty object and the primitives, each a model object.</summary>
    internal static void BuildCreateMenu(OrigamiUI.ContextBuilder builder)
    {
        builder.Item(Loc.Get("model.create_empty"), () => CreateObject(Loc.Get("model.create_empty"), null), icon: Theming.EditorIcons.Cube);
        foreach (PrimitiveKind kind in Enum.GetValues<PrimitiveKind>())
        {
            PrimitiveKind chosen = kind;
            builder.Item(Loc.Get($"model.primitive_{kind.ToString().ToLowerInvariant()}"), () => CreateObject(Loc.Get($"model.primitive_{chosen.ToString().ToLowerInvariant()}"), chosen),
                icon: Theming.EditorIcons.Shapes);
        }
    }

    /// <summary>Makes a model object under the menu's object, from a primitive or empty, selected and recorded for undo.</summary>
    internal static GameObject CreateObject(string name, PrimitiveKind? kind)
    {
        GameObject? parent = MenuContext.ActiveGameObject;
        GameObject go = Panels.HierarchyPanel.CreateGameObject(name, parent.IsValid() && parent!.GetComponent<ModelObject>().IsValid() ? parent : null);
        ModelObject model = go.AddComponent<ModelObject>();
        if (kind is { } primitive) model.Mesh = Primitives.Make(primitive);
        model.RefreshPreview();
        return go;
    }

    [AssetDoubleClickHandler(PMeshDocument.Extension)]
    private static bool OpenModelHandler(string relativePath, Guid guid)
    {
        Open(guid);
        return true;
    }

    /// <summary>A model open in its own world: which asset, and where it is written, none until a model made from objects is first saved.</summary>
    private sealed class ModelSession(Guid guid, string? path, string name) : IsolatedSession
    {
        public Guid Guid { get; private set; } = guid;
        public string? Path { get; private set; } = path;

        public override string Name => Path != null ? System.IO.Path.GetFileNameWithoutExtension(Path) : name;

        public override string SaveLabel => Loc.Get("save.model", new { name = Name });

        public override string HistoryTitle => Loc.Get("undo_history.model");

        public override (string Title, string Body) UnsavedQuestion
            => (Loc.Get("dialog.unsaved_model"), Loc.Get("dialog.unsaved_model_body", new { name = Name }));

        // What the file held when last read or written, to tell its own saves from changes made outside
        private byte[]? _known;

        /// <summary>Starts noticing the file change on disk, from what it holds now.</summary>
        public void Watch()
        {
            _known = FileHash();
            if (EditorAssetBackend.Instance is { } backend) backend.OnAssetsImported += OnImported;
        }

        private void Unwatch()
        {
            if (EditorAssetBackend.Instance is { } backend) backend.OnAssetsImported -= OnImported;
        }

        private byte[]? FileHash()
        {
            if (Path == null || Project.Current == null) return null;
            string file = System.IO.Path.Combine(Project.Current.AssetsPath, Path);
            return File.Exists(file) ? System.Security.Cryptography.SHA256.HashData(File.ReadAllBytes(file)) : null;
        }

        public bool ChangedOnDisk()
        {
            byte[]? now = FileHash();
            return _known != null && now != null && !now.AsSpan().SequenceEqual(_known);
        }

        /// <summary>When the file was imported again with something new in it, asks whether to load it over the world.</summary>
        private void OnImported(string[] paths)
        {
            if (Path == null || !ReferenceEquals(IsolatedWorld.Current, this) || !Array.Exists(paths, p => string.Equals(p, Path, StringComparison.OrdinalIgnoreCase))) return;
            if (!ChangedOnDisk()) return;
            _known = FileHash();
            string file = System.IO.Path.Combine(Project.Current!.AssetsPath, Path);
            OrigamiUI.Origami.Confirm(Loc.Get("model.changed_title"), Loc.Get("model.changed_body", new { name = Name }),
                onYes: () =>
                {
                    Close();
                    OpenFile(file, Guid, Path, Name, recovered: false);
                },
                onNo: () => IsDirty = true);
        }

        public override bool Save()
        {
            if (Application.IsPlaying)
            {
                Debug.LogWarning("[Model] Cannot save a model during play mode.");
                return false;
            }
            // The auto save timer never writes the model's own file, only a copy to recover from
            if (SaveManager.IsAutoSave)
            {
                WriteRecovery();
                return false;
            }
            if (Path != null) return SaveTo(Path);
            PromptSaveAs(SaveManager.TakeAfterSaveAs());
            return false;
        }

        public bool WriteRecovery()
        {
            if (!IsDirty || IsolatedWorld.World is not { } world || RecoveryPath(Guid) is not { } recovery) return false;
            try
            {
                Directory.CreateDirectory(System.IO.Path.GetDirectoryName(recovery)!);
                File.WriteAllBytes(recovery, Gather(world).Write());
                return true;
            }
            catch (Exception e)
            {
                Debug.LogException("[Model] Writing the recovery copy failed", e);
                return false;
            }
        }

        /// <summary>Asks where to save a model with no file yet, offering Assets/Models and the model's name.</summary>
        private void PromptSaveAs(System.Action? after)
        {
            if (Project.Current == null) return;
            string folder = System.IO.Path.Combine(Project.Current.AssetsPath, "Models");
            Directory.CreateDirectory(folder);
            EditorApplication.OpenFileDialog(OrigamiUI.FileDialogMode.Save, chosen =>
            {
                if (chosen == null || Project.Current == null) return;
                string relative = EditorAssetBackend.NormalizePath(System.IO.Path.GetRelativePath(Project.Current.AssetsPath, chosen));
                if (!relative.EndsWith(PMeshDocument.Extension, StringComparison.OrdinalIgnoreCase)) relative += PMeshDocument.Extension;
                if (SaveTo(relative)) after?.Invoke();
            }, System.IO.Path.Combine(folder, name + PMeshDocument.Extension), ["*" + PMeshDocument.Extension], [Loc.Get("model.file_filter")]);
        }

        /// <summary>Writes the model to a path in the project and reimports it. The path becomes the model's file.</summary>
        public bool SaveTo(string relativePath)
        {
            if (IsolatedWorld.World is not { } world || Project.Current == null) return false;

            PMeshDocument document = Gather(world);
            string absolutePath = System.IO.Path.Combine(Project.Current.AssetsPath, relativePath);
            try
            {
                Directory.CreateDirectory(System.IO.Path.GetDirectoryName(absolutePath)!);
                File.WriteAllBytes(absolutePath, document.Write());
            }
            catch (Exception e)
            {
                Debug.LogException($"[Model] Writing {relativePath} failed", e);
                return false;
            }

            EditorAssetBackend? backend = EditorAssetBackend.Instance;
            bool first = Path == null;
            Path = relativePath;
            _known = FileHash();
            if (first)
            {
                backend?.Refresh();
                Guid = backend?.GetEntry(relativePath)?.Guid ?? Guid.Empty;
                Watch();
            }
            else backend?.Reimport(Guid);

            IsDirty = false;
            DeleteRecovery(Guid);
            Debug.Log($"[Model] Saved model: {relativePath}");
            return true;
        }

        public override void SaveAndClose()
        {
            if (Application.IsPlaying)
            {
                Debug.LogWarning("[Model] Cannot save a model during play mode.");
                return;
            }
            if (Path == null)
            {
                PromptSaveAs(Close);
                return;
            }
            Save();
            Close();
        }

        public override void Close()
        {
            Unwatch();
            DeleteRecovery(Guid);
            IsolatedWorld.CloseWorld(this);
            IsolatedWorld.End(this);
            Debug.Log("[Model] Exited editing mode.");
        }
    }
}
