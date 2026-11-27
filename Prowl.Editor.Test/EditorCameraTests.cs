using Prowl.Editor.GUI.SceneView;
using Prowl.Vector;

using Xunit;

namespace Prowl.Editor.Test;

public class EditorCameraTests : EditorTestHarness
{
    private static void AssertNear(Float3 expected, Float3 actual)
        => Assert.True(Float3.Distance(expected, actual) < 1e-3f, $"Expected {expected}, got {actual}");

    [Fact]
    public void TwoDMode_LooksDownZ_Orthographic_KeepingTheFocusOnTheXYPlane()
    {
        var camera = new EditorCamera();
        camera.SetPose(new Float3(3, 4, -10), 30f, 20f);
        Float3 focus = camera.ViewFocusPoint;

        camera.Set2D(true);

        Assert.True(camera.Is2D);
        Assert.True(camera.IsOrthographic);
        AssertNear(Float3.UnitZ, camera.Forward);
        Assert.Equal(focus.X, camera.ViewFocusPoint.X, 3);
        Assert.Equal(focus.Y, camera.ViewFocusPoint.Y, 3);
        Assert.Equal(0f, camera.ViewFocusPoint.Z, 3);
    }

    [Fact]
    public void LeavingTwoDMode_PutsBackThe3DView()
    {
        var camera = new EditorCamera();
        camera.SetPose(new Float3(3, 4, -10), 30f, 20f);

        camera.Set2D(true);
        camera.Set2D(false);

        Assert.False(camera.Is2D);
        Assert.False(camera.IsOrthographic);
        AssertNear(new Float3(3, 4, -10), camera.Position);
        Assert.Equal(30f, camera.Yaw, 3);
        Assert.Equal(20f, camera.Pitch, 3);
    }

    [Fact]
    public void TwoDMode_IgnoresTurningAndProjectionSwaps()
    {
        var camera = new EditorCamera();
        camera.Set2D(true);

        camera.SetOrientation(90f, 45f);
        camera.ToggleProjection();

        AssertNear(Float3.UnitZ, camera.Forward);
        Assert.True(camera.IsOrthographic);
    }

    [Fact]
    public void Framing_InAnOrthographicView_SizesTheView()
    {
        var camera = new EditorCamera();
        camera.Set2D(true);

        camera.Frame(new Float3(5, 6, 2), 3f);

        Assert.Equal(3.3f, camera.OrthographicSize, 3);
        Assert.Equal(5f, camera.ViewFocusPoint.X, 3);
        Assert.Equal(6f, camera.ViewFocusPoint.Y, 3);
    }

    [Fact]
    public void RectTool_DraggingAnEdge_ScalesWhileTheOppositeEdgeStays()
    {
        Float4x4 frame = Float4x4.Identity;

        (Float3 scale, Float3 position) = RectTool.Resized(frame, Float3.Zero, Float3.One, Float2.Zero, new Float2(2, 1), 1, 0, new Float2(1, 0), false);
        AssertNear(new Float3(1.5f, 1, 1), scale);
        AssertNear(Float3.Zero, position);

        (scale, position) = RectTool.Resized(frame, Float3.Zero, Float3.One, Float2.Zero, new Float2(2, 1), -1, 0, new Float2(-1, 0), false);
        AssertNear(new Float3(1.5f, 1, 1), scale);
        AssertNear(new Float3(-1, 0, 0), position);
    }

    [Fact]
    public void RectTool_ShiftOnACorner_KeepsProportions()
    {
        (Float3 scale, _) = RectTool.Resized(Float4x4.Identity, Float3.Zero, Float3.One, Float2.Zero, new Float2(2, 2), 1, 1, new Float2(2, 0.5f), true);

        AssertNear(new Float3(2, 2, 1), scale);
    }

    [Fact]
    public void RectTool_OnATurnedObject_MovesAlongItsOwnAxes()
    {
        Float4x4 turned = Float4x4.CreateFromQuaternion(Quaternion.AxisAngle(Float3.UnitZ, System.MathF.PI / 2));

        (_, Float3 position) = RectTool.Resized(turned, Float3.Zero, Float3.One, new Float2(-1, -1), new Float2(1, 1), -1, 0, new Float2(-2, 0), false);

        AssertNear(new Float3(0, -1, 0), position);
    }

    [Fact]
    public void RectTool_CoversAFlippedSprite()
    {
        var go = new Runtime.GameObject("Sprite");
        Runtime.SpriteRenderer renderer = go.AddComponent<Runtime.SpriteRenderer>();
        var sprite = new Runtime.Resources.Sprite { Rect = new Runtime.Resources.SpriteRect(0, 0, 200, 100), PixelsPerUnit = 100, Pivot = new Float2(0, 0) };
        sprite.BuildQuadGeometry(200, 100);
        renderer.Sprite = sprite;
        renderer.FlipX = true;

        Assert.True(RectTool.LocalRect(go, out Float2 min, out Float2 max));
        Assert.Equal(new Float2(-2, 0), min);
        Assert.Equal(new Float2(0, 1), max);
    }
}
