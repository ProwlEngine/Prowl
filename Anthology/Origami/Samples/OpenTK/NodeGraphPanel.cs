// This file is part of the Prowl Game Engine
// Licensed under the MIT License. See the LICENSE file in the project root for details.

using System;
using System.Collections.Generic;
using System.Linq;

using Prowl.OrigamiUI;
using Prowl.PaperUI;
using Prowl.PaperUI.LayoutEngine;
using Prowl.Scribe;
using Prowl.Vector;

using TextAlignment = Prowl.PaperUI.TextAlignment;

namespace OrigamiSample;

// Hosts the Origami NodeGraph widget. The widget is a pure view + intent emitter; this panel owns
// the model lists and applies every edit (the exact spot a real host also records Undo). Shows data
// ports, execution/behaviour-tree ports, group boxes, wire reroute points, context menus, a
// searchable create popup, node bodies drawn by the host, header badges with tooltips, collapsing,
// grid snapping, wire re-targeting, live wires and the minimap.
public sealed class NodeGraphPanel : DockPanel
{
    // One graph per document. A sub graph is just another document; whether it lives in this file or
    // in its own asset is the host's business, and the widget never hears about it.
    private sealed class GraphDoc
    {
        public string Id = "";
        public string Title = "";
        public bool IsAsset;
        public readonly List<GraphNode> Nodes = new();
        public readonly List<GraphConnection> Wires = new();
        public readonly List<GraphGroup> Groups = new();
        public readonly List<GraphSticky> Stickies = new();
        /// <summary>What the graph takes and what it gives back: the ports of the node that uses it.</summary>
        public readonly List<GraphPort> Inputs = new();
        public readonly List<GraphPort> Outputs = new();
    }

    private const string InBoundary = "sg_in";
    private const string OutBoundary = "sg_out";

    private readonly Dictionary<string, GraphDoc> _docs = new();
    private readonly List<string> _path = new();

    private GraphDoc Current => _docs[_path[^1]];
    private List<GraphNode> Nodes => Current.Nodes;
    private List<GraphConnection> Wires => Current.Wires;
    private List<GraphGroup> Groups => Current.Groups;
    private List<GraphSticky> Stickies => Current.Stickies;
    private readonly List<GraphNode> _clipNodes = new();
    private readonly List<GraphConnection> _clipWires = new();
    private readonly NodeGraphController _ctrl = new();
    private int _selNodes, _selEdges, _nextId = 100;
    private bool _snap = true, _live = true;
    private float _panSpeed = 0.6f;

    // Body state belongs to the node, not the panel, or two copies of a card drive one value.
    private readonly Dictionary<string, float> _noiseScale = new();
    private float ScaleOf(GraphNode n) => _noiseScale.TryGetValue(n.Id, out float v) ? v : 0.45f;

    private static Color StickyYellow = Palette.C(250, 224, 130);

    public override string Title => "Shader Graph";

    private static Color Tex = Palette.C(96, 165, 250);
    private static Color Math = Palette.C(74, 222, 128);
    private static Color Val = Palette.C(251, 191, 36);
    private static Color Out = Palette.C(168, 85, 247);
    private static Color Flow = Palette.C(226, 232, 240);

    // ── create-popup state ──
    private bool _popupOpen;
    private Float2 _popupScreen, _popupGraph;
    private string _search = "";
    private string? _wireNode, _wirePort;
    private bool _wireIsOutput;
    private float _colOx, _colOy, _panelW, _panelH;

    public NodeGraphPanel()
    {
        var root = new GraphDoc { Id = "root", Title = "Shader Graph" };
        _docs[root.Id] = root;
        _path.Add(root.Id);

        root.Nodes.AddRange(new List<GraphNode>
        {
            Node("uv", "UV Coords", 15, 75, 150, Tex, null, Out2("uv", "UV", Tex)),
            Node("time", "Time", 15, 215, 150, Val, null, Out2("t", "Time", Val)),
            Node("noise", "Noise", 195, 60, 150, Math, In2(("uv","UV",Tex),("scale","Scale",Val)), Out2("n","Value",Math)),
            Node("panner", "Panner", 195, 220, 150, Math, In2(("uv","UV",Tex),("time","Time",Val)), Out2("uv","UV",Tex)),
            Node("mul", "Multiply", 375, 45, 150, Math, In2(("a","A",Math),("b","B",Tex)), Out2("o","Out",Math)),
            Node("tex", "Texture Sample", 375, 185, 160, Tex, In2(("uv","UV",Tex)), Out2(("rgb","RGB",Tex),("a","Alpha",Val))),
            Node("output", "Fragment Output", 560, 110, 155, Out, In2(("albedo","Albedo",Out),("emit","Emission",Out),("alpha","Alpha",Val)), null),
            Bt("bt_root", "Selector", 230, 430, OutFlow("c0", "c1")),
            Bt("bt_a", "Move To", 150, 560, null),
            Bt("bt_b", "Attack", 330, 560, null),
        });
        Nodes[7].Inputs.Add(new GraphPort("in", "") { Side = PortSide.Top, Shape = PortShape.Arrow, Color = Flow });
        Nodes[8].Inputs.Add(new GraphPort("in", "") { Side = PortSide.Top, Shape = PortShape.Arrow, Color = Flow });
        Nodes[8].Accent = Tex; Nodes[9].Accent = Palette.C(251, 113, 133);
        Nodes[9].Inputs.Add(new GraphPort("in", "") { Side = PortSide.Top, Shape = PortShape.Arrow, Color = Flow });

        root.Wires.AddRange(new List<GraphConnection>
        {
            new("uv", "uv", "noise", "uv"), new("uv", "uv", "panner", "uv"),
            new("time", "t", "panner", "time"), new("panner", "uv", "tex", "uv"),
            new("noise", "n", "mul", "a"), new("tex", "rgb", "mul", "b"),
            new("mul", "o", "output", "albedo"), new("tex", "rgb", "output", "emit"),
            new("tex", "a", "output", "alpha"),
            new("bt_root", "c0", "bt_a", "in") { Color = Flow }, new("bt_root", "c1", "bt_b", "in") { Color = Flow },
        });

        root.Groups.Add(new GraphGroup { Id = "grp_bt", Title = "Behaviour Tree", Position = new Float2(120, 400), Size = new Float2(340, 230), Color = Flow });

        root.Stickies.Add(new GraphSticky { Id = "note1", Text = "Double-click to edit.\nDrag me, resize from the corner.", Position = new Float2(600, 40), Size = new Float2(200, 110), Color = StickyYellow });

        // Demo: one wire rerouted through a control point (drag it; right-click it to remove).
        Wires.First(w => w.FromNode == "uv" && w.ToNode == "panner").ControlPoints.Add(new Float2(115, 205));

        BuildRippleAsset();
        Decorate();
    }

