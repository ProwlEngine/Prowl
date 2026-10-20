using System;
using System.Collections.Generic;
using System.Linq;

using Prowl.Graphite.Debugger.Trace;
using Prowl.Graphite.Debugging;
using Prowl.OrigamiUI;
using Prowl.PaperUI;
using Prowl.PaperUI.LayoutEngine;

using Color = System.Drawing.Color;

namespace Prowl.Graphite.Debugger.Viewer;

internal readonly record struct ViewerItem(string Key, string Label, string Tooltip = "");

internal sealed partial class ProfilerView
{
    private const int PageRows = 16;
    private const float InfoWidth = 240f;

    private readonly Dictionary<string, string> _viewerSelection = new();
    private readonly Dictionary<string, string> _viewerMode = new();
    private readonly Dictionary<string, int> _pages = new();

    private void ResourceViewer(FrameModel model, ProfileNode node, string context, List<ViewerItem> items, TargetStep? step, float width)
    {
        if (items.Count == 0)
        {
            Dim("empty", "Nothing to view");
            return;
        }

        string key = _viewerSelection.TryGetValue(context, out string? chosen) && items.Any(i => i.Key == chosen) ? chosen : items[0].Key;
        string kind = key[..key.IndexOf(':')];

        string[] modes = kind switch
        {
            "tex" => new[] { "Texture" },
            "vb" => new[] { "Mesh", "Vertices", "Raw" },
            "ib" => new[] { "Mesh", "Indices", "Raw" },
            _ => new[] { "Raw" },
        };
        string mode = _viewerMode.TryGetValue(key, out string? m) && modes.Contains(m) ? m
            : _viewerMode.TryGetValue(kind, out m) && modes.Contains(m) ? m
            : modes[0];

        ViewerData data = ResolveViewerData(model, node, key, step);

        using (_paper.Row("modes").Height(30).Gap(8).AlignItems(LayoutAlignment.Center).Enter())
        {
            TabsBuilder tabs = Origami.Tabs(_paper, "mt", Array.IndexOf(modes, mode), i =>
            {
                _viewerMode[kind] = modes[i];
                _viewerMode[key] = modes[i];
            }).Underline().Height(Metrics.HeaderHeight).Width(UnitValue.Auto);
            foreach (string mt in modes)
                tabs.Tab(mt);
            tabs.Show();
            Spacer("sp");
            Mono("sub", data.Summary, Style.InkDim);
        }

        bool stacked = width < 720f;
        float mainWidth = stacked ? width : width - InfoWidth - 12f;
        using (_paper.Row("body").Width(UnitValue.Stretch()).Height(UnitValue.Auto).Gap(Metrics.SpacingLarge).WrapContent().LineGap(Metrics.SpacingLarge).Enter())
        {
            using (_paper.Column("main").Width(mainWidth).Height(UnitValue.Auto).Gap(Metrics.Spacing).Enter())
            {
                switch (mode)
                {
                    case "Texture": TextureView(model, data, step, mainWidth); break;
                    case "Mesh": MeshView(data, mainWidth); break;
                    default: BufferTable(key, mode, data, mainWidth); break;
                }
            }

            using (_paper.Column("info").Width(stacked ? width : InfoWidth).Height(UnitValue.Auto).Enter())
                KvGrid(stacked ? width : InfoWidth, data.Info, 400f);
        }
    }

    private sealed class ViewerData
    {
        public string Summary = "";
        public List<Kv> Info = new();
        public byte[]? Bytes;
        public byte[]? Raw;
        public CopyRegion? Region;
        public RecordedResource? Resource;
        public RecordedGraphResource? Graph;
        public int PreviewWidth, PreviewHeight;
        public byte[]? Coverage;
        public string Origin = "";
        public byte[]? Vertices;
        public byte[]? Indices;
        public int Stride = 32;
        public bool Index32;
        public int PositionOffset, NormalOffset = 12, UvOffset = 24;
    }

