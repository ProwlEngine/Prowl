using System;
using System.Linq;

using Prowl.Editor.Core;
using Prowl.Editor.GUI.Panels;
using Prowl.Editor.GUI.SceneView;
using Prowl.Runtime;
using Prowl.Runtime.Resources;
using Prowl.Vector;

using Xunit;

namespace Prowl.Editor.Test;

/// <summary>Draws an always shown marker and an outline, and a gizmo only while selected.</summary>
public sealed class HitboxProbe : Component
{
    public int Draws;
    public int SelectedDraws;

    public override void DrawGizmos()
    {
        Draws++;
        Debug.GizmoHitboxSphere(Transform.Position, 0.25f);
        using GizmoBuilder.HitboxLineScope lines = Debug.GizmoHitboxLines();
        Debug.DrawLine(Transform.Position + new Float3(-5, 2, 0), Transform.Position + new Float3(5, 2, 0), Color.White);
    }

    public override void DrawGizmosSelected() => SelectedDraws++;
}

public class GizmoPickingTests : EditorTestHarness
{
    private static readonly Float2 Screen = new(800, 600);

    public GizmoPickingTests()
    {
        GizmoFilter.SetDisabled([]);
        Selection.Clear();
    }

    public override void Dispose()
    {
        GizmoFilter.SetDisabled([]);
        Selection.Clear();
        base.Dispose();
    }

    // A camera 10 units back on -Z, looking down +Z at the origin.
    private static Camera MakeCamera(SceneInstance scene)
    {
        var go = new GameObject("Camera");
        go.Transform.Position = new Float3(0, 0, -10);
        Camera cam = go.AddComponent<Camera>();
        scene.Add(go);
        return cam;
    }

    private static SceneInstance MakeScene()
    {
        var scene = new SceneInstance(SceneMode.Edit);
        scene.Activate();
        return scene;
    }

    // Runs the editor's gizmo pass, then ends the frame so its hitboxes are the ones picking reads.
    private static void DrawFrame(SceneInstance scene)
    {
        var editor = new EditorApplication();
        editor.BeginRender();
        new SceneViewPanel().DrawSceneGizmos(editor, scene);
        Debug.ClearGizmos();
    }

    private static Float2 ScreenOf(Camera cam, Float3 world)
    {
        Float3 p = cam.WorldToScreenPoint(world, Screen);
        return new Float2(p.X, p.Y);
    }

    [Fact]
    public void DisabledTypeDrawsNoGizmosSelectedOrNot()
    {
        using SceneInstance scene = MakeScene();
        var go = new GameObject("Probe");
        HitboxProbe probe = go.AddComponent<HitboxProbe>();
        scene.Add(go);
        Selection.Select(go);

        DrawFrame(scene);
        Assert.Equal(1, probe.Draws);
        Assert.Equal(1, probe.SelectedDraws);

        GizmoFilter.SetEnabled(typeof(HitboxProbe), false);
        DrawFrame(scene);
        Assert.Equal(1, probe.Draws);
        Assert.Equal(1, probe.SelectedDraws);
        Assert.Empty(scene.Gizmos.Hitboxes);
    }

    [Fact]
    public void GizmoPassRecordsHitboxesForTheDrawingObjectOnly()
    {
        using SceneInstance scene = MakeScene();
        var go = new GameObject("Probe");
        go.AddComponent<HitboxProbe>();
        scene.Add(go);

        DrawFrame(scene);

        Assert.NotEmpty(scene.Gizmos.Hitboxes);
        Assert.All(scene.Gizmos.Hitboxes, h => Assert.Same(go, h.Owner));
        Assert.Contains(scene.Gizmos.Hitboxes, h => h.Kind == GizmoHitboxKind.Sphere);
        Assert.Contains(scene.Gizmos.Hitboxes, h => h.Kind == GizmoHitboxKind.Segment);

        // Gizmos drawn outside the editor's pass, by a game, record nothing.
        scene.DrawGizmos();
        Debug.ClearGizmos();
        Assert.Empty(scene.Gizmos.Hitboxes);
    }

    [Fact]
    public void IconIsClickableAcrossItsWholeDrawnSize()
    {
        using SceneInstance scene = MakeScene();
        Camera cam = MakeCamera(scene);
        var light = new GameObject("Light");
        GizmoHitbox[] hitboxes = [new(light, GizmoHitboxKind.Icon, Float3.Zero, Float3.Zero, 0.25f)];

        // Up close the icon is far wider than the old fixed pick radius: its corner still hits.
        cam.Transform.Position = new Float3(0, 0, -1);
        Float2 corner = ScreenOf(cam, new Float3(0.24f, 0.24f, 0));
        Assert.Same(light, SceneViewPanel.PickGizmo(hitboxes, cam, corner, Screen).Owner);

        Float2 outside = ScreenOf(cam, new Float3(0.4f, 0, 0));
        Assert.Null(SceneViewPanel.PickGizmo(hitboxes, cam, outside, Screen).Owner);
    }

    [Fact]
    public void IconWinsOverANearerLineAndALineOverNothing()
    {
        using SceneInstance scene = MakeScene();
        Camera cam = MakeCamera(scene);
        var light = new GameObject("Light");
        var volume = new GameObject("Volume");
        Float2 center = ScreenOf(cam, Float3.Zero);

        GizmoHitbox line = new(volume, GizmoHitboxKind.Segment, new Float3(-5, 0, -5), new Float3(5, 0, -5), 0f);
        GizmoHitbox icon = new(light, GizmoHitboxKind.Icon, Float3.Zero, Float3.Zero, 0.25f);

        Assert.Same(volume, SceneViewPanel.PickGizmo([line], cam, center, Screen).Owner);
        SceneViewPanel.GizmoPick pick = SceneViewPanel.PickGizmo([line, icon], cam, center, Screen);
        Assert.Same(light, pick.Owner);
        Assert.True(pick.IsIcon);

        // Away from the line, inside the outline, nothing is hit.
        Assert.Null(SceneViewPanel.PickGizmo([line], cam, center + new Float2(0, 40), Screen).Owner);
    }

