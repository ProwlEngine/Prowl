// This file is part of the Prowl Game Engine
// Licensed under the MIT License. See the LICENSE file in the project root for details.

using System;

using Prowl.Runtime.Rendering;
using Prowl.Vector;

using Xunit;

namespace Prowl.Runtime.Test;

/// <summary>
/// Pure math tests (no GPU) for the light shadow setup: directional cascade placement, coverage and
/// texel snapping, the caster culling volume, spot projections and the shadow flags lights report.
/// </summary>
public class ShadowTests : RuntimeTestBase
{
    private const int Resolution = 2048;
    private const float CascadeRadius = 35f;
    private const float TexelSize = (CascadeRadius * 2f) / Resolution;

    /// <summary>Creates an angled directional light, so the light-space axes are nothing like the
    /// world axes and a mistake in the basis math can't accidentally cancel out.</summary>
    private DirectionalLight CreateAngledLight()
    {
        GameObject go = CreateGameObject("Directional Light");
        go.Transform.LocalEulerAngles = new Float3(50f, 210f, 0f);
        return go.AddComponent<DirectionalLight>();
    }

    /// <summary>The orthonormal light-space basis GetShadowMatrix builds internally.</summary>
    private static (Float3 right, Float3 up, Float3 forward) LightBasis(DirectionalLight light)
    {
        Float3 forward = light.Transform.Forward;
        Float3 up = Float3.Normalize(light.Transform.Up);
        Float3 right = Float3.Normalize(Float3.Cross(up, forward));
        up = Float3.Normalize(Float3.Cross(forward, right));
        return (right, up, forward);
    }

    private static bool InsideClip(Float3 point, Float4x4 viewProjection)
    {
        Float4 clip = viewProjection * new Float4(point, 1f);
        Float3 ndc = new Float3(clip.X, clip.Y, clip.Z) / clip.W;
        return MathF.Abs(ndc.X) <= 1f && MathF.Abs(ndc.Y) <= 1f && ndc.Z >= 0f && ndc.Z <= 1f;
    }

    [Fact]
    public void DirectionalLight_GetShadowMatrix_CentersOrthoOnFocusWithinTexel()
    {
        DirectionalLight light = CreateAngledLight();
        Float3 focus = new(37.4f, 2.6f, -18.9f);

        light.GetShadowMatrix(focus, Resolution, CascadeRadius, out Float4x4 view, out _);

        // In light view space the focus point should sit at the origin, off only by the texel
        // snapping applied to X and Y (at most half a texel each). If placement drifted further the
        // focal point would no longer be in the middle of the cascade it was built for.
        Float3 focusInLightSpace = Float4x4.TransformPoint(focus, view);
        float tolerance = (TexelSize * 0.5f) + 1e-4f;

        Assert.True(MathF.Abs(focusInLightSpace.X) <= tolerance,
            $"Focus X in light space was {focusInLightSpace.X}, expected within {tolerance}.");
        Assert.True(MathF.Abs(focusInLightSpace.Y) <= tolerance,
            $"Focus Y in light space was {focusInLightSpace.Y}, expected within {tolerance}.");
    }

    [Fact]
    public void DirectionalLight_GetShadowMatrix_SubTexelMovement_ProducesIdenticalView()
    {
        DirectionalLight light = CreateAngledLight();
        (Float3 right, Float3 up, Float3 forward) = LightBasis(light);

        // Start exactly on the texel grid so a fifth-of-a-texel step can't straddle a rounding
        // boundary and legitimately land on the next grid point.
        Float3 focus = (right * (14f * TexelSize)) + (up * (-9f * TexelSize)) + (forward * 6.5f);
        Float3 nudged = focus + (right * (TexelSize * 0.2f));

        light.GetShadowMatrix(focus, Resolution, CascadeRadius, out Float4x4 view, out _);
        light.GetShadowMatrix(nudged, Resolution, CascadeRadius, out Float4x4 nudgedView, out _);

        // Identical, not merely close: a shadow map that slides by a fraction of a texel each frame
        // is what makes shadow edges crawl, and this is exactly the case a moving player hits.
        Assert.Equal(view.ToArray(), nudgedView.ToArray());
    }

    [Fact]
    public void DirectionalLight_GetShadowMatrix_CoversWholeRadius()
    {
        DirectionalLight light = CreateAngledLight();
        (Float3 right, Float3 up, Float3 forward) = LightBasis(light);
        Float3 focus = new(4f, 1f, -3f);

        light.GetShadowMatrix(focus, Resolution, CascadeRadius, out Float4x4 view, out Float4x4 projection);
        Float4x4 viewProjection = projection * view;

        // The cascade is selected for every receiver within its radius of the focus, so every such
        // receiver has to land inside the map, in all directions including along the light.
        float reach = CascadeRadius * 0.99f;
        Assert.True(InsideClip(focus + right * reach, viewProjection), "Receiver along light right fell outside the cascade.");
        Assert.True(InsideClip(focus - up * reach, viewProjection), "Receiver along light down fell outside the cascade.");
        Assert.True(InsideClip(focus + forward * reach, viewProjection), "Receiver away from the light fell outside the cascade.");
        Assert.True(InsideClip(focus - forward * reach, viewProjection), "Receiver toward the light fell outside the cascade.");
    }

