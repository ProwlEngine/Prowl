using System;
using System.Collections.Generic;
using System.Linq;

using Color = System.Drawing.Color;

namespace Prowl.Graphite.Debugger.Viewer;

internal sealed partial class ProfilerView
{
    private static string AccessState(RecordedAccess access)
        => access.Kind == GraphResourceKind.Buffer ? access.BufferUsage.ToString() : (access.DepthUsage ?? access.TextureUsage).ToString();

    private static string PassType(DeepPass pass)
    {
        bool dispatch = pass.Commands.Any(c => c is DispatchCommand or DispatchIndirectCommand);
        bool draw = pass.Commands.Any(c => c is DrawCommand or DrawIndexedCommand or DrawIndirectCommand or DrawIndexedIndirectCommand
            or DrawIndirectCountCommand or DrawIndexedIndirectCountCommand);
        if (dispatch && draw) return "Compute + Raster";
        if (dispatch) return "Compute";
        SetFramebufferCommand? fb = pass.Commands.OfType<SetFramebufferCommand>().FirstOrDefault();
        if (fb != null && fb.Colors.Length == 0) return "Raster (depth only)";
        if (pass.Commands.OfType<DrawCommand>().Any(d => d.VertexCount == 3)) return "Raster (fullscreen)";
        return "Raster";
    }

    private static string? PreviousState(DeepView view, DeepPass pass, string resource)
    {
        string? state = null;
        foreach (DeepPass earlier in view.Passes)
        {
            if (earlier.Index >= pass.Index)
                break;
            foreach (RecordedAccess a in earlier.Accesses)
                if (a.Resource == resource)
                    state = AccessState(a);
        }
        return state;
    }

    private static string Transition(DeepView view, DeepPass pass, RecordedAccess access)
    {
        string to = AccessState(access);
        string from = PreviousState(view, pass, access.Resource) ?? "Undefined";
        return from == to ? "No barrier" : $"{from} -> {to}";
    }

    private static RecordedGraphResource? GraphResource(DeepView view, string name)
        => view.Resources.FirstOrDefault(r => r.Name == name);

    private static RecordedResource? Backing(DeepIndex index, RecordedGraphResource graph)
        => graph.Backings.Length > 0 ? index.Resource(graph.Backings[0].Id) : null;

    private static string Format(RecordedResource? resource, RecordedGraphResource? graph)
    {
        if (resource?.Texture is { } t) return Describe.Format(t.Format);
        if (graph?.Texture is { } g) return g.ColorFormats is { Length: > 0 } c ? Describe.Format(c[0]) : g.DepthFormat is { } d ? Describe.Format(d) : "-";
        if (resource?.Buffer is { } b) return b.Usage.ToString();
        return "-";
    }

    private static long SizeBytes(RecordedResource? resource)
    {
        if (resource?.Texture is { } t)
        {
            double bytes = (double)t.Width * t.Height * Math.Max(1, t.ArrayLayers) * (1 << (int)t.SampleCount) * t.Format.GetSizeInBytes();
            return (long)(t.MipLevels > 1 ? bytes * 1.333 : bytes);
        }
        return resource?.Buffer?.SizeInBytes ?? 0;
    }

