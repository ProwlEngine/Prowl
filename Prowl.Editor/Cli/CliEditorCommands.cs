// This file is part of the Prowl Game Engine
// Licensed under the MIT License. See the LICENSE file in the project root for details.

using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Runtime.CompilerServices;
using System.Text.Json.Nodes;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using System.Xml.Linq;

using Prowl.Editor.Core;
using Prowl.Editor.GUI;
using Prowl.Editor.GUI.Panels;
using Prowl.Editor.Inspector;
using Prowl.Editor.GUI.SceneView;
using Prowl.Editor.Prefabs;
using Prowl.Editor.Projects;
using Prowl.Editor.Projects.Scripting;
using Prowl.Runtime;
using Prowl.Runtime.Rendering.Shaders;
using Prowl.Runtime.Resources;
using Prowl.Vector;

namespace Prowl.Editor;

/// <summary> Every log the editor writes, kept in a ring so the CLI can read what happened between its calls. Thread safe. </summary>
public static class LogHistory
{
    public readonly record struct Entry(long Seq, DateTime TimeUtc, LogSeverity Severity, string Message, DebugStackTrace? Stack);

    private const int Capacity = 4000;
    private static readonly Entry[] s_ring = new Entry[Capacity];
    private static long s_next = 1;
    private static readonly object s_lock = new();

    /// <summary> The sequence number the next log will get. </summary>
    public static long NextSeq { get { lock (s_lock) return s_next; } }

    public static void Add(string message, DebugStackTrace? stack, LogSeverity severity)
    {
        lock (s_lock)
        {
            long seq = s_next++;
            s_ring[seq % Capacity] = new Entry(seq, DateTime.UtcNow, severity, message, stack);
        }
    }

    /// <summary> Logs from <paramref name="since"/> on, oldest first, that pass the filter. </summary>
    public static List<Entry> Since(long since, Func<LogSeverity, bool> filter, int limit)
    {
        var result = new List<Entry>();
        lock (s_lock)
        {
            long first = Math.Max(since, Math.Max(1, s_next - Capacity));
            for (long seq = first; seq < s_next && result.Count < limit; seq++)
            {
                var entry = s_ring[seq % Capacity];
                if (entry.Seq == seq && filter(entry.Severity)) result.Add(entry);
            }
        }
        return result;
    }

    /// <summary> The most recent logs that pass the filter, oldest first. </summary>
    public static List<Entry> Latest(Func<LogSeverity, bool> filter, int limit)
    {
        var result = new List<Entry>();
        lock (s_lock)
        {
            long oldest = Math.Max(1, s_next - Capacity);
            for (long seq = s_next - 1; seq >= oldest && result.Count < limit; seq--)
            {
                var entry = s_ring[seq % Capacity];
                if (entry.Seq == seq && filter(entry.Severity)) result.Add(entry);
            }
        }
        result.Reverse();
        return result;
    }
}

/// <summary> The built in commands an agent uses to drive the editor. </summary>
public static class CliEditorCommands
{
    public enum LogFilter { All, Warning, Error }
    public enum GoAction { Create, Delete, Duplicate, Rename, Parent, Active, Transform }
    public enum ComponentAction { Add, Remove, List }
    public enum SceneAction { Info, Open, Save, SaveAs, New }

    // ================================================================
    //  Editor
    // ================================================================

    [CliCommand("status", "Open project, scene, play and compile state, selection, and the log sequence to pass to 'logs --since'")]
    public static object Status() => new
    {
        project = Project.Current?.Name,
        projectPath = Project.Current?.RootPath,
        scene = EditorSceneManager.CurrentScenePath,
        dirty = EditorSceneManager.IsDirty,
        isPlaying = Application.IsPlaying,
        isPaused = Application.IsPaused,
        compiling = ScriptAssemblyManager.IsCompiling,
        lastCompile = ScriptAssemblyManager.LastCompile is { } c
            ? new { ok = c.Success, errors = c.Diagnostics.Count(d => d.Severity == "Error"), warnings = c.Diagnostics.Count(d => d.Severity == "Warning") }
            : null,
        selection = Selection.Selected.OfType<GameObject>().Where(g => g.IsValid()).Select(CliRefs.Summary).ToList(),
        focused = Window.IsFocused,
        logSeq = LogHistory.NextSeq,
    };

    [CliCommand("logs", "Editor logs, oldest first. Without --since it returns the latest. Pass the nextSeq from the last call as --since to get only what is new")]
    public static object Logs(
        [CliArg("since", "Sequence number to start from, 0 for the latest")] long since = 0,
        [CliArg("level", "Lowest severity to include")] LogFilter level = LogFilter.All,
        [CliArg("limit", "Most entries to return")] int limit = 100,
        [CliArg("stack", "Include stack traces")] bool stack = false)
    {
        Func<LogSeverity, bool> filter = level switch
        {
            LogFilter.Error => s => s is LogSeverity.Error or LogSeverity.Exception,
            LogFilter.Warning => s => s is LogSeverity.Warning or LogSeverity.Error or LogSeverity.Exception,
            _ => _ => true,
        };
        limit = Math.Clamp(limit, 1, 1000);
        var entries = since > 0 ? LogHistory.Since(since, filter, limit) : LogHistory.Latest(filter, limit);
        return new
        {
            nextSeq = since > 0 && entries.Count == limit ? entries[^1].Seq + 1 : LogHistory.NextSeq,
            logs = entries.Select(e => new
            {
                seq = e.Seq,
                time = e.TimeUtc.ToString("HH:mm:ss.fff"),
                severity = e.Severity.ToString(),
                message = e.Message,
                stack = stack ? e.Stack?.ToString() : null,
            }).ToList(),
        };
    }

    [CliCommand("undo", "Undoes, or with --redo redoes, editor steps")]
    public static object UndoCommand([CliArg("steps", "How many steps")] int steps = 1, [CliArg("redo", "Redo instead")] bool redo = false)
    {
        if (Application.IsPlaying) throw new CliException("Undo is not available in play mode.");

        var done = new List<string>();
        for (int i = 0; i < steps; i++)
        {
            if (redo ? !Undo.CanRedo : !Undo.CanUndo) break;
            done.Add(redo ? Undo.RedoDescription : Undo.UndoDescription);
            if (redo) Undo.PerformRedo(); else Undo.PerformUndo();
        }
        return new { done, nextUndo = Undo.CanUndo ? Undo.UndoDescription : null, nextRedo = Undo.CanRedo ? Undo.RedoDescription : null };
    }

    [CliCommand("compile", "Recompiles scripts, waits for the hot reload, and returns the errors and warnings")]
    public static async Task<object> Compile([CliArg("wait", "Wait for the compile to finish")] bool wait = true)
    {
        var finished = new TaskCompletionSource<ScriptAssemblyManager.CompileReport>(TaskCreationOptions.RunContinuationsAsynchronously);
        Action<ScriptAssemblyManager.CompileReport> onFinished = r => finished.TrySetResult(r);
        ScriptAssemblyManager.CompileFinished += onFinished;
        try
        {
            ScriptAssemblyManager.RequestRecompile();
            if (!wait) return new { queued = true };

            // A compile already running finishes first, then the one asked for here runs.
            var report = await finished.Task;
            while (ScriptAssemblyManager.IsCompiling)
            {
                finished = new TaskCompletionSource<ScriptAssemblyManager.CompileReport>(TaskCreationOptions.RunContinuationsAsynchronously);
                report = await finished.Task;
            }
            return CompileResult(report);
        }
        finally
        {
            ScriptAssemblyManager.CompileFinished -= onFinished;
        }
    }

    private static object CompileResult(ScriptAssemblyManager.CompileReport report)
    {
        string root = Project.Current?.RootPath ?? "";
        return new
        {
            ok = report.Success,
            reloaded = report.Reloaded,
            durationMs = report.DurationMs,
            errors = report.Diagnostics.Count(d => d.Severity == "Error"),
            warnings = report.Diagnostics.Count(d => d.Severity == "Warning"),
            diagnostics = report.Diagnostics
                .OrderBy(d => d.Severity == "Error" ? 0 : 1)
                .Take(100)
                .Select(d => new
                {
                    severity = d.Severity,
                    id = d.Id,
                    file = d.File.Length > 0 && root.Length > 0 ? Path.GetRelativePath(root, d.File).Replace('\\', '/') : d.File,
                    line = d.Line,
                    col = d.Column,
                    message = d.Message,
                }).ToList(),
        };
    }

    // ================================================================
    //  Scene and hierarchy
    // ================================================================

    [CliCommand("scene", "Shows, opens, saves or creates the scene. Opening or creating refuses to drop unsaved changes unless --force")]
    public static object SceneCommand(
        [CliArg("action")] SceneAction action = SceneAction.Info,
        [CliArg("path", "Scene path relative to Assets, for open and saveas")] string path = "",
        [CliArg("force", "Discard unsaved changes")] bool force = false)
    {
        switch (action)
        {
            case SceneAction.Open:
                RequirePath(path);
                RequireClean(force);
                if (!EditorSceneManager.OpenScene(CliRefs.AssetPath(path))) throw new CliException($"Could not open '{path}'. See the logs.");
                break;
            case SceneAction.New:
                RequireClean(force);
                EditorSceneManager.NewScene();
                break;
            case SceneAction.Save:
                if (EditorSceneManager.CurrentScenePath == null) throw new CliException("The scene has never been saved. Use --action saveas --path Scenes/Name.scene.");
                if (!EditorSceneManager.Save()) throw new CliException("Saving failed. See the logs.");
                break;
            case SceneAction.SaveAs:
                RequirePath(path);
                if (!EditorSceneManager.SaveAs(CliRefs.AssetPath(path))) throw new CliException("Saving failed. See the logs.");
                break;
        }

        // Opening or creating only queues the swap for the end of the frame. Commands run between frames, so it can happen now.
        if (Scene.IsLoadPending) Scene.ProcessPendingLoad();

        return new
        {
            path = EditorSceneManager.CurrentScenePath,
            dirty = EditorSceneManager.IsDirty,
            objects = Scene.Current.IsValid() ? Scene.Current.AllObjects.Count() : 0,
            roots = Scene.Current.IsValid() ? Scene.Current.RootObjects.Select(g => g.Name).ToList() : [],
        };

        static void RequirePath(string path)
        {
            if (path.Length == 0) throw new CliException("Give --path, relative to the Assets folder.");
        }

        static void RequireClean(bool force)
        {
            if (EditorSceneManager.IsDirty && !force) throw new CliException("The scene has unsaved changes. Save it first, or pass --force to discard them.");
        }
    }

    [CliCommand("tree", "The scene hierarchy, or part of it. --filter lists every object whose name or component type contains the text")]
    public static object Tree(
        [CliArg("root", "Start here instead of the scene root")] string root = "",
        [CliArg("depth", "Levels to descend")] int depth = 3,
        [CliArg("components", "List component types")] bool components = true,
        [CliArg("filter", "Name or component type to search for")] string filter = "")
    {
        IEnumerable<GameObject> starts = root.Length > 0 ? [CliRefs.ResolveGameObject(root)] : CliRefs.Scene.RootObjects;

        if (filter.Length > 0)
        {
            var matches = starts.SelectMany(g => g.GetChildrenDeep().Prepend(g))
                .Where(g => g.Name.Contains(filter, StringComparison.OrdinalIgnoreCase)
                         || g.GetComponents().Any(c => c.GetType().Name.Contains(filter, StringComparison.OrdinalIgnoreCase)))
                .Distinct()
                .Take(200)
                .Select(g => Node(g, 0, components))
                .ToList();
            return new { count = matches.Count, matches };
        }

        return starts.Select(g => Node(g, Math.Max(0, depth), components)).ToList();
    }

    private static JsonObject Node(GameObject go, int depth, bool components)
    {
        var node = CliRefs.Summary(go);
        if (!go.Enabled) node["active"] = false;
        if (go.IsPrefabInstance) node["prefab"] = true;
        if (components) node["components"] = new JsonArray(go.GetComponents().Select(c => (JsonNode)JsonValue.Create(c.GetType().Name)).ToArray());
        if (go.Children.Count > 0)
        {
            if (depth > 0) node["children"] = new JsonArray(go.Children.Select(c => (JsonNode)Node(c, depth - 1, components)).ToArray());
            else node["childCount"] = go.Children.Count;
        }
        return node;
    }

