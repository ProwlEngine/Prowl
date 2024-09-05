// This file is part of the Prowl Game Engine
// Licensed under the MIT License. See the LICENSE file in the project root for details.

using Veldrid;


namespace Prowl.Runtime
{
    public interface IRenderable
    {
        public Material GetMaterial();

        public void GetRenderingData(out PropertyBlock properties, out IGeometryDrawData drawData, out Matrix4x4 model);

        public void GetCullingData(out bool isRenderable, out Bounds bounds);
    }
}
