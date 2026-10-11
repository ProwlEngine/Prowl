using System;
using System.Collections.Generic;
using System.Linq;

using Prowl.Graphite.Debugging;

using Prowl.OrigamiUI;
using Prowl.PaperUI;
using Prowl.PaperUI.LayoutEngine;
using Prowl.Vector;

using Color = System.Drawing.Color;

namespace Prowl.Graphite.Debugger.Viewer;

internal sealed partial class ProfilerView
{
    private const float GraphColumn = 440f;
    private const float PassNodeWidth = 160f;
    private const float GraphHeight = 460f;
    private const float ResourceNodeWidth = 150f;

    private string? _graphSelection;
    private readonly Dictionary<string, NodeGraphController> _graphControllers = new();

    private void TargetsTab(FrameModel model, ProfileNode node, DeepView view, DeepPass pass, List<ProfileNode> passDraws, int position)
    {
        int step = position + 1;

        SetFramebufferCommand? fb = pass.Commands.OfType<SetFramebufferCommand>().FirstOrDefault();
        const string context = "draw:Targets";
        _viewerSelection.TryGetValue(context, out string? current);
        List<ViewerItem> targets = new();
        List<Row> rows = new();
        int color = 0, output = 0;
        foreach (RecordedAccess a in pass.Accesses.Where(a => a.IsOutput && a.Kind == GraphResourceKind.Texture))
        {
            RecordedGraphResource? graph = GraphResource(view, a.Resource);
            RecordedResource? backing = graph != null ? Backing(model.Index!, graph) : null;
            bool depth = a.DepthUsage != null;
            string key = "tex:" + a.Resource;
            (string Load, string Store) ops = fb != null ? (fb.Ops.Color.Load.ToString(), fb.Ops.Color.Store.ToString()) : ("-", "-");
            output++;
            targets.Add(new ViewerItem(key, a.Resource));
            rows.Add(new Row
            {
                Cells = new[] { depth ? "Depth" : "RT" + color++, a.Resource, Format(backing, graph), ops.Load, ops.Store },
                Pick = () => { _viewerSelection[context] = key; _pixel = null; },
                Selected = current == key || (current == null && rows.Count == 0),
            });
        }
        TableSection("Render targets", 0, "Click to view", new[] { "Slot", "Resource", "Format", "Load", "Store" }, new[] { 0.6f, 1.6f, 1.6f, 0.8f, 0.8f }, rows);

        TargetStep targetStep = new(step - 1, node.DrawOrdinal);
        Section("Target viewer", 0, "Magenta = pixels this draw changed", w => ResourceViewer(model, node, context, targets, targetStep, w));

        if (_pixel is { } p && targets.Count > 0)
            PixelHistory(model, node, view, pass, passDraws, targets, step, p);
    }

    private void PixelHistory(FrameModel model, ProfileNode node, DeepView view, DeepPass pass, List<ProfileNode> passDraws, List<ViewerItem> targets, int step, (float U, float V) pixel)
    {
        string key = _viewerSelection.TryGetValue("draw:Targets", out string? chosen) && targets.Any(t => t.Key == chosen) ? chosen : targets[0].Key;
        RecordedGraphResource? graph = GraphResource(view, key[4..]);
        RecordedResource? res = graph != null ? Backing(model.Index!, graph) : null;
        List<Row> rows = new();

        byte[]? Sample(int eventIndex)
        {
            if (res == null)
                return null;
            ReplayOutput? output = Output(Replay(model, view.Index, pass.Index, eventIndex), res.Id);
            if (output == null || output.Regions.Length == 0)
                return null;
            int w = (int)output.Regions[0].Width, h = (int)output.Regions[0].Height;
            int x = Math.Min(w - 1, (int)(pixel.U * w)), y = Math.Min(h - 1, (int)(pixel.V * h));
            int o = (y * w + x) * 4;
            return output.Data.Items.Slice(o, 4).ToArray();
        }

        byte[]? previous = Sample(-1);
        for (int i = 0; i < step && i < passDraws.Count; i++)
        {
            byte[]? now = Sample(i);
            if (now != null && previous != null && !now.AsSpan().SequenceEqual(previous))
            {
                ProfileNode d = passDraws[i];
                rows.Add(new Row
                {
                    Cells = new[] { d.CommandIndex.ToString(), d.Name, d.Ancestor(NodeKind.Marker)?.Name ?? "no marker", "Passed", "" },
                    Pick = Open(d.Id),
                    Selected = d == node,
                });
            }
            previous = now;
        }

        for (int i = 0; i < rows.Count; i++)
        {
            bool last = i == rows.Count - 1;
            rows[i].Cells[4] = last ? "Final write" : "Overwritten";
            rows[i].Colors = new Color?[] { null, null, null, null, last ? Style.Good : Style.InkDim };
        }

        TableSection("Pixel history", 0, "Up to this draw / one replay per event",
            new[] { "EID", "Draw", "Renderer", "Depth test", "Result" }, new[] { 0.5f, 1.6f, 1.2f, 0.8f, 1.2f },
            rows.Count > 0 ? rows : new List<Row> { R("-", "No draws in this pass touched this pixel yet") });
    }