    private void PassTab(FrameModel model, ProfileNode node, string tab, IReadOnlyList<Recording> frames)
    {
        RecordedPass pass = node.Pass!;
        var stats = pass.Stats;
        var vertex = node.CommandBuffer?.VertexStats;
        string view = node.Parent!.Name;

        switch (tab)
        {
            case "Overview":
            {
                RecordedPass? Find(Recording f) => f.Views.FirstOrDefault(v => v.Name == view)?.Passes.FirstOrDefault(p => p.Name == pass.Name);
                double Gpu(Recording f)
                {
                    RecordedView? v = f.Views.FirstOrDefault(x => x.Name == view);
                    RecordedPass? p = Find(f);
                    if (v == null || p == null) return 0;
                    return f.CommandBuffers.FirstOrDefault(b => b.ViewIndex == v.Index && b.PassIndex == p.Index)?.Milliseconds ?? 0;
                }

                double[] gpu = Heavy ? [] : frames.Select(Gpu).ToArray();
                double[] cpu = Heavy ? [] : frames.Select(f => Find(f)?.Stats.CpuMilliseconds ?? 0).ToArray();
                double[] draws = Heavy ? [] : frames.Select(f => (double)(Find(f)?.Stats.Draws ?? 0)).ToArray();
                double Avg(double[] v) => v.Length > 0 ? v.Average() : 0;
                string Sub(double[] v, Func<double, string> format) => Heavy ? "this snapshot" : $"avg {format(Avg(v))}";
                double viewMs = node.Parent!.Milliseconds;

                HeroStrip(new Stat[]
                {
                    new("GPU time", Style.Ms2(node.Milliseconds), Sub(gpu, Style.Ms2), Heavy ? null : Versus(node.Milliseconds, Avg(gpu))),
                    new("CPU record", Style.Ms2(stats.CpuMilliseconds), Sub(cpu, Style.Ms2), Heavy ? null : Versus(stats.CpuMilliseconds, Avg(cpu))),
                    new("Draw calls", Style.Int(stats.Draws), Sub(draws, Style.Int)),
                    new("Share of view", viewMs > 0 ? Style.F(node.Milliseconds / viewMs * 100.0, "0.0") + "%" : "-", $"of {view}"),
                });

                if (!Heavy)
                    ChartsSection("Trend", "Frames without this pass plot as 0", CurrentIndex(frames, model), new[]
                    {
                        new Sparkline("GPU time", gpu, Style.KindDot(NodeKind.Pass), Style.Ms2),
                        new Sparkline("CPU record", cpu, Style.KindDot(NodeKind.CommandBuffer), Style.Ms2),
                    }, 420f, 180f);

                Fold("Details", () => KvSection("Pass", 420, new Kv?[]
                {
                    new("Name", pass.Name),
                    new("View", view),
                    new("Indirect draws", Style.Int(stats.IndirectDraws)),
                    new("Dispatches", stats.Dispatches.ToString()),
                    new("Shader switches", Style.Int(stats.ShaderSwitches)),
                    new("Pipeline binds", Style.Int(stats.PipelineBinds)),
                    new("Resource set binds", Style.Int(stats.ResourceSetBinds)),
                    new("Barriers", stats.Barriers.ToString()),
                    vertex != null ? new("Triangles", Style.F(vertex.Value.InputAssemblyPrimitives / 1e6, "0.000") + " M") : null,
                    vertex != null ? new("Vertices", Style.F(vertex.Value.InputAssemblyVertices / 1e6, "0.000") + " M") : null,
                    vertex != null ? new("Fragment invocations", Style.F(vertex.Value.FragmentShaderInvocations / 1e6, "0.00") + " M") : null,
                }));

                if (Heavy && model.PassOf(node) is { } deep && model.ViewOf(node) is { } deepView)
                    Fold("Replay, attachments and barriers", () => PassDeepOverview(model, deepView, deep));
                break;
            }

            case "Resources":
                PassResources(model, node);
                break;

            case "Commands":
                PassCommands(model, node);
                break;

        }
    }