    [CliCommand("select", "Selects objects so the person watching sees them, optionally framing them in the scene view. Empty clears")]
    public static object Select(
        [CliArg("refs", "Comma separated refs")] string refs = "",
        [CliArg("frame", "Frame the selection in the scene view")] bool frame = false)
    {
        var targets = refs.Trim().Length == 0 ? [] : CliRefs.ResolveGameObjects(refs);
        Selection.Clear();
        foreach (var go in targets) Selection.AddToSelection(go);
        if (targets.Count > 0) Selection.Ping(targets[0].Identifier);
        if (frame && targets.Count > 0) SceneViewPanel.ActiveCamera?.FocusSelection();
        return targets.Select(CliRefs.Summary).ToList();
    }

    [CliCommand("go", "Creates, deletes, duplicates, renames, reparents, activates or moves GameObjects. One undo step per call")]
    public static object Go(
        [CliArg("action")] GoAction action,
        [CliArg("target", "Ref of the object, or a comma list for delete, duplicate and active")] string target = "",
        [CliArg("name", "Name for create and rename")] string name = "",
        [CliArg("parent", "Parent ref for create and parent. / is the scene root")] string parent = "",
        [CliArg("prefab", "Prefab or model asset to instantiate on create")] string prefab = "",
        [CliArg("components", "Comma separated component types to add on create")] string components = "",
        [CliArg("position", "[x, y, z]")] string position = "",
        [CliArg("rotation", "Euler degrees [x, y, z]")] string rotation = "",
        [CliArg("scale", "[x, y, z]")] string scale = "",
        [CliArg("world", "Position and rotation are in world space instead of relative to the parent")] bool world = false,
        [CliArg("active", "For active: whether the objects are active")] bool active = true,
        [CliArg("index", "Sibling index for create and parent")] int index = -1)
    {
        var scene = CliRefs.Scene;
        switch (action)
        {
            case GoAction.Create:
                return CliEdit.Run("Create", () =>
                {
                    GameObject go;
                    if (prefab.Length > 0)
                    {
                        Guid guid = CliRefs.ResolveAssetGuid(prefab);
                        go = PrefabUtility.InstantiatePrefab(guid) is { } instance && instance.IsValid()
                            ? instance : throw new CliException($"'{prefab}' is not a prefab or model that can be instantiated.");
                        if (name.Length > 0) go.Name = name;
                    }
                    else go = new GameObject(name.Length > 0 ? name : "GameObject");

                    scene.Add(go);
                    if (parent.Length > 0 && parent != "/") go.SetParent(CliRefs.ResolveGameObject(parent), worldPositionStays: false);
                    if (index >= 0) go.SetSiblingIndex(index);
                    foreach (string typeName in CliRefs.SplitList(components))
                        go.AddComponent(CliRefs.FindType(typeName, typeof(MonoBehaviour)));
                    ApplyTransform(go, position, rotation, scale, world);

                    Selection.Select(go);
                    Undo.RegisterCreatedObject(go, "CLI: Create");
                    return Described(go);
                });

            case GoAction.Delete:
            {
                var targets = Targets(target);
                if (PrefabUtility.NeedsBreaking(targets))
                    throw new CliException("Deleting this would change a prefab instance's structure. Run 'prefab --action unpack' on the instance first.");
                var summaries = targets.Select(CliRefs.Summary).ToList();
                CliEdit.Run("Delete", () => { foreach (var go in targets) if (go.IsValid()) HierarchyPanel.DeleteOneGameObject(go); });
                return new { deleted = summaries };
            }

            case GoAction.Duplicate:
                return CliEdit.Run("Duplicate", () =>
                {
                    var copies = GameObjectClipboard.Duplicate(Targets(target));
                    foreach (var copy in copies) Undo.RegisterCreatedObject(copy, "CLI: Duplicate");
                    return copies.Select(CliRefs.Summary).ToList();
                });

            case GoAction.Rename:
            {
                var go = CliRefs.ResolveGameObject(Require(target, "target"));
                string newName = Require(name, "name");
                CliEdit.Run("Rename", () =>
                {
                    Undo.RecordGameObjectChange(go, "CLI: Rename", go.Name, newName, (g, v) => g.Name = v);
                    go.Name = newName;
                });
                return CliRefs.Summary(go);
            }

            case GoAction.Parent:
            {
                var targets = Targets(target);
                GameObject? newParent = parent.Length == 0 || parent == "/" ? null : CliRefs.ResolveGameObject(parent);
                if (PrefabUtility.NeedsBreaking(targets))
                    throw new CliException("Moving this would change a prefab instance's structure. Run 'prefab --action unpack' on the instance first.");
                foreach (var go in targets)
                    if (newParent != null && GameObject.IsChildOrSameTransform(newParent, go))
                        throw new CliException($"{CliRefs.PathOf(newParent)} is {go.Name} or inside it.");

                CliEdit.Run("Parent", () =>
                {
                    var actions = targets.Select(go => Reparent(scene, go, newParent, index, world)).ToList();
                    CliEdit.Record("Parent", actions);
                });
                return targets.Select(CliRefs.Summary).ToList();
            }

            case GoAction.Active:
            {
                var targets = Targets(target);
                CliEdit.Run("Active", () => Undo.ApplyGameObjectChanges(targets, "CLI: Active", g => g.Enabled, (g, v) => g.Enabled = v, active));
                return targets.Select(CliRefs.Summary).ToList();
            }

            case GoAction.Transform:
            {
                var go = CliRefs.ResolveGameObject(Require(target, "target"));
                var before = (go.Transform.LocalPosition, go.Transform.LocalRotation, go.Transform.LocalScale);
                CliEdit.Run("Transform", () =>
                {
                    ApplyTransform(go, position, rotation, scale, world);
                    var after = (go.Transform.LocalPosition, go.Transform.LocalRotation, go.Transform.LocalScale);
                    Undo.RecordGameObjectChange(go, "CLI: Transform", before, after, (g, v) =>
                    {
                        g.Transform.LocalPosition = v.Item1;
                        g.Transform.LocalRotation = v.Item2;
                        g.Transform.LocalScale = v.Item3;
                    });
                });
                return Described(go);
            }
        }

        throw new CliException($"Unknown action {action}.");
    }

    private static List<GameObject> Targets(string target) => CliRefs.ResolveGameObjects(Require(target, "target"));

    private static string Require(string value, string name)
        => value.Length > 0 ? value : throw new CliException($"Give --{name}.");

    private static void ApplyTransform(GameObject go, string position, string rotation, string scale, bool world)
    {
        if (position.Length > 0)
        {
            var p = CliRefs.ParseFloat3(position, "position");
            if (world) go.Transform.Position = p; else go.Transform.LocalPosition = p;
        }
        if (rotation.Length > 0)
        {
            var r = CliRefs.ParseFloat3(rotation, "rotation");
            if (world) go.Transform.EulerAngles = r; else go.Transform.LocalEulerAngles = r;
        }
        if (scale.Length > 0) go.Transform.LocalScale = CliRefs.ParseFloat3(scale, "scale");
    }

    private static JsonObject Described(GameObject go)
    {
        var node = CliRefs.Summary(go);
        node["position"] = CliRefs.Json(go.Transform.Position);
        node["localPosition"] = CliRefs.Json(go.Transform.LocalPosition);
        node["localRotation"] = CliRefs.Json(go.Transform.LocalEulerAngles);
        node["localScale"] = CliRefs.Json(go.Transform.LocalScale);
        node["components"] = new JsonArray(go.GetComponents().Select(c => (JsonNode)CliRefs.Summary(c)).ToArray());
        return node;
    }

    private static (Action undo, Action redo) Reparent(Scene scene, GameObject go, GameObject? newParent, int index, bool keepWorld)
    {
        Guid goId = go.Identifier;
        Guid oldParentId = go.Parent.IsValid() ? go.Parent.Identifier : Guid.Empty;
        Guid newParentId = newParent.IsValid() ? newParent.Identifier : Guid.Empty;
        int oldIndex = go.Parent.IsValid() ? go.GetSiblingIndex() ?? -1 : scene.GetRootIndex(go);

        Move(go, newParentId, index);
        return (() => { if (Undo.FindGO(goId) is { } g) Move(g, oldParentId, oldIndex); },
                () => { if (Undo.FindGO(goId) is { } g) Move(g, newParentId, index); });

        void Move(GameObject g, Guid parentId, int siblingIndex)
        {
            var p = parentId == Guid.Empty ? null : Undo.FindGO(parentId);
            g.SetParent(p!, keepWorld);
            if (siblingIndex < 0) return;
            if (p.IsValid()) g.SetSiblingIndex(siblingIndex);
            else if (Scene.Current.IsValid()) Scene.Current.SetRootIndex(g, siblingIndex);
        }
    }

    [CliCommand("component", "Adds (optionally setting --values), removes, or lists components. One undo step per call")]
    public static object Component(
        [CliArg("action")] ComponentAction action,
        [CliArg("target", "GameObject ref, or a component ref like /Player:Rigidbody3D for remove")] string target,
        [CliArg("type", "Component type name for add")] string type = "",
        [CliArg("values", "JSON object of field path to value, applied after add")] string values = "")
    {
        switch (action)
        {
            case ComponentAction.List:
                return CliRefs.ResolveGameObject(target).GetComponents().Select(CliRefs.Summary).ToList();

            case ComponentAction.Add:
            {
                var go = CliRefs.ResolveGameObject(target);
                Type componentType = CliRefs.FindType(Require(type, "type"), typeof(MonoBehaviour));
                var fieldValues = ParseValues(values);
                return CliEdit.Run("Add Component", () =>
                {
                    var added = go.AddComponent(componentType);
                    if (added.IsNotValid()) throw new CliException($"{componentType.Name} could not be added to {go.Name}. See the logs.");
                    try
                    {
                        if (fieldValues.Count > 0) CliRefs.WriteFields(added, fieldValues);
                    }
                    catch
                    {
                        go.RemoveComponent(added);
                        throw;
                    }
                    RecordAdd(go, added);
                    var summary = CliRefs.Summary(added);
                    summary["fields"] = CliRefs.GetFields(added, "");
                    return summary;
                });
            }

            case ComponentAction.Remove:
            {
                if (CliRefs.Resolve(target) is not MonoBehaviour component) throw new CliException("Give a component ref, for example /Player:Rigidbody3D.");
                if (!component.CanDestroy()) throw new CliException($"{component.GetType().Name} can not be removed. Something else needs it.");
                if (PrefabUtility.NeedsBreaking(component)) throw new CliException("This component comes from a prefab. Run 'prefab --action unpack' on the instance first.");
                var summary = CliRefs.Summary(component);
                CliEdit.Run("Remove Component", () => GameObjectInspector.RemoveComponentWithUndo(component));
                return new { removed = summary };
            }
        }

        throw new CliException($"Unknown action {action}.");
    }

    private static void RecordAdd(GameObject go, MonoBehaviour added)
    {
        Guid goId = go.Identifier, compId = added.Identifier;
        Type compType = added.GetType();
        var serialized = Echo.Serializer.Serialize(compType, added);
        Undo.RegisterAction("CLI: Add Component",
            undo: () => { if (Undo.FindGO(goId) is { } g && g.GetComponentByIdentifier(compId) is { } c) g.RemoveComponent(c); },
            redo: () =>
            {
                if (Undo.FindGO(goId) is not { } g) return;
                if (Echo.Serializer.Deserialize(serialized, compType) is MonoBehaviour c) { c.Identifier = compId; g.AddComponent(c); }
            });
    }

    /// <summary> A JSON object of field path to value, each value kept as JSON text. </summary>
    public static List<(string Path, string Json)> ParseValues(string values)
    {
        if (values.Trim().Length == 0) return [];
        JsonObject obj;
        try { obj = JsonNode.Parse(values) as JsonObject ?? throw new CliException("--values must be a JSON object of field path to value."); }
        catch (System.Text.Json.JsonException ex) { throw new CliException($"--values is not valid JSON: {ex.Message}"); }
        return obj.Select(kv => (kv.Key, kv.Value?.ToJsonString() ?? "null")).ToList();
    }

    // ================================================================
    //  Fields
    // ================================================================

