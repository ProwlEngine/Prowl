using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;

using Prowl.Echo;
using Prowl.Editor.GUI.SceneView;
using Prowl.Runtime;
using Prowl.Runtime.Resources;
using Prowl.Vector;

namespace Prowl.Editor;

/// <summary>
/// Central undo/redo system for the editor.
/// Supports property changes (via Echo serialization), structural changes (create/delete GO),
/// and generic action-based undo via lambdas.
/// </summary>
public static class Undo
{
    // ================================================================
    //  Data Structures
    // ================================================================

    private abstract class UndoRecord
    {
        public abstract void PerformUndo();
        public abstract void PerformRedo();
    }

    /// <summary>
    /// Records a property change on a serializable object (typically MonoBehaviour).
    /// Stores before/after EchoObject snapshots. Restores by copying fields onto the live object.
    /// Tracks targets by Identifier (Guid) so records survive destroy/recreate cycles.
    /// </summary>
    private class PropertyRecord : UndoRecord
    {
        public Type TargetType;
        public EchoObject BeforeState;
        public EchoObject AfterState;

        // Identifier-based tracking for MonoBehaviour targets
        public Guid ComponentIdentifier;
        // Fallback for non-MonoBehaviour targets (plain objects)
        public WeakReference<object>? FallbackRef;

        public PropertyRecord(object target, EchoObject before, EchoObject after)
        {
            TargetType = target.GetType();
            BeforeState = before;
            AfterState = after;

            if (target is MonoBehaviour mb)
                ComponentIdentifier = mb.Identifier;
            else
            {
                ComponentIdentifier = Guid.Empty;
                FallbackRef = new WeakReference<object>(target);
            }
        }

        public override void PerformUndo() => RestoreState(BeforeState);
        public override void PerformRedo() => RestoreState(AfterState);

        public object? ResolveTarget()
        {
            if (ComponentIdentifier != Guid.Empty)
                return FindComponentByIdentifier(ComponentIdentifier);
            if (FallbackRef != null && FallbackRef.TryGetTarget(out var fallback))
            {
                if (fallback is EngineObject eo && eo.IsDisposed) return null;
                return fallback;
            }
            return null;
        }

        private void RestoreState(EchoObject state)
        {
            var target = ResolveTarget();
            if (target == null) return;

            CopyFieldsFromEcho(target, TargetType, state);
        }
    }

    /// <summary>
    /// Records an arbitrary undoable action with explicit undo/redo lambdas.
    /// Used for GO header fields, transform changes, structural operations, etc.
    /// </summary>
    private class ActionRecord : UndoRecord
    {
        public Action UndoAction;
        public Action RedoAction;

        public ActionRecord(Action undo, Action redo)
        {
            UndoAction = undo;
            RedoAction = redo;
        }

        public override void PerformUndo() => UndoAction();
        public override void PerformRedo() => RedoAction();
    }

    /// <summary>
    /// One user-visible undo step. May contain multiple records
    /// (e.g., editing multiple selected objects in one frame).
    /// </summary>
    private class UndoStep
    {
        public string Description;
        public List<UndoRecord> Records;
        /// <summary>True if this step contains ONLY PropertyRecords and can be coalesced with the next.</summary>
        public bool IsCoalescable;
        /// <summary>When this step was created/last coalesced (milliseconds).</summary>
        public long Timestamp;

        public UndoStep(string description, List<UndoRecord> records, bool isCoalescable = false)
        {
            Description = description;
            Records = records;
            IsCoalescable = isCoalescable;
            Timestamp = Environment.TickCount64;
        }
    }

    // ================================================================
    //  State
    // ================================================================

    private static readonly List<UndoStep> _undoStack = new();
    private static readonly List<UndoStep> _redoStack = new();

    // Per-frame pending snapshots: target → beforeState (captured at start of draw, before any mutations)
    private static readonly Dictionary<object, EchoObject> _pendingSnapshots = new();

