// This file is part of the Prowl Game Engine
// Licensed under the MIT License. See the LICENSE file in the project root for details.

using Prowl.Icons;

namespace Prowl.Runtime;

[AddComponentMenu($"{FontAwesome6.Tv}  Rendering/{FontAwesome6.Lightbulb}  Point Light")]
public class PointLight : Light
{
    public float radius = 4.0f;

    //public override Camera.CameraData? GetCameraData(int res)
    //{
    //    return null;
    //}

    public override GPULight GetGPULight(int res)
    {
        return new GPULight
        {
            PositionType = new Vector4(GameObject.Transform.position, 1),
            DirectionRange = new Vector4(0, 0, 0, radius),
            Color = color.GetUInt(),
            Intensity = intensity,
            SpotData = new Vector2(0, 0),
            ShadowData = new Vector4(0, 0, 0, 0),
            AtlasX = 0,
            AtlasY = 0,
            AtlasWidth = 0
        };
    }
}
