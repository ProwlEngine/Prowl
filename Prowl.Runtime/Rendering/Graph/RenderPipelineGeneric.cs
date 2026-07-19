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
        camera.LastRenderReport = ExecuteGraph(Graph, camera, in data);
        base.Render(camera, in data);
    }

    /// <summary>Runs the whole ordered pass list for a single camera, presents its final output, and
    /// returns the profiling report for the execution, or null when no capture/report was requested.
    /// Recording only spins up when <see cref="RenderProfiler.RequestCapture"/> or
    /// <see cref="RenderProfiler.RequestReport"/> is armed for this frame - otherwise the whole
    /// instrumentation path (draw call recording, sample scopes, counters) is skipped entirely.</summary>
    protected virtual RenderFrameReport? ExecuteGraph(RenderGraph<TDrawCommand> graph, Camera camera, in RenderingData data)
    {
        bool captureArmed = RenderProfiler.IsCaptureArmed;
        bool recording = captureArmed || RenderProfiler.ConsumeReportRequest();

        RenderProfileRecorder? recorder = null;
        if (recording)
        {
            recorder = new RenderProfileRecorder(camera);
            recorder.BeginFrame();
        }

        RenderTexture? target = camera.UpdateRenderData();
        CameraSnapshot css = new(camera);

        if (Culler != null)
        {
            var (renderables, lights) = CollectRenderables(camera.GameObject.Scene, camera);
            Culler.Cull(css, renderables, lights);
        }

        AllocateResources(graph, css);

        if (recorder != null)
            PopulateResources(recorder.Report, graph, css);

        // Fresh context per execution so its globals are scoped to this camera and never bleed
        // into another camera's passes.
        var context = new RenderContext<TDrawCommand>
        {
            RenderingCamera = camera,
            CameraData = css,
            Culler = Culler,
            Data = data,
            Resources = _resources,
            Profiler = recorder
        };

        PopulateCameraGlobals(context.Globals, css);

        foreach (RenderGraph<TDrawCommand>.PassNode node in graph.OrderedPasses)
        {
            recorder?.BeginPass(node.Pass.Name);

            if (recorder != null)
            {
                PassReport passReport = recorder.Report.Passes[^1];
                AddResourceNames(passReport.Inputs, node.Inputs);
                AddResourceNames(passReport.Outputs, node.Outputs);
                passReport.IsPresentationSource = node.MainOutput.IsValid && node.MainOutput == graph.PresentationSource;
            }

            node.Pass.Render(context);
            recorder?.EndPass();
        }

        Present(context, graph, target);

        if (recorder != null)
        {
            PopulateWiring(recorder.Report, graph);
            PopulateCounters(recorder.Report);
        }

        if (captureArmed)
        {
            RenderFrameReport report = recorder!.Finish();

            if (RenderProfiler.TryBeginCapture(report, _resources, css))
            {
                // Ownership of the pooled render textures transfers to the pending capture; the flush
                // reads them back once the frame closes and releases them. Do NOT release here.
                _resources.Clear();
                _rented.Clear();
            }
            else
            {
                ReleaseResources();
            }

            return report;
        }

        ReleaseResources();

        return recorder?.Finish();
    }

    private static void AddResourceNames(List<string> target, RenderResourceID[] ids)
    {
        if (ids == null)
            return;

        foreach (RenderResourceID id in ids)
            target.Add(RenderResourceID.NameOf(id) ?? id.ToString());
    }

    private void PopulateResources(RenderFrameReport report, RenderGraph<TDrawCommand> graph, CameraSnapshot css)
    {
        foreach (KeyValuePair<RenderResourceID, RenderTextureDesc> entry in graph.Resources)
        {
            RenderTextureDesc desc = entry.Value;
            (int width, int height) = desc.Resolve(css.PixelWidth, css.PixelHeight);

            PixelFormat[] formats = desc.ColorFormats ?? Array.Empty<PixelFormat>();

            long bytes = 0;
            foreach (PixelFormat format in formats)
                bytes += (long)format.GetSizeInBytes() * width * height;

            if (desc.EnableDepth)
                bytes += 4L * width * height;

            report.Resources.Add(new ResourceReport
            {
                Id = RenderResourceID.NameOf(entry.Key) ?? entry.Key.ToString(),
                Width = width,
                Height = height,
                HasDepth = desc.EnableDepth,
                ColorFormats = formats,
                BytesEstimated = bytes,
            });
        }
    }

    private static void PopulateWiring(RenderFrameReport report, RenderGraph<TDrawCommand> graph)
    {
        var writerOf = new Dictionary<string, int>();

        for (int p = 0; p < report.Passes.Count; p++)
        {
            foreach (string output in report.Passes[p].Outputs)
                writerOf.TryAdd(output, p);
        }

        foreach (ResourceReport resource in report.Resources)
        {
            if (writerOf.TryGetValue(resource.Id, out int writer))
                resource.ProducedByPassIndex = writer;

            for (int p = 0; p < report.Passes.Count; p++)
            {
                if (report.Passes[p].Inputs.Contains(resource.Id))
                    resource.ConsumedByPassIndex.Add(p);
            }

            if (resource.ProducedByPassIndex < 0)
                continue;

            foreach (int reader in resource.ConsumedByPassIndex)
            {
                report.Edges.Add(new GraphEdge
                {
                    WriterPassIndex = resource.ProducedByPassIndex,
                    ReaderPassIndex = reader,
                    ResourceId = resource.Id,
                });
            }
        }
    }

    private void PopulateCounters(RenderFrameReport report)
    {
        FrameCounters counters = report.Counters;

        if (Culler != null)
        {
            counters.RenderablesCollected = Culler.RenderablesCollected;
            counters.RenderablesCulled = Culler.RenderablesCulled;
            counters.RenderablesVisible = Culler.RenderablesVisible;
        }

        counters.PooledRtCount = _rented.Count;

        long rtBytes = 0;
        int drawCalls = 0;
        int triangles = 0;

        foreach (ResourceReport resource in report.Resources)
            rtBytes += resource.BytesEstimated;

        foreach (PassReport pass in report.Passes)
        {
            foreach (DrawCallReport call in pass.DrawCalls)
            {
                drawCalls++;
                triangles += call.IndexCount / 3;
            }
        }

        counters.PooledRtBytes = rtBytes;
        counters.DrawCalls = drawCalls;
        counters.TrianglesApprox = triangles;

        PopulateGraphiteCounters(counters);
    }

    private static void PopulateGraphiteCounters(FrameCounters counters)
    {
        GraphicsDevice? device = Graphics.Device;
        if (device == null)
            return;

        ProfileSnapshot p = device.GetProfile();
        Dictionary<string, double> dict = counters.GraphiteCounters;

        AddBinGroup(dict, "Live", p.Live, includeBytes: true);
        AddBinGroup(dict, "Allocated", p.Allocated, includeBytes: true);
        AddBinGroup(dict, "Freed", p.Freed, includeBytes: true);
        AddBinGroup(dict, "BufferMem", p.BufferMem, includeBytes: true);
        AddBinGroup(dict, "BufferOps", p.BufferOps, includeBytes: true);
        AddBinGroup(dict, "Swaps", p.Swaps, includeBytes: false);
    }

    private static void AddBinGroup<TBin>(Dictionary<string, double> dict, string group, ProfileBinGroup<TBin> bins, bool includeBytes)
        where TBin : unmanaged, Enum
    {
        foreach (TBin bin in Enum.GetValues<TBin>())
        {
            ProfileCounter c = bins[bin];
            dict[$"{group}/{bin} Count"] = c.Count;
            if (includeBytes)
                dict[$"{group}/{bin} MB"] = c.Bytes / (1024.0 * 1024.0);
        }
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