    [Fact]
    public void DirectionalLight_CasterFrustum_KeepsCastersFarTowardLight()
    {
        DirectionalLight light = CreateAngledLight();
        (_, _, Float3 forward) = LightBasis(light);
        Float3 focus = new(4f, 1f, -3f);

        light.GetShadowMatrix(focus, Resolution, CascadeRadius, out Float4x4 view, out Float4x4 projection);
        Frustum casters = DirectionalLight.GetCasterFrustum(view, projection);

        // A tall caster far up the light direction still shadows the cascade, the depth clamp flattens
        // it onto the near plane, so culling must not drop it.
        Float3 farTowardLight = focus - forward * 500f;
        Assert.True(casters.Intersects(new AABB(farTowardLight - Float3.One, farTowardLight + Float3.One)));

        // The sides still cull.
        Float3 beside = focus + Float3.Normalize(Float3.Cross(forward, Float3.UnitY)) * (CascadeRadius * 3f);
        Assert.False(casters.Intersects(new AABB(beside - Float3.One, beside + Float3.One)));
    }

    [Fact]
    public void SpotLight_WideAngle_BuildsShadowProjection()
    {
        GameObject go = CreateGameObject("Spot Light");
        SpotLight light = go.AddComponent<SpotLight>();
        light.SpotAngle = 100f;

        light.GetShadowMatrix(out Float4x4 view, out Float4x4 projection);

        Float3 ahead = go.Transform.Position + go.Transform.Forward * (light.Range * 0.5f);
        Assert.True(InsideClip(ahead, projection * view));
    }

    [Fact]
    public void PointLight_CastShadows_ReportsShadowedBeforeFirstShadowRender()
    {
        PointLight light = CreateGameObject("Point Light").AddComponent<PointLight>();
        light.CastShadows = true;

        // The light system reads this before the shadow pass of the frame a light first wins a slot,
        // so it must not depend on a shadow map from an earlier frame.
        Assert.True(light.GetForwardLightData().ShadowEnabled);

        light.CastShadows = false;
        Assert.False(light.GetForwardLightData().ShadowEnabled);
    }

    [Fact]
    public void SpotLight_CastShadows_ReportsShadowedBeforeFirstShadowRender()
    {
        SpotLight light = CreateGameObject("Spot Light").AddComponent<SpotLight>();
        light.CastShadows = true;

        Assert.True(light.GetForwardLightData().ShadowEnabled);

        light.CastShadows = false;
        Assert.False(light.GetForwardLightData().ShadowEnabled);
    }

    /// <summary>World space corners of a view slice, from the frustum tangents of a camera at origin.</summary>
    private static Float3[] SliceCorners(Float3 origin, Quaternion rotation, float tanLeft, float tanRight, float tanDown, float tanUp, float near, float far)
    {
        var corners = new Float3[8];
        int i = 0;
        foreach (float depth in new[] { near, far })
            foreach (float x in new[] { tanLeft, tanRight })
                foreach (float y in new[] { tanDown, tanUp })
                    corners[i++] = origin + rotation * new Float3(x * depth, y * depth, depth);
        return corners;
    }

    private static void AssertInside(Float3[] points, Float3 center, float radius)
    {
        foreach (Float3 p in points)
        {
            float d = Float3.Length(p - center);
            Assert.True(d <= radius + 1e-3f, $"Point {p} is {d} from the sphere center, radius {radius}.");
        }
    }

    [Fact]
    public void ShadowFitView_Perspective_SphereHoldsWholeSlice()
    {
        Quaternion rotation = Quaternion.FromEuler(new Float3(20f, 135f, 0f));
        Float3 origin = new(3f, 2f, -5f);
        Float4x4 projection = Float4x4.CreatePerspectiveFov(70f * Maths.Deg2Rad, 16f / 9f, 0.1f, 1000f);
        ShadowFitView view = ShadowFitView.FromProjection(origin, rotation, projection, 0.1f);

        float tanY = MathF.Tan(35f * Maths.Deg2Rad), tanX = tanY * 16f / 9f;
        foreach ((float near, float far) in new[] { (0.1f, 4f), (4f, 15f), (15f, 50f) })
        {
            view.GetSliceSphere(near, far, out Float3 center, out float radius);
            AssertInside(SliceCorners(origin, rotation, -tanX, tanX, -tanY, tanY, near, far), center, radius);
        }
    }

