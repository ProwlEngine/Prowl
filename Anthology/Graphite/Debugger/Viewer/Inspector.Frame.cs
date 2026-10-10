using System;
using System.Collections.Generic;
using System.Linq;

using Prowl.Graphite.Debugging;

using Color = System.Drawing.Color;

namespace Prowl.Graphite.Debugger.Viewer;

internal sealed partial class ProfilerView
{
    private int CurrentIndex(IReadOnlyList<Recording> frames, FrameModel model)
    {
        if (Heavy)
            return -1;
        for (int i = 0; i < frames.Count; i++)
            if (frames[i] == model.Recording)
                return i;
        return -1;
    }

    private Action Open(string id) => () => _selId = id;

    private void FrameTab(FrameModel model, string tab, IReadOnlyList<Recording> frames)
    {
        Recording rec = model.Recording;
        FrameTiming timing = model.Timing;
        FrameMetrics m = FrameMetrics.From(rec);
        List<ProfileNode> views = model.Root.Children.Where(c => c.Kind == NodeKind.View).ToList();
        List<ProfileNode> outside = model.Root.Children.Where(c => c.Kind == NodeKind.CommandBuffer).ToList();
        List<ProfileNode> passes = views.SelectMany(v => v.Children).ToList();
        int visible = timing.Visibility.Sum(v => v.Visible);

        switch (tab)
        {
            case "Overview":
            {
                RecordedView? first = rec.Views.Length > 0 ? rec.Views[0] : null;
                KvSection("Frame", 420, new Kv?[]
                {
                    new("Frame", $"#{rec.ExecutionId}"),
                    new("Execution id", rec.ExecutionId.ToString()),
                    new("Graph", rec.GraphName),
                    Heavy ? new("Capture", $"Snapshot {OpenSnapshot!.Id} (deep, DeepMode.{OpenSnapshot.Recording.Mode})") : new("Capture", "Live (light)"),
                    new("Views", rec.Views.Length.ToString()),
                    first != null ? new("Resolution", $"{first.PixelWidth} x {first.PixelHeight}") : null,
                });
                RecordedFeatures f = rec.Features;
                (string, bool)[] features =
                {
                    ("GeometryShader", f.GeometryShader), ("TessellationShaders", f.TessellationShaders), ("DrawIndirectBaseInstance", f.DrawIndirectBaseInstance),
                    ("SamplerAnisotropy", f.SamplerAnisotropy), ("DepthClipDisable", f.DepthClipDisable), ("IndependentBlend", f.IndependentBlend),
                    ("CommandBufferDebugMarkers", f.CommandBufferDebugMarkers), ("ShaderFloat64", f.ShaderFloat64),
                };
                Section("Device", 420, "", w =>
                {
                    KvGrid(w, new List<Kv> { new("Backend", rec.Backend.ToString()), new("Adapter", rec.DeviceName) });
                    Dim("features", "Features (hover for support)");
                    Chips(features.Select(x => x.Item2
                        ? new Chip(x.Item1, "Supported", Style.Alpha(Style.Good, 30), Style.Alpha(Style.Good, 140), Style.Good)
                        : new Chip(x.Item1, "Not supported", Style.Alpha(Style.Ink, 10), Style.Border, Style.InkFaint)).ToList());
                });

                bool gpuBound = timing.GpuMilliseconds > timing.CpuMilliseconds;
                KvSection("Timing", 420, new Kv?[]
                {
                    new("CPU record", Style.Ms2(timing.CpuMilliseconds)),
                    new("GPU", Style.Ms2(timing.GpuMilliseconds)),
                    new("Frame time", Style.Ms2(timing.FrameMilliseconds)),
                    new("GPU-bound", gpuBound ? "Yes" : "No", gpuBound ? Style.Bad : Style.Good),
                    new("Submit -> GPU start", Style.Ms2(timing.SubmitLatencyMilliseconds)),
                    new("Command buffers", $"{passes.Count} passes + {outside.Count} outside"),
                    new("Queue submits", timing.QueueSubmits.ToString()),
                });

                RecordedCounters? c = rec.Counters;
                KvSection("Counters", 420, new Kv?[]
                {
                    new("Draw calls", Style.Int(m.Draws)),
                    new("Indirect draws", Style.Int(m.IndirectDraws)),
                    new("Dispatches", Style.Int(m.Dispatches)),
                    new("Shader switches", Style.Int(m.ShaderSwitches)),
                    new("Pipeline binds", Style.Int(m.PipelineBinds)),
                    new("Resource set binds", Style.Int(m.ResourceSetBinds)),
                    new("Resource sets bound", Style.Int(m.ResourceSetsBound)),
                    new("Triangles", Style.Millions(m.Triangles)),
                    new("Vertices", Style.Millions(m.Vertices)),
                    new("Clipped primitives", Style.Millions(m.Clipped)),
                    new("Fragment invocations", Style.F(m.Fragments / 1e6, "0.0") + " M"),
                    new("Visible renderers", Style.Int(visible)),
                    new("Culled renderers", Style.Int(timing.Visibility.Sum(v => v.CulledFrustum + v.CulledOcclusion))),
                    new("Shadow casters", Style.Int(timing.Visibility.Sum(v => v.ShadowCasters))),
                    new("Visible lights", timing.Visibility.Sum(v => v.Lights).ToString()),
                    c != null ? new("Barriers (tex / buf / mem)", $"{c.Barriers[0]} / {c.Barriers[1]} / {c.Barriers[2]}") : null,
                    new("Pass barriers", Style.Int(m.PassBarriers)),
                    new("Render passes", m.Passes.ToString()),
                });
                break;
            }

            case "Timing":
            {
                double gpu = Math.Max(1e-6, timing.GpuMilliseconds);
                TableSection("Views", 0, "Click to open",
                    new[] { "View", "GPU ms", "CPU ms", "% of frame", "Passes", "Draws" }, new[] { 2f, 1f, 1f, 1f, 1f, 1f },
                    views.Select(v => new Row
                    {
                        Cells = new[] { v.Name, Style.F(v.Milliseconds, "0.000"), Style.F(v.CpuMilliseconds, "0.000"), Style.F(v.Milliseconds / gpu * 100, "0.0") + "%", v.Children.Count.ToString(), v.Children.Sum(p => (long)(p.Pass?.Stats.Draws ?? 0)).ToString() },
                        Pick = Open(v.Id),
                    }).ToList());

                TableSection("CPU stages", 380, "",
                    new[] { "Stage", "ms", "Thread" }, new[] { 2f, 0.8f, 1.6f },
                    timing.CpuStages.Select(s => R(s.Name, Style.F(s.Milliseconds, "0.000"), s.Thread)).ToList());

                List<ProfileNode> heaviest = passes.OrderByDescending(p => p.Milliseconds).ToList();
                TableSection("Heaviest passes", 0, $"Top 10 of {heaviest.Count} / click to open",
                    new[] { "Pass", "View", "GPU ms", "CPU ms", "Draws" }, new[] { 2f, 1.5f, 0.8f, 0.8f, 0.7f },
                    heaviest.Take(10).Select(p => new Row
                    {
                        Cells = new[] { p.Name, p.Parent!.Name, Style.F(p.Milliseconds, "0.000"), Style.F(p.CpuMilliseconds, "0.000"), (p.Pass?.Stats.Draws ?? 0).ToString() },
                        Pick = Open(p.Id),
                    }).ToList());

                TableSection("Command buffers outside passes", 380, "",
                    new[] { "Command buffer", "GPU ms", "View", "Pass" }, new[] { 2f, 0.8f, 0.6f, 0.6f },
                    outside.Select(o => new Row
                    {
                        Cells = new[] { o.Name, Style.F(o.Milliseconds, "0.000"), o.CommandBuffer!.ViewIndex.ToString(), o.CommandBuffer.PassIndex.ToString() },
                        Pick = Open(o.Id),
                    }).ToList());
                break;
            }

            case "Memory":
                MemoryTab(rec, m);
                break;

            case "History":
                FrameHistory(frames, CurrentIndex(frames, model));
                break;

            case "Capture":
                CaptureTab(model);
                break;
        }
    }