    // A sub graph kept in its own asset, plus the node in the root graph that uses it. The node's ports
    // are the asset's parameters, so they are built from the document rather than authored here.
    private void BuildRippleAsset()
    {
        var doc = new GraphDoc { Id = "sg_ripple", Title = "Ripple", IsAsset = true };
        doc.Inputs.Add(new GraphPort("uv", "UV") { Color = Tex });
        doc.Inputs.Add(new GraphPort("speed", "Speed") { Color = Val });
        doc.Outputs.Add(new GraphPort("out", "Value") { Color = Math });

        doc.Nodes.Add(InputBoundary(doc, 20, 70));
        doc.Nodes.Add(OutputBoundary(doc, 470, 96));
        doc.Nodes.Add(Node("rip_time", "Time", 40, 250, 140, Val, null, Out2("t", "Time", Val)));
        doc.Nodes.Add(Node("rip_wave", "Sine", 230, 100, 160, Math, In2(("uv", "UV", Tex), ("t", "Time", Val), ("speed", "Speed", Val)), Out2("o", "Value", Math)));

        doc.Wires.Add(new GraphConnection(InBoundary, "uv", "rip_wave", "uv"));
        doc.Wires.Add(new GraphConnection(InBoundary, "speed", "rip_wave", "speed"));
        doc.Wires.Add(new GraphConnection("rip_time", "t", "rip_wave", "t"));
        doc.Wires.Add(new GraphConnection("rip_wave", "o", OutBoundary, "out"));
        doc.Stickies.Add(new GraphSticky
        {
            Id = "sg_note",
            Text = "The two pinned cards are the contract. Add a parameter here and it appears as a port on every node that uses this asset.",
            Position = new Float2(230, 300), Size = new Float2(230, 120), Color = StickyYellow,
        });
        _docs[doc.Id] = doc;

        var node = new GraphNode
        {
            Id = "ripple", Title = "Ripple", Position = new Float2(195, 330), Width = 160,
            Accent = Out, UserData = doc.Id, Collapsible = true,
            Tooltip = "Double click to open the sub graph",
        };
        Nodes.Add(node);
        Wires.Add(new GraphConnection("uv", "uv", node.Id, "uv"));
        Wires.Add(new GraphConnection("time", "t", node.Id, "speed"));
    }

    // The boundary cards: wire to them, but they cannot be dragged or deleted.
    private static GraphNode InputBoundary(GraphDoc doc, float x, float y)
    {
        var n = new GraphNode
        {
            Id = InBoundary, Title = "Inputs", Position = new Float2(x, y), Width = 150, Accent = Math, Pinned = true,
            Tooltip = "The parameters this graph takes. They become the ports on the node that uses it.",
        };
        foreach (var p in doc.Inputs) n.Outputs.Add(new GraphPort(p.Id, p.Label) { Color = p.Color });
        return n;
    }

    private static GraphNode OutputBoundary(GraphDoc doc, float x, float y)
    {
        var n = new GraphNode
        {
            Id = OutBoundary, Title = "Results", Position = new Float2(x, y), Width = 150, Accent = Out, Pinned = true,
            Tooltip = "What the node outside hands on. One port per output.",
        };
        foreach (var p in doc.Outputs) n.Inputs.Add(new GraphPort(p.Id, p.Label) { Color = p.Color });
        return n;
    }

    // Ports of a sub graph node, and of the boundary card, are derived from the document. This runs
    // every frame, so it compares before it rebuilds: nothing is allocated while nothing has changed.
    private void SyncSubGraphs()
    {
        foreach (var n in Nodes)
        {
            if (n.UserData is not string docId || !_docs.TryGetValue(docId, out GraphDoc? doc)) continue;

            // Inputs can be added from out here; results cannot, since they come from inside.
            Rebuild(n.Inputs, doc.Inputs, "Drop a wire here to add a parameter to " + doc.Title);
            Rebuild(n.Outputs, doc.Outputs, null);

            n.Badge ??= doc.IsAsset ? AssetBadge : LocalBadge;

            // A plan of what is inside, drawn the way the minimap draws the whole graph.
            if (n.Body == null)
            {
                GraphDoc inner = doc;
                n.BodyHeight = 64f;
                n.Body = ctx => PreviewBody(ctx, inner);
            }
        }

        var inBoundary = Nodes.FirstOrDefault(n => n.Id == InBoundary);
        if (inBoundary != null)
            Rebuild(inBoundary.Outputs, Current.Inputs, "Drag to an input to add a parameter");

        var outBoundary = Nodes.FirstOrDefault(n => n.Id == OutBoundary);
        if (outBoundary != null)
            Rebuild(outBoundary.Inputs, Current.Outputs, "Drop a wire here to add a result");
    }

    private static void Rebuild(List<GraphPort> target, List<GraphPort> source, string? addTooltip)
    {
        if (Matches(target, source, addTooltip != null)) return;

        target.Clear();
        foreach (var p in source) target.Add(new GraphPort(p.Id, p.Label) { Color = p.Color });
        if (addTooltip != null) target.Add(AddPort(addTooltip));
    }

    private static bool Matches(List<GraphPort> target, List<GraphPort> source, bool hasAddPort)
    {
        if (target.Count != source.Count + (hasAddPort ? 1 : 0)) return false;
        for (int i = 0; i < source.Count; i++)
            if (target[i].Id != source[i].Id || target[i].Label != source[i].Label) return false;
        return true;
    }

    private static readonly GraphBadge AssetBadge = new("asset", Out, "Kept in its own asset and shared by every graph that uses it");
    private static readonly GraphBadge LocalBadge = new("local", null, "Kept inside this graph");

    // The open socket itself. It is not a port: wiring it asks the host to make one.
    private static GraphPort AddPort(string tooltip) => new("add", "")
    {
        IsPlaceholder = true,
        Color = Palette.TLo,
        Tooltip = tooltip,
    };

