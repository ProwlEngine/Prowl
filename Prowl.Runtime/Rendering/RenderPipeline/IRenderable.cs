// This file is part of the Prowl Game Engine
// Licensed under the MIT License. See the LICENSE file in the project root for details.

using System.Runtime.InteropServices;

namespace Prowl.Runtime.Rendering.Pipelines;

public interface IRenderable
{
    public Material GetMaterial();

    public byte GetLayer();

    public void GetRenderingData(out PropertyState properties, out IGeometryDrawData drawData, out Matrix4x4 model);

    public void GetCullingData(out bool isRenderable, out Bounds bounds);
}


public enum LightType
{
    Directional,
    Spot,
    Point,
    Area
}


public interface IRenderableLight
{
    public int GetLightID();
    public LightType GetLightType();
    public Vector3 GetLightPosition();
    public Vector3 GetLightDirection();
    public bool DoCastShadows();
    public void GetShadowMatrix(out Matrix4x4 view, out Matrix4x4 projection);

    public abstract GPULight GetGPULight(int res, bool cameraRelative, Vector3 cameraPosition);
}

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