    [Fact]
    public void HiddenOrDisabledOwnersCannotBeClicked()
    {
        using SceneInstance scene = MakeScene();
        Camera cam = MakeCamera(scene);
        var light = new GameObject("Light");
        light.Enabled = false;
        GizmoHitbox[] hitboxes = [new(light, GizmoHitboxKind.Icon, Float3.Zero, Float3.Zero, 0.25f)];

        Assert.Null(SceneViewPanel.PickGizmo(hitboxes, cam, ScreenOf(cam, Float3.Zero), Screen).Owner);
    }

    [Fact]
    public void ClickSelectsThePrefabRootThenDrillsIn()
    {
        // Outer prefab instance holding a nested instance of another prefab, holding a part.
        var outer = new GameObject("Outer") { PrefabAssetId = Guid.NewGuid() };
        var nested = new GameObject("Nested") { PrefabAssetId = Guid.NewGuid() };
        var part = new GameObject("Part") { PrefabAssetId = nested.PrefabAssetId };
        nested.SetParent(outer);
        part.SetParent(nested);

        Assert.Same(outer, SceneViewPanel.SelectionTarget(part));
        Selection.Select(outer);
        Assert.Same(nested, SceneViewPanel.SelectionTarget(part));
        Selection.Select(nested);
        Assert.Same(part, SceneViewPanel.SelectionTarget(part));
        Selection.Select(part);
        Assert.Same(part, SceneViewPanel.SelectionTarget(part));

        // A Ctrl click on a selected prefab takes that prefab, so it deselects rather than adding the part.
        Selection.Select(outer);
        Assert.Same(outer, SceneViewPanel.SelectionTarget(part, toggling: true));
    }

    [Fact]
    public void GridSnapLandsOnGridPointsAlongTheMovedAxesOnly()
    {
        // Starting off the grid, a move along X lands on the nearest grid point rather than moving in whole
        // steps from 0.3, and the untouched Y and Z stay where they were.
        Float3 start = new(0.3f, 0.7f, 1.2f);
        AssertNear(new Float3(1f, 0.7f, 1.2f), SceneViewPanel.SnapToGrid(start, new Float3(1.1f, 0, 0), 1f, Quaternion.Identity));

        // A plane drag snaps both of its axes.
        AssertNear(new Float3(0.5f, 0.7f, 0.5f), SceneViewPanel.SnapToGrid(start, new Float3(0.4f, 0, -0.5f), 0.5f, Quaternion.Identity));

        // A handle turned by a right angle still uses the world grid.
        Quaternion quarter = Quaternion.AxisAngle(Float3.UnitY, MathF.PI / 2f);
        AssertNear(new Float3(1f, 0.7f, 1.2f), SceneViewPanel.SnapToGrid(start, new Float3(1.1f, 0, 0), 1f, quarter));
    }

    [Fact]
    public void GridSnapOnARotatedHandleStaysOnItsAxis()
    {
        // A local handle at 30 degrees snaps along its own axis, so the result never leaves the dragged line.
        Quaternion space = Quaternion.AxisAngle(Float3.UnitY, MathF.PI / 6f);
        Float3 axis = space * Float3.UnitX;
        Float3 start = new(0.3f, 0f, 0.2f);
        Float3 snapped = SceneViewPanel.SnapToGrid(start, axis * 1.3f, 1f, space);

        Float3 offset = snapped - start;
        AssertNear(axis * Float3.Dot(offset, axis), offset);
    }

    [Fact]
    public void SteppersDoubleTheGridAndWalkTheAnglePresets()
    {
        float grid = SceneTools.MoveSnap, angle = SceneTools.RotateSnap;
        try
        {
            SceneTools.MoveSnap = 1f;
            SceneTools.StepMoveSnap(1);
            Assert.Equal(2f, SceneTools.MoveSnap);
            SceneTools.StepMoveSnap(-1);
            SceneTools.StepMoveSnap(-1);
            Assert.Equal(0.5f, SceneTools.MoveSnap);
            SceneTools.MoveSnap = SceneTools.MaxMoveSnap;
            SceneTools.StepMoveSnap(1);
            Assert.Equal(SceneTools.MaxMoveSnap, SceneTools.MoveSnap);

            // A typed angle steps from the preset nearest to it.
            SceneTools.RotateSnap = 14f;
            SceneTools.StepRotateSnap(1);
            Assert.Equal(30f, SceneTools.RotateSnap);
            SceneTools.StepRotateSnap(-1);
            Assert.Equal(15f, SceneTools.RotateSnap);
            SceneTools.RotateSnap = 180f;
            SceneTools.StepRotateSnap(1);
            Assert.Equal(180f, SceneTools.RotateSnap);
        }
        finally
        {
            SceneTools.MoveSnap = grid;
            SceneTools.RotateSnap = angle;
        }
    }

    private static void AssertNear(Float3 expected, Float3 actual)
        => Assert.True(Float3.Distance(expected, actual) < 1e-4f, $"Expected {expected}, got {actual}");

    [Fact]
    public void ObjectsOutsideAnyPrefabSelectThemselves()
    {
        var parent = new GameObject("Parent");
        var child = new GameObject("Child");
        child.SetParent(parent);

        Assert.Same(child, SceneViewPanel.SelectionTarget(child));
    }
}
