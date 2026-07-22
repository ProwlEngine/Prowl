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
// ports, execution/behaviour-tree ports, group boxes, wire reroute points, context menus, and a
// searchable create popup.
public sealed class NodeGraphPanel : DockPanel
{
    private readonly List<GraphNode> _nodes;
    private readonly List<GraphConnection> _wires;
    private readonly List<GraphGroup> _groups;
    private readonly List<GraphSticky> _stickies;
    private readonly List<GraphNode> _clipNodes = new();
    private readonly List<GraphConnection> _clipWires = new();
    private readonly NodeGraphController _ctrl = new();
    private int _selNodes, _selEdges, _nextId = 100;

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
        _nodes = new List<GraphNode>
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
        };
        _nodes[7].Inputs.Add(new GraphPort("in", "") { Side = PortSide.Top, Shape = PortShape.Arrow, Color = Flow });
        _nodes[8].Inputs.Add(new GraphPort("in", "") { Side = PortSide.Top, Shape = PortShape.Arrow, Color = Flow });
        _nodes[8].Accent = Tex; _nodes[9].Accent = Palette.C(251, 113, 133);
        _nodes[9].Inputs.Add(new GraphPort("in", "") { Side = PortSide.Top, Shape = PortShape.Arrow, Color = Flow });

        _wires = new List<GraphConnection>
        {
            new("uv", "uv", "noise", "uv"), new("uv", "uv", "panner", "uv"),
            new("time", "t", "panner", "time"), new("panner", "uv", "tex", "uv"),
            new("noise", "n", "mul", "a"), new("tex", "rgb", "mul", "b"),
            new("mul", "o", "output", "albedo"), new("tex", "rgb", "output", "emit"),
            new("tex", "a", "output", "alpha"),
            new("bt_root", "c0", "bt_a", "in") { Color = Flow }, new("bt_root", "c1", "bt_b", "in") { Color = Flow },
        };

        _groups = new List<GraphGroup>
        {
            new() { Id = "grp_bt", Title = "Behaviour Tree", Position = new Float2(120, 400), Size = new Float2(340, 230), Color = Flow },
        };

        _stickies = new List<GraphSticky>
        {
            new() { Id = "note1", Text = "Double-click to edit.\nDrag me, resize from the corner.", Position = new Float2(600, 40), Size = new Float2(200, 110), Color = StickyYellow },
        };

        // Demo: one wire rerouted through a control point (drag it; right-click it to remove).
        _wires.First(w => w.FromNode == "uv" && w.ToNode == "panner").ControlPoints.Add(new Float2(115, 205));
    }

    public override void OnGUI(Paper P, float w, float h)
    {
        _panelW = w; _panelH = h;
        using (P.Column("ngroot").Width(P.Percent(100)).Height(P.Percent(100))
            .BackgroundColor(Palette.RootBg).OnPostLayout((hd, r) => { _colOx = (float)r.Min.X; _colOy = (float)r.Min.Y; }).Enter())
        {
            Toolbar(P);
            Origami.NodeGraph(P, "shadergraph", w, h - 38f)
                .Nodes(_nodes).Connections(_wires).Groups(_groups).Stickies(_stickies)
                .Controller(_ctrl)
                .InitialView(new Float2(18, -20), 0.62f)
                .OnSelectionChanged(sel => { _selNodes = sel.Nodes.Count; _selEdges = sel.Edges.Count; })
                // ── the lines a real host wraps in Undo.RegisterAction / BeginContinuous ──
                .OnNodesMoved((nodes, delta) => { foreach (var n in nodes) n.Position += delta; })
                .OnConnect(Connect)
                .OnDeleteSelection(sel =>
                {
                    foreach (var e in sel.Edges) _wires.Remove(e);
                    foreach (var g in sel.Groups) _groups.Remove(g);
                    foreach (var sk in sel.Stickies) _stickies.Remove(sk);
                    foreach (var n in sel.Nodes) { _wires.RemoveAll(c => c.FromNode == n.Id || c.ToNode == n.Id); _nodes.Remove(n); }
                })
                .OnBackgroundContext(gp => BackgroundMenu(P, gp))
                .OnDropWireInEmpty((gp, sn, sp, so) => OpenPopup(P, gp, sn, sp, so))
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
        using (P.Row("ngtb").Width(P.Percent(100)).Height(38).Padding(8, 8, 0, 0).ColBetween(6)
            .BackgroundColor(Palette.GlassIn).Enter())
        {
            Origami.Button(P, "tb_all", "Frame All").Small().OnClick(() => _ctrl.FrameAll()).Show();
            Origami.Button(P, "tb_sel", "Fit Selection").Small().Subtle().OnClick(() => _ctrl.FrameSelection()).Show();
            Origami.Button(P, "tb_selmath", "Select Math").Small().Ghost().OnClick(() => _ctrl.SelectNodes(new[] { "noise", "panner", "mul" })).Show();
            P.Box("tb_sp").Width(P.Stretch());
            P.Box("tb_info").Width(P.Auto).Height(P.Auto).Margin(0, 10, P.Stretch(), P.Stretch())
                .Text($"{_ctrl.Zoom * 100f:0}%   {_selNodes} selected", Fonts.Reg)
                .FontSize(11f * Palette.TS).TextColor(Palette.TMid).Alignment(TextAlignment.MiddleRight);
        }
    }

    // ═══ edits ═══

    private void Connect(ConnectionRequest req)
    {
        bool dup = _wires.Any(c => c.FromNode == req.FromNode && c.FromPort == req.FromPort && c.ToNode == req.ToNode && c.ToPort == req.ToPort);
        _wires.RemoveAll(c => c.ToNode == req.ToNode && c.ToPort == req.ToPort); // single-input: replace
        if (!dup) _wires.Add(new GraphConnection(req.FromNode, req.FromPort, req.ToNode, req.ToPort));
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
            .Separator()
            .Item("Delete", () => DeleteNodes(arr), iconDraw: Trash, danger: true));
    }

    private void GroupMenu(Paper P, GraphGroup g) => Origami.ContextMenu((float)P.PointerPos.X, (float)P.PointerPos.Y, b =>
        b.Header(g.Title).Submenu("Color", s => { foreach (var (name, col) in GroupColors) { var c = col; s.Item(name, () => g.Color = c); } })
         .Separator()
         .Item("Delete Group", () => _groups.Remove(g), iconDraw: Trash, danger: true));

    private void StickyMenu(Paper P, GraphSticky sk) => Origami.ContextMenu((float)P.PointerPos.X, (float)P.PointerPos.Y, b =>
        b.Header("Sticky Note").Submenu("Color", s => { foreach (var (name, col) in StickyColors) { var c = col; s.Item(name, () => sk.Color = c); } })
         .Separator()
         .Item("Delete", () => _stickies.Remove(sk), iconDraw: Trash, danger: true));

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

    private void NewGroupAt(Float2 gp) => _groups.Add(new GraphGroup { Id = "g" + _nextId++, Title = "Group", Position = gp, Size = new Float2(240, 160), Color = Out });
    private void NewStickyAt(Float2 gp) => _stickies.Add(new GraphSticky { Id = "s" + _nextId++, Text = "", Position = gp, Size = new Float2(190, 130), Color = StickyYellow });

    private GraphNode CloneNode(GraphNode n)
    {
        var c = new GraphNode { Id = n.Id, Title = n.Title, Position = n.Position, Width = n.Width, Accent = n.Accent };
        foreach (var p in n.Inputs) c.Inputs.Add(new GraphPort(p.Id, p.Label) { Color = p.Color, Side = p.Side, Shape = p.Shape });
        foreach (var p in n.Outputs) c.Outputs.Add(new GraphPort(p.Id, p.Label) { Color = p.Color, Side = p.Side, Shape = p.Shape });
        return c;
    }

    private void Duplicate(GraphNode n)
    {
        var copy = CloneNode(n);
        copy.Id = "n" + _nextId++; copy.Position += new Float2(30, 30);
        _nodes.Add(copy);
    }

    private void Copy(IReadOnlyList<GraphNode> nodes)
    {
        _clipNodes.Clear(); _clipWires.Clear();
        var ids = nodes.Select(n => n.Id).ToHashSet();
        foreach (var n in nodes) _clipNodes.Add(CloneNode(n));
        foreach (var w in _wires)
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
            map[cn.Id] = nn.Id; _nodes.Add(nn);
        }
        foreach (var cw in _clipWires)
            _wires.Add(new GraphConnection(map[cw.FromNode], cw.FromPort, map[cw.ToNode], cw.ToPort) { Color = cw.Color });
    }

    private void DeleteNodes(IReadOnlyList<GraphNode> nodes)
    {
        foreach (var n in nodes) { _wires.RemoveAll(c => c.FromNode == n.Id || c.ToNode == n.Id); _nodes.Remove(n); }
    }

    private void CreateGroup(IReadOnlyList<GraphNode> nodes)
    {
        float minX = nodes.Min(n => n.Position.X), minY = nodes.Min(n => n.Position.Y);
        float maxX = nodes.Max(n => n.Position.X + n.Width), maxY = nodes.Max(n => n.Position.Y + 110f);
        _groups.Add(new GraphGroup
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
            .BorderColor(Palette.BdSoft).BorderWidth(1).Padding(7, 7, 7, 7).ColBetween(6)
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
        _nodes.Add(n);

        if (_wireNode != null)
        {
            if (_wireIsOutput && n.Inputs.Count > 0) _wires.Add(new GraphConnection(_wireNode, _wirePort!, n.Id, n.Inputs[0].Id));
            else if (!_wireIsOutput && n.Outputs.Count > 0) _wires.Add(new GraphConnection(n.Id, n.Outputs[0].Id, _wireNode, _wirePort!));
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
