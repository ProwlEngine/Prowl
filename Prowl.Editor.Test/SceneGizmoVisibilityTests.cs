using System;
using System.Text.Json.Nodes;

using Prowl.Editor.Core;
using Prowl.Editor.GUI.Panels;
using Prowl.Runtime;
using Prowl.Runtime.Resources;

using Xunit;

namespace Prowl.Editor.Test;

public sealed class SceneGizmoProbe : Component
{
    public int Draws;
    public int SelectedDraws;
    public bool UsedWorldSpace;

    public override void DrawGizmos()
    {
        Draws++;
        UsedWorldSpace = GameCanvas.EditorWorldSpaceOverride;
    }

    public override void DrawGizmosSelected() => SelectedDraws++;
}

public class SceneGizmoVisibilityTests : IDisposable
{
    private readonly bool _wasEditor = Application.IsEditor;
    private readonly bool _wasPlaying = Application.IsPlaying;

    public SceneGizmoVisibilityTests()
    {
        Application.IsEditor = true;
        Application.IsPlaying = false;
        Selection.Clear();
    }

    public void Dispose()
    {
        Selection.Clear();
        Application.IsPlaying = _wasPlaying;
        Application.IsEditor = _wasEditor;
    }

    private static Scene CreateScene(out SceneGizmoProbe probe)
    {
        var scene = new Scene();
        var go = new GameObject("Gizmo probe");
        probe = go.AddComponent<SceneGizmoProbe>();
        scene.Add(go);
        scene.Enable();
        return scene;
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void UpdatesDoNotGenerateGizmosAndVisibleViewsRequestThemInEitherMode(bool playing)
    {
        Application.IsPlaying = playing;
        using Scene scene = CreateScene(out SceneGizmoProbe probe);
        var editor = new EditorApplication();
        editor.OnUpdate(scene);
        editor.BeginRender();
        Assert.Equal(0, probe.Draws);

        new SceneViewPanel().DrawSceneGizmos(editor, scene);
        Assert.Equal(1, probe.Draws);
    }

    [Fact]
    public void MultipleVisibleViewsShareOneComponentGizmoPassPerRenderedFrame()
    {
        using Scene scene = CreateScene(out SceneGizmoProbe probe);
        Selection.Select(probe.GameObject);
        var editor = new EditorApplication();
        var first = new SceneViewPanel();
        var second = new SceneViewPanel();
        bool previousWorldSpace = GameCanvas.EditorWorldSpaceOverride;

        editor.BeginRender();
        first.DrawSceneGizmos(editor, scene);
        second.DrawSceneGizmos(editor, scene);
        Assert.Equal(1, probe.Draws);
        Assert.Equal(1, probe.SelectedDraws);
        Assert.True(probe.UsedWorldSpace);
        Assert.Equal(previousWorldSpace, GameCanvas.EditorWorldSpaceOverride);

        editor.BeginRender();
        second.DrawSceneGizmos(editor, scene);
        Assert.Equal(2, probe.Draws);
        Assert.Equal(2, probe.SelectedDraws);
    }

    [Fact]
    public void DisabledGizmosDoNotConsumeThePassForAnotherVisibleView()
    {
        using Scene scene = CreateScene(out SceneGizmoProbe probe);
        var editor = new EditorApplication();
        var disabled = new SceneViewPanel();
        disabled.RestoreState(new JsonObject { ["gizmos"] = false });

        editor.BeginRender();
        disabled.DrawSceneGizmos(editor, scene);
        Assert.Equal(0, probe.Draws);
        new SceneViewPanel().DrawSceneGizmos(editor, scene);
        Assert.Equal(1, probe.Draws);
    }

    [Fact]
    public void FramesWithoutADrawingSceneViewDoNotGenerateGizmos()
    {
        using Scene scene = CreateScene(out SceneGizmoProbe probe);
        var editor = new EditorApplication();
        editor.BeginRender();
        new SceneViewPanel().DrawSceneGizmos(editor, scene);
        Assert.Equal(1, probe.Draws);

        editor.OnUpdate(scene);
        editor.BeginRender();
        editor.OnUpdate(scene);
        Assert.Equal(1, probe.Draws);
    }
}