    private void MemoryTab(Recording rec, FrameMetrics m)
    {
        RecordedCounters? c = rec.Counters;
        if (c == null)
        {
            KvSection("Memory", 0, new Kv?[] { new("Counters", "Not recorded", Style.InkDim) });
            return;
        }

        var budget = c.MemoryBudget;
        double used = budget.UsageBytes, total = Math.Max(1, budget.BudgetBytes);
        DeepRecording? deep = OpenSnapshot?.Recording;
        Section("VRAM", 0, $"Driver budget {Style.Bytes(used)} of {Style.Bytes(total)} / {Style.F(used / total * 100, "0.0")}%", _ =>
        {
            StackedBar("driver", "Driver usage", new[]
            {
                new Segment("In use", used, Style.Read),
                new Segment("Free", Math.Max(0, total - used), Style.Alpha(Style.Ink, 22)),
            }, Style.Bytes);

            if (deep != null)
                StackedBar("captured", "Resources in this capture, by category", CaptureCategories(deep), Style.Bytes);
            else
                Dim("hint", "Take a snapshot to split memory by resource category.");
        });

        TableSection("Live objects by bin", 0, "Delta over this execution",
            new[] { "Bin", "Live delta", "Bytes delta" }, new[] { 2f, 1f, 1f },
            FrameMetrics.AllocBins.Select((bin, i) => new Row
            {
                Cells = new[] { bin, Signed(c.Live[i]), c.LiveBytes[i] == 0 ? "-" : Signed(c.LiveBytes[i], Style.Bytes) },
                Colors = new Color?[] { null, c.Live[i] > 0 ? Style.Bad : null, c.LiveBytes[i] > 0 ? Style.Bad : null },
            }).ToList());

        TableSection("Resident buffer bytes by role", 420, "Multi-usage buffers count in every role",
            new[] { "Role", "Resident delta" }, new[] { 2f, 1f },
            FrameMetrics.BufferRoleBins.Select((role, i) => R(role, Signed(c.ResidentBytes[i], Style.Bytes))).ToList());

        KvSection("Transfers & events", 420, new Kv?[]
        {
            new("Maps", Style.Int(c.BufferOps[FrameMetrics.Map])),
            new("Unmaps", Style.Int(c.BufferOps[FrameMetrics.Unmap])),
            new("Mapped bytes", Style.Bytes(c.BufferOpBytes[FrameMetrics.Map])),
            new("UpdateBuffer calls", Style.Int(c.BufferOps[FrameMetrics.Update])),
            new("Updated bytes", Style.Bytes(c.BufferOpBytes[FrameMetrics.Update])),
            new("CopyBuffer calls", Style.Int(c.BufferOps[FrameMetrics.Copy])),
            new("Copied bytes", Style.Bytes(c.BufferOpBytes[FrameMetrics.Copy])),
            new("Barriers (texture)", Style.Int(c.Barriers[0])),
            new("Barriers (buffer)", Style.Int(c.Barriers[1])),
            new("Barriers (memory)", Style.Int(c.Barriers[2])),
            new("Resource set binds", Style.Int(c.ResourceSetBinds)),
            new("Sets bound", Style.Int(c.ResourceSetsBound)),
            new("Presents", c.Swaps[0].ToString()),
            new("Resizes", c.Swaps[1].ToString()),
            new("Acquires", c.Swaps[2].ToString()),
        });
    }

