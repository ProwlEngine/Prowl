// This file is part of the Prowl Game Engine
// Licensed under the MIT License. See the LICENSE file in the project root for details.

using Prowl.Runtime.Rendering;

namespace Prowl.Runtime;

public class DirectionalLight : Light
{
    public enum Resolution : int
    {
        _512 = 512,
        _1024 = 1024,
        _2048 = 2048,
        _4096 = 4096,
    }

    public Resolution shadowResolution = Resolution._1024;

    public float shadowDistance = 50f;

    public override void Update()
    {
        GameObject.Scene.PushLight(this);
        Debug.DrawArrow(Transform.position, -Transform.forward, Color.yellow);
        Debug.DrawWireCircle(Transform.position, Transform.forward, 0.5f, Color.yellow);
    }


    public override LightType GetLightType() => LightType.Directional;
    public override void GetShadowMatrix(out Matrix4x4 view, out Matrix4x4 projection)
    {
        Vector3 forward = -Transform.forward;
        projection = Matrix4x4.CreateOrthographic(shadowDistance, shadowDistance, 0.1f, shadowDistance);
        view = Matrix4x4.CreateLookTo(Transform.position - (forward * shadowDistance * 0.5), forward, Transform.up);
    }

    public void UploadToGPU(bool cameraRelative, Vector3 cameraPosition, int atlasX, int atlasY, int atlasWidth)
    {
        PropertyState.SetGlobalVector($"_Sun.direction", Transform.forward);
        PropertyState.SetGlobalVector($"_Sun.color", new Vector3(color.r, color.g, color.b));
        PropertyState.SetGlobalFloat($"_Sun.intensity", intensity);

        GetShadowMatrix(out var view, out var proj);

        if (cameraRelative)
            view.Translation -= cameraPosition;

        PropertyState.SetGlobalMatrix($"_Sun.shadowMatrix", (view * proj));
        PropertyState.SetGlobalFloat($"_Sun.shadowBias", shadowBias);
        PropertyState.SetGlobalFloat($"_Sun.shadowNormalBias", shadowNormalBias);
        PropertyState.SetGlobalFloat($"_Sun.shadowStrength", shadowStrength);
        PropertyState.SetGlobalFloat($"_Sun.shadowDistance", shadowDistance);

        PropertyState.SetGlobalFloat($"_Sun.atlasX", atlasX);
        PropertyState.SetGlobalFloat($"_Sun.atlasY", atlasY);
        PropertyState.SetGlobalFloat($"_Sun.atlasWidth", atlasWidth);
    }
}