    // Cheap: a handful of rectangles and hairlines, no text, no layout.
    private static void PreviewBody(NodeBodyContext ctx, GraphDoc doc)
    {
        var P = ctx.Paper;
        float pad = ctx.S(8f);
        using (P.Box(ctx.Id("plan")).Width(P.Percent(100)).Height(P.Percent(100))
            .Margin(pad, pad, 0, ctx.S(4f))
            .Rounded(ctx.S(5f)).BackgroundColor(Palette.C(10, 10, 24, 0.8f)).IsNotInteractable().Enter())
            P.Draw((canvas, rr) => NodeGraphPreview.Paint(canvas, rr, doc.Nodes, doc.Wires, Origami.Root, ctx.S(4f)));
    }

    private void EnterDoc(string docId)
    {
        if (!_docs.ContainsKey(docId)) return;
        _path.Add(docId);
        _ctrl.FrameAll();
    }

    private void GoUpTo(int depth)
    {
        if (_path.Count <= depth + 1) return;
        while (_path.Count > depth + 1) _path.RemoveAt(_path.Count - 1);
        _ctrl.FrameAll();
    }

    // Everything the selection touches moves into a new document; the wires crossing the boundary
    // become its parameters and its result.
    private void CreateSubGraph(IReadOnlyList<GraphNode> nodes)
    {
        var inside = nodes.Where(n => !n.Pinned).ToList();
        if (inside.Count == 0) return;

        var ids = inside.Select(n => n.Id).ToHashSet();
        var doc = new GraphDoc { Id = "sg" + _nextId++, Title = "Sub graph" };

        var incoming = Wires.Where(w => !ids.Contains(w.FromNode) && ids.Contains(w.ToNode)).ToList();
        var outgoing = Wires.Where(w => ids.Contains(w.FromNode) && !ids.Contains(w.ToNode)).ToList();
        var internalWires = Wires.Where(w => ids.Contains(w.FromNode) && ids.Contains(w.ToNode)).ToList();

        float minX = inside.Min(n => n.Position.X), minY = inside.Min(n => n.Position.Y);
        foreach (var n in inside)
        {
            n.Position += new Float2(230f - minX, 80f - minY);
            doc.Nodes.Add(n);
        }
        doc.Wires.AddRange(internalWires);

        // One parameter per outside source, however many inner nodes it feeds: the same value arriving
        // twice is one input, not two.
        var parameterOf = new Dictionary<string, string>();
        foreach (var w in incoming)
        {
            string key = w.FromNode + "/" + w.FromPort;
            if (!parameterOf.TryGetValue(key, out string? pid))
            {
                pid = "in" + _nextId++;
                parameterOf[key] = pid;
                (string label, Color color) = PortOf(w.FromNode, w.FromPort, output: true);
                doc.Inputs.Add(new GraphPort(pid, label) { Color = color });
            }
            doc.Wires.Add(new GraphConnection(InBoundary, pid, w.ToNode, w.ToPort));
        }

        // And one result per inner source that leaves, so nothing is quietly dropped.
        var resultOf = new Dictionary<string, string>();
        foreach (var w in outgoing)
        {
            string key = w.FromNode + "/" + w.FromPort;
            if (!resultOf.TryGetValue(key, out string? rid))
            {
                rid = "out" + _nextId++;
                resultOf[key] = rid;
                (string label, Color color) = PortOf(w.FromNode, w.FromPort, output: true);
                doc.Outputs.Add(new GraphPort(rid, label) { Color = color });
                doc.Wires.Add(new GraphConnection(w.FromNode, w.FromPort, OutBoundary, rid));
            }
        }

        doc.Nodes.Insert(0, InputBoundary(doc, 20, 80));
        doc.Nodes.Add(OutputBoundary(doc, doc.Nodes.Max(n => n.Position.X + NodeGraphPreview.MeasureWidth(n, Origami.Root.Metrics)) + 80f, 100));
        _docs[doc.Id] = doc;

        var node = new GraphNode
        {
            Id = "n" + _nextId++, Title = "Sub graph", Position = new Float2(minX, minY), Width = 160,
            Accent = Out, UserData = doc.Id, Collapsible = true,
            Tooltip = "Double click to open the sub graph",
        };

        Nodes.RemoveAll(n => ids.Contains(n.Id));
        Wires.RemoveAll(w => ids.Contains(w.FromNode) || ids.Contains(w.ToNode));
        Nodes.Add(node);

        foreach (var w in incoming)
            Wires.Add(new GraphConnection(w.FromNode, w.FromPort, node.Id, parameterOf[w.FromNode + "/" + w.FromPort]));
        foreach (var w in outgoing)
            Wires.Add(new GraphConnection(node.Id, resultOf[w.FromNode + "/" + w.FromPort], w.ToNode, w.ToPort));

        // The wires above name ports the node does not have yet, and it is about to stop being current.
        SyncSubGraphs();
        EnterDoc(doc.Id);
    }

    private GraphNode? NodeOf(string id) => Nodes.FirstOrDefault(n => n.Id == id);

    // A port's own name and colour, so a parameter is called what it was called before it crossed.
    private (string Label, Color Color) PortOf(string nodeId, string portId, bool output)
    {
        var n = Nodes.FirstOrDefault(x => x.Id == nodeId) ?? _docs.Values.SelectMany(d => d.Nodes).FirstOrDefault(x => x.Id == nodeId);
        var p = (output ? n?.Outputs : n?.Inputs)?.FirstOrDefault(x => x.Id == portId);
        string label = p == null || p.Label.Length == 0 ? portId : p.Label;
        return (label, p?.Color ?? Val);
    }

    // Everything the node card itself shows beyond a title and its port labels.
    private void Decorate()
    {
        GraphNode N(string id) => Nodes.First(n => n.Id == id);

        N("noise").Tooltip = "Value noise sampled in UV space.";
        N("noise").BodyHeight = 46f;
        N("noise").Body = ctx => NoiseBody(ctx, () => ScaleOf(ctx.Node), v => _noiseScale[ctx.Node.Id] = v);
        N("noise").Inputs[1].Tooltip = "Tiling, 0 to 1";

        N("panner").BodyHeight = 26f;
        N("panner").Body = ctx => SpeedBody(ctx, _panSpeed);
        N("panner").Collapsible = true;

        N("tex").BodyHeight = 30f;
        N("tex").Body = SwatchBody;
        N("tex").Badge = new GraphBadge("2 uses", Tex, "RGB feeds both Multiply and Emission");
        N("tex").Collapsible = true;

        N("time").BodyHeight = 24f;
        N("time").Body = TimeBody;

        N("output").Tooltip = "The fragment the shader writes.";
        N("output").Collapsible = true;
    }

