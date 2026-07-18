// This file is part of the Prowl Game Engine
// Licensed under the MIT License. See the LICENSE file in the project root for details.

using System;
using System.Collections.Generic;

using Prowl.Graphite;

using Prowl.Runtime.Resources;

namespace Prowl.Runtime.Rendering;

/// <summary>
/// Base class for graph-driven render pipelines. A subclass adds its passes in
/// <see cref="InitializePasses"/>; the pipeline solves them into a dependency-ordered graph and runs
/// it once per camera: it allocates the graph's textures, runs the ordered passes, then blits the
/// presenting pass's main output to the camera target. An optional <see cref="Culler"/> threads the
/// scene in as draw commands for the passes to consume.
/// </summary>
public abstract class RenderPipeline<TDrawCommand> : RenderPipeline
{
    /// <summary>Culls the scene and produces the draw commands the passes consume. Optional.</summary>
    public IRenderCuller<TDrawCommand> Culler { get; set; }

    private readonly List<IPass<TDrawCommand>> _passes = new();
    private RenderGraph<TDrawCommand>? _graph;
    private bool _initialized;

    private readonly Dictionary<RenderResourceID, RenderTexture> _resources = new();
    private readonly List<RenderTexture> _rented = new();

    /// <summary>
    /// Called once, lazily, before the first render. Override to populate the pipeline's passes with
    /// <see cref="AddPass"/>. Their read/write declarations determine execution order.
    /// </summary>
    protected abstract void InitializePasses();

    /// <summary>Adds a pass to this pipeline. Call from <see cref="InitializePasses"/>.</summary>
    protected void AddPass(IPass<TDrawCommand> pass)
        => _passes.Add(pass ?? throw new ArgumentNullException(nameof(pass)));

    /// <summary>The solved graph, built on first use from the passes added in <see cref="InitializePasses"/>.</summary>
    protected RenderGraph<TDrawCommand> Graph
    {
        get
        {
            if (!_initialized)
            {
                InitializePasses();
                _initialized = true;
            }
            return _graph ??= RenderGraph<TDrawCommand>.Build(_passes);
        }
    }

    public override void Render(Camera camera, in RenderingData data)
    {
        ExecuteGraph(Graph, camera, in data);
        base.Render(camera, in data);
    }

    /// <summary>Runs the whole ordered pass list for a single camera and presents its final output.</summary>
    protected virtual void ExecuteGraph(RenderGraph<TDrawCommand> graph, Camera camera, in RenderingData data)
    {
        RenderTexture? target = camera.UpdateRenderData();
        CameraSnapshot css = new(camera);

        if (Culler != null)
        {
            var (renderables, lights) = CollectRenderables(camera.GameObject.Scene, camera);
            Culler.Cull(css, renderables, lights);
        }

        AllocateResources(graph, css);

        // Fresh context per execution so its globals are scoped to this camera and never bleed
        // into another camera's passes.
        var context = new RenderContext<TDrawCommand>
        {
            RenderingCamera = camera,
            CameraData = css,
            Culler = Culler,
            Data = data,
            Resources = _resources
        };

        PopulateCameraGlobals(context.Globals, css);

        foreach (RenderGraph<TDrawCommand>.PassNode node in graph.OrderedPasses)
            node.Pass.Render(context);

        Present(context, graph, target);
        ReleaseResources();
    }

    private void AllocateResources(RenderGraph<TDrawCommand> graph, CameraSnapshot css)
    {
        _resources.Clear();
        foreach (KeyValuePair<RenderResourceID, RenderTextureDesc> entry in graph.Resources)
        {
            (int width, int height) = entry.Value.Resolve(css.PixelWidth, css.PixelHeight);
            RenderTexture rt = RenderTexture.GetTemporaryRT(width, height, entry.Value.EnableDepth, entry.Value.ColorFormats);
            _resources[entry.Key] = rt;
            _rented.Add(rt);
        }
    }

    private void Present(RenderContext<TDrawCommand> context, RenderGraph<TDrawCommand> graph, RenderTexture? target)
    {
        if (!graph.PresentationSource.IsValid || !_resources.TryGetValue(graph.PresentationSource, out RenderTexture source))
            return;

        CommandBuffer cmd = context.GetCommandBuffer("PipelinePresent");
        cmd.Blit(source, target);
        context.SubmitCommandBuffer(cmd);
    }

    private void ReleaseResources()
    {
        foreach (RenderTexture rt in _rented)
            RenderTexture.ReleaseTemporaryRT(rt);

        _rented.Clear();
        _resources.Clear();
    }

    public override void OnDispose()
    {
        foreach (IPass<TDrawCommand> pass in _passes)
            (pass as IDisposable)?.Dispose();

        base.OnDispose();
    }
}
