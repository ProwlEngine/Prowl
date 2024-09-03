// This file is part of the Prowl Game Engine
// Licensed under the MIT License. See the LICENSE file in the project root for details.

using Prowl.Icons;

namespace Prowl.Runtime;

[AddComponentMenu($"{FontAwesome6.Tv}  Rendering/{FontAwesome6.Lightbulb}  Spot Light")]
public class SpotLight : Light
{
    public float distance = 4.0f;
    public float angle = 0.97f;
    public float falloff = 0.96f;

    public override GPULight GetGPULight(int res)
    {
        var forward = Transform.forward;
        var proj = Matrix4x4.CreatePerspectiveFieldOfView(MathD.ToRad(90), 1f, 0.01f, distance);
        var view = Matrix4x4.CreateLookToLeftHanded(Transform.position, -forward, Transform.up);

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

    public override Camera.CameraData? GetCameraData(int res)
    {
        var forward = Transform.forward;
        var proj = Matrix4x4.CreatePerspectiveFieldOfView(MathD.ToRad(90), 1f, 0.01f, distance);
        var view = Matrix4x4.CreateLookToLeftHanded(Transform.position, -forward, Transform.up);
        return new Camera.CameraData(0, Transform.position, view, proj, MathD.ToRad(90), 0.01f, distance, true, Color.clear, new(0, 0, res, res), 1f, LayerMask.Everything, null);
    }
}