    // Immediate action records accumulated this frame (RegisterAction calls)
    private static readonly List<(string description, UndoRecord record)> _pendingActions = new();

    // Deferred created/destroy objects serialized at FlushFrame so components added after registration are captured
    private static readonly List<(GameObject go, string description, bool isCreate)> _pendingStructural = new();

    // Continuous operation state (gizmo drag) tracks by Identifier, not reference
    private static bool _isContinuous;
    private static string _continuousDescription = "";
    private static List<(Guid goId, Float3 pos, Quaternion rot, Float3 scale)>? _continuousStartState;

    // ================================================================
    //  Configuration
    // ================================================================

    public static int MaxSteps { get; set; } = 100;

    // ================================================================
    //  Events
    // ================================================================

    /// <summary>Fires after an undo or redo operation completes.</summary>
    public static event Action? OnUndoRedo;

    // ================================================================
    //  State Queries
    // ================================================================

    public static bool CanUndo => _undoStack.Count > 0;
    public static bool CanRedo => _redoStack.Count > 0;
    public static bool IsContinuous => _isContinuous;

    public static string UndoDescription => _undoStack.Count > 0
        ? $"Undo {_undoStack[^1].Description}"
        : "Undo";

    public static string RedoDescription => _redoStack.Count > 0
        ? $"Redo {_redoStack[^1].Description}"
        : "Redo";

    // ================================================================
    //  Property Recording (for MonoBehaviour / plain objects via PropertyGrid)
    // ================================================================

    /// <summary>
    /// Snapshot an object's current state for undo. Call at the TOP of PropertyGrid.Draw()
    /// or custom editor OnGUI(), BEFORE any widgets draw this captures the state before
    /// any in-place mutations (nested objects, collections, curves, etc.).
    /// No-op if already snapshotted this frame for the same target.
    /// </summary>
    public static void Snapshot(object target)
    {
        if (Application.IsPlaying) return;
        if (target == null) return;
        if (_isContinuous) return;

        if (!_pendingSnapshots.ContainsKey(target))
        {
            var before = Serializer.Serialize(target.GetType(), target);
            _pendingSnapshots[target] = before;
        }
    }

    // ================================================================
    //  Generic Action Recording
    // ================================================================

    /// <summary>
    /// Register an undoable action with explicit undo/redo lambdas.
    /// </summary>
    public static void RegisterAction(string description, Action undo, Action redo)
    {
        if (Application.IsPlaying) return;

        _pendingActions.Add((description, new ActionRecord(undo, redo)));
    }

    /// <summary>
    /// Records a property change on a GameObject, looking the GO back up by Identifier on undo/redo
    /// so the record survives destroy/recreate cycles. Apply receives the live GO and the value to assign.
    /// Caller is still responsible for the immediate write — this only registers the undo entry.
    /// </summary>
    public static void RecordGameObjectChange<T>(GameObject go, string description, T oldValue, T newValue, Action<GameObject, T> apply, bool coalesce = false)
    {
        Guid id = go.Identifier;
        Action undo = () => { var g = FindGO(id); if (g != null) apply(g, oldValue); };
        Action redo = () => { var g = FindGO(id); if (g != null) apply(g, newValue); };
        if (coalesce) RegisterCoalescableAction(description, undo, redo);
        else RegisterAction(description, undo, redo);
    }