    private static bool Usage(RecordedResource resource, BufferUsage usage) => resource.Buffer is { } b && (b.Usage & usage) != 0;

    private static IReadOnlyList<Segment> CaptureCategories(DeepRecording deep)
    {
        (string Label, Func<RecordedResource, bool> Match, Color Color)[] categories =
        {
            ("Textures", r => r.Texture != null, Style.Read),
            ("Vertex buffers", r => Usage(r, BufferUsage.VertexBuffer), Style.Good),
            ("Index buffers", r => Usage(r, BufferUsage.IndexBuffer), Style.KindDot(NodeKind.CommandBuffer)),
            ("Uniform buffers", r => Usage(r, BufferUsage.UniformBuffer), Style.KindDot(NodeKind.Draw)),
            ("Structured buffers", r => Usage(r, BufferUsage.StructuredBufferReadOnly | BufferUsage.StructuredBufferReadWrite), Style.Accent),
            ("Indirect args", r => Usage(r, BufferUsage.IndirectBuffer), Style.Bad),
        };

        List<Segment> segments = new();
        HashSet<RecordedResource> counted = new();
        foreach (var category in categories)
        {
            double bytes = 0;
            foreach (RecordedResource r in deep.Resources)
                if (!counted.Contains(r) && category.Match(r))
                {
                    counted.Add(r);
                    bytes += SizeBytes(r);
                }
            if (bytes > 0)
                segments.Add(new Segment(category.Label, bytes, category.Color));
        }
        double other = deep.Resources.Where(r => !counted.Contains(r)).Sum(r => (double)SizeBytes(r));
        if (other > 0)
            segments.Add(new Segment("Other", other, Style.InkFaint));
        segments.Add(new Segment("Shader code", deep.Programs.Sum(p => p.Stages.Sum(st => (double)st.Code.Length)), Style.KindDot(NodeKind.Program)));
        return segments;
    }