    private void PassDeepOverview(FrameModel model, DeepView view, DeepPass pass)
    {
        DeepIndex index = model.Index!;
        SetFramebufferCommand? fb = pass.Commands.OfType<SetFramebufferCommand>().FirstOrDefault();
        bool replayable = pass.NotReplayable == null;

        KvSection("Replay", 420, new Kv?[]
        {
            new("Status", replayable ? "Replayable" : "NotReplayable", replayable ? Style.Good : Style.Bad),
            replayable
                ? new("Whole pass", model.Deep!.Mode == DeepMode.Full ? "Restored (stored output)" : "Reexecuted")
                : new("Reason", pass.NotReplayable!, Style.Bad),
            new("Single event", replayable ? "Reexecuted up to the event" : "-"),
            new("Type", PassType(pass)),
            new("Render area", fb != null ? $"{fb.Width} x {fb.Height}" : "-"),
            new("Sample count", "1"),
            new("Commands", pass.Commands.Length.ToString()),
        });

        List<string> clears = new();
        foreach (RecordedCommand c in pass.Commands)
        {
            if (c is ClearColorTargetCommand cc)
                clears.Add($"RT{cc.Index} ({Style.F(cc.Color.R, "0.##")}, {Style.F(cc.Color.G, "0.##")}, {Style.F(cc.Color.B, "0.##")}, {Style.F(cc.Color.A, "0.##")})");
            else if (c is ClearDepthStencilCommand cd)
                clears.Add($"Depth {Style.F(cd.Depth, "0.0")} / Stencil {cd.Stencil}");
        }

        long written = 0, read = 0;
        foreach (RecordedAccess a in pass.Accesses.Where(a => a.Kind == GraphResourceKind.Texture))
        {
            long size = GraphResource(view, a.Resource) is { } g ? SizeBytes(Backing(index, g)) : 0;
            if (a.IsOutput) written += size; else read += size;
        }

        string depthName = fb?.Depth != null ? index.Resource(fb.Depth.Texture.Resource)?.Name ?? "-" : "-";
        KvSection("Attachments", 420, new Kv?[]
        {
            new("Color attachments", (fb?.Colors.Length ?? 0).ToString()),
            new("Depth attachment", depthName),
            new("Color load / store", fb != null && fb.Colors.Length > 0 ? Describe.Ops(fb.Ops.Color) : "-"),
            new("Depth load / store", fb?.Depth != null ? Describe.Ops(fb.Ops.Depth) : "-"),
            new("Clear values", clears.Count > 0 ? string.Join(" / ", clears) : "-"),
            new("Bytes written (est.)", Style.Bytes(written)),
            new("Bytes read (est.)", Style.Bytes(read)),
        });

        List<Row> barriers = pass.Accesses
            .Select(a => (a, t: Transition(view, pass, a)))
            .Where(x => x.t != "No barrier")
            .Select(x =>
            {
                string[] parts = x.t.Split(" -> ");
                return R(x.a.Resource, parts[0], parts[1]);
            }).ToList();
        TableSection("Barriers", 420, "From previous declared use in this view",
            new[] { "Resource", "From", "To" }, new[] { 1.4f, 1.2f, 1.2f },
            barriers.Count > 0 ? barriers : new List<Row> { R("-", "No barriers", "") });

        TableSection("Resource versions", 420, "Version at first reference and after the pass",
            new[] { "Resource", "First", "Last", "Needs contents" }, new[] { 1.6f, 0.6f, 0.6f, 1f },
            pass.References.Select(r => R(index.Resource(r.Resource)?.Name ?? $"#{r.Resource.Value}", $"v{r.First}", $"v{r.Last}", r.NeedsContents ? "Yes" : "No")).ToList());
    }

    private void PassResources(FrameModel model, ProfileNode node)
    {
        DeepIndex index = model.Index!;
        DeepView view = model.ViewOf(node)!;
        DeepPass pass = model.PassOf(node)!;
        const string context = "pass:Resources";
        _viewerSelection.TryGetValue(context, out string? current);

        Action Pick(string key) => () => { _viewerSelection[context] = key; _pixel = null; };

        List<RecordedAccess> inputs = pass.Accesses.Where(a => !a.IsOutput && a.Kind == GraphResourceKind.Texture).ToList();
        List<RecordedAccess> outputs = pass.Accesses.Where(a => a.IsOutput && a.Kind == GraphResourceKind.Texture).ToList();
        SetFramebufferCommand? fb = pass.Commands.OfType<SetFramebufferCommand>().FirstOrDefault();

        TableSection("Inputs", 0, inputs.Count > 0 ? "Click to view" : "None",
            new[] { "Resource", "Format", "Size", "State", "Barrier" }, new[] { 1.4f, 1.4f, 0.8f, 1.1f, 1.8f },
            inputs.Select(a =>
            {
                RecordedGraphResource? g = GraphResource(view, a.Resource);
                RecordedResource? r = g != null ? Backing(index, g) : null;
                string key = "tex:" + a.Resource;
                return new Row { Cells = new[] { a.Resource, Format(r, g), Style.Bytes(SizeBytes(r)), AccessState(a), Transition(view, pass, a) }, Pick = Pick(key), Selected = current == key };
            }).ToList());

        TableSection("Outputs", 0, "Click to view",
            new[] { "Resource", "Format", "Size", "Load", "Store", "Transition" }, new[] { 1.4f, 1.4f, 0.8f, 0.7f, 0.7f, 1.8f },
            outputs.Select((a, i) =>
            {
                RecordedGraphResource? g = GraphResource(view, a.Resource);
                RecordedResource? r = g != null ? Backing(index, g) : null;
                (string Load, string Store) ops = fb != null ? (fb.Ops.Color.Load.ToString(), fb.Ops.Color.Store.ToString()) : ("-", "-");
                string key = "tex:" + a.Resource;
                return new Row
                {
                    Cells = new[] { a.Resource, Format(r, g), Style.Bytes(SizeBytes(r)), ops.Load, ops.Store, Transition(view, pass, a) },
                    Colors = new Color?[] { null, null, null, ops.Load == "Clear" ? Style.Good : ops.Load == "Load" ? Style.Bad : Style.InkDim },
                    Pick = Pick(key),
                    Selected = current == key || (current == null && i == 0),
                };
            }).ToList());

        List<RecordedResource> buffers = pass.References
            .Select(r => index.Resource(r.Resource))
            .Where(r => r is { Kind: GraphResourceKind.Buffer })
            .Select(r => r!)
            .ToList();
        TableSection("Buffers referenced", 0, buffers.Count > 0 ? "Click to view / raw only, no shader layout at pass level" : "None",
            new[] { "Buffer", "Usage", "Size", "Origin" }, new[] { 1.6f, 1.2f, 0.8f, 1.2f },
            buffers.Select(b =>
            {
                string key = "buf:" + b.Id.Value;
                return new Row { Cells = new[] { b.Name, b.Buffer?.Usage.ToString() ?? "-", Style.Bytes(b.Buffer?.SizeInBytes ?? 0), b.Origin.ToString() }, Pick = Pick(key), Selected = current == key };
            }).ToList());

        List<ViewerItem> items = new();
        items.AddRange(outputs.Select(a => new ViewerItem("tex:" + a.Resource, a.Resource + " (out)")));
        items.AddRange(inputs.Select(a => new ViewerItem("tex:" + a.Resource, a.Resource + " (in)")));
        items.AddRange(buffers.Select(b => new ViewerItem("buf:" + b.Id.Value, b.Name)));

        Section("Resource viewer", 0, "State shown at end of pass", w => ResourceViewer(model, node, context, items, null, w));
    }

    private static string CommandName(RecordedCommand command)
    {
        string name = command.GetType().Name;
        return name.EndsWith("Command") ? name[..^7] : name;
    }

    private string CommandArgs(DeepIndex index, RecordedCommand command)
    {
        string Res(Trace.TraceVersion v) => $"{index.Resource(v.Resource)?.Name ?? "#" + v.Resource.Value} v{v.Version}";
        return command switch
        {
            SetFramebufferCommand c => $"{string.Join(", ", c.Colors.Select(a => Res(a.Texture)))}{(c.Depth != null ? (c.Colors.Length > 0 ? ", " : "") + Res(c.Depth.Texture) : "")} / {c.Width}x{c.Height}",
            ClearColorTargetCommand c => $"index {c.Index} / ({Style.F(c.Color.R, "0.##")}, {Style.F(c.Color.G, "0.##")}, {Style.F(c.Color.B, "0.##")}, {Style.F(c.Color.A, "0.##")})",
            ClearDepthStencilCommand c => $"Depth {Style.F(c.Depth, "0.0")} / Stencil {c.Stencil}",
            SetPipelineCommand c => $"{index.Program(c.Program)?.Name ?? "?"} / pipeline {c.PipelineId}",
            SetViewportCommand c => $"{c.Viewport.X}, {c.Viewport.Y}, {c.Viewport.Width}, {c.Viewport.Height}",
            SetScissorCommand c => $"{c.X}, {c.Y}, {c.Width}, {c.Height}",
            BindVertexBuffersCommand c => string.Join(" / ", c.Changed.Select(b => $"slot {b.Slot} / {Res(b.Buffer)} / stride {b.Stride}")),
            BindIndexBufferCommand c => $"{Res(c.Buffer)} / {c.Format} / {c.IndexCount} indices",
            SetPropertiesCommand c => $"{c.Changed.Length} changed, {c.Removed.Length} removed",
            SetPushConstantsCommand c => $"{Style.Bytes(c.Data.Length)} @ {c.Offset}",
            SetDescriptorTableCommand c => c.Layout.Length == 0 ? $"set {c.Set} / unbound" : $"set {c.Set} / {c.Entries.Length} slots written",
            DrawCommand c => $"verts {c.VertexCount} / inst {c.InstanceCount} / first {c.FirstVertex}",
            DrawIndexedCommand c => $"indices {c.IndexCount} / first index {c.FirstIndex} / inst {c.InstanceCount} / vertex offset {c.VertexOffset}",
            DispatchCommand c => $"{c.GroupCountX} x {c.GroupCountY} x {c.GroupCountZ}",
            PushMarkerCommand c => c.Name,
            UpdateBufferCommand c => $"{Res(c.After)} @ {c.Offset} / {Style.Bytes(c.Data.Length)}",
            CopyBufferCommand c => $"{Res(c.Source)} -> {Res(c.DestinationAfter)} / {Style.Bytes(c.SizeInBytes)}",
            BlitTextureCommand c => $"{Res(c.Source)} -> {Res(c.DestinationAfter)} / {c.Regions.Length} regions",
            _ => "",
        };
    }

    private void PassCommands(FrameModel model, ProfileNode node)
    {
        DeepPass pass = model.PassOf(node)!;
        DeepIndex index = model.Index!;
        Dictionary<int, string> nodeAt = new();
        void Map(ProfileNode n)
        {
            if (n.CommandIndex >= 0)
                nodeAt[n.CommandIndex] = n.Id;
            foreach (ProfileNode child in n.Children)
                Map(child);
        }
        Map(node);

        List<(string Name, int Count)> histogram = pass.Commands
            .GroupBy(CommandName)
            .Select(g => (g.Key, g.Count()))
            .ToList();
        TableSection("Command histogram", 300, "",
            new[] { "Command", "Count" }, new[] { 2f, 0.7f },
            histogram.Select(h => R(h.Name, h.Count.ToString())).ToList());

        TableSection("Event list", 420, "EID = command index / click to open",
            new[] { "EID", "Command", "Arguments" }, new[] { 0.5f, 1.3f, 2.2f },
            pass.Commands.Select((c, i) => new Row
            {
                Cells = new[] { i.ToString(), CommandName(c), CommandArgs(index, c) },
                Pick = nodeAt.TryGetValue(i, out string? id) ? Open(id) : null,
            }).ToList(), 520f);
    }
}
