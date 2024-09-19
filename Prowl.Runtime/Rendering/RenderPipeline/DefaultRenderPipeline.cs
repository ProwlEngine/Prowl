// This file is part of the Prowl Game Engine
// Licensed under the MIT License. See the LICENSE file in the project root for details.

using System;
using System.Collections.Generic;

using Veldrid;

namespace Prowl.Runtime.RenderPipelines;

public class DefaultRenderPipeline : RenderPipeline
{
    const bool cameraRelative = true;

    private static Mesh s_quadMesh;
    private static Material s_gridMaterial;
    private static Material s_defaultMaterial;
    private static Material s_skybox;
    private static Material s_gizmo;
    private static Mesh s_skyDome;
    private static Texture2D s_whiteTexture;

    public static DefaultRenderPipeline Default = new();


    private static void ValidateDefaults()
    {
        // TODO: FIXME: these values are never null
        s_quadMesh ??= Mesh.CreateQuad(Vector2.one);
        s_gridMaterial ??= new Material(Application.AssetProvider.LoadAsset<Shader>("Defaults/Grid.shader"));
        s_defaultMaterial ??= new Material(Application.AssetProvider.LoadAsset<Shader>("Defaults/DefaultUnlit.shader"));
        s_skybox ??= new Material(Application.AssetProvider.LoadAsset<Shader>("Defaults/ProceduralSky.shader"));
        s_gizmo ??= new Material(Application.AssetProvider.LoadAsset<Shader>("Defaults/Gizmo.shader"));

        s_whiteTexture ??= Texture2D.EmptyWhite;

        // TODO: FIXME: s_skyDome is never null
        // if (s_skyDome == null)
        // {
        GameObject skyDomeModel = Application.AssetProvider.LoadAsset<GameObject>("Defaults/SkyDome.obj").Res;
        MeshRenderer renderer = skyDomeModel.GetComponentInChildren<MeshRenderer>(true, true);

        if (renderer.Mesh.Res is not null)
        {
            s_skyDome = renderer.Mesh.Res;
        }
        // }
    }