    // A draggable bar plus a preview: the pattern for any value edited on the card itself.
    private void NoiseBody(NodeBodyContext ctx, Func<float> get, Action<float> set)
    {
        var P = ctx.Paper;
        float pad = ctx.S(9f);
        using (P.Column(ctx.Id("col")).Width(P.Percent(100)).Height(P.Percent(100)).Padding(pad, pad, 0, 0).Enter())
        {
            using (P.Row(ctx.Id("row")).Width(P.Percent(100)).Height(ctx.S(14f)).Enter())
            {
                P.Box(ctx.Id("lbl")).Width(P.Stretch()).Height(P.Percent(100))
                    .Text("Scale", Fonts.Reg).FontSize(ctx.S(11f)).TextColor(Palette.TMid).Alignment(TextAlignment.MiddleLeft);
                P.Box(ctx.Id("val")).Width(ctx.S(34f)).Height(P.Percent(100))
                    .Text($"{get():0.00}", Fonts.Reg).FontSize(ctx.S(11f)).TextColor(Palette.TLo).Alignment(TextAlignment.MiddleRight);
            }

            float trackW = MathF.Max(1f, (ctx.Node.Width - 18f) * ctx.Zoom);
            // ctx.Control keeps the drag here instead of letting it bubble up and move the node.
            var track = ctx.Control(P.Box(ctx.Id("track")).Width(P.Percent(100)).Height(ctx.S(8f)).Margin(0, 0, ctx.S(5f), 0)
                .Rounded(ctx.S(4f)).BackgroundColor(Palette.C(10, 10, 24, 0.85f))
                .Cursor(PaperCursor.ResizeHorizontal)
                .Tooltip("Drag to set the noise scale"));
            track.OnDragging(this, (self, e) => set(MathF.Min(1f, MathF.Max(0f, get() + (float)e.Delta.X / trackW))));

            float fill = get();
            Color accent = ctx.Accent;
            using (track.Enter())
                P.Draw((canvas, rr) =>
                {
                    float w = (float)rr.Size.X * fill;
                    if (w > 1f)
                        canvas.RectFilled((float)rr.Min.X, (float)rr.Min.Y, w, (float)rr.Size.Y, Color32.FromArgb(255, (int)(accent.R * 255f), (int)(accent.G * 255f), (int)(accent.B * 255f)));
                });
        }
    }

    private void SpeedBody(NodeBodyContext ctx, float speed)
    {
        var P = ctx.Paper;
        float pad = ctx.S(9f);
        using (P.Row(ctx.Id("row")).Width(P.Percent(100)).Height(P.Percent(100)).Padding(pad, pad, 0, 0).Enter())
        {
            P.Box(ctx.Id("lbl")).Width(P.Stretch()).Height(P.Percent(100))
                .Text("Speed", Fonts.Reg).FontSize(ctx.S(11f)).TextColor(Palette.TMid).Alignment(TextAlignment.MiddleLeft);
            P.Box(ctx.Id("val")).Width(ctx.S(40f)).Height(P.Percent(100))
                .Text($"{speed:0.00}", Fonts.Reg).FontSize(ctx.S(11f)).TextColor(Palette.TLo).Alignment(TextAlignment.MiddleRight);
        }
    }

    private static void SwatchBody(NodeBodyContext ctx)
    {
        var P = ctx.Paper;
        float pad = ctx.S(9f);
        using (P.Row(ctx.Id("sw")).Width(P.Percent(100)).Height(P.Percent(100)).Padding(pad, pad, 0, 0).Enter())
            for (int i = 0; i < Swatches.Length; i++)
                P.Box("sw", i).Width(P.Stretch()).Height(ctx.S(14f)).Margin(0, i < Swatches.Length - 1 ? ctx.S(4f) : 0, 0, 0)
                    .Rounded(ctx.S(3f)).BackgroundColor(Swatches[i]).IsNotInteractable();
    }

    private static readonly Color[] Swatches = { Palette.C(236, 122, 84), Palette.C(120, 186, 240), Palette.C(160, 220, 170), Palette.C(232, 206, 128) };

    // A live value drawn on the card, which is what a running graph looks like.
    private static void TimeBody(NodeBodyContext ctx)
    {
        var P = ctx.Paper;
        float pad = ctx.S(9f);
        float t = (float)P.Time;
        using (P.Row(ctx.Id("row")).Width(P.Percent(100)).Height(P.Percent(100)).Padding(pad, pad, 0, 0).Enter())
        {
            P.Box(ctx.Id("lbl")).Width(P.Stretch()).Height(P.Percent(100))
                .Text("seconds", Fonts.Reg).FontSize(ctx.S(11f)).TextColor(Palette.TMid).Alignment(TextAlignment.MiddleLeft);
            P.Box(ctx.Id("val")).Width(ctx.S(46f)).Height(P.Percent(100))
                .Text($"{t:0.0}", Fonts.Reg).FontSize(ctx.S(11f)).TextColor(ctx.Accent).Alignment(TextAlignment.MiddleRight);
        }
    }

    // While "Live" is on, wires carrying signal flow and thicken, and a missing input is flagged.
    private void UpdateLiveState(Paper P)
    {
        // Width is a steady weight per wire, not an animation: only the dots move, which is what reads
        // as flow. A pulsing line just looks like the whole graph is breathing.
        for (int i = 0; i < Wires.Count; i++)
        {
            var w = Wires[i];
            bool signal = _live && w.ToNode == "output";
            w.Flow = signal;
            w.Thickness = signal ? 1f + (i % 3) * 0.6f : 1f;
            w.FlowSpeed = 0.7f + (i % 3) * 0.25f;
        }

        var output = Nodes.FirstOrDefault(n => n.Id == "output");
        if (output == null) return;
        bool albedo = Wires.Any(w => w.ToNode == "output" && w.ToPort == "albedo");
        output.Badge = albedo ? null : NoAlbedoBadge;
    }

    private static readonly GraphBadge NoAlbedoBadge =
        new("!", Palette.C(251, 113, 133), "Albedo has nothing wired into it, so the surface renders black.");

