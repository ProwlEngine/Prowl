// This file is part of the Prowl Game Engine
// Licensed under the MIT License. See the LICENSE file in the project root for details.

using System;

using Prowl.Runtime.Resources;
using Prowl.Vector;

using Silk.NET.OpenXR;

using Xunit;

using Quaternion = Prowl.Vector.Quaternion;

namespace Prowl.Runtime.Test;

/// <summary>
/// Tests for a camera rendering headset eyes: the off center eye projection, where an eye sits relative to the
/// camera transform, per eye history, and the mirror from OpenXR's right handed poses into Prowl's left handed space.
/// </summary>
public class StereoCameraTests : RuntimeTestBase
{
    private const float Epsilon = 1e-4f;

    private Camera CreateCamera(Float3 position, Quaternion rotation)
    {
        GameObject go = CreateGameObject("Camera");
        go.Transform.Position = position;
        go.Transform.Rotation = rotation;
        Camera camera = go.AddComponent<Camera>();
        camera.NearClipPlane = 0.1f;
        camera.FarClipPlane = 100f;
        return camera;
    }

    private static RenderTexture Target(int width, int height) => new(width, height, false, [TextureImageFormat.Color4b]);

    private static XRView View(float left, float right, float up, float down, Float3 position = default)
        => new() { Position = position, Rotation = Quaternion.Identity, TanLeft = left, TanRight = right, TanUp = up, TanDown = down };

    private static Float3 Project(Float4x4 projection, Float3 viewPoint)
    {
        Float4 clip = projection * new Float4(viewPoint.X, viewPoint.Y, viewPoint.Z, 1f);
        return new Float3(clip.X / clip.W, clip.Y / clip.W, clip.Z / clip.W);
    }

    private static void AssertClose(Float3 expected, Float3 actual)
    {
        Assert.InRange(actual.X, expected.X - Epsilon, expected.X + Epsilon);
        Assert.InRange(actual.Y, expected.Y - Epsilon, expected.Y + Epsilon);
        Assert.InRange(actual.Z, expected.Z - Epsilon, expected.Z + Epsilon);
    }

    [Fact]
    public void SymmetricEye_MatchesTheMonoPerspective()
    {
        Camera camera = CreateCamera(Float3.Zero, Quaternion.Identity);
        RenderTexture target = Target(512, 512);

        camera.BeginStereoEye(StereoEye.Left, View(-1f, 1f, 1f, -1f));
        camera.UpdateRenderData(target);

        Float4x4 expected = Float4x4.CreatePerspectiveFov(Maths.ToRadians(90f), 1f, 0.1f, 100f);
        Float3 point = new(0.7f, -0.4f, 12f);
        AssertClose(Project(expected, point), Project(camera.ProjectionMatrix, point));
    }

    [Fact]
    public void OffCenterEye_MapsFrustumEdgesToClipEdges()
    {
        Camera camera = CreateCamera(Float3.Zero, Quaternion.Identity);
        RenderTexture target = Target(400, 300);
        XRView view = View(-1.2f, 0.8f, 1f, -0.6f);

        camera.BeginStereoEye(StereoEye.Right, view);
        camera.UpdateRenderData(target);

        const float z = 5f;
        Float3 topRight = Project(camera.ProjectionMatrix, new Float3(view.TanRight * z, view.TanUp * z, z));
        Float3 bottomLeft = Project(camera.ProjectionMatrix, new Float3(view.TanLeft * z, view.TanDown * z, z));

        Assert.InRange(topRight.X, 1f - Epsilon, 1f + Epsilon);
        Assert.InRange(topRight.Y, 1f - Epsilon, 1f + Epsilon);
        Assert.InRange(bottomLeft.X, -1f - Epsilon, -1f + Epsilon);
        Assert.InRange(bottomLeft.Y, -1f - Epsilon, -1f + Epsilon);
    }