    /// <summary>
    /// Register an undoable action that coalesces with the previous action if it has the
    /// same description and is within the time window. Use for continuous text input (Name fields, etc.).
    /// The undo lambda is kept from the FIRST action; the redo lambda is updated to the LATEST.
    /// </summary>
    public static void RegisterCoalescableAction(string description, Action undo, Action redo)
    {
        if (Application.IsPlaying) return;

        // Check if we can coalesce with the top of the undo stack
        if (_pendingActions.Count == 0 && _undoStack.Count > 0)
        {
            var prev = _undoStack[^1];
            if (prev.Description == description
                && prev.Records.Count == 1
                && prev.Records[0] is ActionRecord
                && Environment.TickCount64 - prev.Timestamp <= CoalesceWindowMs)
            {
                // Update redo to latest value, keep original undo
                prev.Records[0] = new ActionRecord(((ActionRecord)prev.Records[0]).UndoAction, redo);
                prev.Timestamp = Environment.TickCount64;
                _redoStack.Clear();
                return;
            }
        }

        // Can't coalesce push as normal action but mark as coalescable
        _pendingActions.Add((description, new ActionRecord(undo, redo)));
    }

    // ================================================================
    //  Structural Operations
    // ================================================================

    /// <summary>
    /// Register that a GameObject was just created. Undo will destroy it, redo will recreate it.
    /// Serialization is deferred to FlushFrame so components added after this call are captured.
    /// Call AFTER the GO has been added to the scene and parented.
    /// </summary>
    public static void RegisterCreatedObject(GameObject go, string description)
    {
        if (Application.IsPlaying) return;
        if (go == null) return;

        _pendingStructural.Add((go, description, isCreate: true));
    }

    private static void FlushCreatedObject(GameObject go, string description)
    {
        // Now serialize all components have been added by this point
        var serialized = Serializer.Serialize(typeof(object), go);
        var goId = go.Identifier;
        var parentId = go.Parent?.Identifier ?? Guid.Empty;
        var siblingIndex = go.Parent != null ? go.Parent.Children.IndexOf(go) : -1;

        _pendingActions.Add((description, new ActionRecord(
            undo: () =>
            {
                var scene = Scene.Current;
                if (scene == null) return;
                var target = FindGameObjectByIdentifier(scene, goId);
                if (target == null) return;

                if (Selection.IsSelected(target))
                    Selection.Clear();

                foreach (var child in target.GetChildrenDeep().ToList())
                    scene.Remove(child);
                scene.Remove(target);
                target.Dispose();
            },
            redo: () =>
            {
                var scene = Scene.Current;
                if (scene == null) return;

                var restored = Serializer.Deserialize<GameObject>(serialized);
                if (restored == null) return;
                RestoreIdentifiers(restored, serialized);

                scene.Add(restored);
                if (parentId != Guid.Empty)
                {
                    var parent = FindGameObjectByIdentifier(scene, parentId);
                    if (parent != null)
                    {
                        restored.SetParent(parent);
                        if (siblingIndex >= 0 && siblingIndex < parent.Children.Count)
                            restored.SetSiblingIndex(siblingIndex);
                    }
                }

                Selection.Select(restored);
                EditorSceneManager.IsDirty = true;
            })));
    }

    /// <summary>
    /// Register that a GameObject is about to be destroyed. Undo will recreate it, redo will destroy it.
    /// Call BEFORE the GO is removed from the scene.
    /// </summary>
    public static void RegisterDestroyObject(GameObject go, string description)
    {
        if (Application.IsPlaying) return;
        if (go == null) return;

        // Serialize the entire GO tree before destruction
        var serialized = Serializer.Serialize(typeof(object), go);
        var parentId = go.Parent?.Identifier ?? Guid.Empty;
        var siblingIndex = go.Parent != null ? go.Parent.Children.IndexOf(go) : -1;
        var goId = go.Identifier;

        RegisterAction(description,
            undo: () =>
            {
                var scene = Scene.Current;
                if (scene == null) return;

                var restored = Serializer.Deserialize<GameObject>(serialized);
                if (restored == null) return;
                RestoreIdentifiers(restored, serialized);

                scene.Add(restored);
                if (parentId != Guid.Empty)
                {
                    var parent = FindGameObjectByIdentifier(scene, parentId);
                    if (parent != null)
                    {
                        restored.SetParent(parent);
                        if (siblingIndex >= 0 && siblingIndex < parent.Children.Count)
                            restored.SetSiblingIndex(siblingIndex);
                    }
                }

                Selection.Select(restored);
                EditorSceneManager.IsDirty = true;
            },
            redo: () =>
            {
                var scene = Scene.Current;
                if (scene == null) return;
                var target = FindGameObjectByIdentifier(scene, goId);
                if (target == null) return;

                if (Selection.IsSelected(target))
                    Selection.Clear();

                foreach (var child in target.GetChildrenDeep().ToList())
                    scene.Remove(child);
                scene.Remove(target);
                target.Dispose();
                EditorSceneManager.IsDirty = true;
            });
    }

