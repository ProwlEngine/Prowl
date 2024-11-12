// This file is part of the Prowl Game Engine
// Licensed under the MIT License. See the LICENSE file in the project root for details.

using System;

using Prowl.Icons;
using Prowl.Runtime.Rendering.Pipelines;

namespace Prowl.Runtime;

[AddComponentMenu($"{FontAwesome6.Tv}  Rendering/{FontAwesome6.Lightbulb}  Point Light")]
[ExecuteAlways]
public class PointLight : Light
{
    public float radius = 4.0f;

    public override void Update() => RenderPipeline.AddLight(this);

    public override LightType GetLightType() => LightType.Point;

    public override GPULight GetGPULight(int res, bool cameraRelative, Vector3 cameraPosition)
    {
        Vector3 lightPos;
        if (cameraRelative)
        {
            lightPos = Transform.position - cameraPosition;
        }
        else
        {
            lightPos = Transform.position;
        }

        return new GPULight
        {
            PositionType = new Vector4(lightPos, 1),
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

[AddComponentMenu($"{FontAwesome6.Tv}  Rendering/{FontAwesome6.Lightbulb}  Area Light")]
[ExecuteAlways]
public class AreaLight : Light
{
    public float width = 1.0f;
    public float height = 1.0f;
    public bool twoSided = true;

    public override void Update() => RenderPipeline.AddLight(this);
    public override LightType GetLightType() => LightType.Area;

    public override GPULight GetGPULight(int res, bool cameraRelative, Vector3 cameraPosition)
    {
        Vector3 lightPos;
        if (cameraRelative)
        {
            lightPos = Transform.position - cameraPosition;
        }
        else
        {
            lightPos = Transform.position;
        }

        // Pack width and height into DirectionRange.w as 2 half floats
        uint packedSize = ((uint)(width * 512) & 0xFFFF) | (((uint)(height * 512) & 0xFFFF) << 16);
        float packedSizeFloat = BitConverter.Int32BitsToSingle((int)packedSize);

        return new GPULight
        {
            PositionType = new Vector4(lightPos, 2), // Type 2 for area light
            DirectionRange = new Vector4(Transform.forward, packedSizeFloat),
            Color = color.GetUInt(),
            Intensity = intensity,
            SpotData = new Vector2(twoSided ? 1 : 0, 0),
            ShadowData = new Vector4(0, 0, 0, 0),
            AtlasX = 0,
            AtlasY = 0,
            AtlasWidth = 0
        };
    }

    public override void GetShadowMatrix(out Matrix4x4 view, out Matrix4x4 projection)
    {
        view = Matrix4x4.Identity;
        projection = Matrix4x4.Identity;
    }
}