    [Fact]
    public void Eye_IsPlacedRelativeToTheCameraTransform()
    {
        Quaternion turned = Quaternion.AxisAngle(Float3.UnitY, Maths.ToRadians(90f));
        Camera camera = CreateCamera(new Float3(1f, 2f, 3f), turned);
        RenderTexture target = Target(256, 256);
        Float3 eyeOffset = new(-0.032f, 0f, 0f);

        camera.BeginStereoEye(StereoEye.Left, View(-1f, 1f, 1f, -1f, eyeOffset));
        camera.UpdateRenderData(target);

        Float3 expectedPosition = new Float3(1f, 2f, 3f) + turned * eyeOffset;
        AssertClose(expectedPosition, camera.ViewPosition);
        AssertClose(Float3.Zero, Float4x4.TransformPoint(camera.ViewPosition, camera.ViewMatrix));
        AssertClose(new Float3(0f, 0f, 1f), Float4x4.TransformPoint(camera.ViewPosition + camera.Transform.Forward, camera.ViewMatrix));
    }

    [Fact]
    public void EndingTheEye_RestoresTheMonoView()
    {
        Camera camera = CreateCamera(new Float3(0f, 1f, 0f), Quaternion.Identity);
        camera.FieldOfView = 60f;
        RenderTexture target = Target(320, 240);

        camera.BeginStereoEye(StereoEye.Left, View(-1.5f, 0.5f, 1f, -1f, new Float3(0.05f, 0f, 0f)));
        camera.UpdateRenderData(target);
        camera.EndStereoEye();
        camera.UpdateRenderData(target);

        Assert.Equal(StereoEye.Mono, camera.ActiveEye);
        AssertClose(camera.Transform.Position, camera.ViewPosition);

        Float4x4 mono = Float4x4.CreatePerspectiveFov(Maths.ToRadians(60f), 320f / 240f, 0.1f, 100f);
        Float3 point = new(1f, 1f, 10f);
        AssertClose(Project(mono, point), Project(camera.ProjectionMatrix, point));
    }

    [Fact]
    public void PreviousViewProjection_IsKeptPerEye()
    {
        Camera camera = CreateCamera(Float3.Zero, Quaternion.Identity);
        RenderTexture target = Target(128, 128);

        camera.BeginStereoEye(StereoEye.Left, View(-1f, 1f, 1f, -1f));
        camera.UpdateRenderData(target);
        camera.SavePreviousViewProjectionMatrix();
        Assert.True(camera.HasPreviousViewProjectionMatrix);

        camera.BeginStereoEye(StereoEye.Right, View(-1f, 1f, 1f, -1f));
        Assert.False(camera.HasPreviousViewProjectionMatrix);

        camera.EndStereoEye();
        Assert.False(camera.HasPreviousViewProjectionMatrix);

        camera.BeginStereoEye(StereoEye.Left, View(-1f, 1f, 1f, -1f));
        camera.ResetMotionHistory();
        Assert.False(camera.HasPreviousViewProjectionMatrix);
    }

    [Fact]
    public void MotionHistory_SurvivesJoiningASceneWhileTheObjectIsDisabled()
    {
        // The scene view camera joins the scene for each render and leaves after. Joining enabled would count as the
        // camera being enabled again and forget its history every frame, which breaks every temporal effect
        Scene scene = CreateScene(enable: true);
        Camera camera = CreateCamera(Float3.Zero, Quaternion.Identity);
        camera.GameObject.Enabled = false;
        RenderTexture target = Target(128, 128);

        for (int frame = 0; frame < 3; frame++)
        {
            scene.Add(camera.GameObject);
            camera.UpdateRenderData(target);
            if (frame > 0) Assert.True(camera.HasPreviousViewProjectionMatrix);
            camera.SavePreviousViewProjectionMatrix();
            scene.Remove(camera.GameObject);
        }
    }