    public override void OnGUI(Paper P, float w, float h)
    {
        _panelW = w; _panelH = h;
        using (P.Column("ngroot").Width(P.Percent(100)).Height(P.Percent(100))
            .BackgroundColor(Palette.RootBg).OnPostLayout((hd, r) => { _colOx = (float)r.Min.X; _colOy = (float)r.Min.Y; }).Enter())
        {
            Toolbar(P);
            SyncSubGraphs();
            UpdateLiveState(P);

            Origami.NodeGraph(P, "shadergraph_" + Current.Id, w, h - 38f)
                .Nodes(Nodes).Connections(Wires).Groups(Groups).Stickies(Stickies)
                .Controller(_ctrl)
                .InitialView(new Float2(18, -20), 0.62f)
                .Minimap()
                .SnapToGrid(_snap ? 13f : 0f)
                .OnSelectionChanged(sel => { _selNodes = sel.Nodes.Count; _selEdges = sel.Edges.Count; })
                // ── the lines a real host wraps in Undo.RegisterAction / BeginContinuous ──
                .OnNodesMoved((nodes, delta) => { foreach (var n in nodes) n.Position += delta; })
                .OnConnect(Connect)
                // An open socket accepts anything; the host decides what the new port becomes.
                .OnValidateConnection(req => req.ToPlaceholder || req.FromPlaceholder
                    ? req.ToNode == OutBoundary || req.FromNode == InBoundary || NodeOf(req.ToNode)?.UserData is string
                    : true)
                // Dragging a connected input moves that wire instead of starting a second one.
                .OnDisconnect(w => Wires.Remove(w))
                .OnNodeToggleCollapsed(n => n.Collapsed = !n.Collapsed)
                .OnDeleteSelection(sel =>
                {
                    foreach (var e in sel.Edges) Wires.Remove(e);
                    foreach (var g in sel.Groups) Groups.Remove(g);
                    foreach (var sk in sel.Stickies) Stickies.Remove(sk);
                    DeleteNodes(sel.Nodes);
                })
                .OnBackgroundContext(gp => BackgroundMenu(P, gp))
                .OnDropWireInEmpty((gp, sn, sp, so) => OpenPopup(P, gp, sn, sp, so))
                .OnNodeDoubleClick(node => { if (node.UserData is string docId) EnterDoc(docId); })
                .OnNodeContext((node, gp) => NodeMenu(P, node))
                .OnNodesContext((nodes, gp) => MultiMenu(P, nodes))
                .OnGroupMoved((g, members, delta) => { g.Position += delta; foreach (var n in members) n.Position += delta; })
                .OnGroupResized((g, pos, size) => { g.Position = pos; g.Size = size; })
                .OnGroupRenamed((g, title) => g.Title = title)
                .OnGroupContext((g, gp) => GroupMenu(P, g))
                // ── sticky notes ──
                .OnStickyMoved((sk, delta) => sk.Position += delta)
                .OnStickyResized((sk, pos, size) => { sk.Position = pos; sk.Size = size; })
                .OnStickyEdited((sk, text) => sk.Text = text)
                .OnStickyContext((sk, gp) => StickyMenu(P, sk))
                // ── wire reroute control points ──
                .OnWireAddPoint((wire, index, pos) => wire.ControlPoints.Insert(index, pos))
                .OnWireRemovePoint((wire, index) => { if (index < wire.ControlPoints.Count) wire.ControlPoints.RemoveAt(index); })
                .OnWirePointMoved((wire, index, pos) => { if (index < wire.ControlPoints.Count) wire.ControlPoints[index] = pos; })
                .Show();

            if (_popupOpen)
            {
                if (P.IsKeyPressed(PaperKey.Escape)) ClosePopup();
                else DrawCreatePopup(P);
            }
        }
    }

    // ═══ toolbar (drives the graph via NodeGraphController) ═══

    private void Toolbar(Paper P)
    {
        using (P.Row("ngtb").Width(P.Percent(100)).Height(38).Padding(8, 8, 0, 0)
            .BackgroundColor(Palette.GlassIn).Enter())
        {
            for (int i = 0; i < _path.Count; i++)
            {
                int depth = i;
                GraphDoc doc = _docs[_path[i]];
                if (i > 0)
                    P.Box("tb_sep" + i).Width(P.Auto).Height(P.Auto).Margin(4, 4, P.Stretch(), P.Stretch())
                        .Text("/", Fonts.Reg).FontSize(12f * Palette.TS).TextColor(Palette.TLo);

                var crumb = Origami.Button(P, "tb_crumb" + i, doc.Title).Small();
                if (i == _path.Count - 1) crumb.Subtle(); else crumb.Ghost();
                crumb.OnClick(() => GoUpTo(depth)).Show();
            }
            if (_path.Count > 1 && _docs[_path[^1]].IsAsset)
                P.Box("tb_asset").Width(P.Auto).Height(P.Auto).Margin(6, 10, P.Stretch(), P.Stretch())
                    .Text("shared asset", Fonts.Reg).FontSize(11f * Palette.TS).TextColor(Palette.Acc300);

            P.Box("tb_gap").Width(14);
            Origami.Button(P, "tb_all", "Frame All").Small().OnClick(() => _ctrl.FrameAll()).Show();
            Origami.Button(P, "tb_sel", "Fit Selection").Small().Subtle().OnClick(() => _ctrl.FrameSelection()).Show();
            Origami.Button(P, "tb_selmath", "Select Math").Small().Ghost().OnClick(() => _ctrl.SelectNodes(new[] { "noise", "panner", "mul" })).Show();
            Origami.Button(P, "tb_snap", _snap ? "Snap: on" : "Snap: off").Small().Ghost().OnClick(() => _snap = !_snap).Show();
            Origami.Button(P, "tb_live", _live ? "Live: on" : "Live: off").Small().Ghost().OnClick(() => _live = !_live).Show();
            Origami.Button(P, "tb_fold", "Collapse all").Small().Ghost()
                .OnClick(() => { bool fold = Nodes.Any(n => n.Collapsible && !n.Collapsed); foreach (var n in Nodes) if (n.Collapsible) n.Collapsed = fold; }).Show();
            P.Box("tb_sp").Width(P.Stretch());
            P.Box("tb_info").Width(P.Auto).Height(P.Auto).Margin(0, 10, P.Stretch(), P.Stretch())
                .Text($"{_ctrl.Zoom * 100f:0}%   {_selNodes} selected", Fonts.Reg)
                .FontSize(11f * Palette.TS).TextColor(Palette.TMid).Alignment(TextAlignment.MiddleRight);
        }
    }

    // ═══ edits ═══

    private void Connect(ConnectionRequest req)
    {
        if (req.NeedsPort && !TryCreatePort(ref req)) return;

        // Drawing a wire that is already there is a no-op, not a delete and a re-add.
        if (Wires.Any(c => c.FromNode == req.FromNode && c.FromPort == req.FromPort && c.ToNode == req.ToNode && c.ToPort == req.ToPort))
            return;

        Wires.RemoveAll(c => c.ToNode == req.ToNode && c.ToPort == req.ToPort); // single-input: replace
        Wires.Add(new GraphConnection(req.FromNode, req.FromPort, req.ToNode, req.ToPort));
    }