    private static string Signed(long v) => v > 0 ? "+" + v : v.ToString();

    private static string Signed(long v, Func<double, string> format) => (v > 0 ? "+" : "") + format(v);

    private void FrameHistory(IReadOnlyList<Recording> frames, int current)
    {
        FrameMetrics[] m = frames.Select(FrameMetrics.From).ToArray();
        FrameTiming[] t = frames.Select(_source.Timing).ToArray();
        Func<double, string> ms = Style.Ms2, count = Style.Int;
        ChartsSection("Frame history", $"{frames.Count} frames / yellow dot = this frame / dashed = window average", current, new[]
        {
            new Sparkline("CPU vs GPU time", t.Select(x => x.CpuMilliseconds).ToArray(), Style.KindDot(NodeKind.Pass), ms, t.Select(x => x.GpuMilliseconds).ToArray(), Style.Snapshot, "amber CPU / violet GPU"),
            new Sparkline("Draw calls", m.Select(x => (double)x.Draws).ToArray(), Style.KindDot(NodeKind.Draw), count),
            new Sparkline("Shader switches / pipeline binds", m.Select(x => (double)x.ShaderSwitches).ToArray(), Style.Accent, count, m.Select(x => (double)x.PipelineBinds).ToArray(), Style.Good, "pink shader / green pipeline"),
            new Sparkline("Triangles", m.Select(x => (double)x.Triangles / 1e6).ToArray(), Style.Read, v => Style.F(v, "0.00") + " M"),
            new Sparkline("VRAM in use", m.Select(x => (double)x.VramUsage).ToArray(), Style.KindDot(NodeKind.CommandBuffer), Style.Bytes),
            new Sparkline("Live object delta", m.Select(x => (double)x.LiveDelta).ToArray(), Style.Bad, count),
            new Sparkline("Mapped + updated bytes", m.Select(x => (double)x.MappedAndUpdatedBytes).ToArray(), Style.KindDot(NodeKind.Program), Style.Bytes),
            new Sparkline("Buffer maps / frame", m.Select(x => (double)x.Maps).ToArray(), Style.InkSoft, count),
            new Sparkline("Barriers / frame", m.Select(x => (double)x.DeviceBarriers).ToArray(), Style.ChannelR, count),
            new Sparkline("Visible renderers", t.Select(x => (double)x.Visibility.Sum(v => v.Visible)).ToArray(), Style.Good, count),
        });
    }

