// This file is part of the Prowl Game Engine
// Licensed under the MIT License. See the LICENSE file in the project root for details.

using Prowl.Icons;

namespace Prowl.Runtime;

[AddComponentMenu($"{FontAwesome6.Tv}  Rendering/{FontAwesome6.Lightbulb}  Directional Light")]
[ExecuteAlways]
public class DirectionalLight : Light
{
    public enum Resolution : int
    {
        _512 = 512,
        _1024 = 1024,
        _2048 = 2048
    }

    public Resolution shadowResolution = Resolution._1024;

    public float ambientIntensity = 0.05f;
    public float qualitySamples = 16;
    public float blockerSamples = 16;
    public float shadowDistance = 50f;
    public float shadowRadius = 0.02f;
    public float shadowPenumbra = 80f;
    public float shadowMinimumPenumbra = 0.02f;


    public override void GetCullingData(out bool isRenderable, out bool isCullable, out Bounds bounds)
    {
        isRenderable = false;
        isCullable = true;
        bounds = default;
    }


    public override Material GetMaterial()
    {
        return null;
    }


    public override void GetRenderingData(out LightType type, out Vector3 facingDirection)
    {
        type = LightType.Directional;
        facingDirection = Transform.forward;
    }
}
