using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;

using Prowl.Echo;
using Prowl.Editor.GUI.SceneView;
using Prowl.Editor.Prefabs;
using Prowl.Runtime;
using Prowl.Runtime.Resources;
using Prowl.Runtime.Tasks;
using Prowl.Vector;

namespace Prowl.Editor.Core;

/// <summary>What a record changes, for dirty marks and for dropping steps.</summary>
public readonly struct UndoOwner : IEquatable<UndoOwner>
{
    internal enum OwnerKind { None, CurrentScene, Asset, AssetFile, Settings }

    internal OwnerKind Kind { get; }
    internal Guid AssetId { get; }
    internal Type? SettingsType { get; }

    private UndoOwner(OwnerKind kind, Guid asset = default, Type? settings = null)
    {
        Kind = kind;
        AssetId = asset;
        SettingsType = settings;
    }

    /// <summary>Marks nothing.</summary>
    public static UndoOwner None => default;

    /// <summary>The single open scene.</summary>
    public static UndoOwner OfCurrentScene => new(OwnerKind.CurrentScene);

    /// <summary>A file written straight to disk. Keys a step for dropping and marks nothing.</summary>
    public static UndoOwner OfAssetFile(Guid asset) => new(OwnerKind.AssetFile, asset);

    /// <summary>A project or editor settings object.</summary>
    public static UndoOwner OfSettings(Type settings) => new(OwnerKind.Settings, settings: settings);

    /// <summary>A scene object's scene, or a database asset itself.</summary>
    public static UndoOwner Of(EngineObject obj) => obj switch
    {
        GameObject go => IsDontSave(go) ? None : OfCurrentScene,
        Component c => c.GameObject.IsValid() && IsDontSave(c.GameObject) ? None : OfCurrentScene,
        Asset { IsFromDatabase: true } asset => new(OwnerKind.Asset, asset.AssetID),
        _ => None,
    };

    private static bool IsDontSave(GameObject go) => (go.HideFlags & (HideFlags.DontSave | HideFlags.HideAndDontSave)) != 0;

    internal string DisplayName => Kind switch
    {
        OwnerKind.CurrentScene => SceneInstance.Main.IsValid() ? SceneInstance.Main.Name : "Scene",
        OwnerKind.Asset or OwnerKind.AssetFile => AssetDatabase.Get(AssetId) is Asset a ? a.Name : AssetId.ToString(),
        OwnerKind.Settings => SettingsType?.Name ?? "Settings",
        _ => "",
    };

    public bool Equals(UndoOwner other) => Kind == other.Kind && AssetId == other.AssetId && SettingsType == other.SettingsType;
    public override bool Equals(object? obj) => obj is UndoOwner o && Equals(o);
    public override int GetHashCode() => HashCode.Combine(Kind, AssetId, SettingsType);
}

/// <summary>An undo step for state no other recording call can express.</summary>
public interface IUndoOperation
{
    string Name { get; }
    IReadOnlyList<UndoOwner> Owners { get; }
    void Undo();
    void Redo();
}

/// <summary>One step of the history, for display.</summary>
public readonly struct UndoStepInfo
{
    public long Id { get; init; }
    public string Name { get; init; }
    public int Group { get; init; }
    public DateTime Time { get; init; }
    public IReadOnlyList<string> OwnerNames { get; init; }
}

/// <summary>What an undo or redo just did.</summary>
public readonly struct UndoRedoInfo
{
    public string Name { get; init; }
    public int Group { get; init; }
    public bool IsRedo { get; init; }
}

/// <summary>One changed field of a recorded object, passed to <see cref="Undo.postprocessModifications"/>.</summary>
public readonly struct UndoPropertyModification
{
    public object Target { get; init; }
    public string PropertyPath { get; init; }
    public EchoObject? PreviousValue { get; init; }
    public EchoObject? CurrentValue { get; init; }
}

public delegate UndoPropertyModification[] PostprocessModifications(UndoPropertyModification[] modifications);

/// <summary>Dirty state for edits that are not undoable.</summary>
public static class EditorUtility
{
    private static readonly HashSet<Guid> _dirtyAssets = [];

    /// <summary>Marks the object's scene, or the asset itself, as changed.</summary>
    public static void SetDirty(EngineObject obj)
    {
        if (obj.IsNotValid()) return;
        if (obj is Asset { IsFromDatabase: true } asset && !Undo.CanSave(asset)) return;
        Undo.Mark(UndoOwner.Of(obj));
    }

    public static bool IsDirty(Asset asset) => asset.IsFromDatabase && _dirtyAssets.Contains(asset.AssetID);

    internal static IReadOnlyCollection<Guid> DirtyAssets => _dirtyAssets;

    internal static void MarkAssetDirty(Guid asset) => _dirtyAssets.Add(asset);

    internal static void ClearAssetDirty(Guid asset) => _dirtyAssets.Remove(asset);

    /// <summary>Hooks the dirty asset set into Ctrl+S and the unsaved changes prompt.</summary>
    internal static void RegisterSaveHooks()
    {
        Projects.SaveManager.OnSave += SaveDirtyAssets;
        Projects.SaveManager.CollectUnsaved += unsaved =>
        {
            foreach (Guid id in _dirtyAssets)
                if (AssetDatabase.TryGetExisting(id, out Asset asset)) unsaved.Add(asset.Name);
        };
    }

    /// <summary>Writes every dirty asset through the asset backend. Nothing during play, since play code may have changed loaded assets.</summary>
    internal static string? SaveDirtyAssets()
    {
        if (_dirtyAssets.Count == 0 || Application.IsPlaying) return null;
        if (EditorAssetBackend.Instance is not EditorAssetBackend backend) return null;

        var saved = new List<string>();
        foreach (Guid id in _dirtyAssets.ToList())
        {
            if (!AssetDatabase.TryGetExisting(id, out Asset asset)) { _dirtyAssets.Remove(id); continue; }
            if (!backend.SaveAsset(asset)) continue;
            _dirtyAssets.Remove(id);
            saved.Add(asset.Name);
        }
        return saved.Count == 0 ? null : string.Join(", ", saved);
    }

    /// <summary>In memory copies of every dirty asset, taken before play so the reload after play can put the edits back.</summary>
    internal static List<(Asset Asset, MemoryCopy Copy)> CopyDirtyAssets()
    {
        var copies = new List<(Asset, MemoryCopy)>();
        foreach (Guid id in _dirtyAssets)
            if (AssetDatabase.TryGetExisting(id, out Asset asset) && asset.IsLoaded)
            {
                var ctx = new DependencySerializationContext { LinkRuntimeAssets = true, ExternalReferences = new SceneReferenceResolver(asset) };
                copies.Add((asset, new MemoryCopy(Serializer.Serialize(typeof(object), asset, ctx), ctx.LinkedAssets)));
            }
        return copies;
    }

    /// <summary>Refills each asset from its copy, after play reloaded every asset from disk.</summary>
    internal static void RestoreDirtyAssets(List<(Asset Asset, MemoryCopy Copy)> copies)
    {
        foreach ((Asset asset, MemoryCopy copy) in copies)
        {
            try { AssetDatabase.Refill(asset, copy.Data, ReloadReason.Undo); }
            catch (Exception ex) { Runtime.Debug.LogException($"'{asset.Name}' lost its unsaved edits when play stopped", ex); }
        }
    }
}

/// <summary>
/// The editor's undo history: records attached to what they change, grouped into steps.
/// </summary>
public static class Undo
{
    // ================================================================
    //  State
    // ================================================================

    private static List<Step> _undo = [];
    private static List<Step> _redo = [];
    private static List<Entry> _pending = [];

    private static int _currentGroup = 1;
    private static int _lastGroup = 1;
    private static Dictionary<int, string> _groupNames = [];
    private static long _nextStepId = 1;
    private static bool _isProcessing;

    private static bool _mouseHeld;
    private static bool _interactionRecorded;
    private static int _frame;

    private static readonly Dictionary<object, (Capture Capture, int Frame)> _inspectorBaselines = new(ReferenceEqualityComparer.Instance);
    private static readonly HashSet<Type> _warnedTypes = [];

    /// <summary> Maximum number of undo steps kept in the undo stack. Defaults to 100. </summary>
    public static int MaxSteps { get; set; } = 100;

    // ================================================================
    //  Events
    // ================================================================

    public static event Action? undoRedoPerformed;
    public static event Action<UndoRedoInfo>? undoRedoEvent;
    public static event Action? willFlushUndoRecord;

    /// <summary>A step was pushed, joined, undone, redone, dropped or cleared, or the live history changed context.</summary>
    public static event Action? historyChanged;

    /// <summary>Filters the field changes of each flush. Only the returned modifications are recorded.</summary>
    public static PostprocessModifications? postprocessModifications;

    // ================================================================
    //  Queries
    // ================================================================

    public static bool CanUndo => _undo.Count > 0 || _pending.Count > 0;
    public static bool CanRedo => _redo.Count > 0 && !OpenGroupHasStep;
    public static bool isProcessing => _isProcessing;

    /// <summary>A mouse hold that has recorded something. Its records form one step.</summary>
    public static bool InteractionActive => _mouseHeld && _interactionRecorded;

    public static string UndoDescription => _undo.Count > 0 ? $"Undo {_undo[^1].Name}" : "Undo";
    public static string RedoDescription => _redo.Count > 0 ? $"Redo {_redo[^1].Name}" : "Redo";

    public static IReadOnlyList<UndoStepInfo> UndoSteps => [.. _undo.Select(s => s.Info)];
    public static IReadOnlyList<UndoStepInfo> RedoSteps => [.. Enumerable.Reverse(_redo).Select(s => s.Info)];

    private static bool OpenGroupHasStep => _undo.Count > 0 && _undo[^1].Groups.Contains(_currentGroup);

    // ================================================================
    //  Recording object state
    // ================================================================

    /// <summary>Records the object's state now. The changes made to it before the next flush become one record.</summary>
    public static void RecordObject(EngineObject obj, string name)
    {
        if (obj.IsNotValid()) return;
        RecordTarget(obj, TargetKey.For(obj), UndoOwner.Of(obj), name);
    }

    public static void RecordObjects(IReadOnlyList<EngineObject> objs, string name)
    {
        foreach (EngineObject obj in objs) RecordObject(obj, name);
    }

    /// <summary>Records a plain object with an explicit owner. It is found again by a weak reference.</summary>
    public static void RecordObject(object target, UndoOwner owner, string name)
    {
        if (target is EngineObject eo) { if (eo.IsNotValid()) return; RecordTarget(eo, TargetKey.For(eo), owner, name); return; }
        RecordTarget(target, TargetKey.Weak(target), owner, name);
    }

