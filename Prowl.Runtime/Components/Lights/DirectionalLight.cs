// This file is part of the Prowl Game Engine
// Licensed under the MIT License. See the LICENSE file in the project root for details.

using Prowl.Icons;
using Prowl.Runtime.Rendering.Pipelines;

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

    public override void Update()
    {
        RenderPipeline.AddLight(this);
    }

    public override LightType GetLightType() => LightType.Directional;
    public override void GetShadowMatrix(out Matrix4x4 view, out Matrix4x4 projection)
    {
        Vector3 forward = Transform.forward;
        projection = Matrix4x4.CreateOrthographicLeftHanded(shadowDistance, shadowDistance, -1000f, 1000f);
        projection = Graphics.GetGPUProjectionMatrix(projection);
        view = Matrix4x4.CreateLookToLeftHanded(Transform.position, forward, Transform.up);
    }

    public override GPULight GetGPULight(int res)
    {
        GetShadowMatrix(out Matrix4x4 view, out Matrix4x4 proj);

        return new GPULight
        {
            PositionType = new Vector4(qualitySamples, 0, 0, 0),
            DirectionRange = new Vector4(GameObject.Transform.forward, shadowDistance),
            Color = color.GetUInt(),
            Intensity = intensity,
            SpotData = new Vector2(ambientIntensity, 0),
            ShadowData = new Vector4(shadowRadius, shadowPenumbra, shadowBias, shadowNormalBias),
            ShadowMatrix = (view * proj).ToFloat()
        };
    }
}