    private ViewerData ResolveViewerData(FrameModel model, ProfileNode node, string key, TargetStep? step)
    {
        DeepIndex index = model.Index!;
        DeepView view = model.ViewOf(node)!;
        DeepPass pass = model.PassOf(node)!;
        string kind = key[..key.IndexOf(':')];
        string name = key[(key.IndexOf(':') + 1)..];
        ViewerData data = new();

        if (kind == "tex")
        {
            RecordedGraphResource? graph = GraphResource(view, name);
            RecordedResource? res = graph != null ? Backing(index, graph) : index.Resource(name);
            data.Graph = graph;
            data.Resource = res;
            ResolveTexture(model, view, pass, res, step, data);

            var t = res?.Texture;
            List<string> writers = new(), readers = new();
            foreach (DeepPass p in view.Passes)
                foreach (RecordedAccess a in p.Accesses.Where(a => a.Resource == name))
                    (a.IsOutput ? writers : readers).Add(p.Name);
            RecordedAccess? here = pass.Accesses.FirstOrDefault(a => a.Resource == name);

            data.Summary = t is { } d ? $"{Describe.Format(d.Format)} / {d.Width}x{d.Height}{(d.ArrayLayers > 1 ? "x" + d.ArrayLayers : "")} / {Style.Bytes(SizeBytes(res))}" : name;
            data.Info = new List<Kv?>
            {
                new("Name", name),
                t != null ? new("Format", Describe.Format(t.Value.Format)) : null,
                t != null ? new("Dimensions", $"{t.Value.Width} x {t.Value.Height}{(t.Value.ArrayLayers > 1 ? " x " + t.Value.ArrayLayers : "")}{(t.Value.Type == TextureType.TextureCube ? " (cube)" : "")}") : null,
                t != null ? new("Mip levels", t.Value.MipLevels.ToString()) : null,
                t != null ? new("Samples", t.Value.SampleCount.ToString()) : null,
                new("Size", Style.Bytes(SizeBytes(res))),
                t != null ? new("Usage", t.Value.Usage.ToString()) : null,
                new("Origin", graph?.Origin.ToString() ?? res?.Origin.ToString() ?? "-"),
                graph != null ? new("History", graph.HistoryDepth > 0 ? $"Depth {graph.HistoryDepth} / slot {graph.HistorySlot} / {(graph.HistoryValid ? "valid" : "invalid (fresh ring)")}" : "-") : null,
                here != null ? new("State in this pass", AccessState(here), Style.SnapshotInk) : null,
                here != null ? new("Transition", Transition(view, pass, here)) : null,
                new("Contents", data.Origin),
                new("Written by", writers.Count > 0 ? string.Join(", ", writers.Distinct()) : "Imported"),
                new("Read by", readers.Count > 0 ? string.Join(", ", readers.Distinct()) : "-"),
            }.Where(k => k != null).Select(k => k!.Value).ToList();
            return data;
        }

        TraceResourceId id = new(uint.Parse(name));
        RecordedResource? resource = index.Resource(id);
        data.Resource = resource;
        var contents = index.Contents(id, pass);
        data.Bytes = contents?.Data;
        string owner = view.Passes.FirstOrDefault(p => p.References.Any(r => r.Resource == id))?.Name ?? "-";

        if (kind is "vb" or "ib")
        {
            ProfileNode first = node.Kind == NodeKind.Draw ? node : FirstDraw(node) ?? node;
            BoundState bound = Fold(pass, first.CommandIndex);
            RecordedVertexBinding? vbBind = bound.Vertex.Count > 0 ? bound.Vertex.First().Value : null;
            if (vbBind != null)
            {
                data.Vertices = index.Contents(vbBind.Buffer.Resource, pass)?.Data;
                data.Stride = (int)Math.Max(4, vbBind.Stride);
            }
            if (bound.Index != null)
            {
                data.Indices = index.Contents(bound.Index.Buffer.Resource, pass)?.Data;
                data.Index32 = bound.Index.Format == IndexFormat.UInt32;
            }
            RecordedProgram? program = node.Ancestor(NodeKind.Program)?.Program;
            if (program != null)
                foreach (VertexElementDescription e in Describe.Elements(program.VertexLayouts))
                {
                    string semantic = Describe.Name(e.Name);
                    if (semantic.StartsWith("POSITION", StringComparison.OrdinalIgnoreCase)) data.PositionOffset = (int)e.Offset;
                    else if (semantic.StartsWith("NORMAL", StringComparison.OrdinalIgnoreCase)) data.NormalOffset = (int)e.Offset;
                    else if (semantic.StartsWith("TEXCOORD", StringComparison.OrdinalIgnoreCase) || semantic.StartsWith("UV", StringComparison.OrdinalIgnoreCase)) data.UvOffset = (int)e.Offset;
                }

            int vertexCount = data.Vertices != null ? data.Vertices.Length / data.Stride : 0;
            int indexCount = data.Indices != null ? data.Indices.Length / (data.Index32 ? 4 : 2) : 0;
            (Prowl.Vector.Float3 center, Prowl.Vector.Float3 extents) = Bounds(data);
            data.Summary = kind == "vb"
                ? $"{vertexCount} vertices / stride {data.Stride} B / {Style.Bytes(data.Vertices?.Length ?? 0)}"
                : $"{indexCount} indices / {(data.Index32 ? "UInt32" : "UInt16")} / {Style.Bytes(data.Indices?.Length ?? 0)}";
            data.Info = new List<Kv?>
            {
                new("Buffer", resource?.Name ?? name),
                new("Usage", kind == "vb" ? "Vertex" : "Index"),
                new("Mesh", FrameModel.MeshName(resource?.Name ?? "")),
                kind == "vb" ? new("Vertices", Style.Int(vertexCount)) : new("Indices", Style.Int(indexCount)),
                kind == "vb" ? new("Stride", data.Stride + " B") : new("Index format", data.Index32 ? "UInt32" : "UInt16"),
                kind == "vb" && program != null ? new("Layout", string.Join(" / ", Describe.Elements(program.VertexLayouts).Select(e => $"{Describe.Name(e.Name)} {e.Format}"))) : new("Topology", bound.Pipeline?.Topology?.ToString() ?? "TriangleList"),
                new("Triangles", Style.Int(indexCount / 3)),
                new("Size", Style.Bytes(resource?.Buffer?.SizeInBytes ?? 0)),
                new("Bounds center", $"{Style.F(center.X, "0.00")}, {Style.F(center.Y, "0.00")}, {Style.F(center.Z, "0.00")}"),
                new("Bounds extents", $"{Style.F(extents.X, "0.00")}, {Style.F(extents.Y, "0.00")}, {Style.F(extents.Z, "0.00")}"),
                new("Origin", resource?.Origin.ToString() ?? "-"),
                new("First referenced by", owner),
            }.Where(k => k != null).Select(k => k!.Value).ToList();
            return data;
        }

        data.Summary = $"{resource?.Buffer?.Usage.ToString() ?? "Buffer"} / {Style.Bytes(resource?.Buffer?.SizeInBytes ?? 0)}";
        data.Info = new List<Kv>
        {
            new("Buffer", resource?.Name ?? name),
            new("Usage", resource?.Buffer?.Usage.ToString() ?? "-"),
            new("Size", Style.Bytes(resource?.Buffer?.SizeInBytes ?? 0)),
            new("Layout", "Raw only: structured buffers are not reflected", Style.InkDim),
            new("Origin", resource?.Origin.ToString() ?? "-"),
            new("Contents", contents is { } c ? $"{c.Copy.Placement} copy v{c.Copy.Version.Version}" : "Not captured", contents == null ? Style.Bad : null),
            new("First referenced by", owner),
        };
        return data;
    }

