// This file is part of the Prowl Game Engine
// Licensed under the MIT License. See the LICENSE file in the project root for details.

using Prowl.Vector;
using Prowl.Runtime.Rendering;

namespace Prowl.Runtime;

public class SpotLight : Light
{
    public enum Resolution : int
    {
        _256 = 256,
        _512 = 512,
        _1024 = 1024,
        _2048 = 2048,
    }

    public Resolution shadowResolution = Resolution._512;

    public float range = 10f;
    public float innerAngle = 30f; // Inner cone angle in degrees
    public float outerAngle = 45f; // Outer cone angle in degrees

    public override void Update()
    {
        GameObject.Scene.PushLight(this);
        Debug.DrawArrow(Transform.position, Transform.forward * range, color);
        Debug.DrawWireCircle(Transform.position + Transform.forward * range, -Transform.forward, range * Maths.Tan(outerAngle * 0.5f * Maths.Deg2Rad), color);
    }

    public override LightType GetLightType() => LightType.Spot;

    public override void GetShadowMatrix(out Double4x4 view, out Double4x4 projection)
    {
        Double3 forward = Transform.forward;
        // Use perspective projection for spot light shadows
        float fov = outerAngle * 2.0f; // Full cone angle
        projection = Double4x4.CreatePerspectiveFov(fov * Maths.Deg2Rad, 1.0f, 0.1f, range);
        view = Double4x4.CreateLookTo(Transform.position, forward, Transform.up);
    }

    public void UploadToGPU(bool cameraRelative, Double3 cameraPosition, int atlasX, int atlasY, int atlasWidth, int lightIndex)
    {
        Double3 position = cameraRelative ? Transform.position - cameraPosition : Transform.position;

        PropertyState.SetGlobalVector($"_SpotLights[{lightIndex}].position", position);
        PropertyState.SetGlobalVector($"_SpotLights[{lightIndex}].direction", Transform.forward);
        PropertyState.SetGlobalVector($"_SpotLights[{lightIndex}].color", color);
        PropertyState.SetGlobalFloat($"_SpotLights[{lightIndex}].intensity", intensity);
        PropertyState.SetGlobalFloat($"_SpotLights[{lightIndex}].range", range);
        PropertyState.SetGlobalFloat($"_SpotLights[{lightIndex}].innerAngle", (float)Maths.Cos(innerAngle * 0.5f * Maths.Deg2Rad));
        PropertyState.SetGlobalFloat($"_SpotLights[{lightIndex}].outerAngle", (float)Maths.Cos(outerAngle * 0.5f * Maths.Deg2Rad));

        if (castShadows)
        {
            GetShadowMatrix(out var view, out var proj);

            if (cameraRelative)
                view.Translation -= cameraPosition;

            PropertyState.SetGlobalMatrix($"_SpotLights[{lightIndex}].shadowMatrix", Maths.Mul(proj, view));
            PropertyState.SetGlobalFloat($"_SpotLights[{lightIndex}].shadowBias", shadowBias);
            PropertyState.SetGlobalFloat($"_SpotLights[{lightIndex}].shadowNormalBias", shadowNormalBias);
            PropertyState.SetGlobalFloat($"_SpotLights[{lightIndex}].shadowStrength", shadowStrength);
            PropertyState.SetGlobalFloat($"_SpotLights[{lightIndex}].shadowQuality", (float)shadowQuality);

            PropertyState.SetGlobalFloat($"_SpotLights[{lightIndex}].atlasX", atlasX);
            PropertyState.SetGlobalFloat($"_SpotLights[{lightIndex}].atlasY", atlasY);
            PropertyState.SetGlobalFloat($"_SpotLights[{lightIndex}].atlasWidth", atlasWidth);
        }
        else
        {
            PropertyState.SetGlobalFloat($"_SpotLights[{lightIndex}].atlasX", -1);
            PropertyState.SetGlobalFloat($"_SpotLights[{lightIndex}].atlasY", -1);
            PropertyState.SetGlobalFloat($"_SpotLights[{lightIndex}].atlasWidth", 0);
            PropertyState.SetGlobalFloat($"_SpotLights[{lightIndex}].shadowStrength", 0);
        }
    }
}
