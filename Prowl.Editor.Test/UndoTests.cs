// This file is part of the Prowl Game Engine
// Licensed under the MIT License. See the LICENSE file in the project root for details.

using Prowl.Editor.Core;
using Prowl.Editor.GUI.SceneView;
using Prowl.Runtime;
using Prowl.Runtime.Resources;
using Prowl.Vector;

using Xunit;

namespace Prowl.Editor.Test;

/// <summary>A simple component with undoable fields.</summary>
public sealed class UndoComp : Component
{
    public int Value;
    public string Label = "";
}

/// <summary>
/// Tests for the editor <see cref="Undo"/> system - high stakes because a bug here means lost work.
/// Covers property snapshots, action records, structural create/destroy (full restore), identifier
/// survival across destroy/recreate, grouping, continuous (gizmo) operations, history
/// trimming, and play-mode gating. Pending records are flushed via the public <see cref="Undo.IncrementGroup"/>
/// (same path as the per-frame FlushFrame).
/// </summary>
public class UndoTests : EditorTestHarness
{
    public UndoTests() => Undo.Clear(); // static history persists across tests

    private (Scene scene, GameObject go, UndoComp comp) MakeScene()
    {
        var scene = new Scene();
        var go = new GameObject("GO");
        UndoComp comp = go.AddComponent<UndoComp>();
        scene.Add(go);
        Scene.Load(scene);
        Scene.ProcessPendingLoad();
        return (scene, go, comp);
    }

    private static void DestroyGO(Scene s, GameObject go)
    {
        foreach (GameObject? c in go.GetChildrenDeep().ToList())
            s.Remove(c);
        s.Remove(go);
        go.Dispose();
    }

    // ---- Property changes ----

    /// <summary>A component that applies its fields to live state, as the physics and navigation
    /// components do.</summary>
    public sealed class AppliedComp : Component
    {
        public int Value;

        // Not serialized, so a restore cannot write it: only OnValidate can.
        [System.NonSerialized] public int Applied;

        public override void OnValidate() => Applied = Value;
    }

    /// <summary>
    /// Restoring writes fields directly, so a component that pushes its fields into live state has
    /// to be notified the same way an inspector edit notifies it. Without this, an undo restores the
    /// field and leaves the applied state on the value being undone — a collider keeps the shape, a
    /// nav obstacle keeps the hole.
    /// </summary>
    [Fact]
    public void PropertyChange_Undo_NotifiesTheRestoredObject()
    {
        var scene = new Scene();
        var go = new GameObject("GO");
        AppliedComp comp = go.AddComponent<AppliedComp>();
        scene.Add(go);
        Scene.Load(scene);
        Scene.ProcessPendingLoad();

        comp.Value = 10;
        comp.OnValidate();
        Undo.Snapshot(comp);
        comp.Value = 20;
        comp.OnValidate();
        Undo.IncrementGroup();

        Undo.PerformUndo();
        Assert.Equal(10, comp.Value);
        Assert.Equal(10, comp.Applied);

        Undo.PerformRedo();
        Assert.Equal(20, comp.Value);
        Assert.Equal(20, comp.Applied);
    }

    [Fact]
    public void PropertyChange_Undo_Redo()
    {
        (Scene _, GameObject _, UndoComp? comp) = MakeScene();
        comp.Value = 10;
        Undo.Snapshot(comp);
        comp.Value = 20;
        Undo.IncrementGroup();

        Assert.True(Undo.CanUndo);
        Undo.PerformUndo();
        Assert.Equal(10, comp.Value);

        Assert.True(Undo.CanRedo);
        Undo.PerformRedo();
        Assert.Equal(20, comp.Value);
    }

    [Fact]
    public void PropertyChange_MultipleFieldsSameFrame_OneStep()
    {
        (Scene _, GameObject _, UndoComp? comp) = MakeScene();
        comp.Value = 1; comp.Label = "a";
        Undo.Snapshot(comp);
        comp.Value = 2; comp.Label = "b";
        Undo.IncrementGroup();

        Undo.PerformUndo();
        Assert.Equal(1, comp.Value);
        Assert.Equal("a", comp.Label);
        Assert.False(Undo.CanUndo); // single step covered both fields
    }

    [Fact]
    public void NoOpChange_DoesNotCreateStep()
    {
        (Scene _, GameObject _, UndoComp? comp) = MakeScene();
        comp.Value = 5;
        Undo.Snapshot(comp);
        // no mutation
        Undo.IncrementGroup();

        Assert.False(Undo.CanUndo);
    }

    [Fact]
    public void NewAction_ClearsRedoStack()
    {
        (Scene _, GameObject _, UndoComp? comp) = MakeScene();
        comp.Value = 0;
        Undo.Snapshot(comp);
        comp.Value = 1;
        Undo.IncrementGroup();

        Undo.PerformUndo();
        Assert.True(Undo.CanRedo);

        Undo.RegisterAction("New", () => { }, () => { });
        Undo.IncrementGroup();

        Assert.False(Undo.CanRedo);
    }

    // ---- Generic actions ----

    [Fact]
    public void RegisterAction_Undo_Redo()
    {
        MakeScene();
        int x = 1; // simulate the immediate write
        Undo.RegisterAction("A", undo: () => x = 0, redo: () => x = 1);
        Undo.IncrementGroup();

        Undo.PerformUndo();
        Assert.Equal(0, x);
        Undo.PerformRedo();
        Assert.Equal(1, x);
    }

    [Fact]
    public void RegisterActionGroup_OneStep_UndoesAllInReverse()
    {
        MakeScene();
        int x = 11; // both already applied
        var actions = new List<(Action undo, Action redo)>
        {
            (() => x -= 1, () => x += 1),
            (() => x -= 10, () => x += 10),
        };
        Undo.RegisterActionGroup("Group", actions);
        Undo.IncrementGroup();

        Undo.PerformUndo();
        Assert.Equal(0, x); // one step undid both
        Assert.False(Undo.CanUndo);

        Undo.PerformRedo();
        Assert.Equal(11, x);
    }

    // ---- Structural: create ----

    [Fact]
    public void RegisterCreatedObject_Undo_Destroys_Redo_Recreates()
    {
        var scene = new Scene();
        Scene.Load(scene);
        Scene.ProcessPendingLoad();
        Undo.Clear();

        var go = new GameObject("Created");
        go.AddComponent<UndoComp>().Value = 7;
        scene.Add(go);
        Guid goId = go.Identifier;

        Undo.RegisterCreatedObject(go, "Create");
        Undo.IncrementGroup();

        Undo.PerformUndo();
        Assert.Null(Undo.FindGO(goId));

        Undo.PerformRedo();
        GameObject? restored = Undo.FindGO(goId);
        Assert.NotNull(restored);
        Assert.Equal(7, restored!.GetComponent<UndoComp>()!.Value);
    }

    // ---- Structural: destroy (the lost-work case) ----