    // ================================================================
    //  Continuous Operations (Gizmo Drag)
    // ================================================================

    /// <summary>
    /// Begin a continuous undo operation (e.g., gizmo drag).
    /// Snapshots all targets' transforms at the start. Call EndContinuous when done.
    /// </summary>
    public static void BeginContinuous(GameObject[] targets, string description)
    {
        if (Application.IsPlaying) return;
        if (_isContinuous) return;

        _isContinuous = true;
        _continuousDescription = description;
        _continuousStartState = new List<(Guid, Float3, Quaternion, Float3)>();

        foreach (var go in targets)
        {
            if (go == null) continue;
            _continuousStartState.Add((go.Identifier, go.Transform.LocalPosition, go.Transform.LocalRotation, go.Transform.LocalScale));
        }
    }

    /// <summary>
    /// End a continuous operation and push one undo step if anything changed.
    /// </summary>
    public static void EndContinuous()
    {
        if (!_isContinuous || _continuousStartState == null) return;

        var records = new List<UndoRecord>();

        foreach (var (goId, startPos, startRot, startScale) in _continuousStartState)
        {
            var go = FindGO(goId);
            if (go == null) continue;

            var endPos = go.Transform.LocalPosition;
            var endRot = go.Transform.LocalRotation;
            var endScale = go.Transform.LocalScale;

            if (startPos.Equals(endPos) && startRot == endRot && startScale.Equals(endScale))
                continue;

            var capturedId = goId;
            var sPos = startPos; var sRot = startRot; var sScale = startScale;
            var ePos = endPos; var eRot = endRot; var eScale = endScale;

            records.Add(new ActionRecord(
                undo: () =>
                {
                    var g = FindGO(capturedId);
                    if (g == null) return;
                    g.Transform.LocalPosition = sPos;
                    g.Transform.LocalRotation = sRot;
                    g.Transform.LocalScale = sScale;
                },
                redo: () =>
                {
                    var g = FindGO(capturedId);
                    if (g == null) return;
                    g.Transform.LocalPosition = ePos;
                    g.Transform.LocalRotation = eRot;
                    g.Transform.LocalScale = eScale;
                }));
        }

        if (records.Count > 0)
        {
            PushStep(new UndoStep(_continuousDescription, records));
        }

        _isContinuous = false;
        _continuousStartState = null;
    }

    /// <summary>
    /// Cancel a continuous operation without pushing an undo step.
    /// Restores all targets to their start state.
    /// </summary>
    public static void CancelContinuous()
    {
        if (!_isContinuous || _continuousStartState == null) return;

        foreach (var (goId, startPos, startRot, startScale) in _continuousStartState)
        {
            var go = FindGO(goId);
            if (go == null) continue;
            go.Transform.LocalPosition = startPos;
            go.Transform.LocalRotation = startRot;
            go.Transform.LocalScale = startScale;
        }

        _isContinuous = false;
        _continuousStartState = null;
    }

    // ================================================================
    //  Grouping
    // ================================================================

    /// <summary>
    /// Force a new group boundary. Flushes any pending records into a step,
    /// so the next RecordObject starts a fresh step.
    /// </summary>
    public static void IncrementGroup()
    {
        FlushPendingRecords();
    }

