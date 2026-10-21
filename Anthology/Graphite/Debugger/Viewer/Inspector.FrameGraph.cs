using System;
using System.Collections.Generic;
using System.Linq;

using Prowl.Graphite.Debugger.Trace;
using Prowl.Graphite.Debugging;

using Prowl.OrigamiUI;
using Prowl.PaperUI;
using Prowl.PaperUI.LayoutEngine;
using Prowl.Vector;

namespace Prowl.Graphite.Debugger.Viewer;

internal sealed partial class ProfilerView
{
    private const float BlockWidth = 150f;
    private const float KindPad = 28f;
    private const float KindTitle = 40f;
    private const float ColumnGap = 90f;
    private const float RowGap = 28f;
    private const float KindGap = 140f;
    private const float BandGap = 180f;
    private const float FrameGraphHeight = 720f;

    private string? _frameGraphSelection;

    private sealed class KindPass
    {
        public required string Name;
        public required DeepPass Pass;
        public required int InstanceIndex;
        public int First;
        public int Level;
        public int Count;
        public readonly List<RecordedAccess> Accesses = new();
    }

    private sealed class KindResource
    {
        public required string Name;
        public GraphResourceOrigin Origin;
        public GraphResourceKind Kind;
        public readonly HashSet<TraceResourceId> Backings = new();
        public bool Input;
        public bool Export;
        public bool Linked;
    }

    private sealed class KindBlock
    {
        public required GraphKind Kind;
        public int Depth;
        public readonly List<DeepInstance> Instances = new();
        public readonly List<KindPass> Passes = new();
        public readonly Dictionary<string, KindResource> Resources = new();
        public string InputId => "in:" + Kind.Name;
        public string OutputId => "out:" + Kind.Name;
    }

    private static string KindLabel(GraphKind kind) => kind == GraphKind.Root ? "Execution" : kind.Name;

    private static List<KindBlock> BuildKinds(DeepRecording deep)
    {
        Dictionary<GraphKind, KindBlock> kinds = new();
        Dictionary<int, DeepInstance> byIndex = deep.Instances.ToDictionary(i => i.Index);

        int DepthOf(DeepInstance instance)
        {
            int depth = 0;
            while (byIndex.TryGetValue(instance.ParentIndex, out DeepInstance? parent))
            {
                depth++;
                instance = parent;
            }
            return depth;
        }

        foreach (DeepInstance instance in deep.Instances)
        {
            if (!kinds.TryGetValue(instance.Key.Kind, out KindBlock? block))
                kinds[instance.Key.Kind] = block = new KindBlock { Kind = instance.Key.Kind };
            block.Instances.Add(instance);
            block.Depth = Math.Max(block.Depth, DepthOf(instance));

            foreach (DeepPass pass in instance.Passes)
            {
                KindPass? entry = block.Passes.FirstOrDefault(p => p.Name == pass.Name);
                if (entry == null)
                {
                    entry = new KindPass { Name = pass.Name, Pass = pass, InstanceIndex = instance.Index, First = pass.Index };
                    block.Passes.Add(entry);
                }
                entry.Count++;
                entry.First = Math.Min(entry.First, pass.Index);
                foreach (RecordedAccess a in pass.Accesses)
                    if (!entry.Accesses.Any(x => x.Resource == a.Resource && x.IsOutput == a.IsOutput))
                        entry.Accesses.Add(a);
            }

            foreach (RecordedGraphResource resource in instance.Resources)
            {
                if (!block.Resources.TryGetValue(resource.Name, out KindResource? info))
                    block.Resources[resource.Name] = info = new KindResource { Name = resource.Name, Origin = resource.Origin, Kind = resource.Kind };
                foreach (RecordedBacking backing in resource.Backings)
                    info.Backings.Add(backing.Id);
            }
        }

        foreach (KindBlock block in kinds.Values)
            block.Passes.Sort((a, b) => a.First.CompareTo(b.First));

        return kinds.Values.OrderBy(k => k.Depth).ThenBy(k => k.Kind.Name).ToList();
    }