    /// <summary>Records a plain object that is found again through <paramref name="resolve"/> when applied.</summary>
    public static void RecordObject<T>(Func<T> resolve, UndoOwner owner, string name) where T : class
    {
        T? target = resolve();
        if (target == null) return;
        RecordTarget(target, TargetKey.Resolver(() => resolve()), owner, name);
    }

    /// <summary>Records a full copy of the object now, compared whole at the flush.</summary>
    public static void RegisterCompleteObjectUndo(EngineObject obj, string name) => RecordObject(obj, name);

    /// <summary>
    /// A capture taken every time an editor draws, kept only when something changed by the next flush. Unlike
    /// <see cref="RecordObject(EngineObject, string)"/> it is not an explicit record, so it never counts as an
    /// interaction, and it is skipped during play so game code never becomes a step.
    /// </summary>
    internal static void WatchObject(EngineObject obj, string name)
    {
        if (Application.IsPlaying || obj.IsNotValid()) return;
        RecordTarget(obj, TargetKey.For(obj), UndoOwner.Of(obj), name, explicitRecord: false);
    }

    private static void RecordTarget(object target, TargetKey key, UndoOwner owner, string name, bool explicitRecord = true)
    {
        MainThreadContext.AssertMainThread();
        if (target is Asset { IsFromDatabase: true } asset && !CanSave(asset))
        {
            WarnOnce(target.GetType(), $"'{asset.Name}' cannot be saved, so edits to it are not recorded.");
            return;
        }

        int group = _currentGroup;
        if (FindMergeablePending(key, group) != null) { if (explicitRecord) NoteRecord(); return; }

        if (owner.Kind == UndoOwner.OwnerKind.None && target is EngineObject eo and not GameObject and not Component and not Asset { IsFromDatabase: true })
            WarnOnce(eo.GetType(), $"'{eo.GetType().Name}' is recorded with no owner, so undoing it marks nothing.");

        if (explicitRecord) NoteRecord();
        _pending.Add(new PendingObject(target, key, owner, Capture.Of(target)) { Group = group, Name = name });
    }

    // A pending capture of the same target in the same group with no structural change to it since.
    private static PendingObject? FindMergeablePending(TargetKey key, int group)
    {
        for (int i = _pending.Count - 1; i >= 0; i--)
        {
            Entry e = _pending[i];
            if (e.Group != group) continue;
            if (e is PendingObject po && po.Key.Same(key)) return po;
            if (key.ObjectId != Guid.Empty && e.Touches(key.ObjectId)) return null;
        }
        return null;
    }

    // ================================================================
    //  Structure
    // ================================================================

    /// <summary>Records that a GameObject was just created. Undo destroys it, redo brings it back.</summary>
    public static void RegisterCreatedObjectUndo(EngineObject obj, string name)
    {
        MainThreadContext.AssertMainThread();
        if (obj is not GameObject go || go.IsNotValid()) return;
        NoteRecord();
        _pending.Add(new PendingCreated(go) { Group = _currentGroup, Name = name });
    }

    /// <summary>Destroys the object now and records it. Undo brings it back with its identifiers.</summary>
    public static void DestroyObjectImmediate(EngineObject obj)
    {
        MainThreadContext.AssertMainThread();
        switch (obj)
        {
            case GameObject go when go.IsValid():
                if (DropCreatedInGroup(go)) { DestroyNow(go); return; }
                NoteRecord();
                _pending.Add(new ReadyEntry(DestroyRecord.Of(go)) { Group = _currentGroup, Name = $"Destroy {go.Name}" });
                DestroyNow(go);
                break;

            case Component c when c.IsValid() && c.GameObject.IsValid():
                GameObject owner = c.GameObject;
                if (c.IsFromPrefab)
                {
                    Runtime.Debug.LogWarning($"'{c.GetType().Name}' on '{owner.Name}' comes from a prefab, so it cannot be removed without breaking the prefab first.");
                    return;
                }
                if (!c.CanDestroy()) return;
                if (DropAddedInGroup(c)) { DestroyComponentNow(c); return; }
                NoteRecord();
                _pending.Add(new ReadyEntry(DestroyComponentRecord.Of(c)) { Group = _currentGroup, Name = $"Remove {c.GetType().Name}" });
                DestroyComponentNow(c);
                break;
        }
    }

    public static T AddComponent<T>(GameObject go) where T : Component => (T)AddComponent(go, typeof(T));

    /// <summary>Adds a component and every component it requires, and records them.</summary>
    public static Component AddComponent(GameObject go, Type type)
    {
        MainThreadContext.AssertMainThread();
        var before = new HashSet<Component>(go.GetComponents<Component>(), ReferenceEqualityComparer.Instance);
        Component added = go.AddComponent(type);
        var created = go._components.Where(c => !before.Contains(c)).ToList();
        if (created.Count > 0)
        {
            NoteRecord();
            _pending.Add(new PendingAddComponent(go, created) { Group = _currentGroup, Name = $"Add {type.Name}" });
        }
        return added;
    }

    /// <summary>Reparents and records the old parent, sibling index and local transform.</summary>
    public static void SetTransformParent(GameObject go, GameObject? parent, bool worldPositionStays, string name)
    {
        MainThreadContext.AssertMainThread();
        if (go.IsNotValid()) return;
        var record = new ParentRecord(go);
        if (!go.SetParent(parent!, worldPositionStays)) return;
        record.CaptureAfter(go);
        NoteRecord();
        _pending.Add(new ReadyEntry(record) { Group = _currentGroup, Name = name });
    }

    public static void SetSiblingIndex(GameObject go, int index, string name)
    {
        if (go.IsNotValid()) return;
        RegisterChildrenOrderUndo(go.Parent, go.Scene, name);
        if (go.Parent.IsValid()) go.SetSiblingIndex(index);
        else if (go.Scene.IsValid()) go.SiblingIndex = index;
    }

    /// <summary>Records the order of a parent's children, or of the scene's roots when <paramref name="parent"/> is null.</summary>
    public static void RegisterChildrenOrderUndo(GameObject? parent, string name) => RegisterChildrenOrderUndo(parent, SceneInstance.Main, name);

    private static void RegisterChildrenOrderUndo(GameObject? parent, SceneInstance? scene, string name)
    {
        MainThreadContext.AssertMainThread();
        NoteRecord();
        _pending.Add(new PendingChildrenOrder(parent, scene) { Group = _currentGroup, Name = name });
    }

    /// <summary>Records the whole tree, its structure and prefab data. Undo makes the tree match the record.</summary>
    public static void RegisterFullObjectHierarchyUndo(GameObject root, string name)
    {
        MainThreadContext.AssertMainThread();
        if (root.IsNotValid()) return;
        NoteRecord();
        _pending.Add(new PendingHierarchy(root) { Group = _currentGroup, Name = name });
    }

    /// <summary>Records the asset's pending import settings. Undo and redo restore them and never write to disk.</summary>
    public static void RegisterImporterUndo(Guid asset, string name) => RegisterImporterUndo(asset, name, explicitRecord: true);

    /// <summary>As <see cref="RegisterImporterUndo(Guid, string)"/>, for the capture an importer inspector takes every draw.</summary>
    internal static void WatchImporter(Guid asset, string name) => RegisterImporterUndo(asset, name, explicitRecord: false);

    private static void RegisterImporterUndo(Guid asset, string name, bool explicitRecord)
    {
        MainThreadContext.AssertMainThread();
        if (Inspector.ImportSettingsEditor.Pending(asset) is not EchoObject settings) return;
        if (explicitRecord) NoteRecord();
        if (_pending.Any(e => e.Group == _currentGroup && e is PendingImporter && e.Touches(asset))) return;
        _pending.Add(new PendingImporter(asset, settings.Clone()) { Group = _currentGroup, Name = name });
    }

    /// <summary>
    /// Records the objects now and keeps the records open across frames until <see cref="ReleaseRecords"/>, so a tool
    /// that writes over many frames becomes one record from its first state to its last.
    /// </summary>
    internal static object HoldRecords(IReadOnlyList<EngineObject> objs, string name)
    {
        MainThreadContext.AssertMainThread();
        var held = new List<PendingObject>();
        foreach (EngineObject obj in objs)
        {
            if (obj.IsNotValid()) continue;
            var entry = new PendingObject(obj, TargetKey.For(obj), UndoOwner.Of(obj), Capture.Of(obj)) { Group = _currentGroup, Name = name, Held = true };
            _pending.Add(entry);
            held.Add(entry);
        }
        NoteRecord();
        return held;
    }

    /// <summary>Closes records opened by <see cref="HoldRecords"/>. They become a step at the next flush, or are reverted and dropped.</summary>
    internal static void ReleaseRecords(object token, bool revert)
    {
        if (token is not List<PendingObject> held) return;
        foreach (PendingObject entry in held) entry.Held = false;
        if (!revert) return;

        FlushUndoRecordObjects();
        int group = held.Count > 0 ? held[0].Group : -1;
        if (_undo.Count > 0 && _undo[^1].Groups.Contains(group))
        {
            Step step = _undo[^1];
            _undo.RemoveAt(_undo.Count - 1);
            Apply(step, undo: true, mark: false);
            historyChanged?.Invoke();
        }
    }

    /// <summary>Records an operation for state no other call can express. It has already been done.</summary>
    public static void RegisterOperation(IUndoOperation operation)
    {
        MainThreadContext.AssertMainThread();
        NoteRecord();
        _pending.Add(new ReadyEntry(new OperationRecord(operation)) { Group = _currentGroup, Name = operation.Name });
    }

    private static void DestroyNow(GameObject go)
    {
        if (Selection.IsSelected(go)) Selection.RemoveFromSelection(go);
        if (go.Scene.IsValid()) go.Scene.Remove(go);
        go.Dispose();
    }

    private static void DestroyComponentNow(Component c)
    {
        c.GameObject.DetachComponent(c);
        c.Dispose();
    }

    // An object created in the open group and destroyed in it leaves nothing to record.
    private static bool DropCreatedInGroup(GameObject go)
    {
        int index = _pending.FindIndex(e => e.Group == _currentGroup && e is PendingCreated pc && ReferenceEquals(pc.Go, go));
        if (index < 0) return false;
        HashSet<Guid> ids = TreeIds(go);
        _pending.RemoveAll(e => e.Group == _currentGroup && (ReferenceEquals((e as PendingCreated)?.Go, go) || ids.Any(e.Touches)));
        return true;
    }

    private static bool DropAddedInGroup(Component c)
    {
        foreach (Entry e in _pending)
            if (e.Group == _currentGroup && e is PendingAddComponent add && add.Components.Remove(c))
            {
                if (add.Components.Count == 0) _pending.Remove(add);
                _pending.RemoveAll(p => p.Group == _currentGroup && p is PendingObject po && po.Key.ObjectId == c.Identifier);
                return true;
            }
        return false;
    }

