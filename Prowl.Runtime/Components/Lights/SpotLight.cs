// This file is part of the Prowl Game Engine
// Licensed under the MIT License. See the LICENSE file in the project root for details.

using Prowl.Icons;
using Prowl.Runtime.Rendering.Pipelines;

namespace Prowl.Runtime;

[AddComponentMenu($"{FontAwesome6.Tv}  Rendering/{FontAwesome6.Lightbulb}  Spot Light")]
public class SpotLight : Light
{
    public float distance = 4.0f;
    public float angle = 0.97f;
    public float falloff = 0.96f;

    public override LightType GetLightType() => LightType.Spot;

    public override GPULight GetGPULight(int res)
    {
        GetShadowMatrix(out Matrix4x4 view, out Matrix4x4 proj);

        return new GPULight
        {
            PositionType = new Vector4(GameObject.Transform.position, 2),
            DirectionRange = new Vector4(GameObject.Transform.forward, distance),
            Color = color.GetUInt(),
            Intensity = intensity,
            SpotData = new Vector2(angle, falloff),
            ShadowData = new Vector4(0, 0, shadowBias, shadowNormalBias),
            ShadowMatrix = (view * proj).ToFloat(),
            AtlasX = 0,
            AtlasY = 0,
            AtlasWidth = 0
        };
    }

    public override void GetShadowMatrix(out Matrix4x4 view, out Matrix4x4 projection)
    {
        var forward = Transform.forward;
        projection = Matrix4x4.CreatePerspectiveFieldOfView(MathD.ToRad(90), 1f, 0.01f, distance);
        view = Matrix4x4.CreateLookToLeftHanded(Transform.position, -forward, Transform.up);
    }
}
