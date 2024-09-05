// This file is part of the Prowl Game Engine
// Licensed under the MIT License. See the LICENSE file in the project root for details.

using Veldrid;

namespace Prowl.Runtime.RenderPipelines
{
    public class DefaultRenderPipeline : RenderPipeline
    {
        private static Mesh s_gridMesh;
        private static Material s_gridMaterial;
        private static Material s_defaultMaterial;

        public static DefaultRenderPipeline Default = new();

        public override void Render(Framebuffer target, Camera camera, in RenderingData data)
        {
            s_gridMesh ??= Mesh.CreateQuad(Vector2.one);
            s_gridMaterial ??= new Material(Application.AssetProvider.LoadAsset<Shader>("Defaults/Grid.shader"));
            s_defaultMaterial ??= new Material(Application.AssetProvider.LoadAsset<Shader>("Defaults/DefaultUnlit.shader"));

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
                    //if (CullRenderable(renderable, frustum))
                    //    continue;

                    renderable.GetRenderingData(out PropertyBlock properties, out IGeometryDrawData drawData, out Matrix4x4 model);

                    buffer.ApplyPropertyState(properties);

                    buffer.SetColor("_MainColor", Color.white);
                    buffer.SetMatrix("_Matrix_MVP", model.ToFloat() * floatVP);

                    buffer.UpdateBuffer("_PerDraw");

                    buffer.SetDrawData(drawData);
                    buffer.DrawIndexed((uint)drawData.IndexCount, 0, 1, 0, 0);
                }
            }


            if (data.DisplayGrid)
            {
                const float gridScale = 1000;

                Matrix4x4 grid = Matrix4x4.CreateScale(gridScale);

                grid *= data.GridMatrix;

                Matrix4x4 MV = grid * data.View;
                Matrix4x4 MVP = grid * data.View * data.Projection;

                buffer.SetMatrix("_Matrix_MV", MV.ToFloat());
                buffer.SetMatrix("_Matrix_MVP", MVP.ToFloat());

                buffer.SetColor("_GridColor", data.GridColor);
                buffer.SetFloat("_LineWidth", (float)data.GridSizes.z);
                buffer.SetFloat("_PrimaryGridSize", 1 / (float)data.GridSizes.x * gridScale * 2);
                buffer.SetFloat("_SecondaryGridSize", 1 / (float)data.GridSizes.y * gridScale * 2);
                buffer.SetFloat("_Falloff", 15.0f);
                buffer.SetFloat("_MaxDist", System.Math.Min(camera.FarClip, gridScale));

                buffer.SetMaterial(s_gridMaterial, 0);
                buffer.DrawSingle(s_gridMesh);
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