    [Fact]
    public void ShadowFitView_Orthographic_SphereHoldsWholeSlice()
    {
        Quaternion rotation = Quaternion.FromEuler(new Float3(-30f, 40f, 0f));
        Float3 origin = new(-1f, 8f, 2f);
        ShadowFitView view = ShadowFitView.FromProjection(origin, rotation, Float4x4.CreateOrtho(24f, 12f, 0.1f, 100f), 0.1f);

        view.GetSliceSphere(5f, 30f, out Float3 center, out float radius);
        var corners = new Float3[8];
        int i = 0;
        foreach (float depth in new[] { 5f, 30f })
            foreach (float x in new[] { -12f, 12f })
                foreach (float y in new[] { -6f, 6f })
                    corners[i++] = origin + rotation * new Float3(x, y, depth);
        AssertInside(corners, center, radius);
    }

    [Fact]
    public void ShadowFitView_SphereRadius_DoesNotChangeAsTheCameraTurns()
    {
        Float4x4 projection = Float4x4.CreatePerspectiveFov(60f * Maths.Deg2Rad, 1.5f, 0.1f, 1000f);
        ShadowFitView a = ShadowFitView.FromProjection(Float3.Zero, Quaternion.Identity, projection, 0.1f);
        ShadowFitView b = ShadowFitView.FromProjection(new Float3(40f, 3f, 9f), Quaternion.FromEuler(new Float3(15f, 77f, 5f)), projection, 0.1f);

        // A radius that follows the view direction would resize the cascade every frame and make its edges crawl
        a.GetSliceSphere(3f, 12f, out _, out float radiusA);
        b.GetSliceSphere(3f, 12f, out _, out float radiusB);
        Assert.Equal(radiusA, radiusB);
    }

    [Fact]
    public void ShadowFitView_Stereo_SphereHoldsBothEyes()
    {
        Float3 head = new(1f, 1.7f, 4f);
        Quaternion headRotation = Quaternion.FromEuler(new Float3(0f, 30f, 0f));
        Float4x4 headToWorld = Float4x4.CreateTRS(head, headRotation, Float3.One);

        // Canted, asymmetric eyes like a wide field of view headset
        XRView left = new() { Position = new Float3(-0.032f, 0f, 0f), Rotation = Quaternion.FromEuler(new Float3(0f, -10f, 0f)), TanLeft = -1.4f, TanRight = 1.0f, TanDown = -1.2f, TanUp = 1.1f };
        XRView right = new() { Position = new Float3(0.032f, 0f, 0f), Rotation = Quaternion.FromEuler(new Float3(0f, 10f, 0f)), TanLeft = -1.0f, TanRight = 1.4f, TanDown = -1.2f, TanUp = 1.1f };

        ShadowFitView view = ShadowFitView.FromEyes(head, headRotation, 0.05f, [left, right], headToWorld);

        view.GetSliceSphere(2f, 10f, out Float3 center, out float radius);
        Float3 headForward = headRotation * Float3.UnitZ;
        foreach (XRView eye in new[] { left, right })
        {
            Float3 eyeOrigin = Float4x4.TransformPoint(eye.Position, headToWorld);
            Quaternion eyeRotation = headRotation * eye.Rotation;
            foreach (float x in new[] { eye.TanLeft, eye.TanRight })
                foreach (float y in new[] { eye.TanDown, eye.TanUp })
                {
                    // Each corner ray of the eye, cut where it crosses the slice's near and far depth along the head
                    Float3 dir = eyeRotation * new Float3(x, y, 1f);
                    foreach (float depth in new[] { 2f, 10f })
                    {
                        float t = (depth - Float3.Dot(eyeOrigin - head, headForward)) / Float3.Dot(dir, headForward);
                        AssertInside([eyeOrigin + dir * t], center, radius);
                    }
                }
        }
    }

    [Fact]
    public void DirectionalLight_CascadeSplits_EndAtShadowDistanceAndFavourTheNearSlice()
    {
        const float near = 0.1f, distance = 50f;
        float previous = near;
        for (int i = 1; i <= 4; i++)
        {
            float split = DirectionalLight.GetCascadeSplit(i, 4, near, distance);
            Assert.True(split > previous);
            previous = split;
        }
        Assert.Equal(distance, DirectionalLight.GetCascadeSplit(4, 4, near, distance), 3);

        // The first slice is much shorter than an even split, it is the one right in front of the camera
        Assert.True(DirectionalLight.GetCascadeSplit(1, 4, near, distance) < distance / 4f * 0.5f);
    }
}