    [Fact]
    public void HandSetProjection_SurvivesAStereoRender()
    {
        Camera camera = CreateCamera(Float3.Zero, Quaternion.Identity);
        RenderTexture target = Target(128, 128);
        Float4x4 custom = Float4x4.CreateOrtho(4f, 4f, 0.1f, 50f);
        camera.ProjectionMatrix = custom;

        camera.BeginStereoEye(StereoEye.Left, View(-1f, 1f, 1f, -1f));
        camera.UpdateRenderData(target);
        camera.BeginStereoEye(StereoEye.Right, View(-1f, 1f, 1f, -1f));
        camera.UpdateRenderData(target);
        camera.EndStereoEye();

        Assert.True(camera.HasCustomProjectionMatrix);
        Float3 point = new(1f, 0.5f, 10f);
        AssertClose(Project(custom, point), Project(camera.ProjectionMatrix, point));
    }

    [Fact]
    public void ViewPose_FollowsTheTransformBeforeAnyRender()
    {
        Quaternion turned = Quaternion.AxisAngle(Float3.UnitY, Maths.ToRadians(30f));
        Camera camera = CreateCamera(new Float3(2f, 3f, 4f), turned);

        AssertClose(new Float3(2f, 3f, 4f), camera.ViewPosition);
        AssertClose(camera.Transform.Forward, Quaternion.Forward(camera.ViewRotation));
    }

    [Fact]
    public void EyeOffset_ScalesWithThePlayArea()
    {
        GameObject playArea = CreateGameObject("Play Area");
        playArea.Transform.LocalScale = new Float3(10f, 10f, 10f);
        Camera camera = CreateCamera(Float3.Zero, Quaternion.Identity);
        camera.GameObject.SetParent(playArea, worldPositionStays: false);
        RenderTexture target = Target(128, 128);

        camera.BeginStereoEye(StereoEye.Left, View(-1f, 1f, 1f, -1f, new Float3(-0.03f, 0f, 0f)));
        camera.UpdateRenderData(target);

        AssertClose(new Float3(-0.3f, 0f, 0f), camera.ViewPosition);
    }

    [Fact]
    public void SwitchingBetweenMonoAndStereo_ForgetsMotionHistory()
    {
        Camera camera = CreateCamera(Float3.Zero, Quaternion.Identity);
        RenderTexture target = Target(128, 128);

        camera.SetRenderingStereo(false);
        camera.UpdateRenderData(target);
        camera.SavePreviousViewProjectionMatrix();
        Assert.True(camera.HasPreviousViewProjectionMatrix);

        camera.SetRenderingStereo(true);
        camera.SetRenderingStereo(false);
        Assert.False(camera.HasPreviousViewProjectionMatrix);
    }

    private sealed class History { }

    private sealed class EyeStateEffect : ImageEffect
    {
        public History For(Camera camera) => GetEyeState<History>(camera);
        public int Released;
        public override void OnDisable() => ReleaseEyeStates<History>(_ => Released++);
    }

    [Fact]
    public void ImageEffectEyeState_IsSeparatePerEye()
    {
        Camera camera = CreateCamera(Float3.Zero, Quaternion.Identity);
        var effect = new EyeStateEffect();

        History mono = effect.For(camera);
        camera.BeginStereoEye(StereoEye.Left, View(-1f, 1f, 1f, -1f));
        History left = effect.For(camera);
        camera.BeginStereoEye(StereoEye.Right, View(-1f, 1f, 1f, -1f));
        History right = effect.For(camera);

        Assert.NotSame(mono, left);
        Assert.NotSame(left, right);
        Assert.Same(right, effect.For(camera));

        effect.OnDisable();
        Assert.Equal(3, effect.Released);
        Assert.NotSame(right, effect.For(camera));
    }