    private static HashSet<Guid> TreeIds(GameObject root)
    {
        var ids = new HashSet<Guid>();
        Collect(root);
        return ids;

        void Collect(GameObject go)
        {
            ids.Add(go.Identifier);
            foreach (Component c in go._components) ids.Add(c.Identifier);
            foreach (GameObject child in go.Children) Collect(child);
        }
    }

    // ================================================================
    //  Groups
    // ================================================================

    public static int GetCurrentGroup() => _currentGroup;

    public static string GetCurrentGroupName() => _groupNames.TryGetValue(_currentGroup, out string? n) ? n : "";

    public static void SetCurrentGroupName(string name)
    {
        _groupNames[_currentGroup] = name;
        if (OpenGroupHasStep)
        {
            _undo[^1].Name = name;
            historyChanged?.Invoke();
        }
    }

    /// <summary>Closes the current group and starts a new one. A step the closed group pushed clears redo.</summary>
    public static void IncrementCurrentGroup()
    {
        if (OpenGroupHasStep && _redo.Count > 0)
        {
            _redo.Clear();
            historyChanged?.Invoke();
        }
        _groupNames.Remove(_currentGroup);
        _currentGroup = ++_lastGroup;
    }

    /// <summary>Merges every group from <paramref name="groupIndex"/> up into one step.</summary>
    public static void CollapseUndoOperations(int groupIndex)
    {
        foreach (Entry e in _pending)
            if (e.Group > groupIndex) e.Group = groupIndex;

        int first = _undo.Count;
        while (first > 0 && _undo[first - 1].Groups.Max() >= groupIndex) first--;
        if (_undo.Count - first < 2) return;

        Step merged = _undo[first];
        for (int i = first + 1; i < _undo.Count; i++)
        {
            merged.Records.AddRange(_undo[i].Records);
            merged.Groups.UnionWith(_undo[i].Groups);
        }
        _undo.RemoveRange(first + 1, _undo.Count - first - 1);
        historyChanged?.Invoke();
    }

    /// <summary>Undoes everything recorded in the current group and drops it, leaving redo as it was.</summary>
    public static void RevertAllInCurrentGroup()
    {
        MainThreadContext.AssertMainThread();
        FlushUndoRecordObjects();
        if (OpenGroupHasStep)
        {
            Step step = _undo[^1];
            _undo.RemoveAt(_undo.Count - 1);
            Apply(step, undo: true, mark: false);
            historyChanged?.Invoke();
        }
        _interactionRecorded = false;
        _currentGroup = ++_lastGroup;
    }

    /// <summary>Undoes every step down to and including the given group's step, without keeping them for redo.</summary>
    public static void RevertAllDownToGroup(int groupIndex)
    {
        MainThreadContext.AssertMainThread();
        FlushUndoRecordObjects();
        while (_undo.Count > 0)
        {
            Step step = _undo[^1];
            _undo.RemoveAt(_undo.Count - 1);
            Apply(step, undo: true, mark: true);
            if (step.Groups.Contains(groupIndex) || step.Groups.Min() < groupIndex) break;
        }
        historyChanged?.Invoke();
        _currentGroup = ++_lastGroup;
    }

    // Marks the current interaction as having recorded, so its group stays open until the mouse is up.
    private static void NoteRecord()
    {
        if (_mouseHeld && !_interactionRecorded) _interactionRecorded = true;
    }

    // ================================================================
    //  Undo and redo
    // ================================================================

    /// <summary>Undoes one step. Cancels an active interaction instead.</summary>
    public static void PerformUndo()
    {
        MainThreadContext.AssertMainThread();
        if (InteractionActive) { RevertAllInCurrentGroup(); return; }

        FlushUndoRecordObjects();
        IncrementCurrentGroup();
        if (_undo.Count == 0) return;

        Step step = _undo[^1];
        _undo.RemoveAt(_undo.Count - 1);
        if (Apply(step, undo: true, mark: true))
        {
            _redo.Add(step);
            TrimStack(_redo);
        }
        _currentGroup = ++_lastGroup;
        Performed(step, isRedo: false);
    }

    /// <summary>Redoes one step. Does nothing while the open group has a step, so a redo never lands over a newer edit.</summary>
    public static void PerformRedo()
    {
        MainThreadContext.AssertMainThread();
        if (InteractionActive) return;

        FlushUndoRecordObjects();
        if (_redo.Count == 0 || OpenGroupHasStep) return;

        Step step = _redo[^1];
        _redo.RemoveAt(_redo.Count - 1);
        if (Apply(step, undo: false, mark: true))
        {
            _undo.Add(step);
            TrimStack(_undo);
        }
        _currentGroup = ++_lastGroup;
        Performed(step, isRedo: true);
    }

    /// <summary>Undoes every step down to and including the one with this id. Does nothing if it is gone.</summary>
    public static void PerformUndoTo(long stepId)
    {
        if (InteractionActive) return;
        FlushUndoRecordObjects();
        if (!_undo.Any(s => s.Id == stepId)) return;
        while (_undo.Count > 0)
        {
            long top = _undo[^1].Id;
            PerformUndo();
            if (top == stepId || !_redo.Any(s => s.Id == top)) break;
        }
    }

    /// <summary>Redoes every step up to and including the one with this id. Does nothing if it is gone.</summary>
    public static void PerformRedoTo(long stepId)
    {
        if (InteractionActive) return;
        FlushUndoRecordObjects();
        if (!_redo.Any(s => s.Id == stepId)) return;
        while (_redo.Count > 0 && !OpenGroupHasStep)
        {
            long top = _redo[^1].Id;
            PerformRedo();
            if (top == stepId || !_undo.Any(s => s.Id == top)) break;
        }
    }

    private static void Performed(Step step, bool isRedo)
    {
        historyChanged?.Invoke();
        undoRedoEvent?.Invoke(new UndoRedoInfo { Name = step.Name, Group = step.Groups.Min(), IsRedo = isRedo });
        undoRedoPerformed?.Invoke();
    }

    // ================================================================
    //  Flushing
    // ================================================================

    /// <summary>Turns pending recordings into records, joined to the step of their group.</summary>
    public static void FlushUndoRecordObjects()
    {
        if (_pending.Count == 0) return;
        willFlushUndoRecord?.Invoke();

        List<Entry> entries = _pending;
        _pending = [.. entries.Where(e => e is PendingObject { Held: true })];
        if (_pending.Count == entries.Count) return;
        entries = [.. entries.Where(e => e is not PendingObject { Held: true })];

        var records = new List<Record>();
        foreach (Entry entry in entries)
        {
            Record? record;
            try { record = entry.Finish(entries); }
            catch (Exception ex) { Runtime.Debug.LogException($"Recording '{entry.Name}' failed", ex); continue; }
            if (record == null) continue;
            record.Group = entry.Group;
            record.Name = entry.Name;
            records.Add(record);
        }

        if (postprocessModifications != null) records = Postprocess(records);

        bool changed = false;
        foreach (Record record in records)
            changed |= Push(record);
        if (changed) historyChanged?.Invoke();
    }

    private static List<Record> Postprocess(List<Record> records)
    {
        var modifications = new List<UndoPropertyModification>();
        foreach (Record r in records)
        {
            if (r is not ObjectRecord o) continue;
            object? target = o.Key.Resolve(SceneInstance.Main);
            if (target == null) continue;
            foreach (string key in o.After.Keys)
                modifications.Add(new UndoPropertyModification { Target = target, PropertyPath = key, PreviousValue = o.Before.GetValueOrDefault(key), CurrentValue = o.After[key] });
        }
        if (modifications.Count == 0) return records;

        UndoPropertyModification[] kept = postprocessModifications!([.. modifications]) ?? [];
        var keep = new HashSet<(object, string)>(kept.Select(m => (m.Target, m.PropertyPath)));

        var result = new List<Record>(records.Count);
        foreach (Record r in records)
        {
            if (r is ObjectRecord o && o.Key.Resolve(SceneInstance.Main) is { } target)
            {
                foreach (string key in o.After.Keys.ToList())
                    if (!keep.Contains((target, key))) { o.After.Remove(key); o.Before.Remove(key); }
                if (o.After.Count == 0) continue;
            }
            result.Add(r);
        }
        return result;
    }

    // Joins the record to the top step when it is of the same group, else pushes a new step.
    private static bool Push(Record record)
    {
        Step? top = _undo.Count > 0 ? _undo[^1] : null;
        if (top != null && top.Groups.Contains(record.Group)) top.Join(record);
        else
        {
            top = new Step(_nextStepId++, record.Group, _groupNames.TryGetValue(record.Group, out string? groupName) ? groupName : record.Name);
            top.Records.Add(record);
            _undo.Add(top);
            TrimStack(_undo);
        }

        foreach (UndoOwner owner in record.Owners) Mark(owner);

        if (record.Group != _currentGroup) _redo.Clear();
        return true;
    }

    private static void TrimStack(List<Step> stack)
    {
        while (stack.Count > Math.Max(1, MaxSteps))
        {
            if (ReferenceEquals(stack, _undo) && stack[0].Groups.Contains(_currentGroup)) break;
            stack.RemoveAt(0);
        }
    }

    // ================================================================
    //  Applying a step
    // ================================================================

    // Applies the step's records in one restore. False when the restore failed and the history was cleared.
    private static bool Apply(Step step, bool undo, bool mark)
    {
        _isProcessing = true;
        var restore = new Restore(undo);
        IEnumerable<Record> ordered = undo ? Enumerable.Reverse(step.Records) : step.Records;
        try
        {
            foreach (Record r in ordered) r.CollectRecreations(restore);
            restore.FillPrebuilt();
            foreach (Record r in ordered) r.Apply(restore);
            restore.Finish();
        }
        catch (Exception ex)
        {
            restore.DisposeUnattached();
            _isProcessing = false;
            Runtime.Debug.LogException($"{(undo ? "Undo" : "Redo")} of '{step.Name}' failed, so the history was cleared", ex);
            ClearAll();
            return false;
        }
        _isProcessing = false;

        if (mark && !Application.IsPlaying)
            foreach (Record r in step.Records)
                foreach (UndoOwner owner in r.Owners) Mark(owner);

        foreach (object target in restore.Touched)
            if (target is EngineObject eo && eo.IsValid())
                PrefabUtility.NotifyEdited(target);
        return true;
    }

    // ================================================================
    //  Dirty marks
    // ================================================================

