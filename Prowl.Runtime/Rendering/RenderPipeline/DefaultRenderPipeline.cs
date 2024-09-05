// This file is part of the Prowl Game Engine
// Licensed under the MIT License. See the LICENSE file in the project root for details.

using Veldrid;

namespace Prowl.Runtime.RenderPipelines
{
    public class DefaultRenderPipeline : RenderPipeline
    {
        public static DefaultRenderPipeline Default = new();

        public override void Render(Framebuffer target, Camera camera, in RenderingData data)
        {
            CommandBuffer buffer = CommandBufferPool.Get("Rendering Command Buffer");

            buffer.SetRenderTarget(target);
            buffer.ClearRenderTarget(camera.DoClear, camera.DoClear, camera.ClearColor);

            Matrix4x4 vp = data.View * data.Projection;

            // BoundingFrustum frustum = new BoundingFrustum(vp);

            System.Numerics.Matrix4x4 floatVP = vp.ToFloat();

            foreach (RenderBatch batch in EnumerateBatches())
            {
                buffer.SetMaterial(batch.material);

                foreach (IRenderable renderable in batch.renderables)
                {
                    // if (CullRenderable(renderable, frustum))
                    //     continue;

                    renderable.GetRenderingData(out PropertyBlock properties, out IGeometryDrawData drawData, out Matrix4x4 model);

                    buffer.ApplyPropertyState(properties);

                    buffer.SetColor("_MainColor", Color.white);
                    buffer.SetMatrix("_Matrix_MVP", Matrix4x4.Identity.ToFloat());

                    buffer.UpdateBuffer("_PerDraw");

                    buffer.SetDrawData(Mesh.GetFullscreenQuad());
                    buffer.DrawIndexed((uint)Mesh.GetFullscreenQuad().IndexCount, 0, 1, 0, 0);
                }
            }

            Graphics.SubmitCommandBuffer(buffer);

            CommandBufferPool.Release(buffer);
        }


        private static bool CullRenderable(IRenderable renderable, BoundingFrustum cameraFrustum)
        {
            renderable.GetCullingData(out bool isRenderable, out Bounds bounds);

            return !isRenderable || cameraFrustum.Contains(bounds) == ContainmentType.Disjoint;
        }
    }
}