    [Fact]
    public void RegisterDestroyObject_Undo_RestoresComponentDataAndIdentifier()
    {
        (Scene? scene, GameObject? go, UndoComp? comp) = MakeScene();
        comp.Value = 42; comp.Label = "hi";
        Guid goId = go.Identifier;
        Guid compId = comp.Identifier;

        Undo.RegisterDestroyObject(go, "Delete");
        DestroyGO(scene, go);
        Undo.IncrementGroup();

        Assert.Null(Undo.FindGO(goId));

        Undo.PerformUndo();
        GameObject? restored = Undo.FindGO(goId);
        Assert.NotNull(restored);
        UndoComp rc = restored!.GetComponent<UndoComp>()!;
        Assert.Equal(42, rc.Value);
        Assert.Equal("hi", rc.Label);
        Assert.Equal(compId, rc.Identifier); // identifier preserved so future records still resolve
    }

    [Fact]
    public void RegisterDestroyObject_Undo_BringsBackARuntimeMaterialNothingElseHeld()
    {
        (Scene? scene, GameObject? go, UndoComp _) = MakeScene();
        GiveRuntimeMaterial(go);
        Guid goId = go.Identifier;

        Undo.RegisterDestroyObject(go, "Delete");
        DestroyGO(scene, go);
        Undo.IncrementGroup();
        GC.Collect();
        GC.WaitForPendingFinalizers();
        GC.Collect();

        Undo.PerformUndo();

        Assert.Equal("Runtime", Undo.FindGO(goId)!.GetComponent<MeshRenderer>()!.Material!.Name);
    }

    [System.Runtime.CompilerServices.MethodImpl(System.Runtime.CompilerServices.MethodImplOptions.NoInlining)]
    private static void GiveRuntimeMaterial(GameObject go) => go.AddComponent<MeshRenderer>().Material = new Material { Name = "Runtime" };

    public sealed class UndoLinkComp : Component
    {
        public GameObject? Target;
        public int Value;
    }

    [Fact]
    public void RegisterDestroyObject_Undo_MissingScriptKeepsItsLinkToTheRestOfTheScene()
    {
        var source = new Scene();
        var target = new GameObject("Target");
        var holder = new GameObject("Holder");
        UndoLinkComp link = holder.AddComponent<UndoLinkComp>();
        link.Target = target;
        link.Value = 5;
        source.Add(target);
        source.Add(holder);
        string whileMissing = Echo.Serializer.Serialize(typeof(object), source).WriteToString().Replace(nameof(UndoLinkComp), "Ghost_DoesNotExist");

        Scene scene = Echo.Serializer.Deserialize<Scene>(Echo.EchoObject.ReadFromString(whileMissing))!;
        Scene.Load(scene);
        Scene.ProcessPendingLoad();
        GameObject loadedHolder = scene.AllObjects.Single(g => g.Name == "Holder");

        Undo.RegisterDestroyObject(loadedHolder, "Delete");
        DestroyGO(scene, loadedHolder);
        Undo.IncrementGroup();
        Undo.PerformUndo();

        string saved = Echo.Serializer.Serialize(typeof(object), Scene.Current).WriteToString().Replace("Ghost_DoesNotExist", nameof(UndoLinkComp));
        Scene restored = Echo.Serializer.Deserialize<Scene>(Echo.EchoObject.ReadFromString(saved))!;
        UndoLinkComp restoredLink = restored.AllObjects.Single(g => g.Name == "Holder").GetComponent<UndoLinkComp>()!;

        Assert.Equal(5, restoredLink.Value);
        Assert.Same(restored.AllObjects.Single(g => g.Name == "Target"), restoredLink.Target);
    }

    [Fact]
    public void RegisterDestroyObject_Undo_RestoresChildren()
    {
        var scene = new Scene();
        Scene.Load(scene);
        Scene.ProcessPendingLoad();
        Undo.Clear();

        var parent = new GameObject("Parent");
        parent.AddComponent<UndoComp>().Value = 1;
        var child = new GameObject("Child");
        child.AddComponent<UndoComp>().Value = 2;
        child.SetParent(parent);
        scene.Add(parent);

        Guid parentId = parent.Identifier;
        Guid childId = child.Identifier;

        Undo.RegisterDestroyObject(parent, "Delete");
        DestroyGO(scene, parent);
        Undo.IncrementGroup();

        Undo.PerformUndo();
        GameObject? rParent = Undo.FindGO(parentId);
        Assert.NotNull(rParent);
        Assert.Single(rParent!.Children);
        GameObject rChild = rParent.Children[0];
        Assert.Equal(childId, rChild.Identifier);
        Assert.Equal(2, rChild.GetComponent<UndoComp>()!.Value);
    }

    [Fact]
    public void PropertyUndo_SurvivesDestroyAndRecreate()
    {
        (Scene? scene, GameObject? go, UndoComp? comp) = MakeScene();
        comp.Value = 10;
        Undo.Snapshot(comp);
        comp.Value = 20;
        Undo.IncrementGroup(); // property step (resolves comp by identifier)

        Guid goId = go.Identifier;
        Undo.RegisterDestroyObject(go, "Delete");
        DestroyGO(scene, go);
        Undo.IncrementGroup(); // destroy step

        Undo.PerformUndo(); // undo destroy -> recreate go (same identifiers), comp.Value == 20
        GameObject? restored = Undo.FindGO(goId);
        Assert.NotNull(restored);
        Assert.Equal(20, restored!.GetComponent<UndoComp>()!.Value);

        Undo.PerformUndo(); // undo property change -> resolves the RECREATED comp by identifier
        Assert.Equal(10, restored.GetComponent<UndoComp>()!.Value);
    }

    // ---- Coalescing ----

    [Fact]
    public void PropertyEditsInOneGroup_AreOneStep()
    {
        (Scene _, GameObject _, UndoComp? comp) = MakeScene();
        comp.Value = 0;
        Undo.Snapshot(comp);
        comp.Value = 1;
        Undo.FlushUndoRecordObjects(); // one frame of a drag

        Undo.Snapshot(comp);
        comp.Value = 2;
        Undo.FlushUndoRecordObjects(); // the next frame joins the same group's step

        Undo.PerformUndo();
        Assert.Equal(0, comp.Value); // back to the original, single step
        Assert.False(Undo.CanUndo);
    }

    // ---- History trimming ----

    [Fact]
    public void MaxSteps_TrimsOldestSteps()
    {
        MakeScene();
        int prev = Undo.MaxSteps;
        Undo.MaxSteps = 3;
        try
        {
            for (int i = 0; i < 5; i++)
            {
                Undo.RegisterAction($"a{i}", () => { }, () => { });
                Undo.IncrementGroup();
            }

            int undos = 0;
            while (Undo.CanUndo && undos < 20) { Undo.PerformUndo(); undos++; }
            Assert.Equal(3, undos);
        }
        finally { Undo.MaxSteps = prev; }
    }

    [Fact]
    public void Clear_EmptiesHistory()
    {
        (Scene _, GameObject _, UndoComp? comp) = MakeScene();
        comp.Value = 0; Undo.Snapshot(comp); comp.Value = 1; Undo.IncrementGroup();
        Assert.True(Undo.CanUndo);

        Undo.Clear();

        Assert.False(Undo.CanUndo);
        Assert.False(Undo.CanRedo);
    }

