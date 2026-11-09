// This file is part of the Prowl Game Engine
// Licensed under the MIT License. See the LICENSE file in the project root for details.

using Prowl.Editor.Core;
using Prowl.Editor.GUI.SceneView;
using Prowl.Runtime;
using Prowl.Runtime.Resources;

using Xunit;

namespace Prowl.Editor.Test;

/// <summary>Several scenes open at once as sections of the edit world: open, load, unload, remove, dirty marks, saving and setups.</summary>
public class MultiSceneTests : EditorTestHarness
{
    public MultiSceneTests() => Undo.ClearAll();

    public sealed class Marker : Component
    {
        public int Value;
        public GameObject? Target;
    }

    // A second file with the same content, so every identifier in it is shared with the first.
    private Scene Duplicate(string from, string to)
    {
        File.Copy(AssetAbsolutePath(from), AssetAbsolutePath(to));
        return AssetDatabase.Get<Scene>(Assets.ImportFile(to))!;
    }

    private Scene Author(string path, params string[] roots)
    {
        var world = new SceneInstance(SceneMode.Edit);
        foreach (string root in roots) world.Create(root).AddComponent<Marker>();
        return AssetDatabase.Get<Scene>(CreateSceneAsset(world, path))!;
    }

    private static OpenScene Open(Scene scene)
    {
        OpenScene open = EditorSceneManager.Open(scene, force: true);
        SceneInstance.ProcessPendingLoad();
        return open;
    }

    private static OpenScene Additive(Scene scene, bool load = true)
    {
        OpenScene open = EditorSceneManager.OpenAdditive(scene, load);
        SceneInstance.ProcessPendingLoad();
        return open;
    }

    private static GameObject Root(OpenScene scene) => Assert.Single(scene.Section!.Roots);

    [Fact]
    public void Open_ReplacesTheSetupAtTheSafePoint_InEventOrder()
    {
        OpenScene first = Open(Author("A.scene", "a"));
        List<string> events = [];
        void Closing(OpenScene s) => events.Add("closing " + s.Name);
        void Opened(OpenScene s) => events.Add("opened " + s.Name);
        void Loaded(OpenScene s) => events.Add("loaded " + s.Name);
        EditorSceneManager.SceneClosing += Closing;
        EditorSceneManager.SceneOpened += Opened;
        EditorSceneManager.SceneLoaded += Loaded;
        try
        {
            OpenScene second = EditorSceneManager.Open(Author("B.scene", "b"));
            Assert.False(second.IsLoaded);
            Assert.Null(second.Section);

            SceneInstance.ProcessPendingLoad();

            Assert.True(second.IsLoaded);
            Assert.True(second.IsActive);
            Assert.Equal([second], EditorSceneManager.OpenScenes);
            Assert.Equal(["closing A", "opened B", "loaded B"], events);
            Assert.False(first.IsLoaded);
            Assert.Equal("b", Root(second).Name);
        }
        finally
        {
            EditorSceneManager.SceneClosing -= Closing;
            EditorSceneManager.SceneOpened -= Opened;
            EditorSceneManager.SceneLoaded -= Loaded;
        }
    }

    [Fact]
    public void OpenAdditive_AddsASectionAfter_AndNeverListsOneSceneTwice()
    {
        OpenScene a = Open(Author("A.scene", "a"));
        Scene bAsset = Author("B.scene", "b");
        OpenScene b = Additive(bAsset);

        Assert.Equal([a, b], EditorSceneManager.OpenScenes);
        Assert.Equal([EditorSceneManager.EditWorld.Persistent, a.Section, b.Section], EditorSceneManager.EditWorld.Sections);
        Assert.True(a.IsActive);
        Assert.Same(b, Additive(bAsset));
    }