    [CliCommand("get", "Reads a GameObject, a component's serialized fields, an asset's fields, or one field path inside them")]
    public static object? Get(
        [CliArg("target", "A ref, for example /Player:CharacterController or Materials/Metal.mat")] string target,
        [CliArg("path", "Field path inside the target, for example settings.radius or points[2]")] string path = "")
    {
        object resolved = CliRefs.Resolve(target);
        if (resolved is GameObject go)
        {
            if (path.Length > 0) throw new CliException("A GameObject has no field paths. Address a component, for example /Player:CharacterController.");
            var node = Described(go);
            node["active"] = go.Enabled;
            node["tag"] = go.Tag;
            node["layer"] = go.LayerIndex;
            node["children"] = new JsonArray(go.Children.Select(c => (JsonNode)JsonValue.Create(c.Name)).ToArray());
            if (go.IsPrefabInstance && PrefabUtility.GetPrefabInstanceRoot(go) is { } root && root.IsValid())
                node["prefabRoot"] = CliRefs.PathOf(root);
            return node;
        }

        var fields = CliRefs.GetFields(resolved, path);
        if (path.Length > 0) return fields;
        var result = CliRefs.Summary(resolved);
        result["fields"] = fields;
        return result;
    }

    [CliCommand("set", "Writes field paths on a component or asset from JSON. A ref string sets a scene or asset reference. One undo step")]
    public static object Set(
        [CliArg("target", "Component or asset ref")] string target,
        [CliArg("path", "Field path, for example speed or points[2].x")] string path = "",
        [CliArg("value", "JSON value. A bare word is a string, a ref sets a reference")] string value = "",
        [CliArg("values", "JSON object of field path to value, to set several at once")] string values = "")
    {
        var list = ParseValues(values);
        if (path.Length > 0) list.Insert(0, (path, value));
        if (list.Count == 0) throw new CliException("Give --path and --value, or --values with a JSON object.");
        return CliRefs.SetFields(CliRefs.Resolve(target), list);
    }

    // ================================================================
    //  Menus
    // ================================================================

    [CliCommand("menu", "Runs any editor menu item by path, such as GameObject/3D/Cube or Window/General/Console. Without --path lists them")]
    public static object Menu(
        [CliArg("path", "Menu path, or a prefix to list under")] string path = "",
        [CliArg("context", "GameObject the item acts on, for items that use one")] string context = "",
        [CliArg("list", "List the items under --path instead of running it")] bool list = false)
    {
        AppMenuItem? item = FindMenu(path);
        if (list || path.Length == 0 || item is { HasSubItems: true })
        {
            var items = new List<object>();
            ListMenu(item?.SubItems ?? MenuRegistry.RootMenus, path.Length == 0 || item == null ? "" : path.TrimEnd('/') + "/", items);
            return items;
        }

        if (item == null) throw new CliException($"No menu item '{path}'. Run 'menu' to list them.");
        if (path.StartsWith("Assets/Create/", StringComparison.OrdinalIgnoreCase))
            throw new CliException("Assets/Create items wait for someone to type a name. Use 'asset --action create' or 'script'.");
        if (item.OnClick == null) throw new CliException($"'{path}' does nothing when run.");
        if (!(item.IsEnabledFunc?.Invoke() ?? item.IsEnabled)) throw new CliException($"'{path}' is disabled right now.");

        var selectionBefore = Selection.Selected.ToList();
        CliEdit.Run(path, () =>
        {
            if (context.Length > 0) MenuContext.Set(CliRefs.ResolveGameObject(context));
            try { item.OnClick(); }
            finally { if (context.Length > 0) MenuContext.Clear(); }
        });

        var selected = Selection.Selected.OfType<GameObject>().Where(g => g.IsValid()).ToList();
        bool selectionChanged = !selected.Cast<object>().SequenceEqual(selectionBefore);
        return new { ran = path, selected = selectionChanged ? selected.Select(CliRefs.Summary).ToList() : null };
    }

    private static AppMenuItem? FindMenu(string path)
    {
        if (path.Length == 0) return null;
        IReadOnlyList<AppMenuItem> level = MenuRegistry.RootMenus;
        AppMenuItem? found = null;
        foreach (string segment in path.Trim('/').Split('/'))
        {
            found = level.FirstOrDefault(m => !m.IsSeparator && Label(m).Equals(segment, StringComparison.OrdinalIgnoreCase));
            if (found == null) return null;
            level = found.SubItems;
        }
        return found;
    }

    private static string Label(AppMenuItem item) => item.DynamicLabelFunc?.Invoke() ?? item.Label;

    private static void ListMenu(IReadOnlyList<AppMenuItem> items, string prefix, List<object> output)
    {
        foreach (var item in items)
        {
            if (item.IsSeparator) continue;
            string path = prefix + Label(item);
            if (item.HasSubItems) ListMenu(item.SubItems, path + "/", output);
            else if (!path.StartsWith("Assets/Create/", StringComparison.OrdinalIgnoreCase))
                output.Add(new { path, enabled = item.IsEnabledFunc?.Invoke() ?? item.IsEnabled });
        }
    }

    // ================================================================
    //  Assets
    // ================================================================

    public enum AssetAction { Find, Info, Create, Import, Refresh, Move, Delete, Reimport, Types }

    [CliCommand("asset", "Finds, inspects, creates, imports, moves, deletes or reimports assets. Paths are relative to Assets. Run refresh or import after writing files yourself")]
    public static object Asset(
        [CliArg("action")] AssetAction action,
        [CliArg("path", "Asset path or ref, or a folder for find")] string path = "",
        [CliArg("type", "Asset type to find or create, such as Material, AnimationGraph or Folder")] string type = "",
        [CliArg("query", "Text the path must contain, for find")] string query = "",
        [CliArg("to", "New path, for move")] string to = "",
        [CliArg("limit", "Most results for find")] int limit = 50,
        [CliArg("overwrite", "Allow create to replace an existing file")] bool overwrite = false,
        [CliArg("confirm", "Required to delete, since deleting removes the file")] bool confirm = false)
    {
        var assets = CliRefs.Assets;
        switch (action)
        {
            case AssetAction.Find:
            {
                string folder = CliRefs.AssetPath(path);
                IEnumerable<(Guid Guid, string Path, Type? Type)> found = type.Length > 0
                    ? assets.FindAllOfType(CliRefs.FindAssetType(type))
                        .Select(f => (f.guid, assets.GetEntry(f.guid) != null ? f.parentPath : $"{f.parentPath}#{f.name}", (Type?)f.assetType))
                    : assets.GetAllEntries().Select(e => (e.Guid, e.Path, e.MainAssetType));
                var results = found
                    .Where(f => folder.Length == 0 || f.Path.StartsWith(folder, StringComparison.OrdinalIgnoreCase))
                    .Where(f => query.Length == 0 || f.Path.Contains(query, StringComparison.OrdinalIgnoreCase))
                    .Take(Math.Clamp(limit, 1, 1000))
                    .Select(f => new { guid = f.Guid.ToString(), path = f.Path, type = f.Type?.Name })
                    .ToList();
                return new { count = results.Count, results };
            }

            case AssetAction.Info:
            {
                Guid guid = CliRefs.ResolveAssetGuid(Require(path, "path"));
                var entry = assets.GetEntry(guid);
                if (entry == null) return CliRefs.AssetSummary(guid, AssetDatabase.GetAssetType(guid));
                return new
                {
                    guid = entry.Guid.ToString(),
                    path = entry.Path,
                    type = entry.MainAssetType?.Name,
                    importer = entry.ImporterType,
                    subAssets = entry.SubAssets.Select(s => new { name = s.Name, guid = s.Guid.ToString(), type = s.Type?.Name, @ref = $"{entry.Path}#{s.Name}" }).ToList(),
                    dependencies = entry.Dependencies.Select(d => assets.GuidToPathIncludingSubAssets(d) ?? d.ToString()).ToList(),
                };
            }

            case AssetAction.Types:
                return EditorRegistries.AssetMenuEntries
                    .Select(e => new { type = e.Type.Name, menu = e.Name, extension = e.Extension })
                    .Append(new { type = "Folder", menu = "Folder", extension = "" })
                    .ToList();

            case AssetAction.Create:
            {
                string relative = CliRefs.AssetPath(Require(path, "path"));
                string absolute = Path.Combine(Project.Current!.AssetsPath, relative);

                if (type.Equals("Folder", StringComparison.OrdinalIgnoreCase))
                {
                    Directory.CreateDirectory(absolute);
                    MetaFile.EnsureMeta(absolute, "DefaultImporter");
                    assets.InvalidateFolderIndex();
                    return new { created = relative, type = "Folder" };
                }

                var entry = EditorRegistries.AssetMenuEntries.FirstOrDefault(e => e.Type.Name.Equals(Require(type, "type"), StringComparison.OrdinalIgnoreCase)
                                                                               || e.Name.Equals(type, StringComparison.OrdinalIgnoreCase));
                if (entry.Type == null)
                    throw new CliException($"No creatable asset type '{type}'. Run 'asset --action types' to list them.");
                if (!string.IsNullOrEmpty(entry.Extension) && !relative.EndsWith(entry.Extension, StringComparison.OrdinalIgnoreCase))
                    relative += entry.Extension;
                absolute = Path.Combine(Project.Current!.AssetsPath, relative);
                if (File.Exists(absolute) && !overwrite) throw new CliException($"'{relative}' already exists. Pass --overwrite to replace it.");

                Directory.CreateDirectory(Path.GetDirectoryName(absolute)!);
                object instance = entry.Factory != null ? entry.Factory() : Activator.CreateInstance(entry.Type)!;
                if (instance is Runtime.Asset asset) assets.CreateAsset(asset, relative);
                else
                {
                    File.WriteAllText(absolute, Echo.Serializer.Serialize(typeof(object), instance).WriteToString());
                    assets.ImportFile(relative);
                }
                Guid guid = assets.PathToGuid(relative);
                return CliRefs.AssetSummary(guid, entry.Type);
            }

            case AssetAction.Import:
            {
                string relative = CliRefs.AssetPath(Require(path, "path"));
                Guid guid = assets.ImportFile(relative);
                if (guid == Guid.Empty) throw new CliException($"Nothing to import at '{relative}'.");
                return CliRefs.AssetSummary(guid, AssetDatabase.GetAssetType(guid));
            }

            case AssetAction.Refresh:
                assets.Refresh();
                assets.ProcessFileChanges(force: true);
                return new { assets = assets.GetAllEntries().Count() };

            case AssetAction.Reimport:
            {
                Guid guid = CliRefs.ResolveAssetGuid(Require(path, "path"));
                assets.Reimport(guid);
                return CliRefs.AssetSummary(guid, AssetDatabase.GetAssetType(guid));
            }

            case AssetAction.Move:
            {
                string from = CliRefs.AssetPath(Require(path, "path"));
                string target = CliRefs.AssetPath(Require(to, "to"));
                bool isFolder = Directory.Exists(Path.Combine(Project.Current!.AssetsPath, from));
                bool moved = isFolder ? assets.MoveFolder(from, target) : assets.MoveAsset(from, target);
                if (!moved) throw new CliException($"Could not move '{from}' to '{target}'. See the logs.");
                return new { moved = from, to = target };
            }

            case AssetAction.Delete:
            {
                string relative = CliRefs.AssetPath(Require(path, "path"));
                if (!confirm) throw new CliException($"Deleting removes '{relative}' from disk. Pass --confirm to delete it.");
                if (assets.GetEntry(relative) == null) throw new CliException($"No asset at '{relative}'.");
                assets.DeleteAsset(relative);
                return new { deleted = relative };
            }
        }

        throw new CliException($"Unknown action {action}.");
    }

    [CliCommand("script", "Creates a C# script from a template and imports it. Run 'compile' afterwards to build it")]
    public static object Script(
        [CliArg("path", "Script path relative to Assets, such as Scripts/Gun.cs")] string path = "",
        [CliArg("template", "Template name, see --list")] string template = "MonoBehaviour",
        [CliArg("list", "List the templates instead")] bool list = false)
    {
        if (list) return EditorRegistries.ScriptTemplates.Select(t => new { name = t.Name, description = t.Description }).ToList();

        string relative = CliRefs.AssetPath(Require(path, "path"));
        if (!relative.EndsWith(".cs", StringComparison.OrdinalIgnoreCase)) relative += ".cs";
        string absolute = Path.Combine(Project.Current!.AssetsPath, relative);
        if (File.Exists(absolute)) throw new CliException($"'{relative}' already exists. Edit it directly instead.");

        var tpl = EditorRegistries.ScriptTemplates.FirstOrDefault(t => t.Name.Equals(template, StringComparison.OrdinalIgnoreCase)
                                                                       || t.Name.Replace(" ", "").Equals(template, StringComparison.OrdinalIgnoreCase))
            ?? throw new CliException($"No script template '{template}'. Templates: {string.Join(", ", EditorRegistries.ScriptTemplates.Select(t => t.Name))}.");

        string className = Path.GetFileNameWithoutExtension(relative);
        if (!System.Text.RegularExpressions.Regex.IsMatch(className, @"^[A-Za-z_][A-Za-z0-9_]*$"))
            throw new CliException($"'{className}' is not a valid class name.");

        Directory.CreateDirectory(Path.GetDirectoryName(absolute)!);
        File.WriteAllText(absolute, tpl.Generate(className));
        Guid guid = CliRefs.Assets.ImportFile(relative);
        return new { path = relative, guid = guid.ToString(), className, absolutePath = absolute };
    }