    // ---- Continuous (gizmo drag) ----

    [Fact]
    public void Continuous_End_PushesOneStep_UndoRestoresStart_RedoReappliesEnd()
    {
        (Scene _, GameObject? go, UndoComp _) = MakeScene();
        go.Transform.LocalPosition = new Float3(0, 0, 0);

        Undo.BeginContinuous([go], "Move");
        go.Transform.LocalPosition = new Float3(5, 0, 0);
        Undo.EndContinuous();

        Assert.True(Undo.CanUndo);
        Undo.PerformUndo();
        Assert.Equal(0, go.Transform.LocalPosition.X, 4);

        Undo.PerformRedo();
        Assert.Equal(5, go.Transform.LocalPosition.X, 4);
    }

    [Fact]
    public void CancelContinuous_RestoresStart_NoStep()
    {
        (Scene _, GameObject? go, UndoComp _) = MakeScene();
        go.Transform.LocalPosition = new Float3(0, 0, 0);

        Undo.BeginContinuous([go], "Move");
        go.Transform.LocalPosition = new Float3(5, 0, 0);
        Undo.CancelContinuous();

        Assert.Equal(0, go.Transform.LocalPosition.X, 4);
        Assert.False(Undo.CanUndo);
    }

    [Fact]
    public void PerformUndo_DuringContinuous_CancelsInsteadOfUndoing()
    {
        (Scene _, GameObject? go, UndoComp _) = MakeScene();
        go.Transform.LocalPosition = new Float3(0, 0, 0);

        Undo.BeginContinuous([go], "Move");
        go.Transform.LocalPosition = new Float3(5, 0, 0);
        Undo.PerformUndo(); // cancels the drag rather than popping the stack

        Assert.Equal(0, go.Transform.LocalPosition.X, 4);
        Assert.False(Undo.IsContinuous);
        Assert.False(Undo.CanUndo);
    }

    [Fact]
    public void Continuous_NoMovement_PushesNoStep()
    {
        (Scene _, GameObject? go, UndoComp _) = MakeScene();
        Undo.BeginContinuous([go], "Move");
        // no movement
        Undo.EndContinuous();

        Assert.False(Undo.CanUndo);
    }

    // ---- Interaction between actions and snapshots ----

    [Fact]
    public void ActionAndSnapshotSameFrame_PropertySnapshotIsDiscarded()
    {
        (Scene _, GameObject _, UndoComp? comp) = MakeScene();
        comp.Value = 10;
        Undo.Snapshot(comp);
        comp.Value = 20;
        Undo.RegisterAction("Some Action", () => { }, () => { }); // an explicit action this frame
        Undo.IncrementGroup();

        // Only the action step exists; the property snapshot was dropped to avoid a duplicate record.
        Undo.PerformUndo();
        Assert.Equal(20, comp.Value); // property change was NOT tracked
        Assert.False(Undo.CanUndo);   // exactly one step
    }

    // ---- Play-mode gating ----

    [Fact]
    public void WhenPlaying_RecordingIsNoOp()
    {
        bool prev = Application.IsPlaying;
        Application.IsPlaying = true;
        try
        {
            (Scene _, GameObject _, UndoComp? comp) = MakeScene();
            comp.Value = 10;
            Undo.Snapshot(comp);
            comp.Value = 20;
            Undo.IncrementGroup();

            Assert.False(Undo.CanUndo);
        }
        finally { Application.IsPlaying = prev; }
    }

    [Fact]
    public void Undo_OnEmptyHistory_IsSafeNoOp()
    {
        MakeScene();
        Assert.False(Undo.CanUndo);
        Undo.PerformUndo(); // must not throw
        Undo.PerformRedo();
        Assert.False(Undo.CanUndo);
    }

    // ---- Coalescing boundaries ----

    [Fact]
    public void PropertyEdits_InSeparateGroups_StayAsSeparateSteps()
    {
        (Scene _, GameObject _, UndoComp? comp) = MakeScene();
        comp.Value = 0;
        Undo.Snapshot(comp);
        comp.Value = 1;
        Undo.IncrementGroup();

        Undo.Snapshot(comp);
        comp.Value = 2;
        Undo.IncrementGroup();

        // Two groups, two steps.
        Undo.PerformUndo();
        Assert.Equal(1, comp.Value);
        Undo.PerformUndo();
        Assert.Equal(0, comp.Value);
    }

    [Fact]
    public void RegisterCoalescableAction_InOneGroup_KeepsOriginalUndoLatestRedo()
    {
        MakeScene();
        int x = 1;
        Undo.RegisterCoalescableAction("Edit", () => x = 0, () => x = 1);
        Undo.FlushUndoRecordObjects();

        x = 2;
        Undo.RegisterCoalescableAction("Edit", () => x = 0, () => x = 2); // same group, same step

        Undo.PerformUndo();
        Assert.Equal(0, x);            // original undo
        Assert.False(Undo.CanUndo);    // single coalesced step

        Undo.PerformRedo();
        Assert.Equal(2, x);            // latest redo value
    }

    // ---- Multi-object helpers ----

    [Fact]
    public void ApplyGameObjectChanges_AppliesToAll_AsOneUndoStep()
    {
        var scene = new Scene();
        var a = new GameObject("A");
        var b = new GameObject("B");
        scene.Add(a); scene.Add(b);
        Scene.Load(scene);
        Scene.ProcessPendingLoad();
        Undo.Clear();

        Undo.ApplyGameObjectChanges(new[] { a, b }, "Rename", g => g.Name, (g, v) => g.Name = v, "Renamed");
        Assert.Equal("Renamed", a.Name);
        Assert.Equal("Renamed", b.Name);
        Undo.IncrementGroup();

        Undo.PerformUndo();
        Assert.Equal("A", a.Name);
        Assert.Equal("B", b.Name);
        Assert.False(Undo.CanUndo); // single grouped step

        Undo.PerformRedo();
        Assert.Equal("Renamed", a.Name);
        Assert.Equal("Renamed", b.Name);
    }

    // ---- Robustness ----

    [Fact]
    public void PropertyUndo_OnDestroyedTarget_IsSafeNoOp()
    {
        (Scene? scene, GameObject? go, UndoComp? comp) = MakeScene();
        comp.Value = 0;
        Undo.Snapshot(comp);
        comp.Value = 1;
        Undo.IncrementGroup();

        DestroyGO(scene, go); // destroy without registering undo - the record's target is now gone

        Undo.PerformUndo(); // must not throw; resolves to null and no-ops
        Assert.True(Undo.CanRedo);
    }

    [Fact]
    public void Continuous_MultipleTargets_RestoredTogether()
    {
        var scene = new Scene();
        var a = new GameObject("A");
        var b = new GameObject("B");
        scene.Add(a); scene.Add(b);
        Scene.Load(scene);
        Scene.ProcessPendingLoad();
        Undo.Clear();
        a.Transform.LocalPosition = Float3.Zero;
        b.Transform.LocalPosition = Float3.Zero;

        Undo.BeginContinuous([a, b], "Move");
        a.Transform.LocalPosition = new Float3(5, 0, 0);
        b.Transform.LocalPosition = new Float3(0, 7, 0);
        Undo.EndContinuous();

        Undo.PerformUndo();
        Assert.Equal(0, a.Transform.LocalPosition.X, 4);
        Assert.Equal(0, b.Transform.LocalPosition.Y, 4);
        Assert.False(Undo.CanUndo); // one step for both targets
    }