    [Fact]
    public void Unload_KeepsTheSceneListed_LoadBringsItBackInPlace()
    {
        OpenScene a = Open(Author("A.scene", "a"));
        OpenScene b = Additive(Author("B.scene", "b"));
        OpenScene c = Additive(Author("C.scene", "c"));

        EditorSceneManager.Unload(b);
        SceneInstance.ProcessPendingLoad();
        Assert.False(b.IsLoaded);
        Assert.Equal([a, b, c], EditorSceneManager.OpenScenes);
        Assert.DoesNotContain(EditorSceneManager.EditWorld.Objects, go => go.Name == "b");

        EditorSceneManager.Load(b);
        SceneInstance.ProcessPendingLoad();
        Assert.Equal([EditorSceneManager.EditWorld.Persistent, a.Section, b.Section, c.Section], EditorSceneManager.EditWorld.Sections);
    }

    [Fact]
    public void TheLastLoadedScene_CannotBeUnloadedOrRemoved()
    {
        OpenScene a = Open(Author("A.scene", "a"));
        OpenScene b = Additive(Author("B.scene", "b"), load: false);

        Assert.Throws<InvalidOperationException>(() => EditorSceneManager.Unload(a));
        Assert.Throws<InvalidOperationException>(() => EditorSceneManager.Remove(a));

        EditorSceneManager.Remove(b);
        Assert.Equal([a], EditorSceneManager.OpenScenes);
    }

    [Fact]
    public void DirtyScenes_AreRefusedUnlessForced()
    {
        OpenScene a = Open(Author("A.scene", "a"));
        OpenScene b = Additive(Author("B.scene", "b"));
        EditorSceneManager.MarkDirty(b);

        Assert.Throws<InvalidOperationException>(() => EditorSceneManager.Unload(b));
        Assert.Throws<InvalidOperationException>(() => EditorSceneManager.Remove(b));
        Assert.Throws<InvalidOperationException>(() => EditorSceneManager.Open(a.Scene!));

        EditorSceneManager.Unload(b, force: true);
        SceneInstance.ProcessPendingLoad();
        Assert.False(b.IsLoaded);
        Assert.False(b.IsDirty);
    }

    [Fact]
    public void AnEdit_MarksOnlyTheSceneOfWhatItChanged_AndNothingDuringPlay()
    {
        OpenScene a = Open(Author("A.scene", "a"));
        OpenScene b = Additive(Author("B.scene", "b"));
        Marker marker = Root(b).GetComponent<Marker>()!;

        Undo.RecordObject(marker, "Edit");
        marker.Value = 3;
        Undo.FlushUndoRecordObjects(); Undo.IncrementCurrentGroup();

        Assert.False(a.IsDirty);
        Assert.True(b.IsDirty);

        b.Dirty = false;
        using (EnterPlayMode())
            EditorSceneManager.MarkDirty(b);
        Assert.False(b.IsDirty);
    }

    [Fact]
    public void UnloadingAScene_DropsWholeStepsThatTouchIt_AndKeepsTheOthers()
    {
        OpenScene a = Open(Author("A.scene", "a"));
        OpenScene b = Additive(Author("B.scene", "b"));
        Marker inA = Root(a).GetComponent<Marker>()!;
        Marker inB = Root(b).GetComponent<Marker>()!;

        Undo.RecordObject(inA, "Edit A");
        inA.Value = 1;
        Undo.FlushUndoRecordObjects(); Undo.IncrementCurrentGroup();
        Undo.RecordObjects([inA, inB], "Edit Both");
        inA.Value = 2;
        inB.Value = 2;
        Undo.FlushUndoRecordObjects(); Undo.IncrementCurrentGroup();
        Undo.RecordObject(inB, "Edit B");
        inB.Value = 3;
        Undo.FlushUndoRecordObjects(); Undo.IncrementCurrentGroup();

        EditorSceneManager.Unload(b, force: true);
        SceneInstance.ProcessPendingLoad();

        Assert.Equal(["Edit A"], Undo.UndoSteps.Select(s => s.Name));
    }

