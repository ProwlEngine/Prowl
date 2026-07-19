// This file is part of the Prowl Game Engine
// Licensed under the MIT License. See the LICENSE file in the project root for details.

using System;
using System.Collections.Generic;

using Prowl.Graphite;
using Prowl.Graphite.ShaderDef;

using Prowl.Runtime.Resources;

namespace Prowl.Runtime.Rendering;

/// <summary>
/// The per-pass rendering context passed to <see cref="IPass{TDrawCommand}.Render"/>. Exposes the
/// camera being rendered, the culler for pulling draw commands, and resolves the pass's declared
/// texture handles to concrete <c>RenderTexture</c>s. It deliberately does not expose the real
/// camera target: a pass renders into its declared outputs and the pipeline presents the graph's
/// final main output.
/// </summary>
public sealed class RenderContext<TDrawCommand>
{
    /// <summary>The camera currently being rendered.</summary>
    public Camera RenderingCamera { get; internal set; }

    /// <summary>Immutable per-frame snapshot of the rendering camera (matrices, frustum, sizes).</summary>
    public RenderPipeline.CameraSnapshot CameraData { get; internal set; }

    /// <summary>The culler holding this camera's culled/sorted scene.</summary>
    public IRenderCuller<TDrawCommand> Culler { get; internal set; }

    /// <summary>Per-frame render flags (gizmos, grid, scene-view, etc.) for this camera.</summary>
    public RenderingData Data { get; internal set; }

    /// <summary>
    /// A sampleable copy of the opaque pass's depth buffer, taken after opaque geometry is drawn and
    /// before its source depth attachment is written to again. Debug overlays (gizmos, grid) that need
    /// to depth-test against the scene sample this instead of the live depth attachment, since a
    /// texture can't be bound as a framebuffer's depth target and a shader resource at the same time.
    /// </summary>
    public Texture2D? SceneDepthCopy { get; set; }

    /// <summary>
    /// Global shader properties for this execution (camera matrices, time, ambient, ...). Scoped to
    /// the one camera this context renders - a fresh context is created per execution, so globals
    /// never bleed between cameras. Automatically bound on every command buffer rented through
    /// <see cref="GetCommandBuffer"/>, so passes never bind it themselves. Populated by the pipeline
    /// before the pass loop runs.
    /// </summary>
    public PropertySet Globals { get; } = new();

    internal Dictionary<RenderResourceID, RenderTexture> Resources;

    /// <summary>Profiler recording this execution, or null when profiling is off.</summary>
    internal RenderProfileRecorder? Profiler;

    /// <summary>Opens a nested timing region inside the current pass. Pair with <see cref="EndSample"/>.</summary>
    public void BeginSample(string name) => Profiler?.BeginSample(name);

    /// <summary>Closes the most recently opened sample region.</summary>
    public void EndSample() => Profiler?.EndSample();

    /// <summary>
    /// Records one draw call into the current pass's profiling report. No-op when profiling is off.
    /// Mesh/material/shader are captured by asset guid plus display name so the report stays cheap and
    /// serializable, along with the material's active variant keywords.
    /// </summary>
    public void RecordDrawCall(Mesh? mesh, Material? material, int passIndex, int indexCount, int sourceRenderableId)
    {
        if (Profiler == null)
            return;

        Shader? shader = material?.Shader;

        var call = new DrawCallReport
        {
            MeshGuid = mesh?.AssetID ?? Guid.Empty,
            MeshName = mesh?.Name ?? "",
            MaterialGuid = material?.AssetID ?? Guid.Empty,
            MaterialName = material?.Name ?? "",
            ShaderGuid = shader?.AssetID ?? Guid.Empty,
            ShaderName = shader?.Name ?? "",
            PassIndex = passIndex,
            IndexCount = indexCount,
            InstanceCount = 1,
            SourceRenderableId = sourceRenderableId,
        };

        if (material?._localKeywords != null)
        {
            foreach (Keyword keyword in material._localKeywords.Values)
                call.VariantKeywords.Add($"{keyword.Name}={keyword.Value}");
        }

        Profiler.RecordDrawCall(call);
    }

    /// <summary>Resolves a declared handle to the physical render texture the graph allocated for it.</summary>
    public RenderTexture GetTexture(TextureHandle handle)
    {
        if (Resources != null && Resources.TryGetValue(handle.Id, out RenderTexture rt))
            return rt;

        throw new InvalidOperationException(
            $"No render texture is bound for '{handle.Id}'. A pass may only resolve handles it declared in SetupInputs.");
    }

    /// <summary>Rents a command buffer to record this pass's work into, with the globals already bound.</summary>
    public CommandBuffer GetCommandBuffer(string name = "")
    {
        CommandBuffer cmd = Graphics.GetCommandBuffer(name);
        cmd.SetProperties(Globals);
        return cmd;
    }

    /// <summary>Submits a command buffer recorded by this pass.</summary>
    public void SubmitCommandBuffer(CommandBuffer cmd) => Graphics.Submit(cmd);
}