    // ================================================================
    //  Prefabs, materials and import settings
    // ================================================================

    public enum PrefabAction { Create, Apply, Revert, Unpack, Overrides }

    [CliCommand("prefab", "Saves a GameObject as a prefab, or applies, reverts, unpacks or lists the overrides and additions of a prefab instance. Apply writes both into the prefab")]
    public static object Prefab(
        [CliArg("action")] PrefabAction action,
        [CliArg("target", "GameObject ref")] string target,
        [CliArg("path", "Prefab path relative to Assets, for create")] string path = "",
        [CliArg("overwrite", "Replace an existing prefab on create")] bool overwrite = false)
    {
        var go = CliRefs.ResolveGameObject(target);
        GameObject Root() => PrefabUtility.GetPrefabInstanceRoot(go) is { } root && root.IsValid()
            ? root : throw new CliException($"{CliRefs.PathOf(go)} is not part of a prefab instance.");

        switch (action)
        {
            case PrefabAction.Create:
            {
                string relative = CliRefs.AssetPath(Require(path, "path"));
                if (!relative.EndsWith(".prefab", StringComparison.OrdinalIgnoreCase)) relative += ".prefab";
                Directory.CreateDirectory(Path.GetDirectoryName(Path.Combine(Project.Current!.AssetsPath, relative))!);
                bool saved = CliEdit.Run("Create Prefab", () => PrefabUtility.SaveAsPrefabAssetAndConnect(go, relative, overwrite));
                if (!saved) throw new CliException($"Could not save '{relative}'. It may exist already (pass --overwrite), see the logs.");
                Guid guid = CliRefs.Assets.ImportFile(relative);
                return new { prefab = CliRefs.AssetSummary(guid, typeof(PrefabAsset)), instance = CliRefs.Summary(go) };
            }
            case PrefabAction.Apply:
            {
                var root = Root();
                Guid rootId = root.Identifier;
                int overrides = PrefabUtility.CountOverrides(root);
                var additions = PrefabUtility.DescribeAdditions(root);
                CliEdit.Run("Apply Prefab", () =>
                {
                    PrefabUtility.ApplyOverrides(root);
                    // Each apply refreshes the instance, which can replace its objects, so the root is found again every time.
                    foreach (var addition in additions)
                        if (Undo.FindGO(rootId) is { } live && live.IsValid())
                            PrefabUtility.ApplyAddition(live, addition);
                });
                var applied = Undo.FindGO(rootId);
                return new
                {
                    instance = applied.IsValid() ? CliRefs.Summary(applied!) : null,
                    appliedOverrides = overrides,
                    appliedAdditions = additions.Select(a => a.Label).ToList(),
                };
            }
            case PrefabAction.Revert:
            {
                var root = Root();
                CliEdit.Run("Revert Prefab", () => PrefabUtility.RevertOverrides(root));
                return CliRefs.Summary(root);
            }
            case PrefabAction.Unpack:
            {
                var root = Root();
                CliEdit.Run("Unpack Prefab", () => PrefabUtility.UnpackPrefabInstance(root));
                return CliRefs.Summary(root);
            }
            case PrefabAction.Overrides:
            {
                var root = Root();
                return new
                {
                    overrides = PrefabUtility.DescribeOverrides(root)
                        .Select(o => new { o.Path, o.ObjectName, o.ComponentName, o.MemberName, o.InstanceValue })
                        .ToList(),
                    additions = PrefabUtility.DescribeAdditions(root)
                        .Select(a => new { @object = a.ObjectName, component = a.IsWholeObject ? null : a.ComponentName, id = "#" + a.Identifier })
                        .ToList(),
                };
            }
        }

        throw new CliException($"Unknown action {action}.");
    }

    [CliCommand("material", "Shows a material's shader properties, or sets them with --values and swaps the shader with --shader. One undo step")]
    public static object MaterialCommand(
        [CliArg("target", "Material asset ref")] string target,
        [CliArg("values", "JSON object of property name to value. Colors and vectors are arrays, textures are asset refs")] string values = "",
        [CliArg("shader", "Shader asset ref to switch to")] string shader = "")
    {
        if (CliRefs.Resolve(target) is not Material material) throw new CliException($"'{target}' is not a material.");
        var changes = ParseValues(values);

        if (changes.Count > 0 || shader.Length > 0)
        {
            Shader? newShader = null;
            if (shader.Length > 0)
                newShader = CliRefs.ResolveAsset(shader) is Shader s ? s : throw new CliException($"'{shader}' is not a shader.");
            Shader? effective = newShader.IsValid() ? newShader : material.Shader;
            var properties = effective.IsValid() ? effective.Properties.ToDictionary(p => p.Name) : [];

            var writes = new List<Action>();
            foreach (var (name, json) in changes)
            {
                if (!properties.TryGetValue(name, out var property))
                    throw new CliException($"The shader has no property '{name}'. It has: {string.Join(", ", properties.Keys)}.");
                writes.Add(MaterialWrite(material, property, json));
            }

            CliEdit.EditAsset(material, "Material", () =>
            {
                if (newShader.IsValid()) material.Shader = newShader;
                foreach (var write in writes) write();
            });
        }

        return new
        {
            material = CliRefs.Summary(material),
            shader = material.Shader.IsValid() ? CliRefs.AssetSummary(material.Shader.AssetID, typeof(Shader)) : null,
            properties = material.Shader.IsValid()
                ? material.Shader.Properties.Select(p => new
                {
                    name = p.Name,
                    display = p.DisplayName,
                    type = p.PropertyType.ToString(),
                    overridden = material.IsOverridden(p.Name),
                    value = MaterialValue(material, p),
                }).ToList()
                : null,
        };
    }

    private static Action MaterialWrite(Material material, ShaderProperty property, string json)
    {
        string name = property.Name;
        object? Convert(Type type) => CliRefs.ConvertJson(json, type) ?? throw new CliException($"'{name}' can not be set to {json}.");
        return property.PropertyType switch
        {
            ShaderPropertyType.Float => () => material.SetFloat(name, (float)Convert(typeof(float))!),
            ShaderPropertyType.Int => () => material.SetInt(name, (int)Convert(typeof(int))!),
            ShaderPropertyType.Vector2 => () => material.SetVector(name, (Float2)Convert(typeof(Float2))!),
            ShaderPropertyType.Vector3 => () => material.SetVector(name, (Float3)Convert(typeof(Float3))!),
            ShaderPropertyType.Vector4 => () => material.SetVector(name, (Float4)Convert(typeof(Float4))!),
            ShaderPropertyType.Color => () => material.SetColor(name, (Color)Convert(typeof(Color))!),
            ShaderPropertyType.Texture2D => () => material.SetTexture(name, (Texture2D)Convert(typeof(Texture2D))!),
            ShaderPropertyType.Texture3D => () => material.SetTexture3D(name, (Texture3D)Convert(typeof(Texture3D))!),
            _ => throw new CliException($"'{name}' is a {property.PropertyType}, which the CLI can not set yet."),
        };
    }

    private static JsonNode? MaterialValue(Material material, ShaderProperty property)
    {
        var state = material._properties;
        string name = property.Name;
        bool set = material.IsOverridden(name);
        object? value = property.PropertyType switch
        {
            ShaderPropertyType.Float => set ? state.GetFloat(name) : property.Value.X,
            ShaderPropertyType.Int => set ? state.GetInt(name) : (int)property.Value.X,
            ShaderPropertyType.Vector2 => set ? state.GetVector2(name) : null,
            ShaderPropertyType.Vector3 => set ? state.GetVector3(name) : null,
            ShaderPropertyType.Vector4 => set ? state.GetVector4(name) : property.Value,
            ShaderPropertyType.Color => set ? state.GetColor(name) : property.Value,
            ShaderPropertyType.Texture2D => set ? state.GetTexture(name) : property.Texture2DValue,
            ShaderPropertyType.Texture3D => set ? state.GetTexture3D(name) : property.Texture3DValue,
            _ => null,
        };
        return value switch
        {
            null => null,
            Runtime.Asset asset when asset.IsValid() => asset.IsFromDatabase ? CliRefs.AssetSummary(asset.AssetID, asset.GetType()) : JsonValue.Create(asset.Name),
            float or int => CliCommands.ToJson(value),
            _ => CliRefs.EchoToJson(Echo.Serializer.Serialize(value.GetType(), value)),
        };
    }

    // ================================================================
    //  Play mode, input and capture
    // ================================================================

    public enum PlayAction { Status, Start, Stop, Pause, Resume, Step, Wait }

    [CliCommand("play", "Starts, stops, pauses, resumes or steps play mode, or waits while it runs. Returns the errors logged meanwhile")]
    public static async Task<object> Play(
        [CliArg("action")] PlayAction action = PlayAction.Status,
        [CliArg("frames", "Frames to step")] int frames = 1,
        [CliArg("seconds", "Seconds to wait, for wait")] float seconds = 1f)
    {
        long since = LogHistory.NextSeq;
        switch (action)
        {
            case PlayAction.Start:
                if (!Application.IsPlaying)
                {
                    EditorApplication.RequestPlayMode();
                    if (!await CliServer.WaitUntil(() => Application.IsPlaying, TimeSpan.FromSeconds(30)))
                        throw new CliException("Play mode did not start. Something may be asking first, see the logs, or a compile error is blocking it.");
                    await CliServer.NextFrame();
                }
                break;
            case PlayAction.Stop:
                if (Application.IsPlaying)
                {
                    EditorApplication.RequestExitPlayMode();
                    if (!await CliServer.WaitUntil(() => !Application.IsPlaying, TimeSpan.FromSeconds(30)))
                        throw new CliException("Play mode did not stop. See the logs.");
                }
                break;
            case PlayAction.Pause:
                RequirePlaying();
                Application.IsPaused = true;
                break;
            case PlayAction.Resume:
                RequirePlaying();
                Application.IsPaused = false;
                break;
            case PlayAction.Step:
                RequirePlaying();
                for (int i = 0; i < Math.Clamp(frames, 1, 600); i++)
                {
                    Application.IsPaused = true;
                    Application.StepRequested = true;
                    if (!await CliServer.WaitUntil(() => !Application.StepRequested, TimeSpan.FromSeconds(10))) break;
                }
                break;
            case PlayAction.Wait:
                RequirePlaying();
                await Task.Delay(TimeSpan.FromSeconds(Math.Clamp(seconds, 0, 600)));
                break;
        }

        return new
        {
            isPlaying = Application.IsPlaying,
            isPaused = Application.IsPaused,
            frame = Time.FrameCount,
            errors = LogHistory.Since(since, s => s is LogSeverity.Error or LogSeverity.Exception, 50).Select(e => e.Message).ToList(),
        };
    }

    private static void RequirePlaying()
    {
        if (!Application.IsPlaying) throw new CliException("Play mode is not running. Run 'play --action start' first.");
    }

    public enum InputAction { Key, Mouse, Look, Clear }