    [Fact]
    public void Save_WritesOnlyThatScenesFile_AndSaveAsRebindsIt()
    {
        OpenScene a = Open(Author("A.scene", "a"));
        OpenScene b = Additive(Author("B.scene", "b"));
        Root(b).Name = "renamed";
        EditorSceneManager.MarkDirty(b);

        Assert.True(EditorSceneManager.Save(b));
        Assert.False(b.IsDirty);
        Assert.Contains("renamed", File.ReadAllText(AssetAbsolutePath("B.scene")));
        Assert.DoesNotContain("renamed", File.ReadAllText(AssetAbsolutePath("A.scene")));

        Assert.False(EditorSceneManager.SaveAs(b, "A.scene"));
        Assert.True(EditorSceneManager.SaveAs(b, "Copy.scene"));
        Assert.Equal("Copy.scene", b.Path);
        Assert.Same(b.Scene, b.Section!.Source);
    }

    [Fact]
    public void ASetup_RoundTrips_SkippingAMissingScene()
    {
        Scene aAsset = Author("A.scene", "a");
        Scene bAsset = Author("B.scene", "b");
        Scene cAsset = Author("C.scene", "c");
        Open(aAsset);
        OpenScene b = Additive(bAsset);
        Additive(cAsset, load: false);
        EditorSceneManager.SetActive(b);
        SceneSetup[] setup = EditorSceneManager.GetSetup();

        Open(Author("Other.scene", "o"));
        EditorSceneManager.RestoreSetup([.. setup, new SceneSetup { Asset = Guid.NewGuid(), Path = "Gone.scene", IsLoaded = true }]);
        SceneInstance.ProcessPendingLoad();

        Assert.Equal(["A", "B", "C"], EditorSceneManager.OpenScenes.Select(o => o.Name));
        Assert.Equal([true, true, false], EditorSceneManager.OpenScenes.Select(o => o.IsLoaded));
        Assert.Equal("B", EditorSceneManager.ActiveScene.Name);
        Assert.Equal(["a", "b"], EditorSceneManager.EditWorld.Sections.Skip(1).Select(s => Assert.Single(s.Roots).Name));
    }

    [Fact]
    public void AnAdditiveNewScene_IsEmpty()
    {
        Open(Author("A.scene", "a"));
        OpenScene untitled = EditorSceneManager.New(additive: true);
        SceneInstance.ProcessPendingLoad();

        Assert.True(untitled.IsLoaded);
        Assert.Null(untitled.Path);
        Assert.Empty(untitled.Section!.Roots);
    }

    [Fact]
    public void Move_ReordersTheSectionsWithTheScenes()
    {
        OpenScene a = Open(Author("A.scene", "a"));
        OpenScene b = Additive(Author("B.scene", "b"));

        EditorSceneManager.Move(b, 0);

        Assert.Equal([b, a], EditorSceneManager.OpenScenes);
        Assert.Equal([EditorSceneManager.EditWorld.Persistent, b.Section, a.Section], EditorSceneManager.EditWorld.Sections);
    }

    [Fact]
    public void PlayMode_CopiesTheLoadedScenesInOrder_WithTheSameActiveOne()
    {
        Open(Author("A.scene", "a"));
        OpenScene b = Additive(Author("B.scene", "b"));
        Additive(Author("C.scene", "c"), load: false);
        EditorSceneManager.SetActive(b);

        SceneInstance play = EditorSceneManager.BuildPlayWorld();
        try
        {
            Assert.Equal(["A", "B"], play.Sections.Skip(1).Select(s => s.Name));
            Assert.Equal("B", play.ActiveSection.Name);
            Assert.Same(b.Scene, play.ActiveSection.Source);
        }
        finally { play.Dispose(); }
    }

    [Fact]
    public void AnEnvironmentEdit_MarksTheSceneThatOwnsIt_EvenAfterTheActiveSceneChanged()
    {
        OpenScene a = Open(Author("A.scene", "a"));
        OpenScene b = Additive(Author("B.scene", "b"));
        SceneSection section = a.Section!;

        Undo.RecordObject(() => section.IsLoaded ? section.Environment : null, UndoOwner.OfScene(a), "Fog");
        section.Environment.Fog.Density = 0.5f;
        Undo.FlushUndoRecordObjects(); Undo.IncrementCurrentGroup();
        a.Dirty = false;

        EditorSceneManager.SetActive(b);
        Undo.PerformUndo();

        Assert.True(a.IsDirty);
        Assert.False(b.IsDirty);
    }