    // ================================================================
    //  Execute Undo/Redo
    // ================================================================

    public static void PerformUndo()
    {
        if (Application.IsPlaying) return;

        // If continuous mode is active, cancel it instead of undoing
        if (_isContinuous)
        {
            CancelContinuous();
            return;
        }

        // Flush any pending records first
        FlushPendingRecords();

        if (_undoStack.Count == 0) return;

        var step = _undoStack[^1];
        _undoStack.RemoveAt(_undoStack.Count - 1);

        // Undo in reverse order
        for (int i = step.Records.Count - 1; i >= 0; i--)
        {
            try
            {
                step.Records[i].PerformUndo();
            }
            catch (Exception ex)
            {
                Runtime.Debug.LogWarning($"Undo failed for record {i}: {ex.Message}");
            }
        }

        _redoStack.Add(step);
        TrimStack(_redoStack);

        EditorSceneManager.IsDirty = true;
        OnUndoRedo?.Invoke();
    }

    public static void PerformRedo()
    {
        if (Application.IsPlaying) return;
        if (_isContinuous) return;

        // Flush any pending records first
        FlushPendingRecords();

        if (_redoStack.Count == 0) return;

        var step = _redoStack[^1];
        _redoStack.RemoveAt(_redoStack.Count - 1);

        // Redo in forward order
        for (int i = 0; i < step.Records.Count; i++)
        {
            try
            {
                step.Records[i].PerformRedo();
            }
            catch (Exception ex)
            {
                Runtime.Debug.LogWarning($"Redo failed for record {i}: {ex.Message}");
            }
        }

        _undoStack.Add(step);
        TrimStack(_undoStack);

        EditorSceneManager.IsDirty = true;
        OnUndoRedo?.Invoke();
    }

    // ================================================================
    //  Lifecycle
    // ================================================================

    /// <summary>
    /// Called once per frame by EditorApplication after all UI has been drawn.
    /// Flushes pending property snapshots into undo steps.
    /// </summary>
    internal static void FlushFrame()
    {
        FlushPendingRecords();
    }

    /// <summary>
    /// Clear all undo/redo history. Called on scene load.
    /// </summary>
    public static void Clear()
    {
        _undoStack.Clear();
        _redoStack.Clear();
        _pendingSnapshots.Clear();
        _pendingActions.Clear();
        _pendingStructural.Clear();
        _isContinuous = false;
        _continuousStartState = null;
    }

    // ================================================================
    //  Internal Helpers
    // ================================================================

    private static void FlushPendingRecords()
    {
        // Flush deferred structural operations first (serializes created GOs now that components are added)
        foreach (var (go, desc, isCreate) in _pendingStructural)
        {
            if (go == null || go.IsDisposed) continue;
            if (isCreate)
                FlushCreatedObject(go, desc);
        }
        _pendingStructural.Clear();

        // Flush action records FIRST as separate steps (never merge with property changes)
        // Each action is its own undo step (Add Component, Toggle Enabled, Reparent, etc.)
        bool hasActions = _pendingActions.Count > 0;
        foreach (var (desc, record) in _pendingActions)
            PushStep(new UndoStep(desc, [record], isCoalescable: false));
        _pendingActions.Clear();

        // If explicit actions were registered this frame, discard property snapshots.
        // The actions already handle their changes the Snapshot diff would create duplicates
        // (e.g., Component Enabled toggle: RegisterAction changes _enabled, Snapshot also detects it).
        if (hasActions)
        {
            _pendingSnapshots.Clear();
            return;
        }

        // Build property records from snapshots
        var propertyRecords = new List<PropertyRecord>();

        foreach (var (target, before) in _pendingSnapshots)
        {
            if (target is EngineObject eo && eo.IsDisposed) continue;

            var after = Serializer.Serialize(target.GetType(), target);

            if (!before.Equals(after))
                propertyRecords.Add(new PropertyRecord(target, before, after));
        }
        _pendingSnapshots.Clear();

        // Push property records as a coalescable step (separate from actions)
        if (propertyRecords.Count > 0 && TryCoalesce(propertyRecords))
        {
            _redoStack.Clear();
        }
        else if (propertyRecords.Count > 0)
        {
            PushStep(new UndoStep("Modify Properties", propertyRecords.Cast<UndoRecord>().ToList(), isCoalescable: true));
        }
    }