    /// <summary>
    /// Turns a wire dropped on an open socket into a real port, named and coloured after whatever is at
    /// the other end, and rewrites the request to use it. Returns false when nothing here owns that
    /// socket, which leaves the wire undrawn rather than guessing.
    /// </summary>
    private bool TryCreatePort(ref ConnectionRequest req)
    {
        string fromNode = req.FromNode, fromPort = req.FromPort, toNode = req.ToNode, toPort = req.ToPort;

        if (req.ToPlaceholder)
        {
            var target = NodeOf(toNode);
            if (target == null) return false;
            (string label, Color color) = PortOf(fromNode, fromPort, output: true);

            // The results card: a new output for this graph, which grows a port on the node outside.
            if (target.Id == OutBoundary)
            {
                string id = "out" + _nextId++;
                Current.Outputs.Add(new GraphPort(id, label) { Color = color });
                req = new ConnectionRequest(fromNode, fromPort, toNode, id);
                SyncSubGraphs();
                return true;
            }

            // A sub graph node: a new parameter on the graph it points at.
            if (target.UserData is string docId && _docs.TryGetValue(docId, out GraphDoc? doc))
            {
                string id = "in" + _nextId++;
                doc.Inputs.Add(new GraphPort(id, label) { Color = color });
                req = new ConnectionRequest(fromNode, fromPort, toNode, id);
                SyncSubGraphs();
                return true;
            }

            return false;
        }

        // Dragging out of the inputs card names the parameter after whatever it lands on.
        if (req.FromPlaceholder && fromNode == InBoundary)
        {
            (string label, Color color) = PortOf(toNode, toPort, output: false);
            string id = "in" + _nextId++;
            Current.Inputs.Add(new GraphPort(id, label) { Color = color });
            req = new ConnectionRequest(fromNode, id, toNode, toPort);
            SyncSubGraphs();
            return true;
        }

        return false;
    }

    // ═══ context menus (Origami.ContextMenu = proper popup at screen coords) ═══

    private Color Rose => Palette.C(251, 113, 133);
    private IOrigamiIcon Trash => Ico(OrigamiIconSet.Trash, Rose);

    // Right-click on empty canvas.
    private void BackgroundMenu(Paper P, Float2 gp) => Origami.ContextMenu((float)P.PointerPos.X, (float)P.PointerPos.Y, b => b
        .Item("Create Node...", () => OpenPopup(P, gp, null, null, false), iconDraw: Ico(OrigamiIconSet.Plus, Palette.Acc300))
        .Separator()
        .Item("New Group", () => NewGroupAt(gp), iconDraw: Ico(OrigamiIconSet.Layers))
        .Item("New Sticky Note", () => NewStickyAt(gp), iconDraw: Ico(OrigamiIconSet.Document))
        .Item("Paste", () => Paste(gp), enabled: _clipNodes.Count > 0, iconDraw: Ico(OrigamiIconSet.Document)));

    // Right-click on a single node.
    private void NodeMenu(Paper P, GraphNode node) => Origami.ContextMenu((float)P.PointerPos.X, (float)P.PointerPos.Y, b => b
        .Header(node.Title)
        .Item("Open Sub Graph", () => { if (node.UserData is string docId) EnterDoc(docId); }, enabled: node.UserData is string)
        .Item("Copy", () => Copy(new[] { node }), iconDraw: Ico(OrigamiIconSet.Layers))
        .Item("Paste", () => Paste(node.Position + new Float2(30, 30)), enabled: _clipNodes.Count > 0)
        .Item("Duplicate", () => Duplicate(node), iconDraw: Ico(OrigamiIconSet.Layers))
        .Separator()
        .Item("New Group", () => CreateGroup(new[] { node }), iconDraw: Ico(OrigamiIconSet.Layers))
        .Separator()
        .Item("Delete", () => DeleteNodes(new[] { node }), iconDraw: Trash, danger: true));

    // Right-click while 2+ nodes are selected.
    private void MultiMenu(Paper P, IReadOnlyList<GraphNode> nodes)
    {
        var arr = nodes.ToArray();
        Origami.ContextMenu((float)P.PointerPos.X, (float)P.PointerPos.Y, b => b
            .Header($"{arr.Length} nodes")
            .Item("Copy", () => Copy(arr), iconDraw: Ico(OrigamiIconSet.Layers))
            .Item("Duplicate", () => { foreach (var n in arr) Duplicate(n); }, iconDraw: Ico(OrigamiIconSet.Layers))
            .Separator()
            .Item("Create Group", () => CreateGroup(arr), iconDraw: Ico(OrigamiIconSet.Layers))
            .Item("Create Sub Graph", () => CreateSubGraph(arr), iconDraw: Ico(OrigamiIconSet.Layers, Palette.Acc300))
            .Separator()
            .Item("Delete", () => DeleteNodes(arr), iconDraw: Trash, danger: true));
    }

    private void GroupMenu(Paper P, GraphGroup g) => Origami.ContextMenu((float)P.PointerPos.X, (float)P.PointerPos.Y, b =>
        b.Header(g.Title).Submenu("Color", s => { foreach (var (name, col) in GroupColors) { var c = col; s.Item(name, () => g.Color = c); } })
         .Separator()
         .Item("Delete Group", () => Groups.Remove(g), iconDraw: Trash, danger: true));

    private void StickyMenu(Paper P, GraphSticky sk) => Origami.ContextMenu((float)P.PointerPos.X, (float)P.PointerPos.Y, b =>
        b.Header("Sticky Note").Submenu("Color", s => { foreach (var (name, col) in StickyColors) { var c = col; s.Item(name, () => sk.Color = c); } })
         .Separator()
         .Item("Delete", () => Stickies.Remove(sk), iconDraw: Trash, danger: true));

    private static readonly (string, Color)[] GroupColors =
    {
        ("Purple", Out), ("Blue", Tex), ("Green", Math), ("Amber", Val), ("Rose", Palette.C(251, 113, 133)), ("Slate", Flow),
    };
    private static readonly (string, Color)[] StickyColors =
    {
        ("Yellow", StickyYellow), ("Green", Palette.C(178, 226, 160)), ("Blue", Palette.C(160, 200, 245)),
        ("Pink", Palette.C(245, 180, 205)), ("Purple", Palette.C(210, 185, 245)),
    };

