// This file is part of the Prowl Game Engine
// Licensed under the MIT License. See the LICENSE file in the project root for details.

using System;

using Prowl.Vector;

using Xunit;

namespace Prowl.Runtime.Test;

/// <summary>
/// Pure-math tests (no GPU) for <see cref="DirectionalLight.GetShadowMatrix"/>: a cascade lands
/// centered on the focus point it is handed - the rendering camera, or the camera's
/// <see cref="Camera.ShadowFocus"/> target - and the light-space texel snapping that keeps shadow
/// edges from shimmering survives sub-texel movement of that point.
/// </summary>
public class DirectionalLightShadowMatrixTests : RuntimeTestBase
{
    private const int Resolution = 2048;
    private const float CascadeDistance = 35f;
    private const float TexelSize = (CascadeDistance * 2f) / Resolution;

    /// <summary>Creates an angled directional light, so the light-space axes are nothing like the
    /// world axes and a mistake in the basis math can't accidentally cancel out.</summary>
    private DirectionalLight CreateAngledLight()
    {
        GameObject go = CreateGameObject("Directional Light");
        go.Transform.LocalEulerAngles = new Float3(-50f, 30f, 0f);
        return go.AddComponent<DirectionalLight>();
    }

    /// <summary>The orthonormal light-space basis GetShadowMatrix builds internally.</summary>
    private static (Float3 right, Float3 up, Float3 forward) LightBasis(DirectionalLight light)
    {
        Float3 forward = -light.Transform.Forward;
        Float3 up = Float3.Normalize(light.Transform.Up);
        Float3 right = Float3.Normalize(Float3.Cross(up, forward));
        up = Float3.Normalize(Float3.Cross(forward, right));
        return (right, up, forward);
    }

    [Fact]
    public void DirectionalLight_GetShadowMatrix_CentersOrthoOnFocusWithinTexel()
    {
        DirectionalLight light = CreateAngledLight();
        Float3 focus = new(37.4f, 2.6f, -18.9f);

        light.GetShadowMatrix(focus, Resolution, CascadeDistance, out Float4x4 view, out _);

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

        light.GetShadowMatrix(focus, Resolution, CascadeDistance, out Float4x4 view, out _);
        light.GetShadowMatrix(nudged, Resolution, CascadeDistance, out Float4x4 nudgedView, out _);

        // Identical, not merely close: a shadow map that slides by a fraction of a texel each frame
        // is what makes shadow edges crawl, and this is exactly the case a moving player hits.
        Assert.Equal(view.ToArray(), nudgedView.ToArray());
    }
}