    /// <summary>
    /// Try to merge new property records into the previous undo step.
    /// Uses time-based coalescing: merges if same targets, continuous edit chain,
    /// and the previous step was created/updated within 300ms.
    /// </summary>
    private const long CoalesceWindowMs = 300;

    private static bool TryCoalesce(List<PropertyRecord> newRecords)
    {
        if (_undoStack.Count == 0) return false;

        var prev = _undoStack[^1];
        if (!prev.IsCoalescable) return false;
        if (prev.Records.Count != newRecords.Count) return false;

        // Time-based: only coalesce within the time window
        long now = Environment.TickCount64;
        if (now - prev.Timestamp > CoalesceWindowMs) return false;

        for (int i = 0; i < newRecords.Count; i++)
        {
            if (prev.Records[i] is not PropertyRecord prevPR) return false;
            var newPR = newRecords[i];

            // Same target? Compare by identifier for MonoBehaviour, by fallback ref for others
            if (prevPR.ComponentIdentifier != Guid.Empty || newPR.ComponentIdentifier != Guid.Empty)
            {
                if (prevPR.ComponentIdentifier != newPR.ComponentIdentifier) return false;
            }
            else
            {
                var prevTarget = prevPR.ResolveTarget();
                var newTarget = newPR.ResolveTarget();
                if (prevTarget == null || newTarget == null || !ReferenceEquals(prevTarget, newTarget)) return false;
            }

            // Continuous edit chain: previous "after" must match new "before"
            if (!prevPR.AfterState.Equals(newPR.BeforeState)) return false;
        }

        // Coalesce: update AfterState and refresh timestamp
        for (int i = 0; i < newRecords.Count; i++)
            ((PropertyRecord)prev.Records[i]).AfterState = newRecords[i].AfterState;
        prev.Timestamp = now;
        return true;
    }

    private static void PushStep(UndoStep step)
    {
        _undoStack.Add(step);
        _redoStack.Clear(); // New action invalidates redo stack
        TrimStack(_undoStack);
    }

    private static void TrimStack(List<UndoStep> stack)
    {
        while (stack.Count > MaxSteps)
            stack.RemoveAt(0);
    }

    // Fields that must never be overwritten by undo they are identity/internal state
    private static readonly HashSet<string> _undoSkipFields = new()
    {
        "_identifier",        // MonoBehaviour identity must be preserved
        "_instanceID",        // EngineObject instance ID
        "_enabledInHierarchy",// Derived state, not user-settable
        "_go",                // GameObject back-reference (not serialized, but just in case)
        "_hasStarted",        // Lifecycle flags
        "_hasBeenEnabled",
        "_executeAlwaysCached",
        "AssetID",            // Asset identity
        "AssetPath",          // Asset path
        "<IsDisposed>k__BackingField", // Disposed state
    };