    private void CaptureTab(FrameModel model)
    {
        DeepRecording deep = model.Deep!;
        List<Row> status = new();
        List<Row> copies = new();
        long copyBytes = 0;

        foreach (DeepView view in deep.Views)
            foreach (DeepPass pass in view.Passes)
            {
                int before = pass.Copies.Count(c => c.Placement == CopyPlacement.BeforePass);
                int after = pass.Copies.Length - before;
                bool replayable = pass.NotReplayable == null;
                string how = pass.NotReplayable ?? (deep.Mode == DeepMode.Full ? "Restored (pass) / Reexecuted (event)" : "Reexecuted");
                status.Add(new Row
                {
                    Cells = new[] { view.Name, pass.Name, replayable ? "Replayable" : "NotReplayable", how, $"{before} / {after}" },
                    Colors = new Color?[] { null, null, replayable ? Style.Good : Style.Bad },
                    Pick = Open($"v{view.Index}p{pass.Index}"),
                });

                foreach (RecordedCopy copy in pass.Copies)
                {
                    copyBytes += (long)copy.Blob.Length;
                    string name = model.Index!.Resource(copy.Version.Resource)?.Name ?? $"#{copy.Version.Resource.Value}";
                    copies.Add(R(pass.Name, name, $"v{copy.Version.Version}", copy.Placement.ToString(), Style.Bytes(copy.Blob.Length)));
                }
            }

        long blobBytes = deep.Blobs.Sum(b => (long)b.Ref.Length);
        KvSection("Capture", 0, new Kv?[]
        {
            new("Snapshot", $"#{OpenSnapshot!.Id}"),
            new("Mode", $"DeepMode.{deep.Mode}"),
            new("Backend", deep.Backend.ToString()),
            new("Programs", deep.Programs.Length.ToString()),
            new("Resources", deep.Resources.Length.ToString()),
            new("Samplers", deep.Samplers.Length.ToString()),
            new("Copies", copies.Count.ToString()),
            new("Copied bytes", Style.Bytes(copyBytes)),
            new("Blobs", Style.Int(deep.Blobs.Length)),
            new("Blob bytes", Style.Bytes(blobBytes)),
        });
        TableSection("Replay status per pass", 0, "Click to open",
            new[] { "View", "Pass", "Status", "Replay / reason", "Copies (before / after)" }, new[] { 1.2f, 1.3f, 1.1f, 2.4f, 1.3f }, status);
        TableSection("Copies", 0, "Staging copies read back into blobs",
            new[] { "Pass", "Resource", "Version", "Placement", "Bytes" }, new[] { 1.4f, 1.4f, 0.7f, 0.9f, 0.8f }, copies);
    }