    [Fact]
    public void OpenXRForward_BecomesProwlForward()
    {
        Quaternion identity = OpenXRSession.ToProwl(new Quaternionf(0, 0, 0, 1));
        AssertClose(new Float3(0f, 0f, 1f), identity * OpenXRSession.ToProwl(new Vector3f(0, 0, -1)));
        AssertClose(new Float3(0.5f, 1.7f, 2f), OpenXRSession.ToProwl(new Vector3f(0.5f, 1.7f, -2f)));
    }

    // Converting a pose then rotating has to land where rotating in OpenXR space then converting does.
    [Theory]
    [InlineData(0.3f, 0.5f, -0.2f, 0.78f)]
    [InlineData(-0.6f, 0.1f, 0.4f, 0.68f)]
    [InlineData(0f, 0.7071f, 0f, 0.7071f)]
    public void OpenXRRotation_MirrorsWithPositions(float x, float y, float z, float w)
    {
        float length = MathF.Sqrt(x * x + y * y + z * z + w * w);
        var xrRotation = new Quaternionf(x / length, y / length, z / length, w / length);
        var xrPoint = new Vector3f(0.25f, -1.5f, 3f);

        var asSameAlgebra = new Quaternion(xrRotation.X, xrRotation.Y, xrRotation.Z, xrRotation.W);
        Float3 rotatedInXR = asSameAlgebra * new Float3(xrPoint.X, xrPoint.Y, xrPoint.Z);
        Float3 expected = OpenXRSession.ToProwl(new Vector3f(rotatedInXR.X, rotatedInXR.Y, rotatedInXR.Z));

        Float3 actual = OpenXRSession.ToProwl(xrRotation) * OpenXRSession.ToProwl(xrPoint);
        AssertClose(expected, actual);
    }

    // The pose each eye was drawn from goes back to the runtime, so converting there and back must change nothing.
    [Theory]
    [InlineData(0.3f, 0.5f, -0.2f, 0.78f)]
    [InlineData(-0.6f, 0.1f, 0.4f, 0.68f)]
    public void ProwlPose_ConvertsBackToTheSameOpenXRPose(float x, float y, float z, float w)
    {
        float length = MathF.Sqrt(x * x + y * y + z * z + w * w);
        var rotation = new Quaternionf(x / length, y / length, z / length, w / length);
        var position = new Vector3f(0.25f, 1.6f, -0.4f);

        Quaternionf rotationBack = OpenXRSession.ToXr(OpenXRSession.ToProwl(rotation));
        Vector3f positionBack = OpenXRSession.ToXr(OpenXRSession.ToProwl(position));

        Assert.Equal(rotation, rotationBack);
        Assert.Equal(position, positionBack);
    }

    // The application name a runtime is given has a fixed size, and a long one is cut short rather than failing XR.
    [Fact]
    public unsafe void LongNames_AreCutShortToFitTheirBuffer()
    {
        byte* buffer = stackalloc byte[128];
        string name = new string('é', 200);

        OpenXRSession.WriteString(buffer, 128, name);

        string written = OpenXRSession.ReadString(buffer);
        Assert.True(System.Text.Encoding.UTF8.GetByteCount(written) <= 127);
        Assert.StartsWith(written, name);
    }

    // Without a session, asking for an eye fails with a clear reason, and nothing has been drawn into the headset.
    [Fact]
    public void WithoutXR_EyeRequestsFailClearlyAndNothingWasDrawn()
    {
        Scene scene = CreateScene(enable: true);
        var camera = CreateGameObject("Camera").AddComponent<Camera>();
        scene.Add(camera.GameObject);

        Assert.Throws<InvalidOperationException>(() => XR.GetEyeView(StereoEye.Left));
        Assert.Throws<InvalidOperationException>(() => XR.GetEyeTexture(StereoEye.Left, camera, out _));
        Assert.False(XR.HasRenderedStereo(camera));
        Assert.False(XR.IsReconnecting);
        Assert.Equal(0f, XR.DisplayRefreshRate);
    }
}
