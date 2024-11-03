// This file is part of the Prowl Game Engine
// Licensed under the MIT License. See the LICENSE file in the project root for details.

using Prowl.Icons;
using Prowl.Runtime.Rendering.Pipelines;

namespace Prowl.Runtime;

[AddComponentMenu($"{FontAwesome6.Tv}  Rendering/{FontAwesome6.Lightbulb}  Point Light")]
public class PointLight : Light
{
    public float radius = 4.0f;

    public override LightType GetLightType() => LightType.Point;

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
    
    public override void GetShadowMatrix(out Matrix4x4 view, out Matrix4x4 projection)
    {
        view = Matrix4x4.Identity; projection = Matrix4x4.Identity;
    }
}
