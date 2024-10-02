using System;
using System.Numerics;

namespace AssimpSharp
{
    public enum AiLightSourceType
    {
        Undefined = 0x0,
        Directional = 0x1,
        Point = 0x2,
        Spot = 0x3,
        Ambient = 0x4,
        Area = 0x5
    }

    public class AiLight
    {
        public string Name { get; set; } = "";
        public AiLightSourceType Type { get; set; } = AiLightSourceType.Undefined;
        public Vector3 Position { get; set; } = Vector3.Zero;
        public Vector3 Direction { get; set; } = Vector3.Zero;
        public Vector3 Up { get; set; } = Vector3.Zero;
        public float AttenuationConstant { get; set; } = 0f;
        public float AttenuationLinear { get; set; } = 1f;
        public float AttenuationQuadratic { get; set; } = 0f;
        public Vector3 ColorDiffuse { get; set; } = Vector3.Zero;
        public Vector3 ColorSpecular { get; set; } = Vector3.Zero;
        public Vector3 ColorAmbient { get; set; } = Vector3.Zero;
        public float AngleInnerCone { get; set; } = (float)(Math.PI * 2);
        public float AngleOuterCone { get; set; } = (float)(Math.PI * 2);
        public Vector2 Size { get; set; } = Vector2.Zero;
    }
}