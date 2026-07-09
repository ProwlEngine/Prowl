// This file is part of the Prowl Game Engine
// Licensed under the MIT License. See the LICENSE file in the project root for details.

using Prowl.Editor.Core;
using Prowl.Runtime;
using Prowl.Runtime.Resources;
using Prowl.Vector;

using Xunit;

namespace Prowl.Editor.Test;

/// <summary>A simple component with undoable fields.</summary>
public sealed class UndoComp : MonoBehaviour
{
    public int Value;
    public string Label = "";
}

/// <summary>
/// Tests for the editor <see cref="Undo"/> system - high stakes because a bug here means lost work.
/// Covers property snapshots, action records, structural create/destroy (full restore), identifier
/// survival across destroy/recreate, time-window coalescing, continuous (gizmo) operations, history
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
        var comp = go.AddComponent<UndoComp>();
        scene.Add(go);
        Scene.Load(scene);
        return (scene, go, comp);
    }

    private static void DestroyGO(Scene s, GameObject go)
    {
        foreach (var c in go.GetChildrenDeep().ToList())
            s.Remove(c);
        s.Remove(go);
        go.Dispose();
    }

    // ---- Property changes ----

    [Fact]
    public void PropertyChange_Undo_Redo()
    {
        var (_, _, comp) = MakeScene();
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
        var (_, _, comp) = MakeScene();
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
        var (_, _, comp) = MakeScene();
        comp.Value = 5;
        Undo.Snapshot(comp);
        // no mutation
        Undo.IncrementGroup();

        Assert.False(Undo.CanUndo);
    }

    [Fact]
    public void NewAction_ClearsRedoStack()
    {
        var (_, _, comp) = MakeScene();
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
        var restored = Undo.FindGO(goId);
        Assert.NotNull(restored);
        Assert.Equal(7, restored!.GetComponent<UndoComp>()!.Value);
    }

    // ---- Structural: destroy (the lost-work case) ----

    [Fact]
    public void RegisterDestroyObject_Undo_RestoresComponentDataAndIdentifier()
    {
        var (scene, go, comp) = MakeScene();
        comp.Value = 42; comp.Label = "hi";
        Guid goId = go.Identifier;
        Guid compId = comp.Identifier;

        Undo.RegisterDestroyObject(go, "Delete");
        DestroyGO(scene, go);
        Undo.IncrementGroup();

        Assert.Null(Undo.FindGO(goId));

        Undo.PerformUndo();
        var restored = Undo.FindGO(goId);
        Assert.NotNull(restored);
        var rc = restored!.GetComponent<UndoComp>()!;
        Assert.Equal(42, rc.Value);
        Assert.Equal("hi", rc.Label);
        Assert.Equal(compId, rc.Identifier); // identifier preserved so future records still resolve
    }

    [Fact]
    public void RegisterDestroyObject_Undo_RestoresChildren()
    {
        var scene = new Scene();
        Scene.Load(scene);
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
        var rParent = Undo.FindGO(parentId);
        Assert.NotNull(rParent);
        Assert.Single(rParent!.Children);
        var rChild = rParent.Children[0];
        Assert.Equal(childId, rChild.Identifier);
        Assert.Equal(2, rChild.GetComponent<UndoComp>()!.Value);
    }

    [Fact]
    public void PropertyUndo_SurvivesDestroyAndRecreate()
    {
        var (scene, go, comp) = MakeScene();
        comp.Value = 10;
        Undo.Snapshot(comp);
        comp.Value = 20;
        Undo.IncrementGroup(); // property step (resolves comp by identifier)

        Guid goId = go.Identifier;
        Undo.RegisterDestroyObject(go, "Delete");
        DestroyGO(scene, go);
        Undo.IncrementGroup(); // destroy step

        Undo.PerformUndo(); // undo destroy -> recreate go (same identifiers), comp.Value == 20
        var restored = Undo.FindGO(goId);
        Assert.NotNull(restored);
        Assert.Equal(20, restored!.GetComponent<UndoComp>()!.Value);

        Undo.PerformUndo(); // undo property change -> resolves the RECREATED comp by identifier
        Assert.Equal(10, restored.GetComponent<UndoComp>()!.Value);
    }

    // ---- Coalescing ----

    [Fact]
    public void ContinuousPropertyEdits_CoalesceIntoOneStep()
    {
        var (_, _, comp) = MakeScene();
        comp.Value = 0;
        Undo.Snapshot(comp);
        comp.Value = 1;
        Undo.IncrementGroup(); // step: before 0, after 1

        Undo.Snapshot(comp);
        comp.Value = 2;
        Undo.IncrementGroup(); // coalesces (chain 1->2, same target, within window)

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
        var (_, _, comp) = MakeScene();
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
        var (_, go, _) = MakeScene();
        go.Transform.LocalPosition = new Float3(0, 0, 0);

        Undo.BeginContinuous(new[] { go }, "Move");
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
        var (_, go, _) = MakeScene();
        go.Transform.LocalPosition = new Float3(0, 0, 0);

        Undo.BeginContinuous(new[] { go }, "Move");
        go.Transform.LocalPosition = new Float3(5, 0, 0);
        Undo.CancelContinuous();

        Assert.Equal(0, go.Transform.LocalPosition.X, 4);
        Assert.False(Undo.CanUndo);
    }

    [Fact]
    public void PerformUndo_DuringContinuous_CancelsInsteadOfUndoing()
    {
        var (_, go, _) = MakeScene();
        go.Transform.LocalPosition = new Float3(0, 0, 0);

        Undo.BeginContinuous(new[] { go }, "Move");
        go.Transform.LocalPosition = new Float3(5, 0, 0);
        Undo.PerformUndo(); // cancels the drag rather than popping the stack

        Assert.Equal(0, go.Transform.LocalPosition.X, 4);
        Assert.False(Undo.IsContinuous);
        Assert.False(Undo.CanUndo);
    }

    [Fact]
    public void Continuous_NoMovement_PushesNoStep()
    {
        var (_, go, _) = MakeScene();
        Undo.BeginContinuous(new[] { go }, "Move");
        // no movement
        Undo.EndContinuous();

        Assert.False(Undo.CanUndo);
    }

    // ---- Interaction between actions and snapshots ----

    [Fact]
    public void ActionAndSnapshotSameFrame_PropertySnapshotIsDiscarded()
    {
        var (_, _, comp) = MakeScene();
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
            var (_, _, comp) = MakeScene();
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
    public void PropertyEdits_OutsideTimeWindow_StayAsSeparateSteps()
    {
        var (_, _, comp) = MakeScene();
        comp.Value = 0;
        Undo.Snapshot(comp);
        comp.Value = 1;
        Undo.IncrementGroup();

        System.Threading.Thread.Sleep(350); // exceed the 300ms coalesce window

        Undo.Snapshot(comp);
        comp.Value = 2;
        Undo.IncrementGroup();

        // Two distinct steps - granular undo must survive a time gap, not collapse into one.
        Undo.PerformUndo();
        Assert.Equal(1, comp.Value);
        Undo.PerformUndo();
        Assert.Equal(0, comp.Value);
    }

    [Fact]
    public void RegisterCoalescableAction_MergesAndKeepsOriginalUndoLatestRedo()
    {
        MakeScene();
        int x = 1;
        Undo.RegisterCoalescableAction("Edit", () => x = 0, () => x = 1);
        Undo.IncrementGroup();

        x = 2;
        Undo.RegisterCoalescableAction("Edit", () => x = 0, () => x = 2); // coalesces inline with the top

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
        var (scene, go, comp) = MakeScene();
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
        Undo.Clear();
        a.Transform.LocalPosition = Float3.Zero;
        b.Transform.LocalPosition = Float3.Zero;

        Undo.BeginContinuous(new[] { a, b }, "Move");
        a.Transform.LocalPosition = new Float3(5, 0, 0);
        b.Transform.LocalPosition = new Float3(0, 7, 0);
        Undo.EndContinuous();

        Undo.PerformUndo();
        Assert.Equal(0, a.Transform.LocalPosition.X, 4);
        Assert.Equal(0, b.Transform.LocalPosition.Y, 4);
        Assert.False(Undo.CanUndo); // one step for both targets
    }
}
