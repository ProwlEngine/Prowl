// This file is part of the Prowl Game Engine
// Licensed under the MIT License. See the LICENSE file in the project root for details.

using System.Collections.Generic;
using System.Runtime.InteropServices;

namespace Prowl.Runtime;

public abstract class Light : MonoBehaviour
{
    public static readonly List<Light> Lights = new();

    public Color color = Color.white;
    public float intensity = 8.0f;
    public float shadowBias = 0.00001f;
    public float shadowNormalBias = 0.0025f;
    public bool castShadows = true;

    public override void OnEnable() => Lights.Add(this);

    public override void OnDisable() => Lights.Remove(this);

    public abstract GPULight GetGPULight(int res);
    public abstract Camera.CameraData? GetCameraData(int res);

    [StructLayout(LayoutKind.Sequential)]
    public struct GPULight
    {
        public System.Numerics.Vector4 PositionType;
        public System.Numerics.Vector4 DirectionRange;
        public uint Color;
        public float Intensity;
        public System.Numerics.Vector2 SpotData;
        public System.Numerics.Vector4 ShadowData;

        public System.Numerics.Matrix4x4 ShadowMatrix;
        public int AtlasX, AtlasY, AtlasWidth;
        public int Padding;
    }
}
