// This file is part of the Prowl Game Engine
// Licensed under the MIT License. See the LICENSE file in the project root for details.

using System;
using System.Collections.Generic;
using System.IO;

using Prowl.Editor.Core;
using Prowl.Editor.Modeling;
using Prowl.Editor.Projects;
using Prowl.Rosetta;
using Prowl.Runtime;
using Prowl.Runtime.Resources;

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

        PMeshDocument document;
        try
        {
            document = PMeshDocument.Read(File.ReadAllBytes(Path.Combine(Project.Current.AssetsPath, entry.Path)));
        }
        catch (Exception e)
        {
            Debug.LogException("[Model] Reading the model failed", e);
            return;
        }

        Scene edit = EditorSceneManager.EditWorld;
        EditorSceneManager.ApplyPendingNow();

        string name = Path.GetFileNameWithoutExtension(entry.Path);
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
        IsolatedWorld.Begin(new ModelSession(modelGuid, entry.Path), world, edit);
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
        foreach (GameObject root in world.Roots)
        {
            if (root.HideFlags.HasFlag(HideFlags.HideAndDontSave)) continue;
            Collect(root, document);
        }
        return document;
    }

    private static void Collect(GameObject go, PMeshDocument document)
    {
        if (go.GetComponent<ModelObject>() is { } model) document.Objects.Add(model.ToDocumentObject());
        foreach (GameObject child in go.Children) Collect(child, document);
    }

    public static bool Save() => Session?.Save() ?? false;

    public static void SaveAndExit() => Session?.SaveAndClose();

    /// <summary>Leaves the model world without saving.</summary>
    public static void Exit() => Session?.Close();

    [AssetDoubleClickHandler(PMeshDocument.Extension)]
    private static bool OpenModelHandler(string relativePath, Guid guid)
    {
        Open(guid);
        return true;
    }

    /// <summary>A model open in its own world: which asset, and where it is written.</summary>
    private sealed class ModelSession(Guid guid, string path) : IsolatedSession
    {
        public Guid Guid { get; } = guid;
        public string Path { get; } = path;

        public override string Name => System.IO.Path.GetFileNameWithoutExtension(Path);

        public override string SaveLabel => Loc.Get("save.model", new { name = Name });

        public override string HistoryTitle => Loc.Get("undo_history.model");

        public override (string Title, string Body) UnsavedQuestion
            => (Loc.Get("dialog.unsaved_model"), Loc.Get("dialog.unsaved_model_body", new { name = Name }));

        public override bool Save()
        {
            if (Application.IsPlaying)
            {
                Debug.LogWarning("[Model] Cannot save a model during play mode.");
                return false;
            }
            if (IsolatedWorld.World is not { } world || Project.Current == null) return false;

            PMeshDocument document = Gather(world);
            string absolutePath = System.IO.Path.Combine(Project.Current.AssetsPath, Path);
            try
            {
                File.WriteAllBytes(absolutePath, document.Write());
            }
            catch (Exception e)
            {
                Debug.LogException($"[Model] Writing {Path} failed", e);
                return false;
            }

            EditorAssetBackend.Instance?.Reimport(Guid);
            IsDirty = false;
            Debug.Log($"[Model] Saved model: {Path}");
            return true;
        }

        public override void SaveAndClose()
        {
            if (Application.IsPlaying)
            {
                Debug.LogWarning("[Model] Cannot save a model during play mode.");
                return;
            }
            Save();
            Close();
        }

        public override void Close()
        {
            IsolatedWorld.CloseWorld(this);
            IsolatedWorld.End(this);
            Debug.Log("[Model] Exited editing mode.");
        }
    }
}
