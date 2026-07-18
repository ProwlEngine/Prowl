// This file is part of the Prowl Game Engine
// Licensed under the MIT License. See the LICENSE file in the project root for details.

using System;
using System.Collections.Generic;

using Prowl.Graphite;

using Prowl.Runtime.Resources;

namespace Prowl.Runtime.Rendering;

/// <summary>
/// Copies the shadows chain forward, draws the skybox, then the culled opaque geometry, then (in the
/// editor) records the gizmos on top.
/// </summary>
public sealed class OpaquePass : CopyChainPass, IDisposable
{
    private static readonly DrawCommandQuery s_opaqueQuery = new()
    {
        Tag = "RenderOrder",
        TagValue = "Opaque",
        Sort = SortMode.FrontToBack,
    };

    private Texture2D? _depthCopy;

    public OpaquePass() : base("Opaque", DefaultChain.Opaque, present: false, inputId: DefaultChain.Shadows) { }

    public void Dispose()
    {
        if (_depthCopy != null)
            Graphics.DisposeDeferred(_depthCopy);
        _depthCopy = null;
    }

    protected override void OnRender(RenderContext<DrawCommand> context, CommandBuffer cmd, RenderTexture output)
    {
        // The chain copy only blits color into this pass's pooled target; its depth attachment still
        // holds undefined pool contents, so clear it before anything depth-tests against it.
        cmd.ClearRenderTarget(false, true, default);

        SkyboxRenderer.Render(cmd);

        DrawOpaqueGeometry(context, cmd);

        // The depth copy exists only so the gizmo (this pass) and grid (TransparentsPass) shaders can
        // depth-test against the scene. Renders that draw neither - thumbnails, the game view - skip it
        // entirely rather than copy a texture nothing samples.
        if (!context.Data.DisplayGizmos && !context.Data.DisplayGrid)
            return;

        Texture2D depthCopy = GetDepthCopy(output.Width, output.Height);
        cmd.CopyTexture(output.InternalDepth.Handle, depthCopy.Handle);
        context.SceneDepthCopy = depthCopy;

        if (context.Data.DisplayGizmos)
            GizmoRenderer.Render(cmd, depthCopy);
    }

    /// <summary>Draws every culled renderable whose material has a pass tagged <c>RenderOrder=Opaque</c>.</summary>
    private static void DrawOpaqueGeometry(RenderContext<DrawCommand> context, CommandBuffer cmd)
    {
        if (context.Culler == null)
            return;

        IReadOnlyList<DrawCommand> commands = context.Culler.GetDrawCommands(s_opaqueQuery);
        for (int i = 0; i < commands.Count; i++)
        {
            DrawCommand command = commands[i];
            cmd.DrawMesh(command.Mesh, command.Material, command.PassIndex, command.Model, command.Properties);
        }
    }

    /// <summary>
    /// Depth can't stay bound as the framebuffer's depth-stencil attachment and be sampled by the
    /// gizmo/grid shaders at the same time, so this keeps a same-sized standalone copy around instead
    /// of touching <see cref="RenderTexture.InternalDepth"/> directly.
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