    /// <summary>
    /// Copy serializable fields from an EchoObject onto a live object.
    /// Deserializes into a temp then copies fields, preserving the live reference.
    /// Skips identity and internal state fields.
    /// </summary>
    private static void CopyFieldsFromEcho(object target, Type type, EchoObject echo)
    {
        try
        {
            var temp = Serializer.Deserialize(echo, type);
            if (temp == null) return;

            // Walk up the hierarchy to get all serializable fields (matching Echo's behavior)
            var currentType = type;
            while (currentType != null && currentType != typeof(object))
            {
                foreach (var field in currentType.GetFields(
                    BindingFlags.Public |
                    BindingFlags.NonPublic |
                    BindingFlags.Instance |
                    BindingFlags.DeclaredOnly))
                {
                    // Skip identity/internal fields
                    if (_undoSkipFields.Contains(field.Name)) continue;

                    bool shouldSerialize = field.IsPublic || field.GetCustomAttribute<Echo.SerializeFieldAttribute>() != null;
                    bool shouldIgnore = field.GetCustomAttribute<Echo.SerializeIgnoreAttribute>() != null
                                     || field.GetCustomAttribute<NonSerializedAttribute>() != null;
                    if (!shouldSerialize || shouldIgnore) continue;

                    try
                    {
                        var value = field.GetValue(temp);
                        field.SetValue(target, value);
                    }
                    catch
                    {
                        // Skip fields that fail
                    }
                }
                currentType = currentType.BaseType;
            }
        }
        catch (Exception ex)
        {
            Runtime.Debug.LogWarning($"Failed to restore object state: {ex.Message}");
        }
    }

    /// <summary>
    /// Recursively restore identifiers on a deserialized GO tree from the stored EchoObject.
    /// </summary>
    internal static void RestoreIdentifiers(GameObject go, EchoObject serializedGO)
    {
        // Restore GO identifier
        var idStr = serializedGO.Get("Identifier")?.StringValue;
        if (Guid.TryParse(idStr, out var goId))
            go.SetIdentifier(goId);

        // Restore component identifiers
        var comps = serializedGO.Get("Components")?.List;
        if (comps != null)
        {
            var liveComps = go.GetComponents().ToArray();
            for (int i = 0; i < Math.Min(comps.Count, liveComps.Length); i++)
            {
                var compEcho = comps[i];
                // MonoBehaviour._identifier has [SerializeField] so it's in the EchoObject
                var compIdField = compEcho.Get("_identifier");
                if (compIdField != null)
                {
                    // _identifier is a Guid serialized by Echo may be stored as string or via type wrapper
                    if (Guid.TryParse(compIdField.StringValue, out var compId))
                        liveComps[i].Identifier = compId;
                    else if (compIdField.TryGet("$value", out var innerVal) && Guid.TryParse(innerVal.StringValue, out var compId2))
                        liveComps[i].Identifier = compId2;
                }
            }
        }

        // Recurse children
        var children = serializedGO.Get("Children")?.List;
        if (children != null)
        {
            for (int i = 0; i < Math.Min(children.Count, go.Children.Count); i++)
                RestoreIdentifiers(go.Children[i], children[i]);
        }
    }

    // ================================================================
    //  Identifier-Based Resolution (for undo lambdas)
    // ================================================================

    /// <summary>
    /// Find a GameObject by identifier in the current scene. Returns null if not found.
    /// Use this in undo/redo lambdas instead of capturing GO references directly.
    /// </summary>
    public static GameObject? FindGO(Guid identifier)
    {
        var scene = Scene.Current;
        if (scene == null) return null;
        foreach (var root in scene.RootObjects)
        {
            var found = root.FindChildByIdentifier(identifier);
            if (found != null) return found;
        }
        return null;
    }

    /// <summary>
    /// Find a MonoBehaviour component by identifier across all GOs in the current scene.
    /// </summary>
    public static MonoBehaviour? FindComponent(Guid identifier)
    {
        var scene = Scene.Current;
        if (scene == null) return null;
        foreach (var go in scene.AllObjects)
        {
            var comp = go.GetComponentByIdentifier(identifier);
            if (comp != null) return comp;
        }
        return null;
    }

    // Private alias used by PropertyRecord
    private static MonoBehaviour? FindComponentByIdentifier(Guid identifier) => FindComponent(identifier);

    private static GameObject? FindGameObjectByIdentifier(Scene scene, Guid identifier) => FindGO(identifier);
}
