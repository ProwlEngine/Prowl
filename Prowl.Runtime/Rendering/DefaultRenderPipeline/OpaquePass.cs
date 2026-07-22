// This file is part of the Prowl Game Engine
// Licensed under the MIT License. See the LICENSE file in the project root for details.

using System;
using System.Collections.Generic;

using Prowl.Graphite;
using Prowl.Graphite.RenderGraph;

using Prowl.Runtime.Resources;
using Prowl.Vector;

using RenderTexture = Prowl.Graphite.RenderTexture;

namespace Prowl.Runtime.Rendering;

/// <summary>
/// Copies the shadows chain forward, draws the skybox, then the scene's opaque geometry (read directly
/// from the camera's scene <see cref="SceneCuller"/>), then (in the editor) records the gizmos on top.
/// </summary>
public sealed class OpaquePass : CopyChainPass, IDisposable
{
    private Texture2D? _depthCopy;

    public OpaquePass() : base("Opaque", DefaultChain.Opaque, present: false, inputId: DefaultChain.Shadows) { }

    public void Dispose()
    {
        if (_depthCopy != null)
            Graphics.DisposeDeferred(_depthCopy);
        _depthCopy = null;
    }

    protected override void OnRender(RenderContext<CameraView> context, CommandBuffer cmd, RenderTexture output)
    {
        // The chain copy only blits color into this pass's transient target; its depth attachment
        // still holds undefined contents, so clear it before anything depth-tests against it.
        cmd.ClearDepthStencil(1f, 0);

        SkyboxRenderer.Render(cmd);

        DrawOpaqueGeometry(context, cmd);

        // The depth copy exists only so the gizmo (this pass) and grid (TransparentsPass) shaders can
        // depth-test against the scene. Renders that draw neither - thumbnails, the game view - skip it
        // entirely rather than copy a texture nothing samples.
        RenderingData data = context.View.Data;
        if (!data.DisplayGizmos && !data.DisplayGrid)
            return;

        Texture2D depthCopy = GetDepthCopy((int)output.Desc.Width, (int)output.Desc.Height);
        cmd.CopyTexture(output.DepthTexture, depthCopy.Handle);
        context.View.SceneDepthCopy = depthCopy;

        if (data.DisplayGizmos)
            GizmoRenderer.Render(cmd, depthCopy);
    }

    /// <summary>Draws every renderable the camera's scene has submitted into its <see cref="SceneCuller"/>.
    /// Single-instance renderables only for now; GPU-instanced ones (terrain, particles) are skipped
    /// until an instanced draw path exists.</summary>
    private static bool s_loggedNoScene;
    private static bool s_loggedRenderableCount;

    private static void DrawOpaqueGeometry(RenderContext<CameraView> context, CommandBuffer cmd)
    {
        Camera camera = context.View.Camera;
        Scene? scene = camera.Scene;
        if (scene == null)
        {
            if (!s_loggedNoScene)
            {
                s_loggedNoScene = true;
                Debug.LogWarning("OpaquePass: camera.Scene is null, skipping opaque geometry.");
            }
            return;
        }

        var viewer = new ViewerData(camera);
        IReadOnlyList<IRenderable> renderables = scene.Culler.Renderables;

        if (!s_loggedRenderableCount)
        {
            s_loggedRenderableCount = true;
            Debug.LogWarning($"OpaquePass: scene.Culler.Renderables.Count = {renderables.Count}");
        }

        for (int i = 0; i < renderables.Count; i++)
        {
            IRenderable renderable = renderables[i];

            renderable.GetCullingData(out bool isRenderable, out _);
            if (!isRenderable)
            {
                EmitRenderable(renderable, "", "", culled: true, drawCallCount: 0);
                continue;
            }

            renderable.GetRenderingData(viewer, out PropertySet properties, out Mesh mesh, out Float4x4 model, out InstanceData[]? instanceData);
            if (instanceData != null && instanceData.Length > 0)
            {
                EmitRenderable(renderable, "", "", culled: true, drawCallCount: 0);
                continue;
            }

            Material material = renderable.GetMaterial();
            if (mesh.IsNotValid() || material.IsNotValid())
            {
                EmitRenderable(renderable, material?.Name ?? "", "", culled: true, drawCallCount: 0);
                continue;
            }

            cmd.DrawMesh(mesh, material, 0, model, properties);
            EmitRenderable(renderable, material.Name, mesh.Name, culled: false, drawCallCount: 1);
        }
    }

    internal static void EmitRenderable(IRenderable renderable, string materialName, string meshName, bool culled, int drawCallCount)
    {
        if (RenderProfilerHooks.Sink == null)
            return;

        RenderProfilerHooks.Sink.Renderable(new RenderableRecord
        {
            MaterialName = materialName,
            MeshName = meshName,
            Layer = renderable.GetLayer(),
            Position = renderable.GetPosition(),
            Culled = culled,
            DrawCallCount = drawCallCount,
        });
    }

    /// <summary>
    /// Depth can't stay bound as the framebuffer's depth-stencil attachment and be sampled by the
    /// gizmo/grid shaders at the same time, so this keeps a same-sized standalone copy around instead
    /// of touching the pooled chain resource's depth attachment directly.
    /// </summary>
    private Texture2D GetDepthCopy(int width, int height)
    {
        if (_depthCopy == null || _depthCopy.Width != (uint)width || _depthCopy.Height != (uint)height)
        {
            if (_depthCopy != null)
                Graphics.DisposeDeferred(_depthCopy);
            _depthCopy = new Texture2D((uint)width, (uint)height, PixelFormat.D24_UNorm_S8_UInt, TextureUsage.DepthStencil | TextureUsage.Sampled);
            _depthCopy.Name = "Opaque Depth Copy";
        }

        return _depthCopy;
    }
}