    // ---------------------------------------------------------------------
    // A session that edits something other than the open scene borrows the history
    // ---------------------------------------------------------------------

    [Fact]
    public void PushContext_SetsTheHistoryAsideAndPopBringsItBack()
    {
        int value = 0;
        Undo.RegisterAction("Scene Edit", () => value = 1, () => value = 2);
        Undo.IncrementGroup();
        Assert.True(Undo.CanUndo);

        object saved = Undo.PushContext();
        Assert.False(Undo.CanUndo); // the session starts with nothing behind it

        int sessionValue = 0;
        Undo.RegisterAction("Session Edit", () => sessionValue = 1, () => sessionValue = 2);
        Undo.IncrementGroup();
        Assert.True(Undo.CanUndo);

        Undo.PopContext(saved);

        // The session's own step is gone with it, and the scene's is back and still works.
        Assert.True(Undo.CanUndo);
        Undo.PerformUndo();
        Assert.Equal(1, value);
        Assert.Equal(0, sessionValue);
        Assert.False(Undo.CanUndo);
    }

    [Fact]
    public void PopContext_WithNothingSavedIsJustAClear()
    {
        Undo.RegisterAction("Edit", () => { }, () => { });
        Undo.IncrementGroup();
        Assert.True(Undo.CanUndo);

        Undo.PopContext(null);
        Assert.False(Undo.CanUndo);
    }
    // ================================================================
    //  Records, groups and the step restore
    // ================================================================

    [ExecuteAlways]
    public sealed class UndoRefComp : Component
    {
        public GameObject? Target;
        public UndoComp? Comp;
        public List<GameObject> Targets = [];
        public Dictionary<string, GameObject> Named = [];
        public Transform? TargetTransform;
        public UndoNested Nested = new();
        public float Number;

        [System.NonSerialized] public GameObject? TargetAtEnable;
        [System.NonSerialized] public int Enables;
        [System.NonSerialized] public int Disables;
        [System.NonSerialized] public int Validates;

        public override void OnEnable() { Enables++; TargetAtEnable = Target; }
        public override void OnDisable() => Disables++;
        public override void OnValidate() => Validates++;
    }

    public sealed class UndoNested
    {
        public GameObject? Target;
    }

    [ExecuteAlways]
    public sealed class UndoEnableComp : Component
    {
        [System.NonSerialized] public int Enables;
        [System.NonSerialized] public int Disables;
        public override void OnEnable() => Enables++;
        public override void OnDisable() => Disables++;
    }

    public sealed class UndoNeeded : Component { }

    [RequireComponent(typeof(UndoNeeded))]
    public sealed class UndoNeeds : Component { }

    private static Scene OpenScene()
    {
        var scene = new Scene();
        Scene.Load(scene);
        Scene.ProcessPendingLoad();
        Undo.ClearAll();
        EditorSceneManager.IsDirty = false;
        return scene;
    }

    private static GameObject Add(Scene scene, string name, GameObject? parent = null)
    {
        var go = new GameObject(name);
        if (parent.IsValid()) go.SetParent(parent, worldPositionStays: false);
        else scene.Add(go);
        return go;
    }

    private static T Live<T>(Guid id) where T : EngineObject => Scene.Current.FindObjectByIdentifier<T>(id)!;

    [Fact]
    public void DestroyedTogether_ReferenceEachOthersLiveInstancesOnUndo()
    {
        Scene scene = OpenScene();
        GameObject door = Add(scene, "Door");
        GameObject sw = Add(scene, "Switch");
        UndoRefComp doorRef = door.AddComponent<UndoRefComp>();
        UndoRefComp swRef = sw.AddComponent<UndoRefComp>();
        doorRef.Target = sw;
        swRef.Target = door;
        swRef.Targets.Add(door);
        Guid doorId = door.Identifier, swId = sw.Identifier;

        Undo.DestroyObjectImmediate(door);
        Undo.DestroyObjectImmediate(sw);
        Undo.FlushUndoRecordObjects();
        Undo.PerformUndo();

        GameObject liveDoor = Live<GameObject>(doorId), liveSwitch = Live<GameObject>(swId);
        UndoRefComp liveSwitchRef = liveSwitch.GetComponent<UndoRefComp>()!;
        Assert.Same(liveSwitch, liveDoor.GetComponent<UndoRefComp>()!.Target);
        Assert.Same(liveDoor, liveSwitchRef.Target);
        Assert.Same(liveDoor, liveSwitchRef.Targets[0]);
        Assert.Same(liveDoor, liveSwitchRef.TargetAtEnable);
        Assert.Equal(1, liveSwitchRef.Enables);
    }

    [Fact]
    public void DestroyedChild_ComesBackAtItsSiblingIndexWithItsLocalTransform()
    {
        Scene scene = OpenScene();
        GameObject parent = Add(scene, "Parent");
        parent.Transform.LocalRotation = Quaternion.FromEuler(0, 90, 0);
        Add(scene, "First", parent);
        GameObject middle = Add(scene, "Middle", parent);
        Add(scene, "Last", parent);
        middle.Transform.LocalPosition = new Float3(1, 2, 3);
        Guid id = middle.Identifier;

        Undo.DestroyObjectImmediate(middle);
        Undo.FlushUndoRecordObjects();
        Undo.PerformUndo();

        GameObject back = Live<GameObject>(id);
        Assert.Same(parent, back.Parent);
        Assert.Equal(1, parent.Children.IndexOf(back));
        Assert.Equal(new Float3(1, 2, 3), back.Transform.LocalPosition);
    }

    [Fact]
    public void DestroyedRoot_ComesBackAtItsRootIndex()
    {
        Scene scene = OpenScene();
        Add(scene, "A");
        GameObject b = Add(scene, "B");
        Add(scene, "C");
        Guid id = b.Identifier;

        Undo.DestroyObjectImmediate(b);
        Assert.DoesNotContain(scene.AllObjects, g => g.Identifier == id);
        Undo.FlushUndoRecordObjects();
        Undo.PerformUndo();

        Assert.Equal(1, scene.GetRootIndex(Live<GameObject>(id)));
    }

    [Fact]
    public void DestroyedComponent_ComesBackAtItsIndexWithOneOnEnable()
    {
        Scene scene = OpenScene();
        GameObject go = Add(scene, "GO");
        go.AddComponent<UndoComp>();
        UndoEnableComp comp = go.AddComponent<UndoEnableComp>();
        go.AddComponent<UndoNeeded>();
        Guid id = comp.Identifier;

        Undo.DestroyObjectImmediate(comp);
        Undo.FlushUndoRecordObjects();
        Undo.PerformUndo();

        UndoEnableComp back = Live<UndoEnableComp>(id);
        Assert.Equal(1, go._components.IndexOf(back));
        Assert.Equal(1, back.Enables);
    }