    // ═══ node ops ═══

    private void NewGroupAt(Float2 gp) => Groups.Add(new GraphGroup { Id = "g" + _nextId++, Title = "Group", Position = gp, Size = new Float2(240, 160), Color = Out });
    private void NewStickyAt(Float2 gp) => Stickies.Add(new GraphSticky { Id = "s" + _nextId++, Text = "", Position = gp, Size = new Float2(190, 130), Color = StickyYellow });

    private GraphNode CloneNode(GraphNode n)
    {
        var c = new GraphNode
        {
            Id = n.Id, Title = n.Title, Position = n.Position, Width = n.Width, Accent = n.Accent, Icon = n.Icon,
            Tooltip = n.Tooltip, Body = n.Body, BodyHeight = n.BodyHeight, Collapsible = n.Collapsible, Collapsed = n.Collapsed,
            Pill = n.Pill, Pinned = n.Pinned, UserData = n.UserData, Badge = n.Badge,
        };
        foreach (var p in n.Inputs) c.Inputs.Add(ClonePort(p));
        foreach (var p in n.Outputs) c.Outputs.Add(ClonePort(p));
        return c;
    }

    private static GraphPort ClonePort(GraphPort p)
        => new(p.Id, p.Label) { Color = p.Color, Side = p.Side, Shape = p.Shape, Tooltip = p.Tooltip, IsPlaceholder = p.IsPlaceholder };

    private void Duplicate(GraphNode n)
    {
        if (n.Pinned) return;
        var copy = CloneNode(n);
        copy.Id = "n" + _nextId++; copy.Position += new Float2(30, 30);
        Nodes.Add(copy);
    }

    private void Copy(IReadOnlyList<GraphNode> nodes)
    {
        _clipNodes.Clear(); _clipWires.Clear();
        // The boundary cards are the graph's contract, so there is nothing to copy about them.
        var ids = nodes.Where(n => !n.Pinned).Select(n => n.Id).ToHashSet();
        foreach (var n in nodes) if (!n.Pinned) _clipNodes.Add(CloneNode(n));
        foreach (var w in Wires)
            if (ids.Contains(w.FromNode) && ids.Contains(w.ToNode))
                _clipWires.Add(new GraphConnection(w.FromNode, w.FromPort, w.ToNode, w.ToPort) { Color = w.Color });
    }

    private void Paste(Float2 at)
    {
        if (_clipNodes.Count == 0) return;
        Float2 basePos = _clipNodes[0].Position;
        var map = new Dictionary<string, string>();
        foreach (var cn in _clipNodes)
        {
            var nn = CloneNode(cn);
            nn.Id = "n" + _nextId++; nn.Position = at + (cn.Position - basePos);
            map[cn.Id] = nn.Id; Nodes.Add(nn);
        }
        foreach (var cw in _clipWires)
            Wires.Add(new GraphConnection(map[cw.FromNode], cw.FromPort, map[cw.ToNode], cw.ToPort) { Color = cw.Color });
    }

    private void DeleteNodes(IReadOnlyList<GraphNode> nodes)
    {
        foreach (var n in nodes)
        {
            if (n.Pinned) continue;
            Wires.RemoveAll(c => c.FromNode == n.Id || c.ToNode == n.Id);
            Nodes.Remove(n);
            DropSubGraph(n);
        }
    }

    // A local sub graph lives only through the node that uses it. An asset outlives its users.
    private void DropSubGraph(GraphNode node)
    {
        if (node.UserData is not string docId || !_docs.TryGetValue(docId, out GraphDoc? doc) || doc.IsAsset) return;
        if (_docs.Values.Any(d => d.Nodes.Any(n => n.UserData as string == docId))) return;

        _docs.Remove(docId);
        foreach (var inner in doc.Nodes) DropSubGraph(inner);
    }

    private void CreateGroup(IReadOnlyList<GraphNode> nodes)
    {
        if (nodes.Count == 0) return;

        var metrics = Origami.Root.Metrics;
        float minX = nodes.Min(n => n.Position.X), minY = nodes.Min(n => n.Position.Y);
        float maxX = nodes.Max(n => n.Position.X + NodeGraphPreview.MeasureWidth(n, metrics));
        float maxY = nodes.Max(n => n.Position.Y + NodeGraphPreview.MeasureHeight(n, metrics));
        Groups.Add(new GraphGroup
        {
            Id = "g" + _nextId++,
            Title = "Group",
            Position = new Float2(minX - 24, minY - 40),
            Size = new Float2(maxX - minX + 48, maxY - minY + 64),
            Color = Out,
        });
    }

    // ═══ searchable create popup ═══

    private void OpenPopup(Paper P, Float2 graphPos, string? wireNode, string? wirePort, bool wireIsOutput)
    {
        _popupOpen = true; _popupGraph = graphPos; _popupScreen = P.PointerPos; _search = "";
        _wireNode = wireNode; _wirePort = wirePort; _wireIsOutput = wireIsOutput;
    }
    private void ClosePopup() { _popupOpen = false; _wireNode = null; }

    private IEnumerable<NodeSpec> Filtered()
    {
        IEnumerable<NodeSpec> q = Catalog;
        if (_wireNode != null) q = q.Where(s => _wireIsOutput ? s.Inputs.Length > 0 : s.Outputs.Length > 0);
        if (!string.IsNullOrWhiteSpace(_search))
            q = q.Where(s => s.Label.Contains(_search, StringComparison.OrdinalIgnoreCase) || s.Category.Contains(_search, StringComparison.OrdinalIgnoreCase));
        return q;
    }