    [CliCommand("input", "Presses keys or mouse buttons, or moves the mouse, for the game in play mode. Waits until released and returns errors logged meanwhile")]
    public static async Task<object> InputCommand(
        [CliArg("action")] InputAction action,
        [CliArg("keys", "Comma separated key names for key, such as W,ShiftLeft")] string keys = "",
        [CliArg("button", "Mouse button for mouse: 0 left, 1 right, 2 middle")] int button = 0,
        [CliArg("delta", "Mouse movement per frame for look, [x, y]")] string delta = "",
        [CliArg("position", "Where to click for mouse, [x, y] in game view pixels as in a game screenshot. Clicks UI there")] string position = "",
        [CliArg("seconds", "How long to hold")] float seconds = 0.1f,
        [CliArg("wait", "Wait until released")] bool wait = true)
    {
        if (action != InputAction.Clear) RequirePlaying();
        long since = LogHistory.NextSeq;
        double hold = Math.Clamp(seconds, 0.01, 60);

        switch (action)
        {
            case InputAction.Key:
                var parsed = CliRefs.SplitList(Require(keys, "keys")).Select(k => Enum.TryParse(k, ignoreCase: true, out KeyCode code)
                    ? code : throw new CliException($"No key named '{k}'. Names follow the KeyCode enum, such as W, Space, ShiftLeft, Number1, Enter.")).ToList();
                foreach (var code in parsed) SimulatedInput.Press(code, hold);
                break;
            case InputAction.Mouse:
                Int2? at = null;
                if (position.Length > 0)
                {
                    var xy = JsonNode.Parse(position) as JsonArray ?? throw new CliException("--position expects [x, y].");
                    at = new Int2((int)xy[0]!.GetValue<float>(), (int)xy[1]!.GetValue<float>());
                }
                SimulatedInput.PressButton(button, hold, at);
                break;
            case InputAction.Look:
                var look = JsonNode.Parse(Require(delta, "delta")) as JsonArray ?? throw new CliException("--delta expects [x, y].");
                SimulatedInput.Look(new Float2(look[0]!.GetValue<float>(), look[1]!.GetValue<float>()), hold);
                break;
            case InputAction.Clear:
                SimulatedInput.Clear();
                return new { cleared = true };
        }

        if (wait) await CliServer.WaitUntil(() => !SimulatedInput.Busy || !Application.IsPlaying, TimeSpan.FromSeconds(hold + 10));
        return new
        {
            isPlaying = Application.IsPlaying,
            errors = LogHistory.Since(since, s => s is LogSeverity.Error or LogSeverity.Exception, 50).Select(e => e.Message).ToList(),
        };
    }

    public enum ScreenshotView { Scene, Game }

    [CliCommand("screenshot", "Saves what the scene or game view shows as a PNG and returns its path. --frame frames the target in the scene view first")]
    public static async Task<object> Screenshot(
        [CliArg("view")] ScreenshotView view = ScreenshotView.Scene,
        [CliArg("frame", "Ref to frame in the scene view before capturing")] string frame = "",
        [CliArg("out", "PNG path, relative to the project or absolute. Defaults to Library/Cli/Screenshots")] string @out = "")
    {
        if (frame.Length > 0)
        {
            if (view != ScreenshotView.Scene) throw new CliException("--frame only applies to the scene view.");
            Select(frame, frame: true);
        }

        // The view renders during the frame, so wait for one or two that include the change.
        await CliServer.NextFrame();
        await CliServer.NextFrame();

        RenderTexture? rt = view switch
        {
            ScreenshotView.Scene => SceneViewPanel.ActiveCamera?.RenderTarget,
            _ => EditorApplication.Instance?.FindOpenPanel(typeof(GameViewPanel)) is GameViewPanel game ? game.RenderTarget : null,
        };
        if (rt.IsNotValid() || rt!.MainTexture.IsNotValid())
            throw new CliException(view == ScreenshotView.Scene
                ? "The scene view has not drawn yet. Open it with 'menu Window/General/Scene'."
                : "The game view is not open or has not drawn. Open it with 'menu Window/General/Game'.");

        int width = rt.Width, height = rt.Height;
        byte[] pixels = new byte[width * height * 4];
        rt.MainTexture.GetData<byte>(pixels);

        string root = Project.Current!.RootPath;
        string path = @out.Length > 0
            ? Path.GetFullPath(Path.IsPathRooted(@out) ? @out : Path.Combine(root, @out))
            : Path.Combine(root, "Library", "Cli", "Screenshots", $"{view.ToString().ToLowerInvariant()}-{DateTime.Now:yyyyMMdd-HHmmss-fff}.png");
        if (!path.EndsWith(".png", StringComparison.OrdinalIgnoreCase)) path += ".png";
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);

        using (var image = Aperture.Image.FromPixels(pixels, width, height, Aperture.PixelFormat.Rgba8))
            image.Save(path, new Aperture.EncodeOptions { Format = Aperture.ImageFormat.Png, FlipVertically = true });

        return new { path, width, height };
    }

    [CliCommand("build", "Builds the game with the selected pipeline, waits for it, and returns the result")]
    public static async Task<object> BuildCommand(
        [CliArg("out", "Output folder, relative to the project or absolute")] string @out,
        [CliArg("run", "Run the build when it finishes")] bool run = false,
        [CliArg("wait", "Wait for the build to finish")] bool wait = true)
    {
        string path = Path.GetFullPath(Path.IsPathRooted(@out) ? @out : Path.Combine(Project.Current!.RootPath, @out));
        var progress = Build.ProjectBuilder.StartBuildAsync(run, path) ?? throw new CliException("The build did not start. See the logs.");
        if (!wait) return new { started = true, output = path };

        await CliServer.WaitUntil(() => progress.IsComplete, TimeSpan.FromHours(2));
        var result = progress.Result;
        return new
        {
            ok = result?.Success ?? false,
            cancelled = result?.Cancelled ?? false,
            output = result?.OutputPath ?? path,
            seconds = result?.Duration.TotalSeconds ?? 0,
            errors = result?.Errors,
        };
    }

    // ================================================================
    //  Animation graphs
    // ================================================================

    public enum GraphAction { Describe, Types, Edit }

    [CliCommand("graph", "Reads an animation graph, lists node types, or edits it with a JSON list of --ops. See the description of each op in 'graph --action types'")]
    public static object Graph(
        [CliArg("target", "Animation graph asset ref, not needed for types")] string target = "",
        [CliArg("action")] GraphAction action = GraphAction.Describe,
        [CliArg("ops", "JSON array of edits, applied in order as one undo step")] string ops = "",
        [CliArg("filter", "Text a node type must contain, for types")] string filter = "",
        [CliArg("save", "Save the graph after editing")] bool save = true)
    {
        if (action == GraphAction.Types) return CliGraph.Types(filter);

        if (CliRefs.Resolve(Require(target, "target")) is not AnimationGraph graph) throw new CliException($"'{target}' is not an animation graph.");
        if (action == GraphAction.Describe) return CliGraph.Describe(graph);

        JsonArray list;
        try { list = JsonNode.Parse(Require(ops, "ops")) as JsonArray ?? throw new CliException("--ops must be a JSON array."); }
        catch (System.Text.Json.JsonException ex) { throw new CliException($"--ops is not valid JSON: {ex.Message}"); }

        var results = new JsonArray();
        void Change()
        {
            var ids = new Dictionary<string, string>(StringComparer.Ordinal);
            foreach (var op in list)
                results.Add(CliGraph.Apply(graph, op as JsonObject ?? throw new CliException("Each op must be a JSON object."), ids));
            graph.Invalidate();
        }

        // Dry run on a copy first, so a bad op fails before anything changes.
        var copy = Echo.Serializer.Deserialize<AnimationGraph>(Echo.Serializer.Serialize(typeof(AnimationGraph), graph))!;
        var dryIds = new Dictionary<string, string>(StringComparer.Ordinal);
        foreach (var op in list) CliGraph.Apply(copy, op as JsonObject ?? throw new CliException("Each op must be a JSON object."), dryIds);

        if (!AnimationGraphWindow.TryEditOpen(graph.AssetID, "CLI: Graph", Change, save))
            CliEdit.EditAsset(graph, "Graph", Change);

        return new { results, graph = CliGraph.Describe(graph) };
    }

    // ================================================================
    //  API lookup
    // ================================================================

    [CliCommand("api", "Looks up the real scripting API: --search finds types and members by name, --type lists a type's members with their docs, --member narrows to one with parameter docs")]
    public static object Api(
        [CliArg("search", "Text to find in type and member names")] string search = "",
        [CliArg("type", "Type to describe, by short or full name")] string type = "",
        [CliArg("member", "One member of --type, with its parameter docs")] string member = "",
        [CliArg("inherited", "Include members from base types")] bool inherited = false,
        [CliArg("limit", "Most results for search")] int limit = 25)
    {
        if (type.Length > 0) return CliApi.Describe(type, member, inherited);
        if (search.Length > 0) return CliApi.Search(search, Math.Clamp(limit, 1, 200));
        throw new CliException("Give --search text or --type name.");
    }

    [CliCommand("importer", "Shows an asset's import settings, or merges --values into them and reimports. Nested keys use dots, such as clips.Fire.loop")]
    public static object Importer(
        [CliArg("target", "Asset ref")] string target,
        [CliArg("values", "JSON object of settings key to value")] string values = "")
    {
        Guid guid = CliRefs.ResolveAssetGuid(target);
        var entry = CliRefs.Assets.GetEntry(guid) ?? throw new CliException($"'{target}' is a sub asset. Import settings belong to the file it came from.");
        string metaPath = MetaFile.GetMetaPath(Path.Combine(Project.Current!.AssetsPath, entry.Path));
        if (!File.Exists(metaPath)) throw new CliException($"'{entry.Path}' has no .meta file yet. Run 'asset --action import' first.");

        var meta = MetaFile.Read(metaPath);
        var settings = meta.Settings ?? Echo.EchoObject.NewCompound();
        var defaults = EditorRegistries.CreateImporterByName(entry.ImporterType)?.DefaultSettings();
        if (defaults != null)
            foreach (var (key, value) in defaults.Tags)
                if (!settings.TryGet(key, out _)) settings[key] = value.Clone();

        var changes = ParseValues(values);
        if (changes.Count > 0)
        {
            foreach (var (key, json) in changes) CliRefs.SetEchoPath(settings, key, json);
            meta.Settings = settings;
            MetaFile.Write(metaPath, meta);
            ImportSettingsEditor.Forget(guid);
            CliRefs.Assets.Reimport(guid);
        }

        return new
        {
            asset = CliRefs.AssetSummary(guid, entry.MainAssetType),
            importer = entry.ImporterType,
            settings = CliRefs.EchoToJson(settings),
            subAssets = CliRefs.Assets.GetSubAssets(guid).Select(s => new { name = s.Name, type = s.Type?.Name, @ref = $"{entry.Path}#{s.Name}" }).ToList(),
        };
    }
}

/// <summary>
/// Reading and editing animation graph records for the CLI, by node id and pin name, the way the graph editor does it.
/// <para/>
/// Ops: add {type, owner, name, as}, connect {from, to, pin}, disconnect {to, pin}, input {node, pin, value, position,
/// flag, name, text}, set {node, key, value} or {node, name}, remove {node}, root {node}, param {name, kind, value,
/// trigger, remove}, state {machine, name, default, graph, as}, transition {machine, from, to, duration, easing, sync,
/// clampToSource, canInterrupt, remove}, gate {state, enter, exit}. A pin is an index, a pin name, or setting:Key to
/// drive a setting. Ids may be written as $alias for something an earlier op named with "as", and a state's output
/// node, where its pose and Enter and Exit conditions are wired, as $alias.output.
/// </summary>
internal static class CliGraph
{
    public static object Types(string filter) => AnimationNodeRegistry.All
        .Where(t => !t.Hidden && (filter.Length == 0 || t.Id.Contains(filter, StringComparison.OrdinalIgnoreCase)
                                  || t.DisplayName.Contains(filter, StringComparison.OrdinalIgnoreCase)
                                  || t.Category.Contains(filter, StringComparison.OrdinalIgnoreCase)))
        .Select(t => new
        {
            id = t.Id,
            name = t.DisplayName,
            category = t.Category,
            description = t.Description.Length > 0 ? t.Description : null,
            output = t.Output.ToString(),
            pins = t.Inputs.Select(p => new { index = p.Index, name = p.Name, kind = p.Kind.ToString(), optional = p.Optional, repeats = p.Variadic }).ToList(),
            settings = t.Properties.Select(s => new { key = s.Key, kind = s.Kind.ToString(), choices = s.Choices, drivable = s.Drivable }).ToList(),
        })
        .ToList();