    [Fact]
    public void DuplicatedScenes_NeverResolveToEachOther()
    {
        OpenScene a = Open(Author("A.scene", "a"));
        OpenScene copy = Additive(Duplicate("A.scene", "Copy.scene"));
        Marker inA = Root(a).GetComponent<Marker>()!;
        Marker inCopy = Root(copy).GetComponent<Marker>()!;
        Assert.Equal(inA.Identifier, inCopy.Identifier);

        Undo.RecordObject(inCopy, "Edit Copy");
        inCopy.Value = 7;
        Undo.FlushUndoRecordObjects(); Undo.IncrementCurrentGroup();
        inA.Value = 5;

        Undo.PerformUndo();

        Assert.Equal(0, inCopy.Value);
        Assert.Equal(5, inA.Value);
    }

    [Fact]
    public void MovingARootBetweenScenes_IsOneStep_ThatGivesItsOldIdentifierBack()
    {
        OpenScene a = Open(Author("A.scene", "a"));
        OpenScene copy = Additive(Duplicate("A.scene", "Copy.scene"));
        GameObject moving = Root(copy);
        Guid original = moving.Identifier;

        Undo.MoveGameObjectToScene(moving, a, "Move");
        Undo.FlushUndoRecordObjects(); Undo.IncrementCurrentGroup();
        Guid fresh = moving.Identifier;
        Assert.NotEqual(original, fresh);
        Assert.Same(a.Section, moving.Section);
        Assert.True(copy.IsDirty);
        Assert.True(a.IsDirty);

        Undo.PerformUndo();
        Assert.Same(copy.Section, moving.Section);
        Assert.Equal(original, moving.Identifier);

        Undo.PerformRedo();
        Assert.Same(a.Section, moving.Section);
        Assert.Equal(fresh, moving.Identifier);
    }

    [Fact]
    public void ReparentingUnderAnObjectInAnotherScene_MovesIt_AndUndoesAsOneStep()
    {
        OpenScene a = Open(Author("A.scene", "a"));
        OpenScene b = Additive(Author("B.scene", "b"));
        GameObject child = Root(b);
        GameObject parent = Root(a);

        Undo.SetTransformParent(child, parent, true, "Parent");
        Undo.FlushUndoRecordObjects(); Undo.IncrementCurrentGroup();
        Assert.Same(parent, child.Parent);
        Assert.Same(a.Section, child.Section);

        Undo.PerformUndo();
        Assert.False(child.Parent.IsValid());
        Assert.Same(b.Section, child.Section);
        Assert.Single(Undo.RedoSteps);
    }

    [Fact]
    public void AMove_ClearsReferencesThatWouldCrossScenes_AndUndoBringsThemBack()
    {
        var world = new SceneInstance(SceneMode.Edit);
        GameObject door = world.Create("door");
        world.Create("holder").AddComponent<Marker>().Target = door;
        OpenScene a = Open(AssetDatabase.Get<Scene>(CreateSceneAsset(world, "A.scene"))!);
        OpenScene b = Additive(Author("B.scene", "b"));
        Marker holder = a.Section!.Roots.Single(r => r.Name == "holder").GetComponent<Marker>()!;
        GameObject liveDoor = holder.Target!;

        Undo.MoveGameObjectToScene(liveDoor, b, "Move");
        Undo.FlushUndoRecordObjects(); Undo.IncrementCurrentGroup();
        Assert.False(holder.Target.IsValid());

        Undo.PerformUndo();
        Assert.Same(liveDoor, holder.Target);
        Assert.Same(a.Section, liveDoor.Section);
    }

    [Fact]
    public void SaveAs_KeepsTheHistoryValid()
    {
        Open(Author("A.scene", "a"));
        OpenScene b = Additive(Author("B.scene", "b"));
        Marker marker = Root(b).GetComponent<Marker>()!;
        Undo.RecordObject(marker, "Edit");
        marker.Value = 4;
        Undo.FlushUndoRecordObjects(); Undo.IncrementCurrentGroup();

        Assert.True(EditorSceneManager.SaveAs(b, "Renamed.scene"));
        Undo.PerformUndo();

        Assert.Equal(0, marker.Value);
    }
}