    public override void Render(Framebuffer target, Camera camera, in RenderingData data)
    {
        ValidateDefaults();

        CommandBuffer buffer = CommandBufferPool.Get("Rendering Command Buffer");

        bool clearColor = camera.ClearMode == CameraClearMode.ColorOnly || camera.ClearMode == CameraClearMode.DepthColor;
        bool clearDepth = camera.ClearMode == CameraClearMode.DepthOnly || camera.ClearMode == CameraClearMode.DepthColor;
        bool drawSkybox = camera.ClearMode == CameraClearMode.Skybox;

        buffer.SetRenderTarget(target);
        buffer.ClearRenderTarget(clearDepth || drawSkybox, clearColor || drawSkybox, camera.ClearColor);

        Matrix4x4 view = camera.GetViewMatrix(!cameraRelative);
        Vector3 cameraPosition = camera.Transform.position;

        Matrix4x4 projection = camera.GetProjectionMatrix(data.TargetResolution, true);

        Matrix4x4 vp = view * projection;

        // BoundingFrustum frustum = new BoundingFrustum(vp);

        System.Numerics.Matrix4x4 floatVP = vp.ToFloat();

        List<IRenderableLight> lights = GetLights();
        Vector3 sunDirection = Vector3.up;

        if (lights.Count > 0)
        {
            IRenderableLight light0 = lights[0];

            light0.GetRenderingData(out LightType type, out Vector3 facingDirection);

            if (type == LightType.Directional)
            {
                sunDirection = facingDirection;
            }
        }


        if (drawSkybox)
        {
            buffer.SetMaterial(s_skybox);

            buffer.SetMatrix("_Matrix_VP", (camera.GetViewMatrix(false) * projection).ToFloat());
            buffer.SetVector("_SunDir", sunDirection);

            buffer.DrawSingle(s_skyDome);
        }


        foreach (RenderBatch batch in EnumerateBatches())
        {
            buffer.SetMaterial(batch.material);

            foreach (IRenderable renderable in batch.renderables)
            {
                //if (CullRenderable(renderable, frustum))
                //    continue;

                renderable.GetRenderingData(out PropertyBlock properties, out IGeometryDrawData drawData, out Matrix4x4 model);

                if (cameraRelative)
                    model.Translation -= cameraPosition;

                model = Graphics.GetGPUModelMatrix(model);

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

            if (cameraRelative)
                grid.Translation -= cameraPosition;

            Matrix4x4 MV = grid * view;
            Matrix4x4 MVP = grid * view * projection;

            buffer.SetMatrix("_Matrix_MV", MV.ToFloat());
            buffer.SetMatrix("_Matrix_MVP", MVP.ToFloat());

            buffer.SetColor("_GridColor", data.GridColor);
            buffer.SetFloat("_LineWidth", (float)data.GridSizes.z);
            buffer.SetFloat("_PrimaryGridSize", 1 / (float)data.GridSizes.x * gridScale * 2);
            buffer.SetFloat("_SecondaryGridSize", 1 / (float)data.GridSizes.y * gridScale * 2);
            buffer.SetFloat("_Falloff", 15.0f);
            buffer.SetFloat("_MaxDist", System.Math.Min(camera.FarClip, gridScale));

            buffer.SetMaterial(s_gridMaterial, 0);
            buffer.DrawSingle(s_quadMesh);
        }

        // Since for the time being rendering is guranteed to be executed after Update() and all other mono-behaviour methods
        // we can safely assume that all gizmos have been added to Debug and we can draw them here
        if (data.DisplayGizmo)
        {
            (Mesh? wire, Mesh? solid) = Debug.GetGizmoDrawData(cameraRelative, cameraPosition);

            if (wire != null || solid != null)
            {
                // The vertices have already been transformed by the gizmo system to be camera relative (if needed) so we just need to draw them
                buffer.SetMatrix("_Matrix_VP", vp.ToFloat());

                buffer.SetTexture("_MainTexture", s_whiteTexture);
                buffer.SetMaterial(s_gizmo);
                if (wire != null) buffer.DrawSingle(wire);
                if (solid != null) buffer.DrawSingle(solid);
            }

            List<GizmoBuilder.IconDrawCall> icons = Debug.GetGizmoIcons();
            if (icons != null)
            {
                buffer.SetMaterial(s_gizmo);

                foreach (GizmoBuilder.IconDrawCall icon in icons)
                {
                    Vector3 center = icon.center;
                    if (cameraRelative)
                        center -= cameraPosition;
                    Matrix4x4 billboard = Matrix4x4.CreateBillboard(center, Vector3.zero, camera.Transform.up, camera.Transform.forward);

                    buffer.SetMatrix("_Matrix_VP", (billboard * vp).ToFloat());
                    buffer.SetTexture("_MainTexture", icon.texture);

                    buffer.DrawSingle(s_quadMesh);
                }
            }
        }

        /*
        if (target.ColorTargets != null && target.ColorTargets.Length > 0)
        {
            RenderTexture temporaryRT = RenderTexture.GetTemporaryRT(target.Width, target.Height, null, [target.ColorTargets[0].Target.Format]);

            buffer.SetRenderTarget(temporaryRT);

            buffer.SetTexture("_MainTexture", target.ColorTargets[0].Target);

            RenderTexture.ReleaseTemporaryRT(temporaryRT);
        }
        */

        Graphics.SubmitCommandBuffer(buffer);

        CommandBufferPool.Release(buffer);
    }


    private static bool CullRenderable(IRenderable renderable, BoundingFrustum cameraFrustum)
    {
        renderable.GetCullingData(out bool isRenderable, out Bounds bounds);

        return !isRenderable || cameraFrustum.Contains(bounds) == ContainmentType.Disjoint;
    }
}
