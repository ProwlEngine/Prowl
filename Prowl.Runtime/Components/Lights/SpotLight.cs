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
        type = LightType.Spot;
        facingDirection = Transform.forward;
    }
}