    private static void ClassifyResources(List<KindBlock> kinds)
    {
        Dictionary<TraceResourceId, List<(KindBlock Kind, KindResource Resource)>> byBacking = new();
        foreach (KindBlock block in kinds)
            foreach (KindResource resource in block.Resources.Values)
                foreach (TraceResourceId id in resource.Backings)
                {
                    if (!byBacking.TryGetValue(id, out var list))
                        byBacking[id] = list = new();
                    list.Add((block, resource));
                }

        foreach (var list in byBacking.Values)
            if (list.Select(e => e.Kind).Distinct().Count() > 1)
                foreach (var e in list)
                    e.Resource.Linked = true;

        foreach (KindBlock block in kinds)
            foreach (KindResource resource in block.Resources.Values)
            {
                int firstWrite = int.MaxValue, firstRead = int.MaxValue;
                bool written = false, read = false;
                for (int i = 0; i < block.Passes.Count; i++)
                    foreach (RecordedAccess a in block.Passes[i].Accesses.Where(a => a.Resource == resource.Name))
                        if (a.IsOutput)
                        {
                            written = true;
                            firstWrite = Math.Min(firstWrite, i);
                        }
                        else
                        {
                            read = true;
                            firstRead = Math.Min(firstRead, i);
                        }

                bool external = resource.Origin != GraphResourceOrigin.Transient || resource.Linked;
                resource.Input = read && (!written || firstRead <= firstWrite) && external;
                resource.Export = written && external;
            }

        foreach (var list in byBacking.Values)
        {
            var kindsOf = list.Select(e => e.Kind).Distinct().ToList();
            if (kindsOf.Count < 2 || list.Any(e => e.Resource.Export))
                continue;
            int shallow = kindsOf.Min(k => k.Depth);
            foreach (var e in list.Where(e => e.Kind.Depth == shallow))
                e.Resource.Export = true;
        }
    }

