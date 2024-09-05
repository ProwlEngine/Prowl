// This file is part of the Prowl Game Engine
// Licensed under the MIT License. See the LICENSE file in the project root for details.

using Veldrid;


namespace Prowl.Runtime.RenderPipelines
{
    public struct RenderingData
    {
        public Matrix4x4 View;
        public Matrix4x4 Projection;


        public void InitializeFromCamera(Camera camera, Vector2 targetScale)
        {
            View = camera.GetViewMatrix();
            Projection = camera.GetProjectionMatrix(targetScale);
        }
    }

    public abstract class RenderPipeline
    {
        public abstract void Render(Framebuffer target, Camera camera, in RenderingData data);
    }
}