    internal static void Mark(UndoOwner owner)
    {
        if (Application.IsPlaying) return;
        switch (owner.Kind)
        {
            case UndoOwner.OwnerKind.CurrentScene: EditorSceneManager.MarkDirty(); break;
            case UndoOwner.OwnerKind.Asset: EditorUtility.MarkAssetDirty(owner.AssetId); break;
            case UndoOwner.OwnerKind.Settings when owner.SettingsType != null: EditorRegistries.SaveSettings(owner.SettingsType); break;
        }
    }

    internal static bool CanSave(Asset asset)
        => !asset.IsFromDatabase || EditorAssetBackend.Instance is not EditorAssetBackend backend || backend.CanSave(asset.AssetID);

    private static void WarnOnce(Type type, string message)
    {
        if (_warnedTypes.Add(type)) Runtime.Debug.LogWarning(message);
    }

    // ================================================================
    //  Editor frame hooks
    // ================================================================

    /// <summary>Start of an editor frame: flushes what the last frame recorded and drops stale inspector captures.</summary>
    internal static void FlushFrame()
    {
        FlushUndoRecordObjects();
        _frame++;
        if (_inspectorBaselines.Count > 0)
            foreach (object key in _inspectorBaselines.Where(kv => kv.Value.Frame < _frame - 1).Select(kv => kv.Key).ToList())
                _inspectorBaselines.Remove(key);
        if (_inspectorRedirects.Count > 0)
            foreach (object key in _inspectorRedirects.Where(kv => !_inspectorBaselines.ContainsKey(kv.Value)).Select(kv => kv.Key).ToList())
                _inspectorRedirects.Remove(key);
    }

    /// <summary>
    /// Start of an editor frame, after the flush: a mouse press or an editor shortcut commits typed text and
    /// starts a new group, and releasing the mouse ends the interaction.
    /// </summary>
    internal static void BeginFrameInput(bool mouseDown, bool mouseHeld, bool shortcut)
    {
        if (!mouseHeld && _mouseHeld) { _mouseHeld = false; _interactionRecorded = false; }

        if (mouseDown || (shortcut && !InteractionActive))
        {
            OrigamiUI.TextFieldCommits.CommitAll();
            IncrementCurrentGroup();
        }
        if (mouseDown) { _mouseHeld = true; _interactionRecorded = false; }
    }

    /// <summary>Starts a new group when keyboard focus moved to another element.</summary>
    internal static void FocusChanged() => IncrementCurrentGroup();

    /// <summary>End of an editor frame: the group closes unless a drag is recording or a text field has focus.</summary>
    internal static void EndFrame(bool textFocused)
    {
        if (!InteractionActive && !textFocused) IncrementCurrentGroup();
    }

    /// <summary>Whether Undo or Redo pressed now should be ignored: the mouse is held and nothing has recorded yet.</summary>
    internal static bool IgnoresUndoInput => _mouseHeld && !_interactionRecorded;

    /// <summary>A scene loaded by play code ends the play history, as opening a scene ends the edit history.</summary>
    internal static void OnSceneLoaded()
    {
        if (Application.IsPlaying) Clear();
    }

    /// <summary>Keeps dirty assets and assets the history records loaded, so a save or an undo never reloads them from disk.</summary>
    internal static void VisitResidents(AssetWalker walker)
    {
        foreach (Guid id in EditorUtility.DirtyAssets)
            if (AssetDatabase.TryGetExisting(id, out Asset asset)) walker.Visit(asset);

        IEnumerable<Step> steps = _undo.Concat(_redo).Concat(_contexts.SelectMany(c => c.Undo.Concat(c.Redo)));
        foreach (Step step in steps)
            foreach (Record r in step.Records)
            {
                if (r is ObjectRecord { AssetId: { } id } && AssetDatabase.TryGetExisting(id, out Asset recorded))
                    walker.Visit(recorded);
                foreach (UndoOwner owner in r.Owners)
                    if (owner.Kind == UndoOwner.OwnerKind.Asset && AssetDatabase.TryGetExisting(owner.AssetId, out Asset owned))
                        walker.Visit(owned);
            }
    }

    // ================================================================
    //  Inspector
    // ================================================================

    private static EngineObject? _inspectorHolder;
    private static readonly Dictionary<object, EngineObject> _inspectorRedirects = new(ReferenceEqualityComparer.Instance);

    /// <summary>
    /// While open, a plain object the inspector draws as its own root is recorded as part of <paramref name="holder"/>,
    /// so the record is keyed to the component and owned by its scene.
    /// </summary>
    internal static IDisposable InspectorHolder(EngineObject holder)
    {
        EngineObject? previous = _inspectorHolder;
        _inspectorHolder = holder;
        return new HolderScope(previous);
    }

    private sealed class HolderScope(EngineObject? previous) : IDisposable
    {
        public void Dispose() => _inspectorHolder = previous;
    }

    /// <summary>The inspector's capture of a target before it draws. Not a record.</summary>
    internal static void InspectorBaseline(object target)
    {
        if (target == null) return;
        if (target is not EngineObject && _inspectorHolder.IsValid())
        {
            _inspectorRedirects[target] = _inspectorHolder;
            target = _inspectorHolder;
        }
        if (target is EngineObject eo && eo.IsNotValid()) return;
        _inspectorBaselines[target] = (Capture.Of(target), _frame);
    }

    /// <summary>The inspector changed a target. Records the keys that differ from its capture right now.</summary>
    internal static void InspectorChanged(object target, UndoOwner? owner = null)
    {
        if (_inspectorRedirects.TryGetValue(target, out EngineObject? holder)) target = holder;
        if (!_inspectorBaselines.TryGetValue(target, out (Capture Capture, int Frame) baseline)) return;
        if (target is EngineObject eo && eo.IsNotValid()) return;

        Capture after = Capture.Of(target);
        _inspectorBaselines[target] = (after, _frame);
        ObjectRecord? record = ObjectRecord.Diff(target, TargetKey.For(target), owner ?? DefaultOwner(target), baseline.Capture, after);
        if (record == null) return;
        if (target is Asset { IsFromDatabase: true } asset && !CanSave(asset)) return;

        NoteRecord();
        string name = $"Modify {target.GetType().Name}";
        foreach (Entry e in _pending)
            if (e.Group == _currentGroup && e is ReadyEntry { Record: ObjectRecord existing } && existing.Key.Same(record.Key))
            {
                existing.Merge(record);
                return;
            }
        _pending.Add(new ReadyEntry(record) { Group = _currentGroup, Name = name });
    }

    private static UndoOwner DefaultOwner(object target) => target is EngineObject eo ? UndoOwner.Of(eo) : UndoOwner.None;

    // ================================================================
    //  The stack
    // ================================================================

    /// <summary>Clears the history, pending records and every context set aside.</summary>
    public static void ClearAll()
    {
        _undo.Clear();
        _redo.Clear();
        _contexts.Clear();
        ClearPending();
        historyChanged?.Invoke();
    }

    /// <summary>Clears the history. Called on scene load.</summary>
    public static void Clear()
    {
        _undo.Clear();
        _redo.Clear();
        ClearPending();
        historyChanged?.Invoke();
    }

    /// <summary>Drops every step with a record keyed to the object, never part of a step.</summary>
    public static void ClearUndo(EngineObject obj)
    {
        if (ReferenceEquals(obj, null)) return;
        Guid id = obj switch { GameObject go => go.Identifier, Component c => c.Identifier, Asset a => a.AssetID, _ => Guid.Empty };
        if (id == Guid.Empty) return;

        bool dropsOpen = OpenGroupHasStep && _undo[^1].Records.Any(r => r.Touches(id));
        _undo.RemoveAll(s => s.Records.Any(r => r.Touches(id)));
        _redo.RemoveAll(s => s.Records.Any(r => r.Touches(id)));
        _pending.RemoveAll(e => e.Touches(id));
        if (dropsOpen) _currentGroup = ++_lastGroup;
        historyChanged?.Invoke();
    }

    /// <summary>Drops every step with a record owned by the asset, and its dirty mark. For an asset changed outside the editor.</summary>
    internal static void DropAsset(Guid asset)
    {
        EditorUtility.ClearAssetDirty(asset);
        bool Owned(Record r) => r.Owners.Any(o => o.AssetId == asset && o.Kind is UndoOwner.OwnerKind.Asset or UndoOwner.OwnerKind.AssetFile)
                                || r is ObjectRecord { AssetId: { } id } && id == asset;
        bool dropsOpen = OpenGroupHasStep && _undo[^1].Records.Any(Owned);
        int removed = _undo.RemoveAll(s => s.Records.Any(Owned)) + _redo.RemoveAll(s => s.Records.Any(Owned));
        foreach (UndoContext context in _contexts)
            removed += context.Undo.RemoveAll(s => s.Records.Any(Owned)) + context.Redo.RemoveAll(s => s.Records.Any(Owned));
        _pending.RemoveAll(e => e is PendingObject po && po.Key.IsAsset && po.Key.Id == asset);
        if (dropsOpen) _currentGroup = ++_lastGroup;
        if (removed > 0) historyChanged?.Invoke();
    }

    /// <summary>Drops edits that were captured but not yet recorded, keeping the undo and redo history.</summary>
    public static void ClearPending()
    {
        _pending.Clear();
        _inspectorBaselines.Clear();
        _inspectorRedirects.Clear();
        _mouseHeld = false;
        _interactionRecorded = false;
    }

    // ================================================================
    //  Contexts
    // ================================================================

    private static readonly List<UndoContext> _contexts = [];

    private sealed record UndoContext(List<Step> Undo, List<Step> Redo, List<Entry> Pending, int Group);

    /// <summary>
    /// Sets the history aside and starts an empty one, for a session that edits something other than the open
    /// scene. Typed text is committed and pending records flushed first. Returns a token for <see cref="PopContext"/>.
    /// </summary>
    internal static object PushContext()
    {
        OrigamiUI.TextFieldCommits.CommitAll();
        FlushUndoRecordObjects();
        var saved = new UndoContext(_undo, _redo, _pending, _currentGroup);
        _contexts.Add(saved);
        _undo = [];
        _redo = [];
        _pending = [];
        ClearPending();
        _currentGroup = ++_lastGroup;
        historyChanged?.Invoke();
        return saved;
    }

    /// <summary>Puts back a history <see cref="PushContext"/> set aside, discarding the current one. A cleared context restores empty.</summary>
    internal static void PopContext(object? context)
    {
        ClearPending();
        _undo = [];
        _redo = [];
        _pending = [];
        if (context is UndoContext saved && _contexts.Remove(saved))
        {
            _undo = saved.Undo;
            _redo = saved.Redo;
        }
        _currentGroup = ++_lastGroup;
        historyChanged?.Invoke();
    }