    [Fact]
    public void CreateEmptyParent_UndoAndRedo_LeaveOneChild()
    {
        Scene scene = OpenScene();
        GameObject oldParent = Add(scene, "Old");
        GameObject child = Add(scene, "Child", oldParent);
        Guid childId = child.Identifier;

        var parent = new GameObject("New Parent");
        scene.Add(parent);
        Guid parentId = parent.Identifier;
        Undo.RegisterCreatedObjectUndo(parent, "Create Empty Parent");
        Undo.SetTransformParent(child, parent, false, "Create Empty Parent");
        Undo.FlushUndoRecordObjects();

        Undo.PerformUndo();
        Assert.Same(oldParent, Live<GameObject>(childId).Parent);
        Assert.Null(scene.FindObjectByIdentifier<GameObject>(parentId));

        Undo.PerformRedo();
        GameObject liveParent = Live<GameObject>(parentId);
        Assert.Single(liveParent.Children);
        Assert.Empty(oldParent.Children);
        Assert.Single(scene.AllObjects, g => g.Identifier == childId);
    }

    [Fact]
    public void CreatedParent_WithAComponentReferencingTheMovedChild_RedoesWithTheLiveChild()
    {
        Scene scene = OpenScene();
        GameObject child = Add(scene, "Child");
        Guid childId = child.Identifier;

        var parent = new GameObject("Parent");
        scene.Add(parent);
        Guid parentId = parent.Identifier;
        parent.AddComponent<UndoRefComp>().Target = child;
        Undo.RegisterCreatedObjectUndo(parent, "Create");
        Undo.SetTransformParent(child, parent, false, "Create");
        Undo.FlushUndoRecordObjects();

        Undo.PerformUndo();
        Undo.PerformRedo();

        GameObject liveChild = Live<GameObject>(childId);
        Assert.Same(liveChild, Live<GameObject>(parentId).GetComponent<UndoRefComp>()!.Target);
        Assert.Single(scene.AllObjects, g => g.Identifier == childId);
    }

    [Fact]
    public void ReparentThenDestroy_UndoesToTheOriginalParent()
    {
        Scene scene = OpenScene();
        GameObject a = Add(scene, "A");
        GameObject b = Add(scene, "B");
        GameObject x = Add(scene, "X", a);
        Guid xId = x.Identifier;

        Undo.SetTransformParent(x, b, false, "Move");
        Undo.DestroyObjectImmediate(x);
        Undo.FlushUndoRecordObjects();
        Undo.PerformUndo();

        Assert.Same(a, Live<GameObject>(xId).Parent);
    }

    [Fact]
    public void ComponentDisable_UndoFiresOnEnable()
    {
        Scene scene = OpenScene();
        GameObject go = Add(scene, "GO");
        UndoEnableComp comp = go.AddComponent<UndoEnableComp>();
        comp.Enables = 0;

        Undo.RecordObject(comp, "Disable");
        comp.Enabled = false;
        Undo.FlushUndoRecordObjects();
        Undo.PerformUndo();

        Assert.True(comp.Enabled);
        Assert.True(comp.EnabledInHierarchy);
        Assert.Equal(1, comp.Enables);
    }

    [Fact]
    public void ComponentToggle_UndoFiresOnDisable()
    {
        Scene scene = OpenScene();
        GameObject go = Add(scene, "GO");
        UndoEnableComp comp = go.AddComponent<UndoEnableComp>();
        comp.Enabled = false;

        Undo.RecordObject(comp, "Enable");
        comp.Enabled = true;
        Undo.FlushUndoRecordObjects();
        comp.Disables = 0;
        Undo.PerformUndo();

        Assert.False(comp.Enabled);
        Assert.Equal(1, comp.Disables);
    }

    [Fact]
    public void ReferenceField_UndoPointsAtTheSameLiveObject()
    {
        Scene scene = OpenScene();
        GameObject a = Add(scene, "A");
        GameObject b = Add(scene, "B");
        UndoRefComp holder = Add(scene, "Holder").AddComponent<UndoRefComp>();
        holder.Target = a;

        Undo.RecordObject(holder, "Retarget");
        holder.Target = b;
        Undo.FlushUndoRecordObjects();
        Undo.PerformUndo();

        Assert.Same(a, holder.Target);
        Assert.Single(scene.AllObjects, g => g.Name == "A");
    }

    [Fact]
    public void RuntimeMaterialField_UndoIsTheSameMaterial()
    {
        Scene scene = OpenScene();
        MeshRenderer renderer = Add(scene, "R").AddComponent<MeshRenderer>();
        var first = new Material { Name = "First" };
        renderer.Material = first;

        Undo.RecordObject(renderer, "Material");
        renderer.Material = new Material { Name = "Second" };
        Undo.FlushUndoRecordObjects();
        Undo.PerformUndo();

        Assert.Same(first, renderer.Material);
    }

    [Fact]
    public void OnlyChangedFieldsRestore_AnUnchangedSiblingSurvives()
    {
        Scene scene = OpenScene();
        UndoComp comp = Add(scene, "GO").AddComponent<UndoComp>();
        comp.Value = 1;
        comp.Label = "a";

        Undo.RecordObject(comp, "Value");
        comp.Value = 2;
        Undo.FlushUndoRecordObjects();
        comp.Label = "changed elsewhere";
        Undo.PerformUndo();

        Assert.Equal(1, comp.Value);
        Assert.Equal("changed elsewhere", comp.Label);
    }

    [Fact]
    public void UnchangedRecord_RecordsNothing_EvenWithNaN()
    {
        Scene scene = OpenScene();
        UndoRefComp comp = Add(scene, "GO").AddComponent<UndoRefComp>();
        comp.Number = float.NaN;

        Undo.RecordObject(comp, "Nothing");
        Undo.FlushUndoRecordObjects();

        Assert.False(Undo.CanUndo);
    }

    [Fact]
    public void DestroyedTarget_UndoRebindsEveryHolderToTheLiveInstance()
    {
        Scene scene = OpenScene();
        GameObject target = Add(scene, "Target");
        UndoComp targetComp = target.AddComponent<UndoComp>();
        UndoRefComp holder = Add(scene, "Holder").AddComponent<UndoRefComp>();
        holder.Target = target;
        holder.Comp = targetComp;
        holder.Targets.Add(target);
        holder.Named["t"] = target;
        holder.TargetTransform = target.Transform;
        holder.Nested.Target = target;
        Guid id = target.Identifier;

        Undo.DestroyObjectImmediate(target);
        Undo.FlushUndoRecordObjects();
        holder.Validates = 0;
        Undo.PerformUndo();

        GameObject live = Live<GameObject>(id);
        Assert.Same(live, holder.Target);
        Assert.Same(live.GetComponent<UndoComp>(), holder.Comp);
        Assert.Same(live, holder.Targets[0]);
        Assert.Same(live, holder.Named["t"]);
        Assert.Same(live.Transform, holder.TargetTransform);
        Assert.Same(live, holder.Nested.Target);
        Assert.Equal(1, holder.Validates);
    }

