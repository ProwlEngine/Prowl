// This file is part of the Prowl Game Engine
// Licensed under the MIT License. See the LICENSE file in the project root for details.

using System.Threading;

using Prowl.Editor.Core;
using Prowl.Editor.GUI.SceneView;
using Prowl.Runtime;
using Prowl.Runtime.Resources;
using Prowl.Runtime.Tasks;

using Xunit;

namespace Prowl.Editor.Test;

/// <summary>The editor on worlds: play mode on a copy over a dormant edit world, and prefab editing in its own world.</summary>
public class EditorWorldTests : EditorTestHarness
{
    public sealed class Marker : Component
    {
        public int Value = 1;
        public CancellationToken DisabledIn;
        public override void OnDisable() => DisabledIn = GameTask.SessionToken;
    }

    [Fact]
    public void PlayMode_RunsACopyOfEverySection_AndLeavesTheEditWorldUntouched()
    {
        SceneInstance edit = EditorSceneManager.EditWorld;
        GameObject go = edit.Create("Thing");
        Marker marker = go.AddComponent<Marker>();
        SceneSection extra = edit.CreateSection("Extra");
        edit.Create(extra, "Other");

        SceneInstance play = EditorSceneManager.BuildPlayWorld();
        using (EnterPlayMode())
        {
            EditorSceneManager.EnterPlayWorld(play);

            Assert.Equal(SceneState.Dormant, edit.State);
            Assert.Same(play, SceneInstance.Main);
            Assert.Same(play, EditorSceneManager.EditedWorld);
            Assert.Equal(["Persistent", edit.ActiveSection.Name, "Extra"], play.Sections.Select(s => s.Name));
            Assert.Equal(edit.ActiveSection.Name, play.ActiveSection.Name);

            GameObject copy = play.FindObject<GameObject>(go.Identifier)!;
            Assert.NotSame(go, copy);
            copy.GetComponent<Marker>()!.Value = 99;

            EditorSceneManager.DestroyPlayWorld();
        }
        EditorSceneManager.ReturnToEditWorld();

        Assert.True(play.IsDisposed);
        Assert.Same(edit, SceneInstance.Main);
        Assert.Equal(SceneState.Running, edit.State);
        Assert.False(go.IsDisposed);
        Assert.Same(marker, go.GetComponent<Marker>());
        Assert.Equal(1, marker.Value);
    }

    [Fact]
    public void ThePlayWorld_TearsDownInTheSessionItRanIn_WhichThenEnds()
    {
        MainThreadContext.Install();
        try
        {
            EditorSceneManager.EditWorld.Create("Thing").AddComponent<Marker>();
            SceneInstance play = EditorSceneManager.BuildPlayWorld();
            using (EnterPlayMode())
            {
                EditorSceneManager.EnterPlayWorld(play);
                CancellationToken session = GameTask.SessionToken;
                Marker copy = play.Get<Marker>()!;

                EditorSceneManager.DestroyPlayWorld();

                Assert.Equal(session, copy.DisabledIn);
                Assert.True(session.IsCancellationRequested);
            }
            EditorSceneManager.ReturnToEditWorld();
        }
        finally { MainThreadContext.Uninstall(); }
    }

    [Fact]
    public void StoppingPlay_DestroysEveryWorldPlayOpened()
    {
        SceneInstance before = SceneInstance.Create("Before", SceneMode.Game);
        SceneInstance play = EditorSceneManager.BuildPlayWorld();
        SceneInstance opened;
        using (EnterPlayMode())
        {
            EditorSceneManager.EnterPlayWorld(play);
            opened = SceneInstance.Create("Overlay", SceneMode.Game);
            opened.Activate();
            EditorSceneManager.DestroyPlayWorld();
        }
        EditorSceneManager.ReturnToEditWorld();

        Assert.True(opened.IsDisposed);
        Assert.False(before.IsDisposed);
        before.Dispose();
    }

