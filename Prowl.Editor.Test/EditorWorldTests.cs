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
        Scene edit = EditorSceneManager.EditWorld;
        GameObject go = edit.Create("Thing");
        Marker marker = go.AddComponent<Marker>();
        SceneSection extra = edit.CreateSection("Extra");
        edit.Create(extra, "Other");

        Scene play = EditorSceneManager.BuildPlayWorld();
        using (EnterPlayMode())
        {
            EditorSceneManager.EnterPlayWorld(play);

            Assert.Equal(SceneState.Dormant, edit.State);
            Assert.Same(play, Scene.Main);
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
        Assert.Same(edit, Scene.Main);
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
            Scene play = EditorSceneManager.BuildPlayWorld();
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
        Scene before = Scene.Create("Before", SceneMode.Game);
        Scene play = EditorSceneManager.BuildPlayWorld();
        Scene opened;
        using (EnterPlayMode())
        {
            EditorSceneManager.EnterPlayWorld(play);
            opened = Scene.Create("Overlay", SceneMode.Game);
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
        var world = new Scene(SceneMode.Edit);
        Guid on = CreateSceneAsset(world, "On.scene");
        Guid off = CreateSceneAsset(world, "Off.scene");
        world.Dispose();
        Projects.Settings.BuildSettings build = EditorRegistries.GetSettings<Projects.Settings.BuildSettings>();
        List<Projects.Settings.SceneBuildEntry> previous = build.Scenes;
        build.Scenes = [new() { Path = "On.scene", SceneGuid = on }, new() { Path = "Off.scene", SceneGuid = off, Enabled = false }];
        try
        {
            Scene play = EditorSceneManager.BuildPlayWorld();
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
        Scene play = EditorSceneManager.BuildPlayWorld();
        using (EnterPlayMode())
        {
            EditorSceneManager.EnterPlayWorld(play);
            Scene overlay = Scene.Create("Overlay", SceneMode.Game);
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
        Scene edit = EditorSceneManager.EditWorld;
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

    public sealed class WaitsWhenAdded : Component
    {
        public Task? Wait;
        public override void OnAddedToScene() => Wait = GameTask.Frames(100);
    }

    [Fact]
    public void WorkThePlayCopyStartsAsItFills_BelongsToPlay()
    {
        MainThreadContext.Install();
        try
        {
            EditorSceneManager.EditWorld.Create("Thing").AddComponent<WaitsWhenAdded>();
            Scene play = EditorSceneManager.PreparePlayWorld();
            using (EnterPlayMode())
            {
                EditorSceneManager.EnterPlayWorld(play);
                Task wait = play.Get<WaitsWhenAdded>()!.Wait!;
                Assert.False(wait.IsCanceled);
                EditorSceneManager.DestroyPlayWorld();
            }
            EditorSceneManager.ReturnToEditWorld();
        }
        finally { MainThreadContext.Uninstall(); }
    }

    [ExecuteAlways]
    public sealed class ChangesWhenDisabled : Component
    {
        public int Value = 1;
        public override void OnDisable() => Value = 7;
    }

    [Fact]
    public void ThePlayCopy_IsTakenBeforeTheEditWorldSleeps()
    {
        EditorSceneManager.EditWorld.Create("Thing").AddComponent<ChangesWhenDisabled>();
        Scene play = EditorSceneManager.PreparePlayWorld();
        try
        {
            Assert.Equal(1, play.FindObject("Thing")!.GetComponent<ChangesWhenDisabled>()!.Value);
        }
        finally
        {
            play.Dispose();
            EditorSceneManager.ReturnToEditWorld();
        }
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void AnAbandonedPlayStart_DestroysThePlayWorld_AndWakesTheEditWorld(bool entered)
    {
        Scene edit = EditorSceneManager.EditWorld;
        GameObject go = edit.Create("Thing");
        Scene play = EditorSceneManager.PreparePlayWorld();
        using (EnterPlayMode())
        {
            if (entered) EditorSceneManager.EnterPlayWorld(play);
            EditorSceneManager.AbandonPlayWorld(play);
        }
        EditorSceneManager.ReturnToEditWorld();

        Assert.True(play.IsDisposed);
        Assert.Null(EditorSceneManager.PlayWorld);
        Assert.Same(edit, Scene.Main);
        Assert.Equal(SceneState.Running, edit.State);
        Assert.False(go.IsDisposed);
    }

    [Fact]
    public void ApplyingTimeSettings_LeavesThePlayWorldsTimeScale()
    {
        Scene play = EditorSceneManager.BuildPlayWorld();
        try
        {
            play.Clock.TimeScale = 0f;
            new Projects.Settings.TimeSettings { DefaultTimeScale = 2f }.Apply();

            Assert.Equal(0f, play.Clock.TimeScale);
            Assert.Equal(2f, EditorSceneManager.EditWorld.Clock.TimeScale);
        }
        finally
        {
            new Projects.Settings.TimeSettings().Apply();
            play.Dispose();
        }
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
        Scene play = EditorSceneManager.BuildPlayWorld();
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
        Scene edit = EditorSceneManager.EditWorld;
        GameObject sceneObject = edit.Create("InTheScene");
        Selection.Select(sceneObject);

        PrefabEditingMode.Enter(prefab);

        Assert.Equal(0, Selection.Count);
        Scene prefabWorld = PrefabEditingMode.World!;
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

    /// <summary>The isolated world owner answers for the prefab session: its world, its name, its dirty flag from undo.</summary>
    [Fact]
    public void APrefabSessionIsTheIsolatedWorldsSession()
    {
        var authored = new GameObject("Barrel");
        authored.AddComponent<Marker>();
        Guid prefab = CreatePrefabAsset(authored, "Barrel.prefab");

        PrefabEditingMode.Enter(prefab);

        IsolatedSession session = Assert.IsAssignableFrom<IsolatedSession>(IsolatedWorld.Current);
        Assert.Same(PrefabEditingMode.World, IsolatedWorld.World);
        Assert.Equal("Barrel", session.Name);
        Assert.False(IsolatedWorld.IsDirty);
        Assert.False(EditorSceneManager.CanEditScenes);

        GameObject root = IsolatedWorld.World!.FindObject("Barrel")!;
        Undo.RecordObject(root.GetComponent<Marker>()!, "Change Marker");
        root.GetComponent<Marker>()!.Value = 7;
        Undo.FlushUndoRecordObjects();
        Undo.IncrementCurrentGroup();
        Assert.True(IsolatedWorld.IsDirty);
        Assert.True(PrefabEditingMode.IsDirty);

        PrefabEditingMode.Exit();

        Assert.Null(IsolatedWorld.Current);
        Assert.False(IsolatedWorld.IsOpen);
        Assert.True(EditorSceneManager.CanEditScenes);
    }
}