    private void ViewTab(FrameModel model, ProfileNode node, string tab, IReadOnlyList<Recording> frames)
    {
        RecordedView view = model.Recording.Views.First(v => v.Index == node.ViewIndex);
        ViewVisibility? vis = model.Timing.Visibility.FirstOrDefault(v => v.View == view.Name);
        long draws = node.Children.Sum(p => (long)(p.Pass?.Stats.Draws ?? 0));
        ulong triangles = node.Children.Aggregate(0UL, (a, p) => a + (p.CommandBuffer?.VertexStats?.InputAssemblyPrimitives ?? 0));

        switch (tab)
        {
            case "Overview":
                KvSection("View", 420, new Kv?[]
                {
                    new("Name", view.Name),
                    new("Index", view.Index.ToString()),
                    new("Pixel size", $"{view.PixelWidth} x {view.PixelHeight}"),
                    new("Passes", view.Passes.Length.ToString()),
                    new("Skipped passes", view.Skipped.Length.ToString(), view.Skipped.Length > 0 ? Style.Bad : null),
                    new("GPU", Style.Ms2(node.Milliseconds)),
                    new("CPU record", Style.Ms2(node.CpuMilliseconds)),
                    new("Draw calls", Style.Int(draws)),
                    new("Triangles", Style.Millions(triangles)),
                });
                if (vis != null)
                    KvSection("Visibility", 420, new Kv?[]
                    {
                        new("Visible renderers", Style.Int(vis.Visible)),
                        new("Culled (frustum)", Style.Int(vis.CulledFrustum)),
                        new("Culled (occlusion)", Style.Int(vis.CulledOcclusion)),
                        new("Visible lights", vis.Lights.ToString()),
                        new("Shadow casters", vis.ShadowCasters > 0 ? Style.Int(vis.ShadowCasters) : "-"),
                        new("Culling time", Style.Ms(vis.CullingMilliseconds)),
                    });

                TableSection("Skipped passes", 420, "Not executed this frame",
                    new[] { "Pass", "Reason" }, new[] { 1.4f, 1.4f },
                    view.Skipped.Length > 0 ? view.Skipped.Select(s => R(s.Name, s.Reason.ToString())).ToList() : new List<Row> { R("-", "None") });

                if (Heavy)
                {
                    DeepView? deepView = model.ViewOf(node);
                    TableSection("Passes", 0, "Click to open",
                        new[] { "Pass", "Type", "GPU ms", "CPU ms", "Draws", "Writes" }, new[] { 1.6f, 1.3f, 0.7f, 0.7f, 0.6f, 2f },
                        node.Children.Select(p =>
                        {
                            DeepPass? dp = deepView?.Passes.FirstOrDefault(x => x.Index == p.PassIndex);
                            string writes = dp != null ? string.Join(", ", dp.Accesses.Where(a => a.IsOutput).Select(a => a.Resource)) : "";
                            return new Row
                            {
                                Cells = new[] { p.Name, dp != null ? PassType(dp) : "-", Style.F(p.Milliseconds, "0.000"), Style.F(p.CpuMilliseconds, "0.000"), (p.Pass?.Stats.Draws ?? 0).ToString(), writes },
                                Pick = Open(p.Id),
                            };
                        }).ToList());
                }
                else
                {
                    TableSection("Passes", 0, "Click to open",
                        new[] { "Pass", "GPU ms", "CPU ms", "Draws", "Dispatches" }, new[] { 2f, 1f, 1f, 1f, 1f },
                        node.Children.Select(p => new Row
                        {
                            Cells = new[] { p.Name, Style.F(p.Milliseconds, "0.000"), Style.F(p.CpuMilliseconds, "0.000"), (p.Pass?.Stats.Draws ?? 0).ToString(), (p.Pass?.Stats.Dispatches ?? 0).ToString() },
                            Pick = Open(p.Id),
                        }).ToList());
                }
                break;

            case "Render Graph":
                RenderGraphTab(model, node, view);
                break;

            case "History":
            {
                int current = CurrentIndex(frames, model);
                RecordedView? Find(Recording f) => f.Views.FirstOrDefault(v => v.Name == view.Name);
                double Gpu(Recording f)
                {
                    RecordedView? v = Find(f);
                    return v == null ? 0 : f.CommandBuffers.Where(b => b.ViewIndex == v.Index).Sum(b => b.Milliseconds ?? 0);
                }
                ChartsSection("View history", "Frames without this view plot as 0", current, new[]
                {
                    new Sparkline("GPU time", frames.Select(Gpu).ToArray(), Style.Read, Style.Ms2),
                    new Sparkline("CPU record", frames.Select(f => Find(f)?.Passes.Sum(p => p.Stats.CpuMilliseconds) ?? 0).ToArray(), Style.KindDot(NodeKind.Pass), Style.Ms2),
                    new Sparkline("Draw calls", frames.Select(f => (double)(Find(f)?.Passes.Sum(p => (long)p.Stats.Draws) ?? 0)).ToArray(), Style.KindDot(NodeKind.Draw), Style.Int),
                    new Sparkline("Visible renderers", frames.Select(f => (double)(_source.Timing(f).Visibility.FirstOrDefault(v => v.View == view.Name)?.Visible ?? 0)).ToArray(), Style.Good, Style.Int),
                });
                break;
            }
        }
    }

    private void CommandBufferTab(ProfileNode node)
    {
        RecordedCommandBuffer buffer = node.CommandBuffer!;
        KvSection("Command buffer", 420, new Kv?[]
        {
            new("Name", buffer.Name),
            new("Id", buffer.Id.ToString()),
            new("Scope", $"Outside passes (view {buffer.ViewIndex}, pass {buffer.PassIndex})"),
            new("GPU", buffer.Milliseconds is { } ms ? Style.Ms2(ms) : "Not timed"),
            new("Vertex stats", buffer.VertexStats == null ? "None (no draws)" : Style.Int(buffer.VertexStats.Value.InputAssemblyVertices)),
        });
    }
}