    private static GameObject? FindGO(Guid identifier)
    {
        SceneInstance scene = SceneInstance.Main;
        return scene.IsValid() ? scene.FindObject<GameObject>(identifier) : null;
    }

    // ================================================================
    //  Steps
    // ================================================================

    private sealed class Step(long id, int group, string name)
    {
        public readonly long Id = id;
        public readonly HashSet<int> Groups = [group];
        public readonly List<Record> Records = [];
        public string Name = name;
        public readonly DateTime Time = DateTime.Now;

        // A record of an object already recorded in this step merges into it, unless a structural record touching
        // that object sits between them, so redo replays in order.
        public void Join(Record record)
        {
            if (record is ObjectRecord o)
                for (int i = Records.Count - 1; i >= 0; i--)
                {
                    if (Records[i] is ObjectRecord prev && prev.Key.Same(o.Key)) { prev.Merge(o); return; }
                    if (o.Key.ObjectId != Guid.Empty && Records[i].Touches(o.Key.ObjectId)) break;
                }
            Records.Add(record);
            Groups.Add(record.Group);
        }

        public UndoStepInfo Info => new()
        {
            Id = Id,
            Name = Name,
            Group = Groups.Min(),
            Time = Time,
            OwnerNames = [.. Records.SelectMany(r => r.Owners).Distinct().Where(o => o.Kind != UndoOwner.OwnerKind.None).Select(o => o.DisplayName).Distinct()],
        };
    }

    // ================================================================
    //  Target keys
    // ================================================================

    private sealed class TargetKey
    {
        private enum KeyKind { GameObject, Component, Asset, Weak, Resolver }

        private readonly KeyKind _kind;
        private readonly Guid _id;
        private readonly WeakReference<object>? _weak;
        private readonly Func<object?>? _resolve;

        private TargetKey(KeyKind kind, Guid id, WeakReference<object>? weak = null, Func<object?>? resolve = null)
        {
            _kind = kind;
            _id = id;
            _weak = weak;
            _resolve = resolve;
        }

        public static TargetKey For(object target) => target switch
        {
            GameObject go => new(KeyKind.GameObject, go.Identifier),
            Component c => new(KeyKind.Component, c.Identifier),
            Asset { IsFromDatabase: true } a => new(KeyKind.Asset, a.AssetID),
            _ => Weak(target),
        };

        public static TargetKey Weak(object target) => new(KeyKind.Weak, Guid.Empty, new WeakReference<object>(target));

        public static TargetKey Resolver(Func<object?> resolve) => new(KeyKind.Resolver, Guid.Empty, resolve: resolve);

        /// <summary>The scene object identifier, or empty for anything else.</summary>
        public Guid ObjectId => _kind is KeyKind.GameObject or KeyKind.Component ? _id : Guid.Empty;

        public Guid Id => _id;

        public bool IsAsset => _kind == KeyKind.Asset;

        public object? Resolve(SceneInstance? scene)
        {
            switch (_kind)
            {
                case KeyKind.GameObject: return scene.IsValid() ? scene.FindObject<GameObject>(_id) : null;
                case KeyKind.Component: return scene.IsValid() ? scene.FindObject<Component>(_id) : null;
                case KeyKind.Asset: return AssetDatabase.Get(_id);
                case KeyKind.Resolver: return _resolve!();
                default:
                    if (_weak != null && _weak.TryGetTarget(out object? t) && !(t is EngineObject eo && eo.IsDisposed)) return t;
                    return null;
            }
        }

        public bool Same(TargetKey other)
        {
            if (_kind != other._kind) return false;
            return _kind switch
            {
                KeyKind.Weak => _weak!.TryGetTarget(out object? a) && other._weak!.TryGetTarget(out object? b) && ReferenceEquals(a, b),
                KeyKind.Resolver => ReferenceEquals(this, other),
                _ => _id == other._id,
            };
        }
    }

    // ================================================================
    //  Captures
    // ================================================================

    private enum CaptureMode { GameObject, Members, Whole }

    // Keys that are derived or identity, never diffed or restored.
    private static readonly HashSet<string> s_skippedKeys = ["_identifier", "_enabledInHierarchy", "_prefabTemplateIdentity"];

    private sealed class Capture
    {
        public CaptureMode Mode;
        public readonly Dictionary<string, EchoObject> Values = [];
        public readonly List<Asset> Links = [];

        public static Capture Of(object target)
        {
            var capture = new Capture();
            if (target is GameObject go)
            {
                capture.Mode = CaptureMode.GameObject;
                CaptureGameObject(go, capture.Values);
                return capture;
            }

            SerializedMember[] members = ReflectionUtils.GetSerializedMembers(target);
            if (target is Asset { IsFromDatabase: true } || target is ISerializable || SharesState(target, members))
            {
                capture.Mode = CaptureMode.Whole;
                var ctx = new DependencySerializationContext { LinkRuntimeAssets = true, ExternalReferences = new SceneReferenceResolver(target) };
                capture.Values[""] = Serializer.Serialize(typeof(object), target, ctx);
                capture.Links.AddRange(ctx.LinkedAssets);
                return capture;
            }

            capture.Mode = CaptureMode.Members;
            foreach (SerializedMember member in members)
            {
                if (s_skippedKeys.Contains(member.Key)) continue;
                // Each field alone, so its ids are its own and an asset in it stays a link.
                var ctx = new DependencySerializationContext { LinkRuntimeAssets = true, RootByReference = true, ExternalReferences = new SceneReferenceResolver() };
                object? value;
                try { value = member.GetValue(target); }
                catch { continue; }
                capture.Values[member.Key] = Serializer.Serialize(member.Type, value, ctx);
                capture.Links.AddRange(ctx.LinkedAssets);
            }
            return capture;
        }

        // Two top level fields holding the same plain object must restore as one, so the object is stored whole.
        private static bool SharesState(object target, SerializedMember[] members)
        {
            var seen = new HashSet<object>(ReferenceEqualityComparer.Instance);
            foreach (SerializedMember member in members)
            {
                object? value;
                try { value = member.GetValue(target); }
                catch { continue; }
                if (value == null || value is string || value is EngineObject || value is Type || value.GetType().IsValueType) continue;
                if (!seen.Add(value)) return true;
            }
            return false;
        }
    }

    private static void CaptureGameObject(GameObject go, Dictionary<string, EchoObject> values)
    {
        values["Name"] = new EchoObject(go.Name ?? "");
        values["Enabled"] = new EchoObject(go.Enabled);
        values["TagIndex"] = new EchoObject(go.TagIndex);
        values["LayerIndex"] = new EchoObject(go.LayerIndex);
        values["HideFlags"] = new EchoObject((int)go.HideFlags);
        values["Static"] = new EchoObject(go.IsStatic);
        values["LocalPosition"] = Serializer.Serialize(typeof(Float3), go.Transform.LocalPosition);
        values["LocalRotation"] = Serializer.Serialize(typeof(Quaternion), go.Transform.LocalRotation);
        values["LocalScale"] = Serializer.Serialize(typeof(Float3), go.Transform.LocalScale);
    }

    private static void RestoreGameObject(GameObject go, Dictionary<string, EchoObject> values)
    {
        foreach ((string key, EchoObject value) in values)
            switch (key)
            {
                case "Name": go.Name = value.StringValue ?? ""; break;
                case "Enabled": go.Enabled = value.BoolValue; break;
                case "TagIndex": go.TagIndex = value.IntValue; break;
                case "LayerIndex": go.LayerIndex = value.IntValue; break;
                case "HideFlags": go.HideFlags = (HideFlags)value.IntValue; break;
                case "Static": go.IsStatic = value.BoolValue; break;
                case "LocalPosition": go.Transform.LocalPosition = Serializer.Deserialize<Float3>(value); break;
                case "LocalRotation": go.Transform.LocalRotation = Serializer.Deserialize<Quaternion>(value); break;
                case "LocalScale": go.Transform.LocalScale = Serializer.Deserialize<Float3>(value); break;
            }
        if (go.RectTransform is RectTransform rect) rect.MarkLayoutDirty();
    }

    // ================================================================
    //  Pending entries
    // ================================================================

    private abstract class Entry
    {
        public int Group;
        public string Name = "";

        /// <summary>The finished record, or null when nothing changed.</summary>
        public abstract Record? Finish(List<Entry> flushing);

        /// <summary>Whether this entry changes the structure around the object with this identifier.</summary>
        public virtual bool Touches(Guid id) => false;
    }

    private sealed class PendingObject(object target, TargetKey key, UndoOwner owner, Capture before) : Entry
    {
        public readonly object Target = target;
        public readonly TargetKey Key = key;

        /// <summary>Kept pending across flushes until its owner releases it, for a tool spanning frames.</summary>
        public bool Held;

        public override Record? Finish(List<Entry> flushing)
        {
            if (Target is EngineObject eo && eo.IsDisposed) return null;
            return ObjectRecord.Diff(Target, Key, owner, before, Capture.Of(Target));
        }

        public override bool Touches(Guid id) => Key.ObjectId == id;
    }

    private sealed class ReadyEntry(Record record) : Entry
    {
        public readonly Record Record = record;
        public override Record? Finish(List<Entry> flushing) => Record;
        public override bool Touches(Guid id) => Record.Touches(id);
    }

    private sealed class PendingCreated(GameObject go) : Entry
    {
        public readonly GameObject Go = go;

        public override Record? Finish(List<Entry> flushing)
        {
            if (Go.IsDisposed) return null;
            return CreatedRecord.Capture(Go, StructuralIds(Group, flushing));
        }

        public override bool Touches(Guid id) => Go.IsValid() && Go.Identifier == id;
    }

    private sealed class PendingAddComponent(GameObject go, List<Component> components) : Entry
    {
        public readonly List<Component> Components = components;

        public override Record? Finish(List<Entry> flushing)
        {
            var added = new List<(Guid Id, int Index, MemoryCopy Data)>();
            foreach (Component c in Components)
                if (c.IsValid() && go.IsValid())
                    added.Add((c.Identifier, go._components.IndexOf(c), WriteComponent(c)));
            return added.Count == 0 ? null : new AddComponentRecord(go.Identifier, added);
        }

        public override bool Touches(Guid id) => Components.Any(c => c.Identifier == id);
    }

    private sealed class PendingChildrenOrder(GameObject? parent, SceneInstance? scene) : Entry
    {
        private readonly Guid _parent = parent.IsValid() ? parent.Identifier : Guid.Empty;
        private readonly List<Guid> _before = ChildrenOrderRecord.Order(parent, scene);