    [Fact]
    public void TwoRecordsOnOneObjectInOneGroup_MergeIntoOne()
    {
        Scene scene = OpenScene();
        UndoComp comp = Add(scene, "GO").AddComponent<UndoComp>();
        comp.Value = 0;

        Undo.RecordObject(comp, "Edit");
        comp.Value = 1;
        Undo.RecordObject(comp, "Edit");
        comp.Value = 2;
        Undo.FlushUndoRecordObjects();
        Undo.PerformUndo();

        Assert.Equal(0, comp.Value);
        Assert.False(Undo.CanUndo);
        Undo.PerformRedo();
        Assert.Equal(2, comp.Value);
    }

    [Fact]
    public void DragStyleRecordsOverFrames_AreOneStep()
    {
        Scene scene = OpenScene();
        GameObject go = Add(scene, "GO");

        for (int frame = 1; frame <= 3; frame++)
        {
            Undo.RecordObject(go, "Move");
            go.Transform.LocalPosition = new Float3(frame, 0, 0);
            Undo.FlushUndoRecordObjects();
        }
        Undo.IncrementCurrentGroup();
        Undo.PerformUndo();

        Assert.Equal(Float3.Zero, go.Transform.LocalPosition);
        Assert.False(Undo.CanUndo);
    }

    [Fact]
    public void EditAfterUndo_KeepsRedoUntilItsGroupCloses_AndBlocksRedoMeanwhile()
    {
        Scene scene = OpenScene();
        UndoComp comp = Add(scene, "GO").AddComponent<UndoComp>();
        Undo.RecordObject(comp, "A"); comp.Value = 1; Undo.FlushUndoRecordObjects(); Undo.IncrementCurrentGroup();
        Undo.PerformUndo();

        Undo.RecordObject(comp, "B");
        comp.Value = 5;
        Undo.FlushUndoRecordObjects();
        Assert.False(Undo.CanRedo);
        Undo.PerformRedo();
        Assert.Equal(5, comp.Value);

        Undo.IncrementCurrentGroup();
        Assert.Empty(Undo.RedoSteps);
    }

    [Fact]
    public void OneFrameEditAfterUndo_ClearsRedoWhenItIsPushed()
    {
        Scene scene = OpenScene();
        UndoComp comp = Add(scene, "GO").AddComponent<UndoComp>();
        Undo.RecordObject(comp, "A"); comp.Value = 1; Undo.FlushUndoRecordObjects(); Undo.IncrementCurrentGroup();
        Undo.PerformUndo();

        Undo.RecordObject(comp, "B");
        comp.Value = 5;
        Undo.EndFrame(textFocused: false);
        Undo.FlushFrame();

        Assert.Empty(Undo.RedoSteps);
    }

    [Fact]
    public void RevertAllInCurrentGroup_RestoresPopsAndKeepsRedo()
    {
        Scene scene = OpenScene();
        UndoComp comp = Add(scene, "GO").AddComponent<UndoComp>();
        Undo.RecordObject(comp, "A"); comp.Value = 1; Undo.FlushUndoRecordObjects(); Undo.IncrementCurrentGroup();
        Undo.PerformUndo();

        Undo.RecordObject(comp, "Drag"); comp.Value = 7; Undo.FlushUndoRecordObjects();
        Undo.RecordObject(comp, "Drag"); comp.Value = 8;
        Undo.RevertAllInCurrentGroup();

        Assert.Equal(0, comp.Value);
        Assert.Single(Undo.RedoSteps);
        Assert.Empty(Undo.UndoSteps);
    }

    [Fact]
    public void AddComponent_UndoRemovesRequiredComponentsToo_RedoBringsBackValuesSetAfter()
    {
        Scene scene = OpenScene();
        GameObject go = Add(scene, "GO");

        Undo.AddComponent<UndoNeeds>(go);
        UndoComp comp = Undo.AddComponent<UndoComp>(go);
        comp.Value = 9;
        Guid compId = comp.Identifier;
        Undo.FlushUndoRecordObjects();
        Assert.NotNull(go.GetComponent<UndoNeeded>());

        Undo.PerformUndo();
        Assert.Null(go.GetComponent<UndoNeeds>());
        Assert.Null(go.GetComponent<UndoNeeded>());
        Assert.Null(go.GetComponent<UndoComp>());

        Undo.PerformRedo();
        Assert.NotNull(go.GetComponent<UndoNeeds>());
        Assert.NotNull(go.GetComponent<UndoNeeded>());
        Assert.Equal(9, Live<UndoComp>(compId).Value);
    }

    [Fact]
    public void CreatedThenDestroyedInOneGroup_RecordsNothing()
    {
        Scene scene = OpenScene();
        GameObject go = Add(scene, "Temp");
        Undo.RegisterCreatedObjectUndo(go, "Create");
        Undo.RecordObject(go, "Rename");
        go.Name = "Renamed";
        Undo.DestroyObjectImmediate(go);
        Undo.FlushUndoRecordObjects();

        Assert.False(Undo.CanUndo);
    }

    [Fact]
    public void AddComponentThenDestroyInOneGroup_RecordsNothing()
    {
        Scene scene = OpenScene();
        GameObject go = Add(scene, "GO");
        UndoComp comp = Undo.AddComponent<UndoComp>(go);
        Undo.DestroyObjectImmediate(comp);
        Undo.FlushUndoRecordObjects();

        Assert.False(Undo.CanUndo);
    }

    [Fact]
    public void HierarchyRecord_UndoRemovesChildrenAddedAfterIt()
    {
        Scene scene = OpenScene();
        GameObject root = Add(scene, "Root");
        GameObject kept = Add(scene, "Kept", root);
        Guid rootId = root.Identifier, keptId = kept.Identifier;

        Undo.RegisterFullObjectHierarchyUndo(root, "Change");
        Add(scene, "Added", root);
        root.AddComponent<UndoComp>();
        Undo.FlushUndoRecordObjects();
        Undo.PerformUndo();

        GameObject live = Live<GameObject>(rootId);
        Assert.Single(live.Children);
        Assert.Equal(keptId, live.Children[0].Identifier);
        Assert.Null(live.GetComponent<UndoComp>());
    }

    [Fact]
    public void Steps_MarkTheirOwners_OnPushAndUndo()
    {
        Scene scene = OpenScene();
        UndoComp comp = Add(scene, "GO").AddComponent<UndoComp>();

        Undo.RecordObject(comp, "Edit");
        comp.Value = 3;
        Undo.FlushUndoRecordObjects();
        Assert.True(EditorSceneManager.IsDirty);

        EditorSceneManager.IsDirty = false;
        Undo.PerformUndo();
        Assert.True(EditorSceneManager.IsDirty);
    }

    [Fact]
    public void DontSaveObjects_MarkNothing()
    {
        Scene scene = OpenScene();
        GameObject go = Add(scene, "Hidden");
        go.HideFlags = HideFlags.DontSave;

        Undo.RecordObject(go, "Rename");
        go.Name = "X";
        Undo.FlushUndoRecordObjects();

        Assert.False(EditorSceneManager.IsDirty);
        Assert.True(Undo.CanUndo);
    }