    public static JsonObject Describe(AnimationGraph graph)
    {
        var nodes = new JsonArray();
        foreach (var record in graph.Nodes)
        {
            var type = AnimationNodeRegistry.Get(record.Type);
            var node = new JsonObject { ["id"] = record.Id, ["type"] = record.Type };
            if (record.Name.Length > 0) node["name"] = record.Name;
            if (record.Owner.Length > 0) node["owner"] = record.Owner;

            var inputs = new JsonArray();
            for (int i = 0; i < record.Inputs.Count; i++)
            {
                var input = record.Inputs[i];
                var pin = new JsonObject { ["pin"] = i, ["name"] = type?.PinAt(i)?.Name };
                if (input.Node.Length > 0) pin["from"] = input.Node;
                if (input.Value != 1f) pin["value"] = input.Value;
                if (input.Flag) pin["flag"] = true;
                if (input.Position != default) pin["position"] = new JsonArray(input.Position.X, input.Position.Y);
                if (input.Name.Length > 0) pin["inputName"] = input.Name;
                if (input.Text.Length > 0) pin["text"] = input.Text;
                inputs.Add(pin);
            }
            if (inputs.Count > 0) node["inputs"] = inputs;

            if (record.Properties.Count > 0)
            {
                var props = new JsonObject();
                foreach (var (key, value) in record.Properties) props[key] = ValueJson(value);
                node["settings"] = props;
            }
            if (record.PropertyInputs.Count > 0)
                node["drivenSettings"] = new JsonObject(record.PropertyInputs.Select(kv => KeyValuePair.Create(kv.Key, (JsonNode?)JsonValue.Create(kv.Value))));

            if (record.States.Count > 0)
                node["states"] = new JsonArray(record.States.Select(s => (JsonNode)new JsonObject
                {
                    ["id"] = s.Id,
                    ["name"] = s.Name,
                    ["default"] = s.IsDefault,
                    ["any"] = s.IsAny,
                    ["graph"] = s.Graph.IsValid() ? CliRefs.AssetSummary(s.Graph!.AssetID, typeof(AnimationGraph)) : null,
                    ["output"] = graph.OwnedNode(s.Id, AnimationNodeIds.StateOutput)?.Id,
                    ["transitions"] = new JsonArray(s.Transitions.Select(t => (JsonNode)new JsonObject
                    {
                        ["to"] = t.To,
                        ["duration"] = t.Duration,
                        ["easing"] = t.Easing.ToString(),
                        ["sync"] = t.Sync.ToString(),
                        ["clampToSource"] = t.ClampToSource,
                        ["canInterrupt"] = t.CanInterrupt,
                    }).ToArray()),
                }).ToArray());

            nodes.Add(node);
        }

        return new JsonObject
        {
            ["graph"] = CliRefs.AssetSummary(graph.AssetID, typeof(AnimationGraph)),
            ["root"] = graph.RootNode,
            ["parameters"] = new JsonArray(graph.Parameters.Select(p => (JsonNode)new JsonObject
            {
                ["name"] = p.Name,
                ["kind"] = p.Kind.ToString(),
                ["value"] = p.Kind switch
                {
                    NodeValueKind.Flag => p.Flag,
                    NodeValueKind.Integer => p.Integer,
                    NodeValueKind.Vector => CliRefs.Json(p.Vector),
                    NodeValueKind.Text or NodeValueKind.Id => p.Text,
                    _ => p.Number,
                },
                ["trigger"] = p.Trigger ? true : null,
            }).ToArray()),
            ["nodes"] = nodes,
        };
    }

    private static JsonNode? ValueJson(NodeValue value) => value.Kind switch
    {
        NodeValueKind.Number => value.Number,
        NodeValueKind.Flag => value.Flag,
        NodeValueKind.Integer => value.Integer,
        NodeValueKind.Text or NodeValueKind.Id => value.Text,
        NodeValueKind.Vector => CliRefs.Json(value.Vector),
        NodeValueKind.Clip => AssetJson(value.Clip),
        NodeValueKind.Mask => AssetJson(value.Mask),
        NodeValueKind.Avatar => AssetJson(value.Avatar),
        NodeValueKind.Graph => AssetJson(value.Graph),
        NodeValueKind.Curve => value.Curve == null ? null : CliRefs.EchoToJson(Echo.Serializer.Serialize(typeof(AnimationCurve), value.Curve)),
        _ => null,
    };

    private static JsonNode? AssetJson(Asset? asset)
        => asset.IsValid() && asset!.IsFromDatabase ? CliRefs.AssetSummary(asset.AssetID, asset.GetType()) : null;

    public static JsonNode? Apply(AnimationGraph graph, JsonObject op, Dictionary<string, string> ids)
    {
        string kind = Str(op, "op") ?? throw new CliException("Each op needs an \"op\".");
        switch (kind)
        {
            case "add":
            {
                string typeId = Str(op, "type") ?? throw new CliException("add needs a \"type\". See 'graph --action types'.");
                var type = AnimationNodeRegistry.Get(typeId) ?? throw new CliException($"No node type '{typeId}'. See 'graph --action types'.");
                string owner = Id(op, "owner", ids, graph, required: false) ?? "";
                var record = graph.AddNode(type.Id);
                record.Owner = owner;
                record.Name = Str(op, "name") ?? "";
                for (int i = 0; i < type.Inputs.Count; i++) record.Inputs.Add(new GraphInputRecord());
                AnimationNodeEditor.For(type).OnCreated(graph, record);
                if (owner.Length == 0 && graph.RootNode.Length == 0 && type.IsPose) graph.RootNode = record.Id;
                Alias(op, ids, record.Id);
                return new JsonObject { ["added"] = record.Id };
            }

            case "connect":
            {
                string from = Id(op, "from", ids, graph)!;
                var target = Node(graph, Id(op, "to", ids, graph)!);
                if (Node(graph, from) == target) throw new CliException("A node can not feed itself.");
                string? pinText = op["pin"] is JsonValue v && v.TryGetValue(out string? s) ? s : null;
                if (pinText != null && pinText.StartsWith("setting:", StringComparison.OrdinalIgnoreCase))
                {
                    target.PropertyInputs[pinText[8..]] = from;
                    return new JsonObject { ["connected"] = from, ["to"] = target.Id, ["setting"] = pinText[8..] };
                }

                int pin = PinIndex(target, op["pin"], allowNext: true);
                while (target.Inputs.Count <= pin) target.Inputs.Add(new GraphInputRecord());
                target.Inputs[pin].Node = from;
                if (AnimationNodeRegistry.Get(target.Type) is { } type)
                    while (target.Inputs.Count < type.WholeGroups(target.Inputs.Count)) target.Inputs.Add(new GraphInputRecord());
                return new JsonObject { ["connected"] = from, ["to"] = target.Id, ["pin"] = pin };
            }

            case "disconnect":
            {
                var target = Node(graph, Id(op, "to", ids, graph)!);
                if (op["pin"] is JsonValue v && v.TryGetValue(out string? s) && s.StartsWith("setting:", StringComparison.OrdinalIgnoreCase))
                {
                    if (target.PropertyInputs.ContainsKey(s[8..])) target.PropertyInputs[s[8..]] = string.Empty;
                    return null;
                }
                int pin = PinIndex(target, op["pin"], allowNext: false);
                if (pin < target.Inputs.Count) target.Inputs[pin].Node = string.Empty;
                AnimationGraphView.TrimVariadic(target);
                return null;
            }

            case "input":
            {
                var target = Node(graph, Id(op, "node", ids, graph)!);
                int pin = PinIndex(target, op["pin"], allowNext: false);
                while (target.Inputs.Count <= pin) target.Inputs.Add(new GraphInputRecord());
                var input = target.Inputs[pin];
                if (op["value"] is { } value) input.Value = value.GetValue<float>();
                if (op["flag"] is { } flag) input.Flag = flag.GetValue<bool>();
                if (op["name"] is { } name) input.Name = name.GetValue<string>();
                if (op["text"] is { } text) input.Text = text.GetValue<string>();
                if (op["position"] is JsonArray position) input.Position = new Float2(position[0]!.GetValue<float>(), position[1]!.GetValue<float>());
                return null;
            }

            case "set":
            {
                var target = Node(graph, Id(op, "node", ids, graph)!);
                if (op["name"] is { } newName && op["key"] == null) { target.Name = newName.GetValue<string>(); return null; }
                string key = Str(op, "key") ?? throw new CliException("set needs a \"key\", or a \"name\" to rename the node.");
                var type = AnimationNodeRegistry.Get(target.Type);
                var setting = type?.Properties.FirstOrDefault(p => p.Key.Equals(key, StringComparison.OrdinalIgnoreCase));
                if (type != null && setting == null)
                    throw new CliException($"{target.Type} has no setting '{key}'. It has: {string.Join(", ", type.Properties.Select(p => p.Key))}.");
                target.Properties[setting?.Key ?? key] = ToValue(op["value"], setting?.Kind ?? Infer(op["value"]), setting);
                return null;
            }

            case "remove":
            {
                var target = Node(graph, Id(op, "node", ids, graph)!);
                if (target.Type is AnimationNodeIds.StateOutput or AnimationNodeIds.GraphOutput)
                    throw new CliException("Output nodes go with their state or graph. Remove the state instead.");

                var inside = new HashSet<string> { target.Id };
                foreach (var state in target.States) inside.Add(state.Id);
                graph.Nodes.Remove(target);
                foreach (var record in graph.Nodes)
                {
                    foreach (var input in record.Inputs) if (input.Node == target.Id) input.Node = string.Empty;
                    foreach (string driven in record.PropertyInputs.Keys.ToList())
                        if (record.PropertyInputs[driven] == target.Id) record.PropertyInputs[driven] = string.Empty;
                    AnimationGraphView.TrimVariadic(record);
                }
                if (graph.RootNode == target.Id) graph.RootNode = string.Empty;
                AnimationGraphView.RemoveInside(graph, inside);
                return null;
            }

            case "root":
            {
                var target = Node(graph, Id(op, "node", ids, graph)!);
                if (target.Owner.Length > 0) throw new CliException("The root must be a node of the graph itself, not one inside a state.");
                graph.RootNode = target.Id;
                return null;
            }

            case "param":
            {
                string name = Str(op, "name") ?? throw new CliException("param needs a \"name\".");
                var existing = graph.Parameters.FirstOrDefault(p => p.Name == name);
                if (op["remove"]?.GetValue<bool>() == true)
                {
                    if (existing != null) graph.Parameters.Remove(existing);
                    return null;
                }

                var parameter = existing ?? new GraphParameterRecord { Name = name };
                if (Str(op, "kind") is { } kindText)
                    parameter.Kind = Enum.TryParse(kindText, ignoreCase: true, out NodeValueKind parsed) ? parsed
                        : throw new CliException($"Parameter kind must be one of {string.Join(", ", Enum.GetNames<NodeValueKind>())}.");
                if (op["trigger"] is { } trigger) parameter.Trigger = trigger.GetValue<bool>();
                if (op["value"] is { } value)
                {
                    var converted = ToValue(value, parameter.Kind, null);
                    parameter.Number = converted.Number;
                    parameter.Flag = converted.Flag;
                    parameter.Integer = converted.Integer;
                    parameter.Vector = converted.Vector;
                    parameter.Text = converted.Text;
                }
                if (existing == null) graph.Parameters.Add(parameter);
                return null;
            }

            case "state":
            {
                var machine = Node(graph, Id(op, "machine", ids, graph)!);
                if (machine.Type != AnimationNodeIds.StateMachine)
                    throw new CliException($"{machine.Id} is a {machine.Type}, not a state machine.");
                string name = Str(op, "name") ?? throw new CliException("state needs a \"name\".");
                if (machine.States.Any(s => s.Name == name)) throw new CliException($"The machine already has a state named '{name}'.");

                var state = new GraphStateRecord
                {
                    Id = AnimationGraphView.NewStateId(),
                    Name = name,
                    IsDefault = !machine.States.Exists(s => !s.IsAny),
                };
                if (op["default"]?.GetValue<bool>() == true)
                {
                    foreach (var other in machine.States) other.IsDefault = false;
                    state.IsDefault = true;
                }
                if (Str(op, "graph") is { } played)
                    state.Graph = CliRefs.ResolveAsset(played) is AnimationGraph playedGraph ? playedGraph : throw new CliException($"'{played}' is not an animation graph.");

                machine.States.Add(state);
                var output = AnimationGraphView.EnsureOutput(graph, state.Id, AnimationNodeIds.StateOutput);
                Alias(op, ids, state.Id);
                if (Str(op, "as") is { } alias) ids[alias.TrimStart('$') + ".output"] = output.Id;
                return new JsonObject { ["state"] = state.Id, ["output"] = output.Id };
            }

            case "transition":
            {
                var machine = Node(graph, Id(op, "machine", ids, graph)!);
                var from = StateNamed(machine, Str(op, "from"));
                var to = StateNamed(machine, Str(op, "to"));
                if (to.IsAny) throw new CliException("Nothing transitions into Any State.");
                if (ReferenceEquals(from, to)) throw new CliException("A transition needs two different states.");

                var transition = from.Transitions.FirstOrDefault(t => t.To == to.Name);
                if (op["remove"]?.GetValue<bool>() == true)
                {
                    if (transition != null) from.Transitions.Remove(transition);
                    return null;
                }
                if (transition == null) from.Transitions.Add(transition = new GraphTransitionRecord { To = to.Name, Duration = 0.2f });
                if (op["duration"] is { } duration) transition.Duration = duration.GetValue<float>();
                if (Str(op, "easing") is { } easing) transition.Easing = Enum.Parse<Motion.TransitionEasing>(easing, ignoreCase: true);
                if (Str(op, "sync") is { } sync) transition.Sync = Enum.Parse<Motion.TransitionSync>(sync, ignoreCase: true);
                if (op["clampToSource"] is { } clamp) transition.ClampToSource = clamp.GetValue<bool>();
                if (op["canInterrupt"] is { } interrupt) transition.CanInterrupt = interrupt.GetValue<bool>();
                return null;
            }

            case "gate":
            {
                string stateId = Id(op, "state", ids, graph)!;
                var output = graph.OwnedNode(stateId, AnimationNodeIds.StateOutput)
                    ?? throw new CliException($"No state '{stateId}'. Use the id a state op returned, or \"as\" to name it.");
                while (output.Inputs.Count < 3) output.Inputs.Add(new GraphInputRecord());
                if (op["enter"] is { } enter) output.Inputs[1].Flag = enter.GetValue<bool>();
                if (op["exit"] is { } exit) output.Inputs[2].Flag = exit.GetValue<bool>();
                return null;
            }
        }

        throw new CliException($"Unknown op '{kind}'. Ops: add, connect, disconnect, input, set, remove, root, param, state, transition, gate.");
    }

