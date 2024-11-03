﻿// This file is part of the Prowl Game Engine
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
    public int qualitySamples = 32;
    public int blockerSamples = 16;
    public float shadowDistance = 50f;
    public float shadowRadius = 70f;

    public override void Update()
    {
        RenderPipeline.AddLight(this);
    }

    public override LightType GetLightType() => LightType.Directional;
    public override void GetShadowMatrix(out Matrix4x4 view, out Matrix4x4 projection)
    {
        Vector3 forward = Transform.forward;
        projection = Matrix4x4.CreateOrthographicLeftHanded(shadowDistance, shadowDistance, -shadowDistance, shadowDistance);
        projection = Graphics.GetGPUProjectionMatrix(projection);
        view = Matrix4x4.CreateLookToLeftHanded(Transform.position, forward, Transform.up);
    }

    public override GPULight GetGPULight(int res, bool cameraRelative, Vector3 cameraPosition)
    {
        Vector3 forward = Transform.forward;
        Matrix4x4 proj = Matrix4x4.CreateOrthographicLeftHanded(shadowDistance, shadowDistance, -shadowDistance, shadowDistance);
        proj = Graphics.GetGPUProjectionMatrix(proj);
        Matrix4x4 view;
        Vector3 lightPos;

        if (cameraRelative)
        {
            view = Matrix4x4.CreateLookToLeftHanded(Transform.position - cameraPosition, forward, Transform.up);
        }
        else
        {
            view = Matrix4x4.CreateLookToLeftHanded(Transform.position, forward, Transform.up);
        }

        return new GPULight
        {
            PositionType = new Vector4(qualitySamples, blockerSamples, 0, 0),
            DirectionRange = new Vector4(GameObject.Transform.forward, shadowDistance),
            Color = color.GetUInt(),
            Intensity = intensity,
            SpotData = new Vector2(ambientIntensity, 0),
            ShadowData = new Vector4(shadowRadius, 0.0, shadowBias, shadowNormalBias),
            ShadowMatrix = (view * proj).ToFloat()
        };
    }
}