        public override Record? Finish(List<Entry> flushing)
        {
            GameObject? live = _parent == Guid.Empty ? null : FindGO(_parent);
            List<Guid> after = ChildrenOrderRecord.Order(live, SceneInstance.Main);
            return after.SequenceEqual(_before) ? null : new ChildrenOrderRecord(_parent, _before, after);
        }

        public override bool Touches(Guid id) => id == _parent || _before.Contains(id);
    }

    private sealed class PendingHierarchy(GameObject root) : Entry
    {
        private readonly HierarchyState _before = HierarchyState.Capture(root);

        public override Record? Finish(List<Entry> flushing)
        {
            GameObject? live = FindGO(_before.RootId);
            HierarchyState? after = live.IsValid() ? HierarchyState.Capture(live) : null;
            if (after != null && after.Data.Data.Equals(_before.Data.Data) && after.Placement == _before.Placement) return null;
            return new HierarchyRecord(_before, after);
        }

        public override bool Touches(Guid id) => _before.Ids.Contains(id);
    }

    private sealed class PendingImporter(Guid asset, EchoObject before) : Entry
    {
        public override Record? Finish(List<Entry> flushing)
        {
            if (Inspector.ImportSettingsEditor.Pending(asset) is not EchoObject after || after.Equals(before)) return null;
            return new ImporterRecord(asset, before, after.Clone());
        }

        public override bool Touches(Guid id) => id == asset;
    }

    // Children and components with their own structural record in the group, left out of a created object's capture.
    private static HashSet<Guid> StructuralIds(int group, List<Entry> flushing)
    {
        var ids = new HashSet<Guid>();
        IEnumerable<Record> records = flushing.Where(e => e.Group == group).OfType<ReadyEntry>().Select(e => e.Record);
        if (_undo.Count > 0 && _undo[^1].Groups.Contains(group)) records = records.Concat(_undo[^1].Records);
        foreach (Record r in records)
            if (r is ParentRecord p) ids.Add(p.Id);
        foreach (Entry e in flushing)
            if (e.Group == group && e is PendingAddComponent add)
                foreach (Component c in add.Components) ids.Add(c.Identifier);
        return ids;
    }

    private static MemoryCopy WriteComponent(Component c)
    {
        var ctx = new DependencySerializationContext { LinkRuntimeAssets = true, ExternalReferences = new SceneReferenceResolver(c) };
        return new MemoryCopy(Serializer.Serialize(typeof(Component), c, ctx), ctx.LinkedAssets);
    }

    // ================================================================
    //  Records
    // ================================================================

    private abstract class Record
    {
        public int Group;
        public string Name = "";

        public virtual IEnumerable<UndoOwner> Owners => [];

        /// <summary>Hands the objects this record brings back to the restore to build before anything applies.</summary>
        public virtual void CollectRecreations(Restore restore) { }

        public abstract void Apply(Restore restore);

        public virtual bool Touches(Guid id) => false;
    }

    private sealed class ObjectRecord : Record
    {
        public readonly TargetKey Key;
        private readonly UndoOwner _owner;
        private readonly CaptureMode _mode;
        public readonly Dictionary<string, EchoObject> Before;
        public readonly Dictionary<string, EchoObject> After;
        private readonly List<Asset> _links;

        private ObjectRecord(TargetKey key, UndoOwner owner, CaptureMode mode, Dictionary<string, EchoObject> before, Dictionary<string, EchoObject> after, List<Asset> links)
        {
            Key = key;
            _owner = owner;
            _mode = mode;
            Before = before;
            After = after;
            _links = links;
        }

        public override IEnumerable<UndoOwner> Owners => [_owner];

        public Guid? AssetId => Key.IsAsset ? Key.Id : null;

        public static ObjectRecord? Diff(object target, TargetKey key, UndoOwner owner, Capture before, Capture after)
        {
            if (before.Mode != after.Mode || after.Mode == CaptureMode.Whole)
            {
                if (before.Mode != CaptureMode.Whole || after.Mode != CaptureMode.Whole) return null;
                EchoObject b = before.Values[""], a = after.Values[""];
                if (b.Equals(a)) return null;
                return new ObjectRecord(key, owner, CaptureMode.Whole, new() { [""] = b }, new() { [""] = a }, [.. before.Links, .. after.Links]);
            }

            var beforeKeys = new Dictionary<string, EchoObject>();
            var afterKeys = new Dictionary<string, EchoObject>();
            foreach ((string k, EchoObject value) in after.Values)
            {
                if (before.Values.TryGetValue(k, out EchoObject? old) && old.Equals(value)) continue;
                if (old != null) beforeKeys[k] = old;
                afterKeys[k] = value;
            }
            if (afterKeys.Count == 0) return null;
            return new ObjectRecord(key, owner, after.Mode, beforeKeys, afterKeys, [.. before.Links, .. after.Links]);
        }

        // Keeps the earliest before value of each key and takes the latest after value.
        public void Merge(ObjectRecord later)
        {
            foreach ((string k, EchoObject value) in later.After)
            {
                if (!After.ContainsKey(k) && later.Before.TryGetValue(k, out EchoObject? b)) Before[k] = b;
                After[k] = value;
            }
            _links.AddRange(later._links);
        }

        public override void Apply(Restore restore)
        {
            object? target = Key.Resolve(restore.Scene);
            if (target == null)
            {
                Runtime.Debug.LogWarning($"'{Name}' could not find what it recorded, so that part was skipped.");
                return;
            }

            Write(target, restore.IsUndo ? Before : After, restore);
            restore.Touched.Add(target);
        }

        private void Write(object target, Dictionary<string, EchoObject> values, Restore restore)
        {
            switch (_mode)
            {
                case CaptureMode.GameObject:
                    RestoreGameObject((GameObject)target, values);
                    return;

                case CaptureMode.Whole:
                    WriteWhole(target, values[""], restore);
                    break;

                default:
                    SerializedMember[] members = ReflectionUtils.GetSerializedMembers(target);
                    foreach ((string key, EchoObject value) in values)
                    {
                        if (key == "_enabled" && target is Component c)
                        {
                            c.Enabled = value.BoolValue;
                            continue;
                        }
                        int index = Array.FindIndex(members, m => m.Key == key);
                        if (index < 0) continue;
                        try
                        {
                            object? restored = Serializer.Deserialize(value, members[index].Type, restore.LinkContext());
                            if (DisposedAssetIn(restored) is Asset gone)
                            {
                                Runtime.Debug.LogWarning($"'{key}' on {target.GetType().Name} held '{gone.Name}', which no longer exists, so it was left as it is.");
                                continue;
                            }
                            members[index].SetValue(target, restored);
                        }
                        catch (Exception ex) { Runtime.Debug.LogWarning($"Undo could not restore '{key}' on {target.GetType().Name}: {ex.Message}"); }
                    }
                    break;
            }

            if (target is EngineObject eo && eo.IsValid())
            {
                try { eo.OnValidate(); }
                catch (Exception ex) { Runtime.Debug.LogException($"OnValidate threw on {eo.GetType().Name}", ex); }
            }
        }

        // A runtime asset the record linked that has been disposed since, directly or in a collection.
        private static Asset? DisposedAssetIn(object? value)
        {
            if (value is Asset { IsDisposed: true } asset) return asset;
            if (value is IEnumerable items and not string)
                foreach (object? item in items)
                    if (item is Asset { IsDisposed: true } inner) return inner;
            return null;
        }

        private static void WriteWhole(object target, EchoObject data, Restore restore)
        {
            if (target is Asset { IsFromDatabase: true } asset)
            {
                AssetDatabase.Refill(asset, data, ReloadReason.Undo);
                return;
            }

            SerializationContext ctx = restore.LinkContext();
            if (target is Component c)
            {
                bool enabled = c._enabled, inHierarchy = c._enabledInHierarchy;
                GameObject.PreservingIdentifiersWhile(() => { Fill(); return 0; });
                bool wanted = c._enabled;
                c._enabled = enabled;
                c._enabledInHierarchy = inHierarchy;
                c.Enabled = wanted;
                return;
            }
            Fill();

            void Fill()
            {
                if (target is ISerializable serializable) serializable.Deserialize(data, ctx);
                else Serializer.DeserializeInto(data, target, ctx);
            }
        }

        public override bool Touches(Guid id) => Key.ObjectId == id || (Key.ObjectId == Guid.Empty && Key.Id == id && id != Guid.Empty);
    }

    private sealed class ImporterRecord(Guid asset, EchoObject before, EchoObject after) : Record
    {
        public override void Apply(Restore restore) => Inspector.ImportSettingsEditor.SetPending(asset, (restore.IsUndo ? before : after).Clone());
        public override bool Touches(Guid id) => id == asset;
    }

    private sealed class OperationRecord(IUndoOperation operation) : Record
    {
        public override IEnumerable<UndoOwner> Owners => operation.Owners;
        public override void Apply(Restore restore)
        {
            if (restore.IsUndo) operation.Undo();
            else operation.Redo();
        }
    }

    // Where a GameObject sits: its parent, or none for a root, and its index among its siblings or the roots.
    private readonly record struct Placement(Guid Parent, int Index)
    {
        public static Placement Of(GameObject go)
            => go.Parent.IsValid()
                ? new Placement(go.Parent.Identifier, go.Parent.Children.IndexOf(go))
                : new Placement(Guid.Empty, go.Scene.IsValid() ? go.SiblingIndex : -1);
    }

    private sealed class CreatedRecord : Record
    {
        private readonly Guid _id;
        private readonly MemoryCopy _data;
        private readonly Placement _placement;
        private readonly HashSet<Guid> _ids;

        private CreatedRecord(Guid id, MemoryCopy data, Placement placement, HashSet<Guid> ids)
        {
            _id = id;
            _data = data;
            _placement = placement;
            _ids = ids;
        }

        public static CreatedRecord Capture(GameObject go, HashSet<Guid> excluded)
        {
            MemoryCopy data = WriteTree(go, excluded, out HashSet<Guid> ids);
            return new CreatedRecord(go.Identifier, data, Placement.Of(go), ids);
        }

        public override IEnumerable<UndoOwner> Owners => [UndoOwner.OfCurrentScene];

        public override void CollectRecreations(Restore restore)
        {
            if (!restore.IsUndo) restore.Prebuild(_data.Data, typeof(GameObject));
        }

        public override void Apply(Restore restore)
        {
            if (restore.IsUndo) restore.DestroyGameObject(_id);
            else restore.AttachGameObject(_id, _placement, select: true);
        }

        public override bool Touches(Guid id) => _ids.Contains(id);
    }

    private sealed class DestroyRecord : Record
    {
        private readonly Guid _id;
        private readonly MemoryCopy _data;
        private readonly Placement _placement;
        private readonly HashSet<Guid> _ids;