    private static string? Str(JsonObject op, string key) => op[key] is JsonValue v && v.TryGetValue(out string? s) ? s : null;

    private static void Alias(JsonObject op, Dictionary<string, string> ids, string id)
    {
        if (Str(op, "as") is { } alias) ids[alias.TrimStart('$')] = id;
    }

    private static string? Id(JsonObject op, string key, Dictionary<string, string> ids, AnimationGraph graph, bool required = true)
    {
        string? text = Str(op, key);
        if (text == null || text.Length == 0)
            return required ? throw new CliException($"This op needs \"{key}\".") : null;
        if (text.StartsWith('$'))
            return ids.TryGetValue(text[1..], out string? id) ? id : throw new CliException($"No earlier op was named {text}.");
        return text;
    }

    private static GraphNodeRecord Node(AnimationGraph graph, string id)
        => graph.Find(id) ?? throw new CliException($"The graph has no node '{id}'.");

    private static GraphStateRecord StateNamed(GraphNodeRecord machine, string? name)
        => machine.States.FirstOrDefault(s => s.Name == name || s.Id == name)
           ?? throw new CliException($"The machine has no state '{name}'. It has: {string.Join(", ", machine.States.Select(s => s.Name))}.");

    private static int PinIndex(GraphNodeRecord target, JsonNode? pin, bool allowNext)
    {
        var type = AnimationNodeRegistry.Get(target.Type);
        if (pin is JsonValue value && value.TryGetValue(out int index)) return index;

        string? name = pin is JsonValue named && named.TryGetValue(out string? text) ? text : null;
        if (name == null && !allowNext) throw new CliException("Give a \"pin\": an index or a pin name.");
        if (type == null) return name == null ? target.Inputs.Count : throw new CliException($"Unknown node type {target.Type}, so pins must be given by index.");

        var declared = name == null ? null : type.Inputs.FirstOrDefault(p => p.Name.Equals(name, StringComparison.OrdinalIgnoreCase))
            ?? throw new CliException($"{target.Type} has no pin '{name}'. Pins: {string.Join(", ", type.Inputs.Select(p => p.Name))}.");

        if (declared is { Variadic: false }) return declared.Index;

        // A repeating pin, or no pin given: the first free one of that name, or the same pin in a new group.
        for (int i = 0; i < target.Inputs.Count; i++)
        {
            var at = type.PinAt(i);
            bool fits = declared == null ? (i >= type.VariadicStart || at is { Variadic: false }) : at?.Name == declared.Name;
            if (fits && target.Inputs[i].Node.Length == 0) return i;
        }
        if (declared == null && type.VariadicStride == 0) throw new CliException($"Every pin of {target.Id} is wired. Name a pin to replace its wire.");
        int start = Math.Max(type.VariadicStart, type.WholeGroups(target.Inputs.Count));
        return start + Math.Max(0, declared?.GroupOffset ?? 0);
    }

    private static NodeValueKind Infer(JsonNode? value) => value switch
    {
        JsonArray => NodeValueKind.Vector,
        JsonValue v when v.TryGetValue(out bool _) => NodeValueKind.Flag,
        JsonValue v when v.TryGetValue(out string? _) => NodeValueKind.Text,
        _ => NodeValueKind.Number,
    };

    private static NodeValue ToValue(JsonNode? json, NodeValueKind kind, NodeSetting? setting)
    {
        string text = json?.ToJsonString() ?? "null";
        T Convert<T>() => (T)(CliRefs.ConvertJson(text, typeof(T)) ?? throw new CliException($"Could not read {text} as {typeof(T).Name}."));
        try
        {
            return kind switch
            {
                NodeValueKind.Number => NodeValue.FromNumber(json!.GetValue<float>()),
                NodeValueKind.Flag => NodeValue.FromFlag(json!.GetValue<bool>()),
                NodeValueKind.Integer => NodeValue.FromInteger(json!.GetValue<int>()),
                NodeValueKind.Vector => NodeValue.FromVector(Convert<Float3>()),
                NodeValueKind.Clip => NodeValue.FromClip(Convert<AnimationClip>()),
                NodeValueKind.Mask => NodeValue.FromMask(Convert<AvatarMask>()),
                NodeValueKind.Avatar => NodeValue.FromAvatar(Convert<Avatar>()),
                NodeValueKind.Graph => NodeValue.FromGraph(Convert<AnimationGraph>()),
                NodeValueKind.Curve => NodeValue.FromCurve(Convert<AnimationCurve>()),
                NodeValueKind.Text or NodeValueKind.Id => Choice(json!.GetValue<string>(), kind, setting),
                _ => throw new CliException($"Settings of kind {kind} can not be set from the CLI."),
            };
        }
        catch (Exception ex) when (ex is InvalidOperationException or FormatException or NullReferenceException)
        {
            throw new CliException($"'{setting?.Key ?? "value"}' expects a {kind}, got {text}.");
        }
    }

    private static NodeValue Choice(string text, NodeValueKind kind, NodeSetting? setting)
    {
        if (setting?.Choices is { } choices && !choices.Contains(text, StringComparer.OrdinalIgnoreCase))
            throw new CliException($"'{setting.Key}' must be one of {string.Join(", ", choices)}.");
        return new NodeValue { Kind = kind, Text = text };
    }
}

/// <summary>
/// Looks up types and members by reflection over everything loaded, with summaries from the XML doc file next to each
/// assembly, so an agent can find the real API instead of guessing it.
/// </summary>
internal static class CliApi
{
    private const BindingFlags Members = BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance | BindingFlags.Static | BindingFlags.DeclaredOnly;
    private static readonly string[] s_skippedPrefixes = ["System", "Microsoft", "netstandard", "mscorlib", "WindowsBase", "Mono.", "xunit", "testhost", "ProwlEval"];
    private static readonly Dictionary<Assembly, Dictionary<string, XElement>?> s_docs = new();

    public static IEnumerable<Assembly> Assemblies() => ScriptAssemblyManager.LiveAssemblies()
        .Where(a => !a.IsDynamic && !s_skippedPrefixes.Any(p => (a.GetName().Name ?? "").StartsWith(p, StringComparison.Ordinal)));

    public static IEnumerable<Type> Types() => Assemblies()
        .SelectMany(a => { try { return a.GetExportedTypes(); } catch (Exception) { return []; } })
        .Where(t => !t.IsDefined(typeof(CompilerGeneratedAttribute)));

    public static object Search(string text, int limit)
    {
        var types = Types().ToList();
        var typeHits = types
            .Where(t => t.Name.Contains(text, StringComparison.OrdinalIgnoreCase))
            .OrderBy(t => t.Name.Equals(text, StringComparison.OrdinalIgnoreCase) ? 0 : t.Name.StartsWith(text, StringComparison.OrdinalIgnoreCase) ? 1 : 2)
            .ThenBy(t => t.Name.Length)
            .Take(limit)
            .Select(t => new { kind = Kind(t), name = TypeName(t, full: true), assembly = t.Assembly.GetName().Name, summary = Summary(t.Assembly, "T:" + DocName(t)) })
            .ToList();

        var memberHits = types
            .SelectMany(t => Visible(t).Where(m => m.Name.Contains(text, StringComparison.OrdinalIgnoreCase)).Select(m => (Type: t, Member: m)))
            .OrderBy(h => h.Member.Name.Equals(text, StringComparison.OrdinalIgnoreCase) ? 0 : 1)
            .ThenBy(h => h.Type.Name.Length)
            .Take(limit)
            .Select(h => new { type = TypeName(h.Type, full: true), member = Signature(h.Member), summary = Summary(h.Type.Assembly, DocId(h.Member)) })
            .ToList();

        return new { types = typeHits, members = memberHits };
    }

    private static int Rank(Type type)
    {
        if (ScriptAssemblyManager.IsScriptAssembly(type.Assembly)) return 0;
        return type.Assembly.GetName().Name switch { "Prowl.Runtime" => 1, "Prowl.Vector" => 2, _ => int.MaxValue };
    }

    public static object Describe(string typeName, string member, bool inherited)
    {
        var matches = Types().Where(t => t.FullName == typeName || TypeName(t, full: true) == typeName || TypeName(t, full: false) == typeName
                                         || (t.Name.Split('`')[0] == typeName && !t.IsNested)).Distinct().ToList();
        if (matches.Count == 0)
        {
            var close = Types().Where(t => t.Name.Contains(typeName, StringComparison.OrdinalIgnoreCase)).Select(t => TypeName(t, full: true)).Take(10).ToList();
            throw new CliException($"No type '{typeName}'.{(close.Count > 0 ? $" Did you mean: {string.Join(", ", close)}?" : " Try --search.")}");
        }
        // A short name shared with a library means the engine's or the game's own type, which is what scripts use.
        var others = new List<Type>();
        if (matches.Count > 1)
        {
            int best = matches.Min(Rank);
            var preferred = matches.Where(t => Rank(t) == best).ToList();
            if (best == int.MaxValue || preferred.Count > 1)
                throw new CliException($"'{typeName}' is ambiguous: {string.Join(", ", matches.Select(t => t.FullName))}. Use the full name.");
            others = matches.Except(preferred).ToList();
            matches = preferred;
        }
        Type type = matches[0];

        var members = (inherited ? Hierarchy(type).SelectMany(Visible) : Visible(type))
            .Where(m => member.Length == 0 || m.Name.Equals(member, StringComparison.OrdinalIgnoreCase))
            .ToList();
        if (member.Length > 0 && members.Count == 0)
            throw new CliException($"{type.Name} has no member '{member}'{(inherited ? "" : " of its own. Pass --inherited to include base types")}.");

        return new
        {
            kind = Kind(type),
            name = TypeName(type, full: true),
            assembly = type.Assembly.GetName().Name,
            baseType = type.BaseType is { } b && b != typeof(object) && b != typeof(ValueType) && b != typeof(Enum) ? TypeName(b, full: true) : null,
            interfaces = type.GetInterfaces().Where(i => i.IsPublic).Select(i => TypeName(i, full: false)).ToList(),
            summary = Summary(type.Assembly, "T:" + DocName(type)),
            remarks = Doc(type.Assembly, "T:" + DocName(type), "remarks"),
            alsoNamed = others.Count > 0 ? others.Select(t => TypeName(t, full: true)).ToList() : null,
            values = type.IsEnum ? Enum.GetNames(type) : null,
            members = members.Take(300).Select(m =>
            {
                string id = DocId(m);
                var docs = DocsFor(m.DeclaringType!.Assembly);
                XElement? element = docs != null && docs.TryGetValue(id, out var found) ? found : null;
                return new
                {
                    signature = Signature(m),
                    declaredOn = m.DeclaringType != type ? TypeName(m.DeclaringType!, full: false) : null,
                    summary = Text(element?.Element("summary")),
                    parameters = member.Length > 0 ? element?.Elements("param").Select(p => $"{p.Attribute("name")?.Value}: {Text(p)}").ToList() : null,
                    returns = member.Length > 0 ? Text(element?.Element("returns")) : null,
                    remarks = member.Length > 0 ? Text(element?.Element("remarks")) : null,
                };
            }).ToList(),
        };
    }

