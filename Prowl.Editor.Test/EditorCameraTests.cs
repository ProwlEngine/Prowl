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
}
