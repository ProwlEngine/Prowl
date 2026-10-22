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
    private readonly Dictionary<string, NodeGraphController> _graphControllers = new();

    private void TargetsTab(FrameModel model, ProfileNode node, DeepInstance view, DeepPass pass, List<ProfileNode> passDraws, int position)
    {
        int step = position + 1;

        SetFramebufferCommand? fb = pass.Commands.OfType<SetFramebufferCommand>().FirstOrDefault();
        const string context = "draw:Targets";
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
            });
            Mark(rows[^1], 1, key);
        }
        if (targets.Count > 0)
        {
            string selectedKey = SelectedKey(context, targets);
            for (int i = 0; i < rows.Count; i++)
                rows[i].Selected = targets[i].Key == selectedKey;
        }

        TargetStep targetStep = new(step - 1, node.DrawOrdinal);
        ResourceTable(model, node, context, "Render targets", "Click to preview / magenta = pixels this draw changed",
            new[] { "Slot", "Resource", "Format", "Load", "Store" }, new[] { 0.6f, 1.6f, 1.6f, 0.8f, 0.8f }, rows, targets, targetStep);

        if (_pixel is { } p && targets.Count > 0)
            PixelHistory(model, node, view, pass, passDraws, targets, step, p);
    }

    private void PixelHistory(FrameModel model, ProfileNode node, DeepInstance view, DeepPass pass, List<ProfileNode> passDraws, List<ViewerItem> targets, int step, (float U, float V) pixel)
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

    private void RenderGraphTab(FrameModel model, ProfileNode node, RecordedInstance recordedInstance)
    {
        DeepInstance? view = model.InstanceOf(node);
        if (view == null)
            return;

        GraphTab(model, new[] { view }, $"{model.Number}|{view.Index}", "Render graph / " + view.Name);

        if (recordedInstance.Skipped.Length > 0)
            TableSection("Skipped passes", 420, "Not in the graph this frame",
                new[] { "Pass", "Reason" }, new[] { 1.4f, 1.4f },
                recordedInstance.Skipped.Select(s => R(s.Name, s.Reason.ToString())).ToList());
    }
}