    private void DrawCreatePopup(Paper P)
    {
        const float pw = 236f, ph = 300f;
        float px = System.Math.Clamp(_popupScreen.X - _colOx, 4f, MathF.Max(4f, _panelW - pw - 4f));
        float py = System.Math.Clamp(_popupScreen.Y - _colOy, 4f, MathF.Max(4f, _panelH - ph - 4f));

        P.Box("ngbackdrop").PositionType(PositionType.SelfDirected).Left(0).Top(0)
            .Width(P.Percent(100)).Height(P.Percent(100)).Layer(Layer.Overlay)
            .OnClick(_ => ClosePopup());

        using (P.Column("ngpopup").PositionType(PositionType.SelfDirected).Left(px).Top(py)
            .Width(pw).Height(P.Auto).MaxHeight(320).Layer(Layer.Overlay + 10)
            .BackgroundColor(Palette.C(20, 16, 30, 0.98f)).Rounded(10)
            .BorderColor(Palette.BdSoft).BorderWidth(1).Padding(7, 7, 7, 7).Gap(6)
            .DropShadow(0, 8, 24, 0, Palette.C(0, 0, 0, 0.5f)).Enter())
        {
            Origami.SearchField(P, "ngsearch", _search, v => _search = v, _wireNode != null ? "Compatible nodes..." : "Search nodes...").Show();

            Origami.ScrollView(P, "ngplist", pw - 14, 240).Body(() =>
            {
                string? lastCat = null;
                foreach (var s in Filtered())
                {
                    if (s.Category != lastCat)
                    {
                        lastCat = s.Category;
                        P.Box("hc_" + s.Category).Width(P.Percent(100)).Height(P.Auto).Margin(3, 0, 5, 3)
                            .Text(s.Category.ToUpperInvariant(), Fonts.Semi).FontSize(9.5f * Palette.TS).LetterSpacing(0.6f)
                            .TextColor(Palette.Acc300).Alignment(TextAlignment.MiddleLeft);
                    }
                    var spec = s;
                    using (P.Row("it_" + spec.Label).Width(P.Percent(100)).Height(28).Rounded(6).Padding(8, 8, 0, 0)
                        .Hovered.BackgroundColor(Palette.Hover).End().OnClick(_ => CreateFromSpec(spec)).Enter())
                    {
                        P.Box("d_" + spec.Label).Width(8).Height(8).Rounded(4).Margin(0, 9, P.Stretch(), P.Stretch()).BackgroundColor(spec.Accent);
                        P.Box("l_" + spec.Label).Width(P.Stretch()).Height(P.Auto).Margin(0, 0, P.Stretch(), P.Stretch())
                            .Text(spec.Label, Fonts.Reg).FontSize(12f * Palette.TS).TextColor(Palette.THi).Alignment(TextAlignment.MiddleLeft);
                    }
                }
                if (!Filtered().Any())
                    P.Box("noresult").Width(P.Percent(100)).Height(40).Text("No matches", Fonts.Reg)
                        .FontSize(11.5f * Palette.TS).TextColor(Palette.TLo).Alignment(TextAlignment.MiddleCenter);
            });
        }
    }

    private void CreateFromSpec(NodeSpec spec)
    {
        var n = new GraphNode { Id = "n" + _nextId++, Title = spec.Label, Position = _popupGraph, Width = 155, Accent = spec.Accent };
        foreach (var (id, label, col) in spec.Inputs) n.Inputs.Add(new GraphPort(id, label) { Color = col });
        foreach (var (id, label, col) in spec.Outputs) n.Outputs.Add(new GraphPort(id, label) { Color = col });
        Nodes.Add(n);

        if (_wireNode != null)
        {
            if (_wireIsOutput && n.Inputs.Count > 0) Wires.Add(new GraphConnection(_wireNode, _wirePort!, n.Id, n.Inputs[0].Id));
            else if (!_wireIsOutput && n.Outputs.Count > 0) Wires.Add(new GraphConnection(n.Id, n.Outputs[0].Id, _wireNode, _wirePort!));
        }
        ClosePopup();
    }

    // ═══ catalog ═══

    private struct NodeSpec
    {
        public string Category, Label; public Color Accent;
        public (string id, string label, Color col)[] Inputs, Outputs;
    }

    private static readonly NodeSpec[] Catalog =
    {
        Spec("Input", "Time", Val, N(), N(("t","Time",Val))),
        Spec("Input", "UV Coords", Tex, N(), N(("uv","UV",Tex))),
        Spec("Input", "Value", Val, N(), N(("v","Value",Val))),
        Spec("Math", "Add", Math, N(("a","A",Math),("b","B",Math)), N(("o","Out",Math))),
        Spec("Math", "Multiply", Math, N(("a","A",Math),("b","B",Math)), N(("o","Out",Math))),
        Spec("Math", "Noise", Math, N(("uv","UV",Tex)), N(("n","Value",Math))),
        Spec("Texture", "Texture Sample", Tex, N(("uv","UV",Tex)), N(("rgb","RGB",Tex),("a","Alpha",Val))),
        Spec("Texture", "Panner", Tex, N(("uv","UV",Tex),("time","Time",Val)), N(("uv","UV",Tex))),
        Spec("Output", "Fragment Output", Out, N(("albedo","Albedo",Out),("emit","Emission",Out)), N()),
    };

    // ═══ tiny builders ═══

    private static (string, string, Color)[] N(params (string, string, Color)[] p) => p;
    private static NodeSpec Spec(string cat, string label, Color a, (string, string, Color)[] ins, (string, string, Color)[] outs)
        => new() { Category = cat, Label = label, Accent = a, Inputs = ins, Outputs = outs };

    private static (string, string, Color)[] In2(params (string, string, Color)[] p) => p;
    private static (string, string, Color)[] Out2(string id, string label, Color c) => new[] { (id, label, c) };
    private static (string, string, Color)[] Out2(params (string, string, Color)[] p) => p;
    private static (string, string, Color)[] OutFlow(params string[] ids) => ids.Select(i => (i, "", Flow)).ToArray();

    private static GraphNode Node(string id, string title, float x, float y, float w, Color accent,
        (string, string, Color)[]? ins, (string, string, Color)[]? outs)
    {
        var n = new GraphNode { Id = id, Title = title, Position = new Float2(x, y), Width = w, Accent = accent };
        if (ins != null) foreach (var (pid, label, c) in ins) n.Inputs.Add(new GraphPort(pid, label) { Color = c });
        if (outs != null) foreach (var (pid, label, c) in outs) n.Outputs.Add(new GraphPort(pid, label) { Color = c });
        return n;
    }

    private static GraphNode Bt(string id, string title, float x, float y, (string, string, Color)[]? outs)
    {
        var n = new GraphNode { Id = id, Title = title, Position = new Float2(x, y), Width = 130, Accent = Flow };
        if (outs != null) foreach (var (pid, _, _) in outs) n.Outputs.Add(new GraphPort(pid, "") { Side = PortSide.Bottom, Shape = PortShape.Arrow, Color = Flow });
        return n;
    }

    private static IOrigamiIcon Ico(SvgIcon icon) => icon.Tinted(Palette.TMid);
    private static IOrigamiIcon Ico(SvgIcon icon, Color c) => icon.Tinted(c);
}

internal static class Fonts
{
    public static FontFile Reg => Origami.Root.Font!;
    public static FontFile Semi => Origami.Root.SemiBold ?? Origami.Root.Font!;
}
