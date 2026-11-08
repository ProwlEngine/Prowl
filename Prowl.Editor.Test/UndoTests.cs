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

/// <summary>An operation made of two actions, for tests of the stack itself.</summary>
public sealed class UndoTestOp(string name, Action undo, Action redo) : IUndoOperation
{
    public string Name => name;
    public IReadOnlyList<UndoOwner> Owners => [];
    public void Undo() => undo();
    public void Redo() => redo();
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

    private static GameObject? FindGO(Guid id) => SceneInstance.Main.FindObjectByIdentifier<GameObject>(id);

    private (SceneInstance scene, GameObject go, UndoComp comp) MakeScene()
    {
        var scene = new SceneInstance(SceneMode.Edit);
        var go = new GameObject("GO");
        UndoComp comp = go.AddComponent<UndoComp>();
        scene.Add(go);
        SceneInstance.Load(scene);
        SceneInstance.ProcessPendingLoad();
        return (scene, go, comp);
    }

    private static void DestroyGO(SceneInstance s, GameObject go)
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
        var scene = new SceneInstance(SceneMode.Edit);
        var go = new GameObject("GO");
        AppliedComp comp = go.AddComponent<AppliedComp>();
        scene.Add(go);
        SceneInstance.Load(scene);
        SceneInstance.ProcessPendingLoad();

        comp.Value = 10;
        comp.OnValidate();
        Undo.RecordObject(comp, "Edit");
        comp.Value = 20;
        comp.OnValidate();
        Undo.FlushUndoRecordObjects(); Undo.IncrementCurrentGroup();

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
        (SceneInstance _, GameObject _, UndoComp? comp) = MakeScene();
        comp.Value = 10;
        Undo.RecordObject(comp, "Edit");
        comp.Value = 20;
        Undo.FlushUndoRecordObjects(); Undo.IncrementCurrentGroup();

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
        (SceneInstance _, GameObject _, UndoComp? comp) = MakeScene();
        comp.Value = 1; comp.Label = "a";
        Undo.RecordObject(comp, "Edit");
        comp.Value = 2; comp.Label = "b";
        Undo.FlushUndoRecordObjects(); Undo.IncrementCurrentGroup();

