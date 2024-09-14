// This file is part of the Prowl Game Engine
// Licensed under the MIT License. See the LICENSE file in the project root for details.

namespace Prowl.Runtime;

public interface IRenderable
{
    public Material GetMaterial();

    public void GetRenderingData(out PropertyBlock properties, out IGeometryDrawData drawData, out Matrix4x4 model);

    public void GetCullingData(out bool isRenderable, out Bounds bounds);
}


public enum LightType
{
    Directional,
    Spot,
    Point
}


public interface IRenderableLight
{
    public Material GetMaterial();

    public void GetRenderingData(out LightType type, out Vector3 facingDirection);

    public void GetCullingData(out bool isRenderable, out bool isCullable, out Bounds bounds);
}