    private void FrameGraphTab(FrameModel model)
    {
        DeepRecording deep = model.Deep!;
        List<KindBlock> kinds = BuildKinds(deep);
        ClassifyResources(kinds);
        OrigamiMetrics metrics = Origami.Current.Metrics;

        List<GraphNode> nodes = new();
        List<GraphConnection> connections = new();
        List<GraphGroup> groups = new();
        Dictionary<KindBlock, Float2> origins = new();
        HashSet<string> wired = new();

        void Wire(string fromNode, string fromPort, string toNode, string toPort, System.Drawing.Color color)
        {
            string id = $"{fromNode}.{fromPort}>{toNode}.{toPort}";
            if (wired.Add(id))
                connections.Add(new GraphConnection(fromNode, fromPort, toNode, toPort) { Color = color, Id = id });
        }

        string PassId(KindBlock k, KindPass p) => $"p:{k.Kind.Name}:{p.Name}";

        float bandY = 0f;
        foreach (var band in kinds.GroupBy(k => k.Depth).OrderBy(g => g.Key))
        {
            float x = 0f, bandHeight = 0f;
            foreach (KindBlock kind in band)
            {
                List<KindResource> inputs = kind.Resources.Values.Where(r => r.Input).OrderBy(r => r.Name).ToList();
                List<KindResource> exports = kind.Resources.Values.Where(r => r.Export).OrderBy(r => r.Name).ToList();

                for (int i = 0; i < kind.Passes.Count; i++)
                {
                    KindPass p = kind.Passes[i];
                    int level = 0;
                    foreach (RecordedAccess a in p.Accesses.Where(a => !a.IsOutput))
                        for (int j = i - 1; j >= 0; j--)
                            if (kind.Passes[j].Accesses.Any(w => w.IsOutput && w.Resource == a.Resource))
                            {
                                level = Math.Max(level, kind.Passes[j].Level + 1);
                                break;
                            }
                    p.Level = level;
                }

                int columns = kind.Passes.Count > 0 ? kind.Passes.Max(p => p.Level) + 1 : 0;
                string inId = "in:" + kind.Kind.Name, outId = "out:" + kind.Kind.Name;

                GraphNode inBlock = new() { Id = inId, Title = "Inputs", Width = BlockWidth, Accent = Style.Good, Pinned = true };
                foreach (KindResource r in inputs)
                {
                    inBlock.Inputs.Add(new GraphPort("x:" + r.Name, r.Name) { Color = Style.Good });
                    inBlock.Outputs.Add(new GraphPort("o:" + r.Name, r.Name) { Color = Style.Read });
                }
                GraphNode outBlock = new() { Id = outId, Title = "Outputs", Width = BlockWidth, Accent = Style.Write, Pinned = true };
                foreach (KindResource r in exports)
                {
                    outBlock.Inputs.Add(new GraphPort("i:" + r.Name, r.Name) { Color = Style.Write });
                    outBlock.Outputs.Add(new GraphPort("x:" + r.Name, r.Name) { Color = Style.Good });
                }

                List<GraphNode> passNodes = new();
                float[] columnHeight = new float[Math.Max(columns, 1)];
                foreach (KindPass p in kind.Passes)
                {
                    GraphNode gn = new()
                    {
                        Id = PassId(kind, p),
                        Title = p.Name,
                        Width = PassNodeWidth,
                        Accent = Style.KindDot(NodeKind.Pass),
                        Badge = p.Count > 1 ? new GraphBadge("x" + p.Count) : null,
                        Tooltip = $"{p.Count} of {kind.Instances.Count} {KindLabel(kind.Kind)} instance(s)",
                        Outline = _frameGraphSelection == PassId(kind, p) ? Style.Selection : null,
                        UserData = p,
                    };
                    foreach (RecordedAccess a in p.Accesses)
                        if (a.IsOutput)
                            gn.Outputs.Add(new GraphPort("o:" + a.Resource, a.Resource) { Color = Style.Write });
                        else
                            gn.Inputs.Add(new GraphPort("i:" + a.Resource, a.Resource) { Color = Style.Read });
                    passNodes.Add(gn);
                }

                float blockHeight = Math.Max(NodeGraphPreview.MeasureHeight(inBlock, metrics), NodeGraphPreview.MeasureHeight(outBlock, metrics));
                float[] columnY = new float[Math.Max(columns, 1)];
                for (int i = 0; i < passNodes.Count; i++)
                {
                    int c = kind.Passes[i].Level;
                    passNodes[i].Position = new Float2(0, columnY[c]);
                    columnY[c] += NodeGraphPreview.MeasureHeight(passNodes[i], metrics) + RowGap;
                }

                float contentHeight = Math.Max(blockHeight, columnY.Max() - RowGap);
                float left = x + KindPad;
                float top = bandY + KindTitle;
                float passLeft = left + BlockWidth + ColumnGap;
                float outLeft = passLeft + columns * (PassNodeWidth + ColumnGap);
                float width = outLeft + BlockWidth + KindPad - x;

                inBlock.Position = new Float2(left, top + (contentHeight - blockHeight) / 2f);
                outBlock.Position = new Float2(outLeft, top + (contentHeight - blockHeight) / 2f);
                for (int i = 0; i < passNodes.Count; i++)
                {
                    int c = kind.Passes[i].Level;
                    float used = columnY[c] - RowGap;
                    passNodes[i].Position = new Float2(passLeft + c * (PassNodeWidth + ColumnGap), top + (contentHeight - used) / 2f + passNodes[i].Position.Y);
                }

                nodes.Add(inBlock);
                nodes.AddRange(passNodes);
                nodes.Add(outBlock);
                float height = KindTitle + contentHeight + KindPad;
                groups.Add(new GraphGroup
                {
                    Id = "k:" + kind.Kind.Name,
                    Title = kind.Kind == GraphKind.Root ? "Once per execution" : $"Once per {kind.Kind.Name} ({kind.Instances.Count})",
                    Position = new Float2(x, bandY),
                    Size = new Float2(width, height),
                    Color = Style.KindDot(NodeKind.Instance),
                });

                for (int i = 0; i < kind.Passes.Count; i++)
                {
                    KindPass p = kind.Passes[i];
                    foreach (RecordedAccess a in p.Accesses.Where(a => !a.IsOutput))
                    {
                        int writer = -1;
                        for (int j = i - 1; j >= 0 && writer < 0; j--)
                            if (kind.Passes[j].Accesses.Any(w => w.IsOutput && w.Resource == a.Resource))
                                writer = j;
                        if (writer >= 0)
                            Wire(PassId(kind, kind.Passes[writer]), "o:" + a.Resource, PassId(kind, p), "i:" + a.Resource, Style.Read);
                        else if (inputs.Any(r => r.Name == a.Resource))
                            Wire(inId, "o:" + a.Resource, PassId(kind, p), "i:" + a.Resource, Style.Read);
                    }
                }
                foreach (KindResource r in exports)
                {
                    int last = kind.Passes.FindLastIndex(p => p.Accesses.Any(a => a.IsOutput && a.Resource == r.Name));
                    if (last >= 0)
                        Wire(PassId(kind, kind.Passes[last]), "o:" + r.Name, outId, "i:" + r.Name, Style.Write);
                }

                origins[kind] = new Float2(x, bandY);
                x += width + KindGap;
                bandHeight = Math.Max(bandHeight, height);
            }
            bandY += bandHeight + BandGap;
        }

        Dictionary<TraceResourceId, List<(KindBlock Kind, KindResource Resource)>> byBacking = new();
        foreach (KindBlock block in kinds)
            foreach (KindResource resource in block.Resources.Values.Where(r => r.Linked))
                foreach (TraceResourceId id in resource.Backings)
                {
                    if (!byBacking.TryGetValue(id, out var list))
                        byBacking[id] = list = new();
                    list.Add((block, resource));
                }

        foreach (var list in byBacking.Values)
        {
            var providers = list.Where(e => e.Resource.Export).ToList();
            if (providers.Count == 0)
            {
                int shallow = list.Min(e => e.Kind.Depth);
                providers = list.Where(e => e.Kind.Depth == shallow).ToList();
            }
            foreach (var consumer in list.Where(e => e.Resource.Input && !providers.Contains(e)))
                foreach (var provider in providers.Where(p => p.Kind != consumer.Kind))
                    if (provider.Resource.Export)
                        Wire(provider.Kind.OutputId, "x:" + provider.Resource.Name, consumer.Kind.InputId, "x:" + consumer.Resource.Name, Style.Good);
        }

        string controllerKey = "frame|" + model.Number;
        bool fresh = !_graphControllers.TryGetValue(controllerKey, out NodeGraphController? controller);
        if (fresh)
        {
            controller = new NodeGraphController();
            _graphControllers[controllerKey] = controller;
            controller.SetView(new Float2(Metrics.PaddingLarge, Metrics.PaddingLarge), 0.5f);
        }

        Section("Render graph / whole frame", 0, "Grouped by how often the work runs / green wires carry resources between scopes / click a pass to trace it", w =>
        {
            using (_paper.Row("legend").Height(Metrics.CompactHeight).Gap(Metrics.PaddingLarge).AlignItems(LayoutAlignment.Center).Enter())
            {
                LegendItem("w", Style.Write, "Write");
                LegendItem("r", Style.Read, "Read");
                LegendItem("x", Style.Good, "Crosses scopes");
                LegendItem("p", Style.KindDot(NodeKind.Pass), "Pass");
            }

            List<Kv> detail = new();
            string? open = null;
            KindPass? selected = nodes.FirstOrDefault(n => n.Id == _frameGraphSelection)?.UserData as KindPass;
            if (selected != null)
            {
                KindBlock owner = kinds.First(k => k.Passes.Contains(selected));
                detail.Add(new("Pass", selected.Name));
                detail.Add(new("Runs in", $"{selected.Count} of {owner.Instances.Count} {KindLabel(owner.Kind)} instance(s)"));
                detail.Add(new("Reads", string.Join(", ", selected.Accesses.Where(a => !a.IsOutput).Select(a => a.Resource)) is { Length: > 0 } rd ? rd : "-"));
                detail.Add(new("Writes", string.Join(", ", selected.Accesses.Where(a => a.IsOutput).Select(a => a.Resource)) is { Length: > 0 } wr ? wr : "-"));
                open = $"i{selected.InstanceIndex}p{selected.Pass.Index}";
            }
            else
            {
                detail.Add(new("Scopes", kinds.Count.ToString()));
                detail.Add(new("Instances", deep.Instances.Length.ToString()));
                detail.Add(new("Passes", kinds.Sum(k => k.Passes.Count).ToString()));
                detail.Add(new("Selection", "None - click a pass"));
            }

            bool stacked = w < 720f;
            float graphWidth = stacked ? w : w - InfoWidth - Metrics.SpacingLarge;
            float infoWidth = stacked ? w : InfoWidth;
            using (_paper.Row("body").Width(UnitValue.Stretch()).Height(UnitValue.Auto).Gap(Metrics.SpacingLarge).WrapContent().LineGap(Metrics.SpacingLarge).Enter())
            {
                Origami.NodeGraph(_paper, "framegraph", graphWidth, FrameGraphHeight)
                    .Nodes(nodes)
                    .Connections(connections)
                    .Groups(groups)
                    .Controller(controller!)
                    .ReadOnly()
                    .Grid()
                    .OnSelectionChanged(sel => _frameGraphSelection = sel.Nodes.Count > 0 && sel.Nodes[0].UserData is KindPass ? sel.Nodes[0].Id : null)
                    .Show();

                using (_paper.Column("detail").Width(infoWidth).Height(UnitValue.Auto).Gap(Metrics.Spacing).Enter())
                {
                    KvGrid(infoWidth, detail, 400f);
                    if (open != null)
                        Origami.Button(_paper, "open", "Open pass in inspector", () => _selId = open).Small().NotFocusable().LeadingIcon(OrigamiGlyphs.ArrowRight_I).Show();
                }
            }
        });
    }
}