    private static IEnumerable<Type> Hierarchy(Type type)
    {
        for (Type? t = type; t != null && t != typeof(object) && t != typeof(ValueType); t = t.BaseType) yield return t;
    }

    // Public and protected members a script can use or override, without accessors, event plumbing or compiler output.
    private static IEnumerable<MemberInfo> Visible(Type type) => type.GetMembers(Members).Where(m =>
    {
        if (m.IsDefined(typeof(CompilerGeneratedAttribute)) || m.Name.Contains('<')) return false;
        return m switch
        {
            MethodBase method => (method.IsPublic || method.IsFamily || method.IsFamilyOrAssembly) && (!method.IsSpecialName || method is ConstructorInfo || method.Name.StartsWith("op_", StringComparison.Ordinal)),
            FieldInfo field => (field.IsPublic || field.IsFamily) && !field.IsSpecialName,
            PropertyInfo property => (property.GetMethod ?? property.SetMethod) is { } accessor && (accessor.IsPublic || accessor.IsFamily),
            EventInfo e => e.AddMethod is { } add && (add.IsPublic || add.IsFamily),
            Type nested => nested.IsNestedPublic,
            _ => false,
        };
    });

    private static string Kind(Type t) => t.IsEnum ? "enum" : t.IsInterface ? "interface" : t.IsValueType ? "struct" : typeof(Delegate).IsAssignableFrom(t) ? "delegate" : t.IsAbstract && t.IsSealed ? "static class" : "class";

    // ================================================================
    //  C# signatures
    // ================================================================

    private static string Signature(MemberInfo member)
    {
        switch (member)
        {
            case ConstructorInfo ctor:
                return $"{Access(ctor)}{TypeName(ctor.DeclaringType!, full: false)}({Parameters(ctor)})";
            case MethodInfo method:
                string generic = method.IsGenericMethodDefinition ? $"<{string.Join(", ", method.GetGenericArguments().Select(a => a.Name))}>" : "";
                return $"{Access(method)}{Modifiers(method)}{TypeName(method.ReturnType, full: false)} {method.Name}{generic}({Parameters(method)})";
            case PropertyInfo property:
                var getter = property.GetMethod;
                var setter = property.SetMethod;
                var any = getter ?? setter!;
                string index = property.GetIndexParameters() is { Length: > 0 } ip ? $"[{string.Join(", ", ip.Select(p => $"{TypeName(p.ParameterType, full: false)} {p.Name}"))}]" : "";
                string accessors = (getter is { IsPublic: true } || getter is { IsFamily: true } ? "get; " : "") + (setter is { IsPublic: true } || setter is { IsFamily: true } ? "set; " : "");
                return $"{Access(any)}{Modifiers(any)}{TypeName(property.PropertyType, full: false)} {(index.Length > 0 ? "this" + index : property.Name)} {{ {accessors}}}";
            case FieldInfo field:
                string kind = field.IsLiteral ? "const " : field.IsStatic ? "static " : field.IsInitOnly ? "readonly " : "";
                string value = field.IsLiteral && field.GetRawConstantValue() is { } raw ? $" = {raw}" : "";
                return $"{(field.IsPublic ? "public " : "protected ")}{kind}{TypeName(field.FieldType, full: false)} {field.Name}{value}";
            case EventInfo e:
                return $"public event {TypeName(e.EventHandlerType!, full: false)} {e.Name}";
            case Type nested:
                return $"{Kind(nested)} {TypeName(nested, full: false)}";
        }
        return member.Name;
    }

    private static string Access(MethodBase method) => method.IsPublic ? "public " : "protected ";

    private static string Modifiers(MethodBase method)
    {
        if (method.IsStatic) return "static ";
        if (method.IsAbstract) return "abstract ";
        if (method is MethodInfo m && m.GetBaseDefinition() != m) return "override ";
        if (method.IsVirtual && !method.IsFinal) return "virtual ";
        return "";
    }

    private static string Parameters(MethodBase method) => string.Join(", ", method.GetParameters().Select(p =>
    {
        Type type = p.ParameterType;
        string prefix = "";
        if (type.IsByRef)
        {
            type = type.GetElementType()!;
            prefix = p.IsOut ? "out " : p.IsIn ? "in " : "ref ";
        }
        if (p.IsDefined(typeof(ParamArrayAttribute))) prefix = "params ";
        string defaultValue = p.HasDefaultValue ? " = " + (p.DefaultValue switch { null => "default", string s => $"\"{s}\"", bool b => b ? "true" : "false", var v => Convert.ToString(v, System.Globalization.CultureInfo.InvariantCulture) }) : "";
        return $"{prefix}{TypeName(type, full: false)} {p.Name}{defaultValue}";
    }));

    private static readonly Dictionary<Type, string> s_keywords = new()
    {
        [typeof(void)] = "void", [typeof(bool)] = "bool", [typeof(byte)] = "byte", [typeof(sbyte)] = "sbyte", [typeof(short)] = "short",
        [typeof(ushort)] = "ushort", [typeof(int)] = "int", [typeof(uint)] = "uint", [typeof(long)] = "long", [typeof(ulong)] = "ulong",
        [typeof(float)] = "float", [typeof(double)] = "double", [typeof(decimal)] = "decimal", [typeof(char)] = "char",
        [typeof(string)] = "string", [typeof(object)] = "object",
    };

    private static string TypeName(Type type, bool full)
    {
        if (s_keywords.TryGetValue(type, out string? keyword)) return keyword;
        if (type.IsGenericParameter) return type.Name;
        if (type.IsArray) return TypeName(type.GetElementType()!, full) + "[" + new string(',', type.GetArrayRank() - 1) + "]";
        if (type.IsByRef || type.IsPointer) return TypeName(type.GetElementType()!, full) + (type.IsPointer ? "*" : "");
        if (Nullable.GetUnderlyingType(type) is { } underlying) return TypeName(underlying, full) + "?";

        string name = type.Name;
        int tick = name.IndexOf('`');
        if (tick >= 0) name = name[..tick];
        if (type.IsNested && type.DeclaringType != null) name = TypeName(type.DeclaringType, full) + "." + name;
        else if (full && type.Namespace != null) name = type.Namespace + "." + name;

        var args = type.GetGenericArguments();
        if (type.IsNested && type.DeclaringType != null) args = args.Skip(type.DeclaringType.GetGenericArguments().Length).ToArray();
        return args.Length > 0 ? $"{name}<{string.Join(", ", args.Select(a => TypeName(a, full: false)))}>" : name;
    }

    // ================================================================
    //  XML docs
    // ================================================================

    private static Dictionary<string, XElement>? DocsFor(Assembly assembly)
    {
        lock (s_docs)
        {
            if (s_docs.TryGetValue(assembly, out var cached)) return cached;
            Dictionary<string, XElement>? docs = null;
            try
            {
                string path = string.IsNullOrEmpty(assembly.Location) ? "" : Path.ChangeExtension(assembly.Location, ".xml");
                if (path.Length > 0 && File.Exists(path))
                    docs = XDocument.Load(path).Descendants("member")
                        .Where(m => m.Attribute("name") != null)
                        .GroupBy(m => m.Attribute("name")!.Value)
                        .ToDictionary(g => g.Key, g => g.First());
            }
            catch (Exception ex) when (ex is IOException or System.Xml.XmlException or UnauthorizedAccessException) { }
            s_docs[assembly] = docs;
            return docs;
        }
    }

    private static string? Summary(Assembly assembly, string id) => Doc(assembly, id, "summary");

    private static string? Doc(Assembly assembly, string id, string tag)
        => DocsFor(assembly) is { } docs && docs.TryGetValue(id, out var element) ? Text(element.Element(tag)) : null;

    // Doc text with cross references shown by name and whitespace collapsed.
    private static string? Text(XElement? element)
    {
        if (element == null) return null;
        var sb = new System.Text.StringBuilder();
        foreach (var node in element.Nodes())
        {
            if (node is XText text) sb.Append(text.Value);
            else if (node is XElement e)
            {
                string? reference = e.Attribute("cref")?.Value ?? e.Attribute("name")?.Value ?? e.Attribute("langword")?.Value;
                if (reference != null && e.IsEmpty) sb.Append(ShortRef(reference));
                else sb.Append(Text(e));
            }
        }
        string collapsed = Regex.Replace(sb.ToString(), @"\s+", " ").Trim();
        return collapsed.Length == 0 ? null : collapsed;
    }

    private static string ShortRef(string cref)
    {
        string name = cref.Length > 2 && cref[1] == ':' ? cref[2..] : cref;
        int paren = name.IndexOf('(');
        if (paren >= 0) name = name[..paren];
        string[] parts = name.Split('.');
        bool isMember = cref.StartsWith("M:", StringComparison.Ordinal) || cref.StartsWith("P:", StringComparison.Ordinal)
                        || cref.StartsWith("F:", StringComparison.Ordinal) || cref.StartsWith("E:", StringComparison.Ordinal);
        return isMember && parts.Length >= 2 ? string.Join(".", parts.TakeLast(2)) : parts[^1];
    }

    private static string DocId(MemberInfo member) => member switch
    {
        ConstructorInfo ctor => $"M:{DocName(ctor.DeclaringType!)}.{(ctor.IsStatic ? "#cctor" : "#ctor")}{DocParameters(ctor)}",
        MethodInfo method => $"M:{DocName(method.DeclaringType!)}.{method.Name}{(method.IsGenericMethodDefinition ? "``" + method.GetGenericArguments().Length : "")}{DocParameters(method)}"
                             + (method.Name is "op_Implicit" or "op_Explicit" ? "~" + DocType(method.ReturnType) : ""),
        PropertyInfo property => $"P:{DocName(property.DeclaringType!)}.{property.Name}{(property.GetIndexParameters().Length > 0 ? "(" + string.Join(",", property.GetIndexParameters().Select(p => DocType(p.ParameterType))) + ")" : "")}",
        FieldInfo field => $"F:{DocName(field.DeclaringType!)}.{field.Name}",
        EventInfo e => $"E:{DocName(e.DeclaringType!)}.{e.Name}",
        Type nested => "T:" + DocName(nested),
        _ => "",
    };

    private static string DocParameters(MethodBase method)
    {
        var parameters = method.GetParameters();
        return parameters.Length == 0 ? "" : "(" + string.Join(",", parameters.Select(p => DocType(p.ParameterType))) + ")";
    }

    // The name a type has in an XML doc id: namespace qualified, nested types joined with dots, generic arity kept.
    private static string DocName(Type type)
    {
        string name = type.IsNested && type.DeclaringType != null ? DocName(type.DeclaringType) + "." + type.Name : (type.Namespace != null ? type.Namespace + "." : "") + type.Name;
        return name;
    }

    // A type as it appears inside a doc id's parameter list.
    private static string DocType(Type type)
    {
        if (type.IsByRef) return DocType(type.GetElementType()!) + "@";
        if (type.IsPointer) return DocType(type.GetElementType()!) + "*";
        if (type.IsArray) return DocType(type.GetElementType()!) + (type.GetArrayRank() == 1 ? "[]" : "[" + string.Join(",", Enumerable.Repeat("0:", type.GetArrayRank())) + "]");
        if (type.IsGenericParameter) return (type.DeclaringMethod != null ? "``" : "`") + type.GenericParameterPosition;
        if (type.IsGenericType && !type.IsGenericTypeDefinition)
        {
            var definition = type.GetGenericTypeDefinition();
            string baseName = DocName(definition);
            int tick = baseName.LastIndexOf('`');
            if (tick >= 0) baseName = baseName[..tick];
            return baseName + "{" + string.Join(",", type.GetGenericArguments().Select(DocType)) + "}";
        }
        return DocName(type);
    }
}