    [Fact]
    public void PostprocessModifications_FiltersKeys()
    {
        Scene scene = OpenScene();
        UndoComp comp = Add(scene, "GO").AddComponent<UndoComp>();
        Undo.postprocessModifications = mods => [.. mods.Where(m => m.PropertyPath != nameof(UndoComp.Label))];
        try
        {
            Undo.RecordObject(comp, "Edit");
            comp.Value = 1;
            comp.Label = "x";
            Undo.FlushUndoRecordObjects();
            Undo.PerformUndo();
            Assert.Equal(0, comp.Value);
            Assert.Equal("x", comp.Label);

            Undo.ClearAll();
            Undo.RecordObject(comp, "Label only");
            comp.Label = "y";
            Undo.FlushUndoRecordObjects();
            Assert.False(Undo.CanUndo);
        }
        finally { Undo.postprocessModifications = null; }
    }

    [Fact]
    public void PerformUndoTo_AndRedoTo_ApplyEveryStepBetween()
    {
        Scene scene = OpenScene();
        UndoComp comp = Add(scene, "GO").AddComponent<UndoComp>();
        int changes = 0;
        Undo.historyChanged += Count;
        try
        {
            for (int i = 1; i <= 3; i++)
            {
                Undo.RecordObject(comp, $"Set {i}");
                comp.Value = i;
                Undo.FlushUndoRecordObjects();
                Undo.IncrementCurrentGroup();
            }
            Assert.True(changes >= 3);

            Undo.PerformUndoTo(Undo.UndoSteps[0].Id);
            Assert.Equal(0, comp.Value);
            Assert.Equal(3, Undo.RedoSteps.Count);

            Undo.PerformRedoTo(Undo.RedoSteps[1].Id);
            Assert.Equal(2, comp.Value);
        }
        finally { Undo.historyChanged -= Count; }

        void Count() => changes++;
    }

    [Fact]
    public void ComponentQueuedForDestroy_IsRebound()
    {
        Scene scene = OpenScene();
        GameObject go = Add(scene, "GO");
        UndoComp comp = go.AddComponent<UndoComp>();
        UndoRefComp holder = Add(scene, "Holder").AddComponent<UndoRefComp>();
        holder.Comp = comp;
        Guid id = comp.Identifier;

        Undo.DestroyObjectImmediate(comp);
        Undo.FlushUndoRecordObjects();
        Undo.PerformUndo();

        Assert.Same(Live<UndoComp>(id), holder.Comp);
    }

    [Fact]
    public void RecreatedUnderDisabledParent_GetsNoOnEnable()
    {
        Scene scene = OpenScene();
        GameObject parent = Add(scene, "Parent");
        GameObject child = Add(scene, "Child", parent);
        UndoEnableComp comp = child.AddComponent<UndoEnableComp>();
        Guid id = comp.Identifier;
        parent.Enabled = false;

        Undo.DestroyObjectImmediate(child);
        Undo.FlushUndoRecordObjects();
        Undo.PerformUndo();

        UndoEnableComp back = Live<UndoEnableComp>(id);
        Assert.Equal(0, back.Enables);
        Assert.False(back.EnabledInHierarchy);
    }
    // ================================================================
    //  Editor frame rules
    // ================================================================

    // One editor frame: flush, input, then the frame's edits, then the frame's end.
    private static void Frame(bool mouseDown = false, bool mouseHeld = false, bool shortcut = false, Action? edits = null)
    {
        Undo.FlushFrame();
        Undo.BeginFrameInput(mouseDown, mouseHeld || mouseDown, shortcut);
        edits?.Invoke();
        Undo.EndFrame(textFocused: false);
    }

    [Fact]
    public void InspectorDrawWithNoEdit_RecordsNothing()
    {
        Scene scene = OpenScene();
        UndoComp comp = Add(scene, "GO").AddComponent<UndoComp>();

        Frame(edits: () => Undo.InspectorBaseline(comp));
        Frame();

        Assert.False(Undo.CanUndo);
        Assert.False(EditorSceneManager.IsDirty);
    }

    [Fact]
    public void InspectorEdit_RecordsAndMarksDirty_WithoutWhatTheGameChangedAfter()
    {
        Scene scene = OpenScene();
        UndoComp comp = Add(scene, "GO").AddComponent<UndoComp>();

        Frame(edits: () =>
        {
            Undo.InspectorBaseline(comp);
            comp.Value = 4;
            Undo.InspectorChanged(comp);
            comp.Label = "game wrote this";
        });
        Frame();

        Assert.True(EditorSceneManager.IsDirty);
        Undo.PerformUndo();
        Assert.Equal(0, comp.Value);
        Assert.Equal("game wrote this", comp.Label);
    }

    [Fact]
    public void InspectorCommitAFrameAfterTheFieldStoppedDrawing_StillRecords()
    {
        Scene scene = OpenScene();
        UndoComp comp = Add(scene, "GO").AddComponent<UndoComp>();

        Frame(edits: () => Undo.InspectorBaseline(comp));
        Frame(edits: () => { comp.Label = "committed"; Undo.InspectorChanged(comp); });
        Frame();

        Undo.PerformUndo();
        Assert.Equal("", comp.Label);
    }

    [Fact]
    public void Drag_IsOneStep_AndARenameBeforeItIsAnother()
    {
        Scene scene = OpenScene();
        GameObject go = Add(scene, "GO");

        Frame(edits: () => { Undo.RecordObject(go, "Rename"); go.Name = "Renamed"; });
        Frame(mouseDown: true, edits: () => { Undo.RecordObject(go, "Move"); go.Transform.LocalPosition = new Float3(1, 0, 0); });
        Frame(mouseHeld: true, edits: () => { Undo.RecordObject(go, "Move"); go.Transform.LocalPosition = new Float3(2, 0, 0); });
        Frame(mouseHeld: true, edits: () => { Undo.RecordObject(go, "Move"); go.Transform.LocalPosition = new Float3(3, 0, 0); });
        Frame();
        Frame();

        Undo.PerformUndo();
        Assert.Equal(Float3.Zero, go.Transform.LocalPosition);
        Assert.Equal("Renamed", go.Name);
        Undo.PerformUndo();
        Assert.Equal("GO", go.Name);
    }

    [Fact]
    public void UndoDuringADrag_RevertsTheDrag_AndRedoDoesNothing()
    {
        Scene scene = OpenScene();
        GameObject go = Add(scene, "GO");
        Frame(edits: () => { Undo.RecordObject(go, "Rename"); go.Name = "Renamed"; });
        Frame();

        Frame(mouseDown: true, edits: () => { Undo.RecordObject(go, "Move"); go.Transform.LocalPosition = new Float3(1, 0, 0); });
        Frame(mouseHeld: true, edits: () => { Undo.RecordObject(go, "Move"); go.Transform.LocalPosition = new Float3(2, 0, 0); });
        Assert.True(Undo.InteractionActive);

        Undo.PerformRedo();
        Assert.Equal(new Float3(2, 0, 0), go.Transform.LocalPosition);

        Undo.PerformUndo();
        Assert.Equal(Float3.Zero, go.Transform.LocalPosition);
        Assert.Equal("Renamed", go.Name);
        Assert.Empty(Undo.RedoSteps);
    }

