// This file is part of the Prowl Game Engine
// Licensed under the MIT License. See the LICENSE file in the project root for details.

using Prowl.Icons;
using Prowl.Runtime.Rendering.Pipelines;

namespace Prowl.Runtime;

[AddComponentMenu($"{FontAwesome6.Tv}  Rendering/{FontAwesome6.Lightbulb}  Point Light")]
public class PointLight : Light
{
    public float radius = 4.0f;

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
        type = LightType.Point;
        facingDirection = Transform.forward;
    }
}