    [Fact]
    public void PlayMode_ListsTheEnabledBuildScenes()
    {
        var world = new SceneInstance(SceneMode.Edit);
        Guid on = CreateSceneAsset(world, "On.scene");
        Guid off = CreateSceneAsset(world, "Off.scene");
        world.Dispose();
        Projects.Settings.BuildSettings build = EditorRegistries.GetSettings<Projects.Settings.BuildSettings>();
        List<Projects.Settings.SceneBuildEntry> previous = build.Scenes;
        build.Scenes = [new() { Path = "On.scene", SceneGuid = on }, new() { Path = "Off.scene", SceneGuid = off, Enabled = false }];
        try
        {
            SceneInstance play = EditorSceneManager.BuildPlayWorld();
            using (EnterPlayMode())
            {
                EditorSceneManager.EnterPlayWorld(play);
                Assert.Equal([on], SceneBuild.List.Select(s => s.AssetID));
                EditorSceneManager.DestroyPlayWorld();
            }
            EditorSceneManager.ReturnToEditWorld();
        }
        finally { build.Scenes = previous; }
    }

    [Fact]
    public void TheGameView_HostsWorldsPlayPresents_AboveThePlayWorld()
    {
        SceneInstance play = EditorSceneManager.BuildPlayWorld();
        using (EnterPlayMode())
        {
            EditorSceneManager.EnterPlayWorld(play);
            SceneInstance overlay = SceneInstance.Create("Overlay", SceneMode.Game);
            overlay.Present = true;
            overlay.RenderOrder = 10;
            overlay.Activate();

            Assert.Equal([play, overlay], GUI.Panels.GameViewPanel.HostedWorlds());
            EditorSceneManager.DestroyPlayWorld();
        }
        EditorSceneManager.ReturnToEditWorld();

        Assert.Equal([EditorSceneManager.EditWorld], GUI.Panels.GameViewPanel.HostedWorlds());
    }

    [Fact]
    public void ASelectionSnapshot_RestoresTheSameObjects_AndAssets()
    {
        SceneInstance edit = EditorSceneManager.EditWorld;
        GameObject first = edit.Create("First");
        GameObject second = edit.Create("Second");
        GameObject gone = edit.Create("Gone");
        Selection.Select(gone);
        Selection.AddToSelection(first);
        Selection.AddToSelection(second);
        Selection.Snapshot snapshot = Selection.TakeSnapshot();
        Selection.Clear();
        gone.Dispose();

        Selection.RestoreSnapshot(snapshot);

        Assert.Equal([first, second], Selection.Selected);
        Assert.Same(second, Selection.GetActiveAs<GameObject>());
    }

    [Fact]
    public void TheEditHistory_StillResolves_AfterPlay()
    {
        Marker marker = EditorSceneManager.EditWorld.Create("Thing").AddComponent<Marker>();
        Undo.Clear();
        Undo.RecordObject(marker, "Edit");
        marker.Value = 5;
        Undo.FlushUndoRecordObjects(); Undo.IncrementCurrentGroup();

        object editHistory = Undo.PushContext();
        SceneInstance play = EditorSceneManager.BuildPlayWorld();
        using (EnterPlayMode())
        {
            EditorSceneManager.EnterPlayWorld(play);
            EditorSceneManager.DestroyPlayWorld();
        }
        Undo.PopContext(editHistory);
        EditorSceneManager.ReturnToEditWorld();

        Undo.PerformUndo();
        Assert.Equal(1, marker.Value);
    }

    [Fact]
    public void PrefabEditing_HappensInItsOwnWorld_OverADormantEditWorld()
    {
        var authored = new GameObject("Crate");
        authored.AddComponent<Marker>();
        Guid prefab = CreatePrefabAsset(authored, "Crate.prefab");
        SceneInstance edit = EditorSceneManager.EditWorld;
        GameObject sceneObject = edit.Create("InTheScene");
        Selection.Select(sceneObject);

        PrefabEditingMode.Enter(prefab);

        Assert.Equal(0, Selection.Count);
        SceneInstance prefabWorld = PrefabEditingMode.World!;
        Assert.NotSame(edit, prefabWorld);
        Assert.Same(prefabWorld, EditorSceneManager.EditedWorld);
        Assert.Equal(SceneState.Dormant, edit.State);
        Assert.NotNull(prefabWorld.FindObject("Crate"));
        GameObject created = prefabWorld.Create("Made While Editing");

        PrefabEditingMode.Exit();

        Assert.True(prefabWorld.IsDisposed);
        Assert.True(created.IsDisposed);
        Assert.Same(edit, EditorSceneManager.EditedWorld);
        Assert.Equal(SceneState.Running, edit.State);
        Assert.False(sceneObject.IsDisposed);
    }
}
