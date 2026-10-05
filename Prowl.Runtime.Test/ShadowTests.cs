// This file is part of the Prowl Game Engine
// Licensed under the MIT License. See the LICENSE file in the project root for details.

using System;

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
}