        private DestroyRecord(Guid id, MemoryCopy data, Placement placement, HashSet<Guid> ids)
        {
            _id = id;
            _data = data;
            _placement = placement;
            _ids = ids;
        }

        public static DestroyRecord Of(GameObject go) => new(go.Identifier, WriteTree(go, [], out HashSet<Guid> ids), Placement.Of(go), ids);

        public override IEnumerable<UndoOwner> Owners => [UndoOwner.OfCurrentScene];

        public override void CollectRecreations(Restore restore)
        {
            if (restore.IsUndo) restore.Prebuild(_data.Data, typeof(GameObject));
        }

        public override void Apply(Restore restore)
        {
            if (restore.IsUndo) restore.AttachGameObject(_id, _placement, select: true);
            else restore.DestroyGameObject(_id);
        }

        public override bool Touches(Guid id) => _ids.Contains(id);
    }

    private sealed class DestroyComponentRecord : Record
    {
        private readonly Guid _go;
        private readonly Guid _id;
        private readonly int _index;
        private readonly MemoryCopy _data;

        private DestroyComponentRecord(Guid go, Guid id, int index, MemoryCopy data)
        {
            _go = go;
            _id = id;
            _index = index;
            _data = data;
        }

        public static DestroyComponentRecord Of(Component c) => new(c.GameObject.Identifier, c.Identifier, c.GameObject._components.IndexOf(c), WriteComponent(c));

        public override IEnumerable<UndoOwner> Owners => [UndoOwner.OfCurrentScene];

        public override void CollectRecreations(Restore restore)
        {
            if (restore.IsUndo) restore.Prebuild(_data.Data, typeof(Component));
        }

        public override void Apply(Restore restore)
        {
            if (restore.IsUndo) restore.AttachComponent(_id, _go, _index);
            else restore.DestroyComponent(_id);
        }

        public override bool Touches(Guid id) => id == _id || id == _go;
    }

    private sealed class AddComponentRecord(Guid go, List<(Guid Id, int Index, MemoryCopy Data)> added) : Record
    {
        public override IEnumerable<UndoOwner> Owners => [UndoOwner.OfCurrentScene];

        public override void CollectRecreations(Restore restore)
        {
            if (!restore.IsUndo)
                foreach ((Guid _, int _, MemoryCopy data) in added) restore.Prebuild(data.Data, typeof(Component));
        }

        public override void Apply(Restore restore)
        {
            if (restore.IsUndo)
                for (int i = added.Count - 1; i >= 0; i--) restore.DestroyComponent(added[i].Id);
            else
                foreach ((Guid id, int index, MemoryCopy _) in added) restore.AttachComponent(id, go, index);
        }

        public override bool Touches(Guid id) => id == go || added.Any(a => a.Id == id);
    }

    private sealed class ParentRecord : Record
    {
        public readonly Guid Id;
        private readonly Placement _before;
        private readonly (Float3 Pos, Quaternion Rot, Float3 Scale) _beforeLocal;
        private Placement _after;
        private (Float3 Pos, Quaternion Rot, Float3 Scale) _afterLocal;

        public ParentRecord(GameObject go)
        {
            Id = go.Identifier;
            _before = Placement.Of(go);
            _beforeLocal = (go.Transform.LocalPosition, go.Transform.LocalRotation, go.Transform.LocalScale);
        }

        public void CaptureAfter(GameObject go)
        {
            _after = Placement.Of(go);
            _afterLocal = (go.Transform.LocalPosition, go.Transform.LocalRotation, go.Transform.LocalScale);
        }

        public override IEnumerable<UndoOwner> Owners => [UndoOwner.OfCurrentScene];

        public override void Apply(Restore restore)
        {
            if (restore.Find(Id) is not GameObject go)
            {
                Runtime.Debug.LogWarning($"'{Name}' could not find the object it moved, so that part was skipped.");
                return;
            }

            (Placement place, (Float3 Pos, Quaternion Rot, Float3 Scale) local) = restore.IsUndo ? (_before, _beforeLocal) : (_after, _afterLocal);
            restore.Place(go, place);
            go.Transform.LocalPosition = local.Pos;
            go.Transform.LocalRotation = local.Rot;
            go.Transform.LocalScale = local.Scale;
        }

        public override bool Touches(Guid id) => id == Id || id == _before.Parent || id == _after.Parent;
    }

    private sealed class ChildrenOrderRecord(Guid parent, List<Guid> before, List<Guid> after) : Record
    {
        public static List<Guid> Order(GameObject? parent, SceneInstance? scene)
        {
            if (parent.IsValid()) return [.. parent.Children.Select(c => c.Identifier)];
            return scene.IsValid() ? [.. scene.Roots.Select(r => r.Identifier)] : [];
        }

        public override IEnumerable<UndoOwner> Owners => [UndoOwner.OfCurrentScene];

        public override void Apply(Restore restore)
        {
            List<Guid> order = restore.IsUndo ? before : after;
            GameObject? p = parent == Guid.Empty ? null : restore.Find(parent) as GameObject;
            for (int i = 0; i < order.Count; i++)
            {
                if (restore.Find(order[i]) is not GameObject child) continue;
                if (p.IsValid()) { if (ReferenceEquals(child.Parent, p)) child.SetSiblingIndex(i); }
                else if (child.Parent.IsNotValid() && restore.Scene.IsValid()) child.SiblingIndex = i;
            }
        }

        public override bool Touches(Guid id) => id == parent || before.Contains(id);
    }

    // A whole tree with its prefab data, and where it sits.
    private sealed class HierarchyState
    {
        public Guid RootId;
        public MemoryCopy Data = null!;
        public Placement Placement;
        public HashSet<Guid> Ids = [];

        public static HierarchyState Capture(GameObject root)
        {
            var state = new HierarchyState { RootId = root.Identifier, Placement = Placement.Of(root) };
            state.Data = WriteTree(root, [], out state.Ids);
            return state;
        }
    }

    // Undo and redo rebuild the tree whole from the recorded state, keeping every identifier, so objects added
    // since are gone, ones removed since are back, and the prefab data matches.
    private sealed class HierarchyRecord(HierarchyState before, HierarchyState? after) : Record
    {
        public override IEnumerable<UndoOwner> Owners => [UndoOwner.OfCurrentScene];

        public override void Apply(Restore restore)
        {
            HierarchyState? target = restore.IsUndo ? before : after;
            Guid rootId = before.RootId;
            GameObject? live = restore.Find(rootId) as GameObject;
            Placement placement = live.IsValid() ? Placement.Of(live) : (target ?? before).Placement;
            bool selected = live.IsValid() && Selection.IsSelected(live);

            if (live.IsValid()) restore.DestroyGameObject(rootId);
            if (target == null) return;

            restore.BuildNow(target.Data.Data, typeof(GameObject));
            restore.AttachGameObject(rootId, placement, select: selected);
        }

        public override bool Touches(Guid id) => before.Ids.Contains(id) || (after?.Ids.Contains(id) ?? false);
    }

    // Writes a tree by value, leaving out the listed children and components, and linking everything else.
    private static MemoryCopy WriteTree(GameObject root, HashSet<Guid> excluded, out HashSet<Guid> ids)
    {
        var written = new List<object>();
        var writtenIds = new HashSet<Guid>();
        Collect(root);
        ids = writtenIds;

        var ctx = new DependencySerializationContext { LinkRuntimeAssets = true, ExternalReferences = new SceneReferenceResolver([.. written]) };
        EchoObject data = Serializer.Serialize(typeof(object), root, ctx);
        if (excluded.Count > 0) StripLinkedMembers(data);
        return new MemoryCopy(data, ctx.LinkedAssets);

        void Collect(GameObject go)
        {
            written.Add(go);
            written.Add(go.Transform);
            writtenIds.Add(go.Identifier);
            foreach (Component c in go._components)
                if (!excluded.Contains(c.Identifier)) { written.Add(c); writtenIds.Add(c.Identifier); }
            foreach (GameObject child in go.Children)
                if (!excluded.Contains(child.Identifier)) Collect(child);
        }
    }

    // A left out child or component is written as a link inside its parent's lists. It is dropped from them, so
    // the fill never parents a live object raw, and its own record puts it back.
    private static void StripLinkedMembers(EchoObject node)
    {
        if (node.TagType == EchoType.List)
        {
            foreach (EchoObject item in node.List) StripLinkedMembers(item);
            return;
        }
        if (node.TagType != EchoType.Compound) return;

        foreach (string list in (string[])["Children", "Components"])
            if (node.TryGet(list, out EchoObject? items) && items!.TagType == EchoType.List)
                items.List.RemoveAll(IsLink);

        foreach (string name in node.GetNames().ToList())
            if (node.TryGet(name, out EchoObject? child)) StripLinkedMembers(child!);

        static bool IsLink(EchoObject e) => e.TagType == EchoType.Compound && e.TryGet("$extern", out _);
    }

    // ================================================================
    //  Restore
    // ================================================================

    // One step's restore. Objects the step brings back are built outside the scene first, all of them before any
    // is filled, so links between them resolve whatever order the records come in. Then the records apply and
    // attach them through the ordinary paths, so lifecycle fires once on finished objects.
    private sealed class Restore : IExternalReferenceResolver
    {
        public readonly bool IsUndo;
        public readonly SceneInstance? Scene = Runtime.Resources.SceneInstance.Main;
        public readonly List<object> Touched = [];

        private readonly Dictionary<Guid, EngineObject> _built = [];
        private readonly List<EngineObject> _builtRoots = [];
        private readonly HashSet<EngineObject> _attached = new(ReferenceEqualityComparer.Instance);
        private readonly List<(EchoObject Data, Type Type, Dictionary<int, object> Ids)> _fills = [];
        private readonly HashSet<Guid> _recreated = [];

        public Restore(bool undo) => IsUndo = undo;

        public SerializationContext LinkContext() => new() { ExternalReferences = this };

        /// <summary>Builds an object the step brings back, outside the scene. Its links to objects built later are set by the second fill.</summary>
        public void Prebuild(EchoObject data, Type type)
        {
            SerializationContext ctx = LinkContext();
            EngineObject? built = GameObject.PreservingIdentifiersWhile(() => Serializer.Deserialize(data, type, ctx) as EngineObject);
            if (ReferenceEquals(built, null)) throw new InvalidOperationException("A recorded object could not be rebuilt.");

            Guid id = IdOf(built);
            if (_built.ContainsKey(id))
            {
                built.Dispose();
                return;
            }

            Register(built);
            _builtRoots.Add(built);
            var ids = new Dictionary<int, object>();
            foreach ((int key, object value) in ctx.idToObject)
                if (value is EngineObject or Transform) ids[key] = value;
            _fills.Add((data, type, ids));
        }