    [Fact]
    public void UndoWhileTheMouseIsHeldAndNothingRecorded_IsIgnored()
    {
        Scene scene = OpenScene();
        Frame(mouseDown: true);
        Assert.True(Undo.IgnoresUndoInput);
        Frame();
        Assert.False(Undo.IgnoresUndoInput);
    }

    [Fact]
    public void UndoDuringALegacyContinuousEdit_CancelsIt()
    {
        Scene scene = OpenScene();
        GameObject go = Add(scene, "GO");
        Frame(mouseDown: true, edits: () => Undo.BeginContinuous([go], "Move"));
        go.Transform.LocalPosition = new Float3(5, 0, 0);

        Assert.False(Undo.IgnoresUndoInput);
        Undo.PerformUndo();

        Assert.Equal(Float3.Zero, go.Transform.LocalPosition);
        Assert.False(Undo.IsContinuous);
    }

    [Fact]
    public void ContextClearedWhileSetAside_PopsBackEmpty()
    {
        Scene scene = OpenScene();
        UndoComp comp = Add(scene, "GO").AddComponent<UndoComp>();
        Undo.RecordObject(comp, "Edit"); comp.Value = 1; Undo.FlushUndoRecordObjects();

        object saved = Undo.PushContext();
        Undo.ClearAll(); // a script reload during play or prefab editing
        Undo.PopContext(saved);

        Assert.False(Undo.CanUndo);
    }

    [Fact]
    public void PushContext_FlushesAnEditMadeThatFrame()
    {
        Scene scene = OpenScene();
        UndoComp comp = Add(scene, "GO").AddComponent<UndoComp>();
        Undo.RecordObject(comp, "Edit");
        comp.Value = 1;

        object saved = Undo.PushContext();
        Assert.False(Undo.CanUndo);
        Undo.PopContext(saved);

        Undo.PerformUndo();
        Assert.Equal(0, comp.Value);
    }
    // ================================================================
    //  Assets
    // ================================================================

    private Material CreateMaterial(string path, float value)
    {
        var material = new Material();
        material.SetFloat("_Value", value);
        Assets.CreateAsset(material, path);
        Undo.ClearAll();
        return material;
    }

    [Fact]
    public void DatabaseAsset_UndoesWholeAndMarksTheAsset()
    {
        OpenScene();
        Material material = CreateMaterial("M.mat", 1);

        Undo.RecordObject(material, "Edit");
        material.SetFloat("_Value", 2);
        Undo.FlushUndoRecordObjects();
        Assert.True(EditorUtility.IsDirty(material));
        Assert.False(EditorSceneManager.IsDirty);

        Undo.PerformUndo();
        Assert.Equal(1, material.GetFloat("_Value"));
        Undo.PerformRedo();
        Assert.Equal(2, material.GetFloat("_Value"));
    }

    [Fact]
    public void ExternalChange_DropsTheAssetsSteps_ButTheEditorsOwnSaveDoesNot()
    {
        OpenScene();
        Material material = CreateMaterial("M.mat", 1);

        Undo.RecordObject(material, "Edit");
        material.SetFloat("_Value", 2);
        Undo.IncrementGroup();
        Assert.True(Assets.SaveAsset(material));
        Assets.Refresh();
        Assert.True(Undo.CanUndo);

        var outside = new Material();
        outside.SetFloat("_Value", 5);
        string file = AssetAbsolutePath("M.mat");
        File.WriteAllText(file, Echo.Serializer.Serialize(typeof(object), outside).WriteToString());
        File.SetLastWriteTimeUtc(file, DateTime.UtcNow.AddMinutes(1));
        Assets.Refresh();

        Assert.False(Undo.CanUndo);
        Assert.False(EditorUtility.IsDirty(material));
    }

    [Fact]
    public void ImporterRecord_RestoresPendingSettings_AndWritesNothing()
    {
        OpenScene();
        Material material = CreateMaterial("M.mat", 1);
        Guid id = material.AssetID;
        string meta = File.ReadAllText(AssetAbsolutePath("M.mat") + ".meta");

        Undo.RegisterImporterUndo(id, "Setting");
        Inspector.ImportSettingsEditor.Pending(id)!["Quality"] = new Echo.EchoObject(5);
        Undo.FlushUndoRecordObjects();

        Undo.PerformUndo();
        Assert.False(Inspector.ImportSettingsEditor.Pending(id)!.TryGet("Quality", out _));
        Undo.PerformRedo();
        Assert.Equal(5, Inspector.ImportSettingsEditor.Pending(id)!["Quality"].IntValue);
        Assert.Equal(meta, File.ReadAllText(AssetAbsolutePath("M.mat") + ".meta"));
    }

    [Fact]
    public void RuntimeAssetDisposedSinceTheRecord_IsLeftAlone()
    {
        Scene scene = OpenScene();
        MeshRenderer renderer = Add(scene, "R").AddComponent<MeshRenderer>();
        var first = new Material { Name = "First" };
        renderer.Material = first;

        Undo.RecordObject(renderer, "Material");
        var second = new Material { Name = "Second" };
        renderer.Material = second;
        Undo.FlushUndoRecordObjects();
        first.Dispose();
        Undo.PerformUndo();

        Assert.Same(second, renderer.Material);
    }

    [Fact]
    public void CustomEditorSnapshotsOverADrag_AreOneStep_AndAHoldWithNoChangeIsNoInteraction()
    {
        Scene scene = OpenScene();
        UndoComp comp = Add(scene, "GO").AddComponent<UndoComp>();

        Frame(mouseDown: true, edits: () => Undo.Snapshot(comp));
        Assert.False(Undo.InteractionActive);
        Frame(mouseHeld: true, edits: () => { Undo.Snapshot(comp); comp.Value = 1; });
        Frame(mouseHeld: true, edits: () => { Undo.Snapshot(comp); comp.Value = 2; });
        Frame(mouseHeld: true, edits: () => { Undo.Snapshot(comp); comp.Value = 3; });
        Frame();
        Frame();

        Undo.PerformUndo();
        Assert.Equal(0, comp.Value);
        Assert.False(Undo.CanUndo);
    }

    [Fact]
    public void EmbeddedProfileEdit_RecordsAndMarksTheHoldersScene()
    {
        Scene scene = OpenScene();
        Runtime.Rendering.Volume volume = Add(scene, "V").AddComponent<Runtime.Rendering.Volume>();
        var profile = new Runtime.Rendering.VolumeProfile { Name = "Embedded" };
        volume.SharedProfile = profile;
        Type type = Inspector.VolumeProfileGUI.ComponentTypes[0];
        EditorSceneManager.IsDirty = false;

        Undo.Snapshot(profile, UndoOwner.Of(volume));
        profile.Add(type);
        Undo.FlushUndoRecordObjects();
        Assert.True(EditorSceneManager.IsDirty);

        Undo.PerformUndo();
        Assert.False(profile.Has(type));
    }
}