        Undo.PerformUndo();
        Assert.Equal(1, comp.Value);
        Assert.Equal("a", comp.Label);
        Assert.False(Undo.CanUndo); // single step covered both fields
    }

    [Fact]
    public void NoOpChange_DoesNotCreateStep()
    {
        (SceneInstance _, GameObject _, UndoComp? comp) = MakeScene();
        comp.Value = 5;
        Undo.RecordObject(comp, "Edit");
        // no mutation
        Undo.FlushUndoRecordObjects(); Undo.IncrementCurrentGroup();

        Assert.False(Undo.CanUndo);
    }

    [Fact]
    public void NewAction_ClearsRedoStack()
    {
        (SceneInstance _, GameObject _, UndoComp? comp) = MakeScene();
        comp.Value = 0;
        Undo.RecordObject(comp, "Edit");
        comp.Value = 1;
        Undo.FlushUndoRecordObjects(); Undo.IncrementCurrentGroup();

        Undo.PerformUndo();
        Assert.True(Undo.CanRedo);

        Undo.RegisterOperation(new UndoTestOp("New", () => { }, () => { }));
        Undo.FlushUndoRecordObjects(); Undo.IncrementCurrentGroup();

        Assert.False(Undo.CanRedo);
    }

    // ---- Generic actions ----

    // ---- Structural: create ----

    [Fact]
    public void RegisterCreatedObject_Undo_Destroys_Redo_Recreates()
    {
        var scene = new SceneInstance(SceneMode.Edit);
        SceneInstance.Load(scene);
        SceneInstance.ProcessPendingLoad();
        Undo.Clear();

        var go = new GameObject("Created");
        go.AddComponent<UndoComp>().Value = 7;
        scene.Add(go);
        Guid goId = go.Identifier;

        Undo.RegisterCreatedObjectUndo(go, "Create");
        Undo.FlushUndoRecordObjects(); Undo.IncrementCurrentGroup();

        Undo.PerformUndo();
        Assert.Null(FindGO(goId));

        Undo.PerformRedo();
        GameObject? restored = FindGO(goId);
        Assert.NotNull(restored);
        Assert.Equal(7, restored!.GetComponent<UndoComp>()!.Value);
    }

    // ---- Structural: destroy (the lost-work case) ----

    [Fact]
    public void RegisterDestroyObject_Undo_RestoresComponentDataAndIdentifier()
    {
        (SceneInstance? scene, GameObject? go, UndoComp? comp) = MakeScene();
        comp.Value = 42; comp.Label = "hi";
        Guid goId = go.Identifier;
        Guid compId = comp.Identifier;

        Undo.DestroyObjectImmediate(go);
        Undo.FlushUndoRecordObjects(); Undo.IncrementCurrentGroup();

        Assert.Null(FindGO(goId));

        Undo.PerformUndo();
        GameObject? restored = FindGO(goId);
        Assert.NotNull(restored);
        UndoComp rc = restored!.GetComponent<UndoComp>()!;
        Assert.Equal(42, rc.Value);
        Assert.Equal("hi", rc.Label);
        Assert.Equal(compId, rc.Identifier); // identifier preserved so future records still resolve
    }

    [Fact]
    public void RegisterDestroyObject_Undo_BringsBackARuntimeMaterialNothingElseHeld()
    {
        (SceneInstance? scene, GameObject? go, UndoComp _) = MakeScene();
        GiveRuntimeMaterial(go);
        Guid goId = go.Identifier;

        Undo.DestroyObjectImmediate(go);
        Undo.FlushUndoRecordObjects(); Undo.IncrementCurrentGroup();
        GC.Collect();
        GC.WaitForPendingFinalizers();
        GC.Collect();

        Undo.PerformUndo();

        Assert.Equal("Runtime", FindGO(goId)!.GetComponent<MeshRenderer>()!.Material!.Name);
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
        var source = new SceneInstance(SceneMode.Edit);
        var target = new GameObject("Target");
        var holder = new GameObject("Holder");
        UndoLinkComp link = holder.AddComponent<UndoLinkComp>();
        link.Target = target;
        link.Value = 5;
        source.Add(target);
        source.Add(holder);
        string whileMissing = source.CaptureTree().WriteToString().Replace(nameof(UndoLinkComp), "Ghost_DoesNotExist");

        SceneInstance scene = SceneInstance.Instantiate(SceneContent.Read(Echo.EchoObject.ReadFromString(whileMissing)), SceneMode.Edit);
        SceneInstance.Load(scene);
        SceneInstance.ProcessPendingLoad();
        GameObject loadedHolder = scene.AllObjects.Single(g => g.Name == "Holder");

        Undo.DestroyObjectImmediate(loadedHolder);
        Undo.FlushUndoRecordObjects(); Undo.IncrementCurrentGroup();
        Undo.PerformUndo();

        string saved = SceneInstance.Main.CaptureTree().WriteToString().Replace("Ghost_DoesNotExist", nameof(UndoLinkComp));
        SceneInstance restored = SceneInstance.Instantiate(SceneContent.Read(Echo.EchoObject.ReadFromString(saved)), SceneMode.Edit);
        UndoLinkComp restoredLink = restored.AllObjects.Single(g => g.Name == "Holder").GetComponent<UndoLinkComp>()!;

        Assert.Equal(5, restoredLink.Value);
        Assert.Same(restored.AllObjects.Single(g => g.Name == "Target"), restoredLink.Target);
    }

    [Fact]
    public void RegisterDestroyObject_Undo_RestoresChildren()
    {
        var scene = new SceneInstance(SceneMode.Edit);
        SceneInstance.Load(scene);
        SceneInstance.ProcessPendingLoad();
        Undo.Clear();

        var parent = new GameObject("Parent");
        parent.AddComponent<UndoComp>().Value = 1;
        var child = new GameObject("Child");
        child.AddComponent<UndoComp>().Value = 2;
        child.SetParent(parent);
        scene.Add(parent);

        Guid parentId = parent.Identifier;
        Guid childId = child.Identifier;

        Undo.DestroyObjectImmediate(parent);
        Undo.FlushUndoRecordObjects(); Undo.IncrementCurrentGroup();

        Undo.PerformUndo();
        GameObject? rParent = FindGO(parentId);
        Assert.NotNull(rParent);
        Assert.Single(rParent!.Children);
        GameObject rChild = rParent.Children[0];
        Assert.Equal(childId, rChild.Identifier);
        Assert.Equal(2, rChild.GetComponent<UndoComp>()!.Value);
    }

    [Fact]
    public void PropertyUndo_SurvivesDestroyAndRecreate()
    {
        (SceneInstance? scene, GameObject? go, UndoComp? comp) = MakeScene();
        comp.Value = 10;
        Undo.RecordObject(comp, "Edit");
        comp.Value = 20;
        Undo.FlushUndoRecordObjects(); Undo.IncrementCurrentGroup(); // property step (resolves comp by identifier)

        Guid goId = go.Identifier;
        Undo.DestroyObjectImmediate(go);
        Undo.FlushUndoRecordObjects(); Undo.IncrementCurrentGroup(); // destroy step

        Undo.PerformUndo(); // undo destroy -> recreate go (same identifiers), comp.Value == 20
        GameObject? restored = FindGO(goId);
        Assert.NotNull(restored);
        Assert.Equal(20, restored!.GetComponent<UndoComp>()!.Value);

        Undo.PerformUndo(); // undo property change -> resolves the RECREATED comp by identifier
        Assert.Equal(10, restored.GetComponent<UndoComp>()!.Value);
    }

    // ---- Coalescing ----

    [Fact]
    public void PropertyEditsInOneGroup_AreOneStep()
    {
        (SceneInstance _, GameObject _, UndoComp? comp) = MakeScene();
        comp.Value = 0;
        Undo.RecordObject(comp, "Edit");
        comp.Value = 1;
        Undo.FlushUndoRecordObjects(); // one frame of a drag

        Undo.RecordObject(comp, "Edit");
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
                Undo.RegisterOperation(new UndoTestOp($"a{i}", () => { }, () => { }));
                Undo.FlushUndoRecordObjects(); Undo.IncrementCurrentGroup();
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
        (SceneInstance _, GameObject _, UndoComp? comp) = MakeScene();
        comp.Value = 0; Undo.RecordObject(comp, "Edit"); comp.Value = 1; Undo.FlushUndoRecordObjects(); Undo.IncrementCurrentGroup();
        Assert.True(Undo.CanUndo);

        Undo.Clear();

        Assert.False(Undo.CanUndo);
        Assert.False(Undo.CanRedo);
    }

    // ---- Continuous (gizmo drag) ----

    // ---- Interaction between actions and snapshots ----

    // ---- Play-mode gating ----

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
        (SceneInstance _, GameObject _, UndoComp? comp) = MakeScene();
        comp.Value = 0;
        Undo.RecordObject(comp, "Edit");
        comp.Value = 1;
        Undo.FlushUndoRecordObjects(); Undo.IncrementCurrentGroup();

        Undo.RecordObject(comp, "Edit");
        comp.Value = 2;
        Undo.FlushUndoRecordObjects(); Undo.IncrementCurrentGroup();

        // Two groups, two steps.
        Undo.PerformUndo();
        Assert.Equal(1, comp.Value);
        Undo.PerformUndo();
        Assert.Equal(0, comp.Value);
    }

    // ---- Multi-object helpers ----

    // ---- Robustness ----

    [Fact]
    public void PropertyUndo_OnDestroyedTarget_IsSafeNoOp()
    {
        (SceneInstance? scene, GameObject? go, UndoComp? comp) = MakeScene();
        comp.Value = 0;
        Undo.RecordObject(comp, "Edit");
        comp.Value = 1;
        Undo.FlushUndoRecordObjects(); Undo.IncrementCurrentGroup();

        DestroyGO(scene, go); // destroy without registering undo - the record's target is now gone

        Undo.PerformUndo(); // must not throw; resolves to null and no-ops
        Assert.True(Undo.CanRedo);
    }

    // ---------------------------------------------------------------------
    // A session that edits something other than the open scene borrows the history
    // ---------------------------------------------------------------------

    [Fact]
    public void PushContext_SetsTheHistoryAsideAndPopBringsItBack()
    {
        int value = 0;
        Undo.RegisterOperation(new UndoTestOp("Scene Edit", () => value = 1, () => value = 2));
        Undo.FlushUndoRecordObjects(); Undo.IncrementCurrentGroup();
        Assert.True(Undo.CanUndo);

        object saved = Undo.PushContext();
        Assert.False(Undo.CanUndo); // the session starts with nothing behind it

        int sessionValue = 0;
        Undo.RegisterOperation(new UndoTestOp("Session Edit", () => sessionValue = 1, () => sessionValue = 2));
        Undo.FlushUndoRecordObjects(); Undo.IncrementCurrentGroup();
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
        Undo.RegisterOperation(new UndoTestOp("Edit", () => { }, () => { }));
        Undo.FlushUndoRecordObjects(); Undo.IncrementCurrentGroup();
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

    private static SceneInstance OpenScene()
    {
        var scene = new SceneInstance(SceneMode.Edit);
        SceneInstance.Load(scene);
        SceneInstance.ProcessPendingLoad();
        Undo.ClearAll();
        EditorSceneManager.IsDirty = false;
        return scene;
    }

    private static GameObject Add(SceneInstance scene, string name, GameObject? parent = null)
    {
        var go = new GameObject(name);
        if (parent.IsValid()) go.SetParent(parent, worldPositionStays: false);
        else scene.Add(go);
        return go;
    }

    private static T Live<T>(Guid id) where T : EngineObject => SceneInstance.Main.FindObjectByIdentifier<T>(id)!;

    [Fact]
    public void DestroyedTogether_ReferenceEachOthersLiveInstancesOnUndo()
    {
        SceneInstance scene = OpenScene();
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
        SceneInstance scene = OpenScene();
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
        SceneInstance scene = OpenScene();
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
        SceneInstance scene = OpenScene();
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
        SceneInstance scene = OpenScene();
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
        SceneInstance scene = OpenScene();
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
        SceneInstance scene = OpenScene();
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
        SceneInstance scene = OpenScene();
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
        SceneInstance scene = OpenScene();
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
        SceneInstance scene = OpenScene();
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
        SceneInstance scene = OpenScene();
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
        SceneInstance scene = OpenScene();
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
        SceneInstance scene = OpenScene();
        UndoRefComp comp = Add(scene, "GO").AddComponent<UndoRefComp>();
        comp.Number = float.NaN;

        Undo.RecordObject(comp, "Nothing");
        Undo.FlushUndoRecordObjects();

        Assert.False(Undo.CanUndo);
    }

    [Fact]
    public void DestroyedTarget_UndoRebindsEveryHolderToTheLiveInstance()
    {
        SceneInstance scene = OpenScene();
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
        SceneInstance scene = OpenScene();
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
        SceneInstance scene = OpenScene();
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
        SceneInstance scene = OpenScene();
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
        SceneInstance scene = OpenScene();
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
        SceneInstance scene = OpenScene();
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
        SceneInstance scene = OpenScene();
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
        SceneInstance scene = OpenScene();
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
        SceneInstance scene = OpenScene();
        GameObject go = Add(scene, "GO");
        UndoComp comp = Undo.AddComponent<UndoComp>(go);
        Undo.DestroyObjectImmediate(comp);
        Undo.FlushUndoRecordObjects();

        Assert.False(Undo.CanUndo);
    }

    [Fact]
    public void HierarchyRecord_UndoRemovesChildrenAddedAfterIt()
    {
        SceneInstance scene = OpenScene();
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
        SceneInstance scene = OpenScene();
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
        SceneInstance scene = OpenScene();
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
        SceneInstance scene = OpenScene();
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
        SceneInstance scene = OpenScene();
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
        SceneInstance scene = OpenScene();
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
        SceneInstance scene = OpenScene();
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
        SceneInstance scene = OpenScene();
        UndoComp comp = Add(scene, "GO").AddComponent<UndoComp>();

        Frame(edits: () => Undo.InspectorBaseline(comp));
        Frame();

        Assert.False(Undo.CanUndo);
        Assert.False(EditorSceneManager.IsDirty);
    }

    [Fact]
    public void InspectorEdit_RecordsAndMarksDirty_WithoutWhatTheGameChangedAfter()
    {
        SceneInstance scene = OpenScene();
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
        SceneInstance scene = OpenScene();
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
        SceneInstance scene = OpenScene();
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
        SceneInstance scene = OpenScene();
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
        SceneInstance scene = OpenScene();
        Frame(mouseDown: true);
        Assert.True(Undo.IgnoresUndoInput);
        Frame();
        Assert.False(Undo.IgnoresUndoInput);
    }

    [Fact]
    public void ContextClearedWhileSetAside_PopsBackEmpty()
    {
        SceneInstance scene = OpenScene();
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
        SceneInstance scene = OpenScene();
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
        Undo.FlushUndoRecordObjects(); Undo.IncrementCurrentGroup();
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
        SceneInstance scene = OpenScene();
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
    public void EmbeddedProfileEdit_RecordsAndMarksTheHoldersScene()
    {
        SceneInstance scene = OpenScene();
        Runtime.Rendering.Volume volume = Add(scene, "V").AddComponent<Runtime.Rendering.Volume>();
        var profile = new Runtime.Rendering.VolumeProfile { Name = "Embedded" };
        volume.SharedProfile = profile;
        Type type = Inspector.VolumeProfileGUI.ComponentTypes[0];
        EditorSceneManager.IsDirty = false;

        Undo.RecordObject(profile, UndoOwner.Of(volume), "Add Override");
        profile.Add(type);
        Undo.FlushUndoRecordObjects();
        Assert.True(EditorSceneManager.IsDirty);

        Undo.PerformUndo();
        Assert.False(profile.Has(type));
    }

    [Fact]
    public void JumpToAStepThatIsGone_DoesNothing()
    {
        SceneInstance scene = OpenScene();
        UndoComp comp = Add(scene, "GO").AddComponent<UndoComp>();
        Undo.RecordObject(comp, "Edit"); comp.Value = 1; Undo.FlushUndoRecordObjects(); Undo.IncrementCurrentGroup();

        Undo.PerformUndoTo(long.MaxValue);
        Undo.PerformRedoTo(long.MaxValue);

        Assert.Equal(1, comp.Value);
        Assert.Single(Undo.UndoSteps);
    }

    [Fact]
    public void SaveDirtyAssets_WritesEachDirtyAsset_ButNotDuringPlay()
    {
        OpenScene();
        Material material = CreateMaterial("M.mat", 1);
        Undo.RecordObject(material, "Edit");
        material.SetFloat("_Value", 3);
        Undo.FlushUndoRecordObjects();

        Application.IsPlaying = true;
        try { Assert.Null(EditorUtility.SaveDirtyAssets()); }
        finally { Application.IsPlaying = false; }
        Assert.True(EditorUtility.IsDirty(material));

        Assert.NotNull(EditorUtility.SaveDirtyAssets());
        Assert.False(EditorUtility.IsDirty(material));
        Assert.Equal(3, Load<Material>(material.AssetID)!.GetFloat("_Value"));
        Assert.Contains("3", File.ReadAllText(AssetAbsolutePath("M.mat")));
    }

    [Fact]
    public void NestedGridEdit_IsRecordedOnItsHolderComponent()
    {
        SceneInstance scene = OpenScene();
        GameObject target = Add(scene, "Target");
        UndoRefComp holder = Add(scene, "Holder").AddComponent<UndoRefComp>();

        Frame(edits: () =>
        {
            using (Undo.InspectorHolder(holder)) Undo.InspectorBaseline(holder.Nested);
            holder.Nested.Target = target;
            Undo.InspectorChanged(holder.Nested);
        });
        Frame();

        Assert.True(EditorSceneManager.IsDirty);
        Undo.PerformUndo();
        Assert.True(ReferenceEquals(holder.Nested.Target, null));
    }

    private sealed class SettingsProbe : Inspector.ImportSettingsEditor
    {
        public override void OnGUI(PaperUI.Paper paper, string id, AssetEntry entry, EngineObject? asset) { }
        public Echo.EchoObject Draw(AssetEntry entry) => Settings(entry);
    }

    [Fact]
    public void ImportSettingsChangedInTheInspector_Undo()
    {
        OpenScene();
        Material material = CreateMaterial("M.mat", 1);
        AssetEntry entry = Assets.GetEntry(material.AssetID)!;
        var editor = new SettingsProbe();

        editor.Draw(entry)["Quality"] = new Echo.EchoObject(7);
        Undo.FlushUndoRecordObjects();
        Undo.IncrementCurrentGroup();
        editor.Draw(entry);
        Undo.FlushUndoRecordObjects();

        Undo.PerformUndo();
        Assert.False(Inspector.ImportSettingsEditor.Pending(material.AssetID)!.TryGet("Quality", out _));
        Assert.Single(Undo.RedoSteps);
    }

    [Fact]
    public void ProjectSettingsEdit_IsRecordedOnTheNextFrame()
    {
        OpenScene();
        EditorRegistries.SettingsEntry entry = EditorRegistries.SettingsEntries.First(e => e.Type == typeof(Projects.Settings.TimeSettings));
        var time = (Projects.Settings.TimeSettings)entry.Instance;
        int original = time.MaxFixedIterations;
        try
        {
            GUI.Panels.ProjectSettingsPanel.TrackEdits(entry);
            time.MaxFixedIterations = original + 2;
            Time.CurrentTime.Advance(0.016f);
            GUI.Panels.ProjectSettingsPanel.TrackEdits(entry);
            Undo.FlushUndoRecordObjects();

            Assert.Equal("Undo Modify Time", Undo.UndoDescription);
            Undo.PerformUndo();
            Assert.Equal(original, time.MaxFixedIterations);
            Undo.PerformRedo();
            Assert.Equal(original + 2, time.MaxFixedIterations);
        }
        finally
        {
            time.MaxFixedIterations = original;
            time.Apply();
        }
    }

    [Fact]
    public void ProjectSettingsChangedWhilePanelHidden_IsNotRecorded()
    {
        OpenScene();
        EditorRegistries.SettingsEntry entry = EditorRegistries.SettingsEntries.First(e => e.Type == typeof(Projects.Settings.TimeSettings));
        var time = (Projects.Settings.TimeSettings)entry.Instance;
        int original = time.MaxFixedIterations;
        try
        {
            GUI.Panels.ProjectSettingsPanel.TrackEdits(entry);
            Time.CurrentTime.Advance(0.016f);
            time.MaxFixedIterations = original + 2;
            Time.CurrentTime.Advance(0.016f);
            GUI.Panels.ProjectSettingsPanel.TrackEdits(entry);
            Undo.FlushUndoRecordObjects();

            Assert.False(Undo.CanUndo);
        }
        finally
        {
            time.MaxFixedIterations = original;
            time.Apply();
        }
    }

    [Fact]
    public void GenericImportSettingEdit_LandsOnThePendingSettingsAndUndoes()
    {
        OpenScene();
        Material material = CreateMaterial("M.mat", 1);
        Guid guid = material.AssetID;

        Frame(mouseDown: true, edits: () => GUI.Panels.InspectorPanel.SetImportSetting(guid, "Quality", new Echo.EchoObject(7)));
        Frame();

        Assert.Equal(7, Inspector.ImportSettingsEditor.Pending(guid)!["Quality"].IntValue);
        Assert.Single(Undo.UndoSteps);

        Undo.PerformUndo();
        Assert.False(Inspector.ImportSettingsEditor.Pending(guid)!.TryGet("Quality", out _));

        Undo.PerformRedo();
        Assert.Equal(7, Inspector.ImportSettingsEditor.Pending(guid)!["Quality"].IntValue);
    }

    [Fact]
    public void GenericImportSettingSave_WritesThePendingSettingsToTheMeta()
    {
        OpenScene();
        Material material = CreateMaterial("M.mat", 1);
        Guid guid = material.AssetID;
        string metaPath = MetaFile.GetMetaPath(AssetAbsolutePath(Assets.GetEntry(guid)!.Path));

        Frame(mouseDown: true, edits: () => GUI.Panels.InspectorPanel.SetImportSetting(guid, "Quality", new Echo.EchoObject(7)));
        GUI.Panels.InspectorPanel.SaveImportSettings(Assets, guid, metaPath);

        Assert.Equal(7, MetaFile.Read(metaPath).Settings!["Quality"].IntValue);
    }

    [Fact]
    public void SpriteSettingEdit_IsOneStepWithTheImportSettingsAndUndoRestoresTheSharedSprite()
    {
        OpenScene();
        Material material = CreateMaterial("M.mat", 1);
        Guid guid = material.AssetID;
        Importers.SpriteEditTarget sprite = Importers.SpriteEditRegistry.Get(guid);
        float ppu = sprite.Settings.PixelsPerUnit;

        Frame(mouseDown: true, edits: () =>
            Inspector.TextureAssetEditor.EditSprite(guid, sprite, "Pixels Per Unit", s => s.PixelsPerUnit = ppu * 2));
        Frame();

        Assert.Single(Undo.UndoSteps);
        Assert.Equal(ppu * 2, Importers.TextureSpriteMeta.ReadFrom(Inspector.ImportSettingsEditor.Pending(guid)).PixelsPerUnit);

        Undo.PerformUndo();
        Importers.SpriteEditTarget restored = Importers.SpriteEditRegistry.Get(guid);
        Assert.Equal(ppu, restored.Settings.PixelsPerUnit);
        Assert.True(restored.Dirty);
        Assert.False(Inspector.ImportSettingsEditor.Pending(guid)!.TryGet("sprite", out _));

        Undo.PerformRedo();
        Assert.Equal(ppu * 2, Importers.SpriteEditRegistry.Get(guid).Settings.PixelsPerUnit);
        Assert.Equal(ppu * 2, Importers.TextureSpriteMeta.ReadFrom(Inspector.ImportSettingsEditor.Pending(guid)).PixelsPerUnit);
    }

    [Fact]
    public void AnimationGraphNodesHoldEverySettingBeforeAnyDraw()
    {
        EditorRegistries.Initialize();
        var graph = new AnimationGraph();
        GraphNodeRecord loaded = graph.AddNode(AnimationNodeIds.Clip);
        GraphNodeRecord legacyOutput = graph.AddNode(AnimationNodeIds.StateOutput);

        var domain = new Inspector.AnimationGraphDomain(graph);
        GraphNodeRecord created = domain.Create(AnimationNodeIds.Clip, Float2.Zero, "")!;

        foreach (NodeSetting setting in AnimationNodeRegistry.Get(AnimationNodeIds.Clip)!.Properties)
        {
            Assert.True(loaded.Properties.ContainsKey(setting.Key));
            Assert.True(created.Properties.ContainsKey(setting.Key));
        }
        Assert.Equal(3, legacyOutput.Inputs.Count);
        Assert.False(legacyOutput.Inputs[1].Flag);
        Assert.True(legacyOutput.Inputs[2].Flag);
    }

    [Fact]
    public void UndoOfAnEditInTheOpenGroup_DropsTheStaleRedoUnderIt()
    {
        SceneInstance scene = OpenScene();
        UndoComp comp = Add(scene, "GO").AddComponent<UndoComp>();
        Undo.RecordObject(comp, "A"); comp.Value = 1; Undo.FlushUndoRecordObjects(); Undo.IncrementCurrentGroup();
        Undo.PerformUndo();

        Undo.RecordObject(comp, "B");
        comp.Value = 5;
        Undo.FlushUndoRecordObjects(); // pushed while its group is still open, as typing into a focused field does
        Undo.PerformUndo();

        Assert.Single(Undo.RedoSteps);
        Undo.PerformRedo();
        Assert.Equal(5, comp.Value);
        Undo.PerformRedo();
        Assert.Equal(5, comp.Value);
    }

    [Fact]
    public void HierarchyRecordWithNoChange_IsNotAStep()
    {
        SceneInstance scene = OpenScene();
        GameObject root = Add(scene, "Root");
        Add(scene, "Child", root);

        Undo.RegisterFullObjectHierarchyUndo(root, "Revert");
        Undo.FlushUndoRecordObjects();

        Assert.False(Undo.CanUndo);
        Assert.False(EditorSceneManager.IsDirty);
    }

    [Fact]
    public void CurrentGroupName_NamesItsStep()
    {
        SceneInstance scene = OpenScene();
        UndoComp comp = Add(scene, "GO").AddComponent<UndoComp>();

        Undo.SetCurrentGroupName("Do The Thing");
        Undo.RecordObject(comp, "Edit");
        comp.Value = 1;
        Undo.FlushUndoRecordObjects();

        Assert.Equal("Do The Thing", Undo.UndoSteps[^1].Name);
    }

    [Fact]
    public void ToolScopeOverManyFrames_IsOneStepFromFirstToLastState()
    {
        SceneInstance scene = OpenScene();
        GameObject go = Add(scene, "GO");
        var scope = new ToolUndoScope("Tool", [go]);
        for (int frame = 1; frame <= 3; frame++)
        {
            Frame(edits: () => go.Transform.LocalPosition = new Float3(frame, 0, 0));
        }
        scope.Dispose();
        Frame();

        Undo.PerformUndo();
        Assert.Equal(Float3.Zero, go.Transform.LocalPosition);
        Undo.PerformRedo();
        Assert.Equal(new Float3(3, 0, 0), go.Transform.LocalPosition);
    }

    [Fact]
    public void WatchedObject_RecordsOnlyRealChanges_AndIsNoInteraction()
    {
        SceneInstance scene = OpenScene();
        UndoComp comp = Add(scene, "GO").AddComponent<UndoComp>();

        Frame(mouseDown: true, edits: () => Undo.WatchObject(comp, "Watch"));
        Assert.False(Undo.InteractionActive);
        Frame(mouseHeld: true, edits: () => Undo.WatchObject(comp, "Watch"));
        Frame();

        Assert.False(Undo.CanUndo);
    }
}