    private sealed class GraphResourceInfo
    {
        public required string Name;
        public readonly List<int> Writers = new();
        public readonly List<int> Readers = new();
        public bool Imported;
        public int Start, End, Lane;
        public bool Above;
    }

    private void RenderGraphTab(FrameModel model, ProfileNode node, RecordedView recordedView)
    {
        DeepView? view = model.ViewOf(node);
        if (view == null)
            return;
        DeepIndex index = model.Index!;
        List<DeepPass> passes = view.Passes.OrderBy(p => p.Index).ToList();

        Dictionary<string, GraphResourceInfo> resources = new();
        List<string> order = new();
        GraphResourceInfo Touch(string name)
        {
            if (!resources.TryGetValue(name, out GraphResourceInfo? info))
            {
                info = new GraphResourceInfo { Name = name };
                resources[name] = info;
                order.Add(name);
            }
            return info;
        }

        for (int i = 0; i < passes.Count; i++)
            foreach (RecordedAccess a in passes[i].Accesses.Where(a => a.Kind == GraphResourceKind.Texture))
                (a.IsOutput ? Touch(a.Resource).Writers : Touch(a.Resource).Readers).Add(i);

        int shift = 0;
        foreach (GraphResourceInfo info in resources.Values)
        {
            RecordedGraphResource? g = GraphResource(view, info.Name);
            info.Imported = g?.Origin != GraphResourceOrigin.Transient || info.Writers.Count == 0;
            if (info.Imported && info.Writers.Count == 0)
                shift = 1;
        }

        foreach (GraphResourceInfo info in resources.Values)
        {
            info.Start = info.Writers.Count > 0 ? info.Writers.Min() + shift : 0;
            info.End = Math.Max(info.Start, info.Writers.Concat(info.Readers).Max() + shift);
        }

        List<List<(int, int)>> laneA = new(), laneB = new();
        foreach (GraphResourceInfo info in resources.Values.OrderBy(r => r.End - r.Start).ThenBy(r => r.Start))
        {
            bool placed = false;
            for (int lvl = 0; !placed; lvl++)
                foreach (bool up in new[] { true, false })
                {
                    List<List<(int, int)>> lanes = up ? laneA : laneB;
                    while (lanes.Count <= lvl)
                        lanes.Add(new());
                    if (lanes[lvl].All(iv => info.End < iv.Item1 || info.Start > iv.Item2))
                    {
                        lanes[lvl].Add((info.Start, info.End));
                        info.Lane = lvl;
                        info.Above = up;
                        placed = true;
                        break;
                    }
                }
        }

        List<GraphNode> nodes = new();
        List<GraphConnection> connections = new();
        for (int i = 0; i < passes.Count; i++)
        {
            DeepPass p = passes[i];
            ProfileNode? passNode = node.Children.FirstOrDefault(c => c.PassIndex == p.Index);
            GraphNode gn = new()
            {
                Id = "p" + i,
                Title = p.Name,
                Position = new Float2((i + shift) * GraphColumn, 0),
                Width = PassNodeWidth,
                Accent = Style.KindDot(NodeKind.Pass),
                Tooltip = $"{PassType(p)} / {(passNode != null ? Style.Ms2(passNode.Milliseconds) : "-")}",
                Badge = new GraphBadge(passNode != null ? Style.F(passNode.Milliseconds, "0.00") + " ms" : "-"),
                Outline = _graphSelection == "p" + i ? Style.Selection : null,
                Dimmed = p.NotReplayable != null,
                UserData = p,
            };
            foreach (RecordedAccess a in p.Accesses.Where(a => a.Kind == GraphResourceKind.Texture))
            {
                if (a.IsOutput)
                    gn.Outputs.Add(new GraphPort("o:" + a.Resource, a.Resource) { Color = Style.Write });
                else
                    gn.Inputs.Add(new GraphPort("i:" + a.Resource, a.Resource) { Color = Style.Read });
            }
            nodes.Add(gn);
        }

        foreach (GraphResourceInfo info in resources.Values)
        {
            RecordedGraphResource? g = GraphResource(view, info.Name);
            RecordedResource? backing = g != null ? Backing(index, g) : null;
            float y = (info.Above ? -1 : 1) * (140f + info.Lane * 80f);
            float x = info.Writers.Count == 0 ? (GraphColumn - ResourceNodeWidth) / 2f : info.Start * GraphColumn + PassNodeWidth + (GraphColumn - PassNodeWidth - ResourceNodeWidth) / 2f;
            GraphNode rn = new()
            {
                Id = "r:" + info.Name,
                Title = info.Name,
                Position = new Float2(x, y),
                Width = ResourceNodeWidth,
                Accent = info.Imported ? Style.Good : Style.KindDot(NodeKind.CommandBuffer),
                Tooltip = $"{Format(backing, g)} / {Style.Bytes(SizeBytes(backing))}",
                Badge = new GraphBadge(Format(backing, g)),
                Outline = _graphSelection == "r:" + info.Name ? Style.Selection : null,
            };
            if (info.Writers.Count > 0)
                rn.Inputs.Add(new GraphPort("w", "write") { Color = Style.Write });
            if (info.Readers.Count > 0)
                rn.Outputs.Add(new GraphPort("r", "read") { Color = Style.Read });
            nodes.Add(rn);

            foreach (int w in info.Writers.Distinct())
                connections.Add(new GraphConnection("p" + w, "o:" + info.Name, rn.Id, "w") { Color = Style.Write, Id = $"w{w}:{info.Name}" });
            foreach (int r in info.Readers.Distinct())
                connections.Add(new GraphConnection(rn.Id, "r", "p" + r, "i:" + info.Name) { Color = Style.Read, Id = $"r{r}:{info.Name}" });
        }

        string controllerKey = $"{model.Number}|{view.Index}";
        bool fresh = !_graphControllers.TryGetValue(controllerKey, out NodeGraphController? controller);
        if (fresh)
        {
            controller = new NodeGraphController();
            _graphControllers[controllerKey] = controller;
            controller.SetView(new Float2(Metrics.PaddingLarge, GraphHeight / 2f), 0.6f);
        }

        Section("Render graph / " + view.Name, 0, "Passes run left to right / imported resources in green / click a node to trace it", w =>
        {
            using (_paper.Row("legend").Height(Metrics.CompactHeight).Gap(Metrics.PaddingLarge).AlignItems(LayoutAlignment.Center).Enter())
            {
                LegendItem("w", Style.Write, "Write");
                LegendItem("r", Style.Read, "Read");
                LegendItem("p", Style.KindDot(NodeKind.Pass), "Pass");
                LegendItem("t", Style.KindDot(NodeKind.CommandBuffer), "Transient resource");
                LegendItem("i", Style.Good, "Imported");
            }

            List<Kv> detail = new();
            string? openId = null;
            if (_graphSelection is { } s && s.StartsWith("p") && int.TryParse(s[1..], out int pi) && pi < passes.Count)
            {
                DeepPass p = passes[pi];
                ProfileNode? passNode = node.Children.FirstOrDefault(c => c.PassIndex == p.Index);
                openId = passNode?.Id;
                detail.Add(new("Pass", p.Name));
                detail.Add(new("Type", PassType(p)));
                detail.Add(new("GPU", passNode != null ? Style.Ms(passNode.Milliseconds) : "-"));
                detail.Add(new("Reads", string.Join(", ", p.Accesses.Where(a => !a.IsOutput).Select(a => a.Resource)) is { Length: > 0 } rd ? rd : "-"));
                detail.Add(new("Writes", string.Join(", ", p.Accesses.Where(a => a.IsOutput).Select(a => a.Resource)) is { Length: > 0 } wr ? wr : "-"));
                detail.Add(new("Replay", p.NotReplayable ?? "Replayable", p.NotReplayable != null ? Style.Bad : Style.Good));
            }
            else if (_graphSelection is { } r && r.StartsWith("r:") && resources.TryGetValue(r[2..], out GraphResourceInfo? info))
            {
                RecordedGraphResource? g = GraphResource(view, info.Name);
                RecordedResource? backing = g != null ? Backing(index, g) : null;
                List<int> span = info.Writers.Concat(info.Readers).OrderBy(x => x).ToList();
                detail.Add(new("Resource", info.Name));
                detail.Add(new("Format", Format(backing, g)));
                detail.Add(new("Size", backing?.Texture is { } t ? $"{t.Width}x{t.Height} / {Style.Bytes(SizeBytes(backing))}" : "-"));
                detail.Add(new("History", g != null && g.HistoryDepth > 0 ? $"Depth {g.HistoryDepth} / slot {g.HistorySlot} / {(g.HistoryValid ? "valid" : "invalid")}" : "-"));
                detail.Add(new("Written by", info.Writers.Count > 0 ? string.Join(", ", info.Writers.Distinct().Select(i => passes[i].Name)) : "-"));
                detail.Add(new("Read by", info.Readers.Count > 0 ? string.Join(", ", info.Readers.Distinct().Select(i => passes[i].Name)) : "-"));
                detail.Add(new("Lifetime", span.Count > 0 ? $"{passes[span[0]].Name} -> {passes[span[^1]].Name}" : "-"));
                detail.Add(new("Origin", g?.Origin.ToString() ?? "-"));
            }
            else
            {
                detail.Add(new("Passes", passes.Count.ToString()));
                detail.Add(new("Skipped", recordedView.Skipped.Length.ToString()));
                detail.Add(new("Resources", resources.Count.ToString()));
                detail.Add(new("Imported", resources.Values.Count(r => r.Imported).ToString()));
                detail.Add(new("Selection", "None - click a node"));
            }

            bool stacked = w < 720f;
            float graphWidth = stacked ? w : w - InfoWidth - Metrics.SpacingLarge;
            float infoWidth = stacked ? w : InfoWidth;
            using (_paper.Row("body").Width(UnitValue.Stretch()).Height(UnitValue.Auto).Gap(Metrics.SpacingLarge).WrapContent().LineGap(Metrics.SpacingLarge).Enter())
            {
                Origami.NodeGraph(_paper, "graph", graphWidth, GraphHeight)
                    .Nodes(nodes)
                    .Connections(connections)
                    .Controller(controller!)
                    .ReadOnly()
                    .Grid()
                    .OnSelectionChanged(sel => _graphSelection = sel.Nodes.Count > 0 ? sel.Nodes[0].Id : null)
                    .Show();

                using (_paper.Column("detail").Width(infoWidth).Height(UnitValue.Auto).Gap(Metrics.Spacing).Enter())
                {
                    KvGrid(infoWidth, detail, 400f);
                    if (openId != null)
                        Origami.Button(_paper, "open", "Open pass in inspector", () => _selId = openId).Small().NotFocusable().LeadingIcon(OrigamiIconSet.ArrowRight).Show();
                }
            }
        });

        TableSection("Graph resources", 0, "History = depth / ring slot this execution / validity",
            new[] { "Resource", "Format", "Size", "Lifetime", "Origin", "History" }, new[] { 1.4f, 1.4f, 0.8f, 2f, 1f, 1.6f },
            order.Select(name =>
            {
                GraphResourceInfo info = resources[name];
                RecordedGraphResource? g = GraphResource(view, name);
                RecordedResource? backing = g != null ? Backing(index, g) : null;
                List<int> span = info.Writers.Concat(info.Readers).OrderBy(x => x).ToList();
                return new Row
                {
                    Cells = new[]
                    {
                        name, Format(backing, g), Style.Bytes(SizeBytes(backing)),
                        span.Count > 0 ? $"{passes[span[0]].Name} -> {passes[span[^1]].Name}" : "-",
                        g?.Origin.ToString() ?? "-",
                        g != null && g.HistoryDepth > 0 ? $"Depth {g.HistoryDepth} / slot {g.HistorySlot} / {(g.HistoryValid ? "valid" : "invalid")}" : "-",
                    },
                    Pick = () => _graphSelection = "r:" + name,
                    Selected = _graphSelection == "r:" + name,
                };
            }).ToList(), 520f);

        if (recordedView.Skipped.Length > 0)
            TableSection("Skipped passes", 420, "Not in the graph this frame",
                new[] { "Pass", "Reason" }, new[] { 1.4f, 1.4f },
                recordedView.Skipped.Select(s => R(s.Name, s.Reason.ToString())).ToList());
    }
}