        /// <summary>Fills every prebuilt object again with every other one in place, so every link between them resolves.</summary>
        public void FillPrebuilt()
        {
            foreach ((EchoObject data, Type type, Dictionary<int, object> ids) in _fills)
            {
                SerializationContext ctx = LinkContext();
                foreach ((int key, object value) in ids) ctx.ReadInto(key, value);
                GameObject.PreservingIdentifiersWhile(() => Serializer.Deserialize(data, type, ctx));
            }
            _fills.Clear();
        }

        /// <summary>Builds and fills one tree at once, for a record that only knows what it needs at its turn.</summary>
        public void BuildNow(EchoObject data, Type type)
        {
            if (_built.ContainsKey(Guid.Empty)) _built.Remove(Guid.Empty);
            SerializationContext ctx = LinkContext();
            EngineObject? built = GameObject.PreservingIdentifiersWhile(() => Serializer.Deserialize(data, type, ctx) as EngineObject);
            if (ReferenceEquals(built, null)) throw new InvalidOperationException("A recorded object could not be rebuilt.");
            if (_built.TryGetValue(IdOf(built), out EngineObject? old) && !_attached.Contains(old)) old.Dispose();
            Register(built);
            _builtRoots.Add(built);
        }

        private static Guid IdOf(EngineObject obj) => obj switch { GameObject go => go.Identifier, Component c => c.Identifier, _ => Guid.Empty };

        private void Register(EngineObject obj)
        {
            if (obj is GameObject go)
            {
                _built[go.Identifier] = go;
                foreach (Component c in go._components) _built[c.Identifier] = c;
                foreach (GameObject child in go.Children) Register(child);
            }
            else if (obj is Component c) _built[c.Identifier] = c;
        }

        /// <summary>A prebuilt object, or the live one in the step's scene.</summary>
        public EngineObject? Find(Guid id)
        {
            if (_built.TryGetValue(id, out EngineObject? built) && built.IsValid()) return built;
            return Scene.IsValid() ? Scene.FindObject<EngineObject>(id) : null;
        }

        public void AttachGameObject(Guid id, Placement placement, bool select)
        {
            if (!_built.TryGetValue(id, out EngineObject? obj) || obj is not GameObject go || _attached.Contains(go))
            {
                Runtime.Debug.LogWarning("Undo could not bring back an object it recorded, so that part was skipped.");
                return;
            }

            go.ClearHierarchyState();
            Place(go, placement);
            _attached.Add(go);
            MarkRecreated(go);
            Touched.Add(go);
            if (select) Selection.Select(go);
        }

        public void AttachComponent(Guid id, Guid goId, int index)
        {
            if (!_built.TryGetValue(id, out EngineObject? obj) || obj is not Component c || _attached.Contains(c) || Find(goId) is not GameObject go)
            {
                Runtime.Debug.LogWarning("Undo could not bring back a component it recorded, so that part was skipped.");
                return;
            }

            c._enabledInHierarchy = false;
            go.AttachComponent(c, index);
            _attached.Add(c);
            _recreated.Add(c.Identifier);
            Touched.Add(c);
        }

        // Puts a GameObject under its parent at its index, or among the scene's roots, without moving it in world space.
        public void Place(GameObject go, Placement placement)
        {
            GameObject? parent = placement.Parent == Guid.Empty ? null : Find(placement.Parent) as GameObject;
            if (parent.IsValid())
            {
                go.SetParent(parent, worldPositionStays: false);
                if (placement.Index >= 0) go.SetSiblingIndex(placement.Index);
                return;
            }

            if (go.Parent.IsValid()) go.SetParent(null!, worldPositionStays: false);
            if (!Scene.IsValid()) return;
            if (go.Scene != Scene)
            {
                go.RecomputeHierarchyState();
                Scene.Add(go);
            }
            if (placement.Index >= 0) go.SiblingIndex = placement.Index;
        }

        public void DestroyGameObject(Guid id)
        {
            if (Find(id) is not GameObject go)
            {
                Runtime.Debug.LogWarning("Undo could not find an object it should remove, so that part was skipped.");
                return;
            }
            DestroyNow(go);
        }

        public void DestroyComponent(Guid id)
        {
            if (Find(id) is not Component c || c.GameObject.IsNotValid())
            {
                Runtime.Debug.LogWarning("Undo could not find a component it should remove, so that part was skipped.");
                return;
            }
            DestroyComponentNow(c);
        }

        private void MarkRecreated(GameObject go)
        {
            _recreated.Add(go.Identifier);
            foreach (Component c in go._components) _recreated.Add(c.Identifier);
            foreach (GameObject child in go.Children) MarkRecreated(child);
        }

        /// <summary>Rebinds every live reference to what the step brought back, and disposes what no record attached.</summary>
        public void Finish()
        {
            if (_recreated.Count > 0 && Scene.IsValid()) ReferenceRebinder.Rebind(Scene, _recreated);
            DisposeUnattached();
        }

        public void DisposeUnattached()
        {
            foreach (EngineObject root in _builtRoots)
                if (!_attached.Contains(root) && !IsInsideAttached(root) && root.IsValid())
                    root.Dispose();
            _builtRoots.Clear();
        }

        private bool IsInsideAttached(EngineObject obj)
            => obj is GameObject go && go.Scene.IsValid() || obj is Component c && c.GameObject.IsValid() && c.GameObject.Scene.IsValid();

        object? IExternalReferenceResolver.GetReferenceKey(object value) => null;

        object? IExternalReferenceResolver.ResolveReference(object key, Type targetType)
        {
            if (key is not Guid id) return null;
            EngineObject? found = Find(id);
            if (typeof(Transform).IsAssignableFrom(targetType))
                return found is GameObject go ? go.Transform : null;
            return found;
        }
    }

    // ================================================================
    //  Rebinding
    // ================================================================

    // Points every live reference to an object a step brought back at the live instance, the way an undone
    // destruction gives the same object back to everything that held it.
    internal static class ReferenceRebinder
    {
        public static void Rebind(SceneInstance scene, HashSet<Guid> recreated)
        {
            var live = new Dictionary<Guid, EngineObject>();
            foreach (GameObject go in scene.Objects)
            {
                if (recreated.Contains(go.Identifier)) live[go.Identifier] = go;
                foreach (Component c in go._components)
                    if (recreated.Contains(c.Identifier)) live[c.Identifier] = c;
            }
            if (live.Count == 0) return;

            object? Map(object value)
            {
                switch (value)
                {
                    case GameObject go when live.TryGetValue(go.Identifier, out EngineObject? l) && !ReferenceEquals(l, go): return l;
                    case Component c when live.TryGetValue(c.Identifier, out EngineObject? l) && !ReferenceEquals(l, c): return l;
                    case Transform t when t.GameObject is GameObject owner && live.TryGetValue(owner.Identifier, out EngineObject? l)
                                          && l is GameObject lg && !ReferenceEquals(lg, owner):
                        return lg.Transform;
                    default: return value;
                }
            }

            foreach (GameObject go in scene.Objects.ToList())
                foreach (Component c in go._components.ToList())
                {
                    var visited = new HashSet<object>(ReferenceEqualityComparer.Instance) { c };
                    if (!RewriteMembers(c, Map, visited)) continue;
                    try { c.OnValidate(); }
                    catch (Exception ex) { Runtime.Debug.LogException($"OnValidate threw on {c.GetType().Name}", ex); }
                }
        }

        // Rewrites every reference held in the object's serialized members. True when anything changed.
        internal static bool RewriteMembers(object owner, Func<object, object?> map, HashSet<object> visited)
        {
            bool changed = false;
            foreach (SerializedMember member in ReflectionUtils.GetSerializedMembers(owner))
            {
                object? value;
                try { value = member.GetValue(owner); }
                catch { continue; }
                object? rewritten = Rewrite(value, map, visited, ref changed, out bool replaced);
                if (replaced)
                {
                    try { member.SetValue(owner, rewritten); }
                    catch { }
                }
            }
            return changed;
        }

        private static object? Rewrite(object? value, Func<object, object?> map, HashSet<object> visited, ref bool changed, out bool replaced)
        {
            replaced = false;
            if (value == null || value is string || value is Asset || value is Type || value is Delegate) return value;

            if (value is EngineObject || value is Transform)
            {
                object? mapped = map(value);
                if (ReferenceEquals(mapped, value)) return value;
                replaced = true;
                changed = true;
                return mapped;
            }

            Type type = value.GetType();
            if (type.IsPrimitive || type.IsEnum) return value;

            if (type.IsValueType)
            {
                bool inner = false;
                object boxed = value;
                RewriteMembersInto(boxed, map, visited, ref inner);
                if (!inner) return value;
                changed = true;
                replaced = true;
                return boxed;
            }

            if (!visited.Add(value)) return value;

            switch (value)
            {
                case Array array:
                    for (int i = 0; i < array.Length; i++)
                    {
                        object? item = Rewrite(array.GetValue(i), map, visited, ref changed, out bool r);
                        if (r) array.SetValue(item, i);
                    }
                    return value;

                case IDictionary dict:
                {
                    var entries = new List<(object Key, object? Value)>();
                    bool any = false;
                    foreach (DictionaryEntry e in dict)
                    {
                        object k = Rewrite(e.Key, map, visited, ref changed, out bool rk)!;
                        object? v = Rewrite(e.Value, map, visited, ref changed, out bool rv);
                        any |= rk || rv;
                        entries.Add((k, v));
                    }
                    if (any)
                    {
                        dict.Clear();
                        foreach ((object k, object? v) in entries) dict[k] = v;
                    }
                    return value;
                }

                case IList list:
                    for (int i = 0; i < list.Count; i++)
                    {
                        object? item = Rewrite(list[i], map, visited, ref changed, out bool r);
                        if (r) list[i] = item;
                    }
                    return value;
            }

            if (type.IsGenericType && type.GetGenericTypeDefinition() == typeof(HashSet<>))
            {
                var items = new List<object?>();
                bool any = false;
                foreach (object? item in (IEnumerable)value)
                {
                    items.Add(Rewrite(item, map, visited, ref changed, out bool r));
                    any |= r;
                }
                if (any)
                {
                    type.GetMethod("Clear")!.Invoke(value, null);
                    var add = type.GetMethod("Add")!;
                    foreach (object? item in items) add.Invoke(value, [item]);
                }
                return value;
            }

            if (value is IEnumerable) return value;

            RewriteMembersInto(value, map, visited, ref changed);
            return value;
        }

        private static void RewriteMembersInto(object owner, Func<object, object?> map, HashSet<object> visited, ref bool changed)
        {
            if (RewriteMembers(owner, map, visited)) changed = true;
        }
    }
}