    private static ProfileNode? FirstDraw(ProfileNode node)
    {
        List<ProfileNode> draws = new();
        node.CollectDraws(draws);
        return draws.Count > 0 ? draws[0] : null;
    }

    private static (Prowl.Vector.Float3, Prowl.Vector.Float3) Bounds(ViewerData data)
    {
        if (data.Vertices == null || data.Vertices.Length < data.Stride)
            return (default, default);
        float[] min = { float.MaxValue, float.MaxValue, float.MaxValue }, max = { float.MinValue, float.MinValue, float.MinValue };
        for (int o = 0; o + data.Stride <= data.Vertices.Length; o += data.Stride)
            for (int k = 0; k < 3; k++)
            {
                float v = BitConverter.ToSingle(data.Vertices, o + data.PositionOffset + k * 4);
                min[k] = MathF.Min(min[k], v);
                max[k] = MathF.Max(max[k], v);
            }
        return (new((min[0] + max[0]) / 2, (min[1] + max[1]) / 2, (min[2] + max[2]) / 2), new((max[0] - min[0]) / 2, (max[1] - min[1]) / 2, (max[2] - min[2]) / 2));
    }

    private void BufferTable(string key, string mode, ViewerData data, float width)
    {
        string pageKey = key + "|" + mode;
        int page = _pages.GetValueOrDefault(pageKey);
        List<Row> rows = new();
        string[] head;
        float[] flex;
        int total;
        string rangeLabel;

        if (mode == "Vertices" && data.Vertices != null)
        {
            total = data.Vertices.Length / data.Stride;
            page = Math.Clamp(page, 0, Math.Max(0, (total - 1) / PageRows));
            head = new[] { "#", "POSITION", "NORMAL", "TEXCOORD0" };
            flex = new[] { 0.4f, 1.4f, 1.4f, 1f };
            for (int i = page * PageRows; i < Math.Min(total, (page + 1) * PageRows); i++)
            {
                int o = i * data.Stride;
                string Vec(int offset, int n) => string.Join(", ", Enumerable.Range(0, n).Select(k => Style.F(BitConverter.ToSingle(data.Vertices, o + offset + k * 4), "0.0000")));
                rows.Add(new Row { Cells = new[] { i.ToString(), Vec(data.PositionOffset, 3), Vec(data.NormalOffset, 3), Vec(data.UvOffset, 2) }, Colors = new Color?[] { Style.InkDim } });
            }
            rangeLabel = $"Rows {page * PageRows}-{Math.Min(total, (page + 1) * PageRows) - 1} of {Style.Int(total)}";
        }
        else if (mode == "Indices" && data.Indices != null)
        {
            int size = data.Index32 ? 4 : 2;
            total = data.Indices.Length / size / 3;
            page = Math.Clamp(page, 0, Math.Max(0, (total - 1) / PageRows));
            head = new[] { "Tri", "i0", "i1", "i2", "Byte offset" };
            flex = new[] { 0.6f, 1f, 1f, 1f, 1f };
            uint Index(int i) => data.Index32 ? BitConverter.ToUInt32(data.Indices, i * 4) : BitConverter.ToUInt16(data.Indices, i * 2);
            for (int t = page * PageRows; t < Math.Min(total, (page + 1) * PageRows); t++)
                rows.Add(new Row { Cells = new[] { t.ToString(), Index(t * 3).ToString(), Index(t * 3 + 1).ToString(), Index(t * 3 + 2).ToString(), Style.Hex32((ulong)(t * 3 * size), 6) }, Colors = new Color?[] { Style.InkDim } });
            rangeLabel = $"Rows {page * PageRows}-{Math.Min(total, (page + 1) * PageRows) - 1} of {Style.Int(total)}";
        }
        else
        {
            byte[] bytes = (key.StartsWith("vb:") ? data.Vertices : key.StartsWith("ib:") ? data.Indices : data.Bytes) ?? Array.Empty<byte>();
            total = (bytes.Length + 15) / 16;
            page = Math.Clamp(page, 0, Math.Max(0, (total - 1) / PageRows));
            string interp = key.StartsWith("ib:") ? (data.Index32 ? "u32" : "u16") : key.StartsWith("vb:") ? "f32" : "u32";
            head = new[] { "Offset", "Bytes", "As " + interp };
            flex = new[] { 0.6f, 2.4f, 1.6f };
            for (int line = page * PageRows; line < Math.Min(total, (page + 1) * PageRows); line++)
            {
                int start = line * 16, len = Math.Min(16, bytes.Length - start);
                string hex = string.Join("  ", Enumerable.Range(0, 4).Select(g => string.Join(" ", Enumerable.Range(g * 4, 4).Where(b => b < len).Select(b => bytes[start + b].ToString("X2")))).Where(s => s.Length > 0));
                List<string> values = new();
                if (interp == "u16")
                    for (int b = 0; b + 1 < len; b += 2) values.Add(BitConverter.ToUInt16(bytes, start + b).ToString());
                else
                    for (int b = 0; b + 3 < len; b += 4)
                        values.Add(interp == "f32" ? Style.F(BitConverter.ToSingle(bytes, start + b), "G4") : BitConverter.ToUInt32(bytes, start + b).ToString());
                rows.Add(new Row { Cells = new[] { Style.Hex32((ulong)start, 6), hex, string.Join("  ", values) }, Colors = new Color?[] { Style.InkDim, Style.Ink, Style.RawValue } });
            }
            rangeLabel = bytes.Length == 0 ? "No captured contents" : $"Bytes {Style.Hex32((ulong)(page * PageRows * 16), 6)}-{Style.Hex32((ulong)Math.Min(bytes.Length, (page + 1) * PageRows * 16) - 1, 6)}";
        }

        int pages = Math.Max(1, (total + PageRows - 1) / PageRows);
        Table(width, head, flex, rows, 520f);

        using (_paper.Row("pager").Height(Metrics.HeaderHeight).Gap(Metrics.Spacing).AlignItems(LayoutAlignment.Center).Enter())
        {
            Origami.IconButton(_paper, "first", OrigamiGlyphs.ArrowLeft_I, () => _pages[pageKey] = 0).Small().NotFocusable().Subtle().Tooltip("First").Show();
            Origami.IconButton(_paper, "prev", OrigamiGlyphs.ChevronLeft_I, () => _pages[pageKey] = Math.Max(0, page - 1)).Small().NotFocusable().Subtle().Tooltip("Previous").Show();
            Origami.IconButton(_paper, "next", OrigamiGlyphs.ChevronRight_I, () => _pages[pageKey] = Math.Min(pages - 1, page + 1)).Small().NotFocusable().Subtle().Tooltip("Next").Show();
            Origami.IconButton(_paper, "last", OrigamiGlyphs.ArrowRight_I, () => _pages[pageKey] = pages - 1).Small().NotFocusable().Subtle().Tooltip("Last").Show();
            Mono("label", $"{rangeLabel} / page {page + 1}/{pages}", Style.InkDim);
        }
    }
}
