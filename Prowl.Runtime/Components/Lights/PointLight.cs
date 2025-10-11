// This file is part of the Prowl Game Engine
// Licensed under the MIT License. See the LICENSE file in the project root for details.

using Prowl.Vector;
using Prowl.Runtime.Rendering;

namespace Prowl.Runtime;

public class PointLight : Light
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

    public override void Update()
    {
        GameObject.Scene.PushLight(this);
        Debug.DrawWireSphere(Transform.position, range, color);
    }

    public override LightType GetLightType() => LightType.Point;

    public override void GetShadowMatrix(out Double4x4 view, out Double4x4 projection)
    {
        // Point lights use cubemap shadows, but for now we'll use a simple perspective projection
        // This is a placeholder - proper point light shadows would require 6 shadow maps (cubemap)
        projection = Double4x4.CreatePerspectiveFov(90f * Maths.Deg2Rad, 1.0f, 0.1f, range);
        view = Double4x4.CreateLookTo(Transform.position, Transform.forward, Transform.up);
    }

    public void UploadToGPU(bool cameraRelative, Double3 cameraPosition, int atlasX, int atlasY, int atlasWidth, int lightIndex)
    {
        Double3 position = cameraRelative ? Transform.position - cameraPosition : Transform.position;

        PropertyState.SetGlobalVector($"_PointLights[{lightIndex}].position", position);
        PropertyState.SetGlobalVector($"_PointLights[{lightIndex}].color", new Double3(color.R, color.G, color.B));
        PropertyState.SetGlobalFloat($"_PointLights[{lightIndex}].intensity", intensity);
        PropertyState.SetGlobalFloat($"_PointLights[{lightIndex}].range", range);

        if (castShadows)
        {
            GetShadowMatrix(out var view, out var proj);

            if (cameraRelative)
                view.Translation -= cameraPosition;

            PropertyState.SetGlobalMatrix($"_PointLights[{lightIndex}].shadowMatrix", Maths.Mul(proj, view));
            PropertyState.SetGlobalFloat($"_PointLights[{lightIndex}].shadowBias", shadowBias);
            PropertyState.SetGlobalFloat($"_PointLights[{lightIndex}].shadowNormalBias", shadowNormalBias);
            PropertyState.SetGlobalFloat($"_PointLights[{lightIndex}].shadowStrength", shadowStrength);

            PropertyState.SetGlobalFloat($"_PointLights[{lightIndex}].atlasX", atlasX);
            PropertyState.SetGlobalFloat($"_PointLights[{lightIndex}].atlasY", atlasY);
            PropertyState.SetGlobalFloat($"_PointLights[{lightIndex}].atlasWidth", atlasWidth);
        }
        else
        {
            PropertyState.SetGlobalFloat($"_PointLights[{lightIndex}].atlasX", -1);
            PropertyState.SetGlobalFloat($"_PointLights[{lightIndex}].atlasY", -1);
            PropertyState.SetGlobalFloat($"_PointLights[{lightIndex}].atlasWidth", 0);
            PropertyState.SetGlobalFloat($"_PointLights[{lightIndex}].shadowStrength", 0);
        }
    }
}
