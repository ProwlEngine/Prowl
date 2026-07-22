// This file is part of the Prowl Game Engine
// Licensed under the MIT License. See the LICENSE file in the project root for details.

using System;
using System.Collections.Generic;
using System.Linq;

using Prowl.PaperUI;
using Prowl.PaperUI.LayoutEngine;
using Prowl.Quill;
using Prowl.Scribe;
using Prowl.Vector;

using Color = System.Drawing.Color;
using TextAlignment = Prowl.PaperUI.TextAlignment;

namespace Prowl.OrigamiUI;

/// <summary>Which edge of a node a port sits on. Left/Right = horizontal data flow;
/// Top/Bottom = vertical flow (behaviour trees, execution stacks).</summary>
public enum PortSide { Left, Right, Top, Bottom }

/// <summary>Port glyph. <see cref="Circle"/> = data socket; <see cref="Arrow"/> = execution/flow.</summary>
public enum PortShape { Circle, Arrow }

/// <summary>An input or output socket on a <see cref="GraphNode"/>.</summary>
public sealed class GraphPort
{
    public string Id = "";
    public string Label = "";
    public Color? Color;
    /// <summary>Edge to place the port on. Null = auto (inputs Left, outputs Right).</summary>
    public PortSide? Side;
    public PortShape Shape = PortShape.Circle;
    public object? UserData;

    public GraphPort() { }
    public GraphPort(string id, string label) { Id = id; Label = label; }
}

/// <summary>
/// A node. <see cref="Position"/> is graph space (unscaled). Ports are split into
/// <see cref="Inputs"/> / <see cref="Outputs"/> (data-flow direction); each port's
/// <see cref="GraphPort.Side"/> chooses which edge it renders on, so a horizontal data node and a
/// vertical behaviour-tree node share one code path.
/// </summary>
public sealed class GraphNode
{
    public string Id = "";
    public string Title = "";
    public Float2 Position;
    public float Width = 172f;
    public IOrigamiIcon? Icon;
    public Color? Accent;
    public List<GraphPort> Inputs = new();
    public List<GraphPort> Outputs = new();
    public object? UserData;
    /// <summary>Render as a compact rounded capsule with no header (relays, reroutes).</summary>
    public bool Pill;
}

/// <summary>A directed wire from an output port to an input port, referenced by id.</summary>
public sealed class GraphConnection
{
    public string FromNode = "";
    public string FromPort = "";
    public string ToNode = "";
    public string ToPort = "";
    public Color? Color;
    /// <summary>Optional reroute points (graph space) the wire is routed through, in order from output to input.</summary>
    public List<Float2> ControlPoints = new();
    public object? UserData;

    public GraphConnection() { }
    public GraphConnection(string fromNode, string fromPort, string toNode, string toPort)
    {
        FromNode = fromNode; FromPort = fromPort; ToNode = toNode; ToPort = toPort;
    }
}

/// <summary>
/// A group box (a.k.a. comment/frame): a titled, coloured rectangle drawn behind the nodes.
/// Membership is spatial — nodes whose centre lies inside the box move with it — so nodes join or
/// leave simply by being dragged in or out. Host owns the list; the widget edits it via events.
/// </summary>
public sealed class GraphGroup
{
    public string Id = "";
    public string Title = "Group";
    public Float2 Position;
    public Float2 Size = new(260, 180);
    public Color? Color;
    public object? UserData;
}

/// <summary>A user request to create a wire, normalized so <c>From*</c> is the output side and
/// <c>To*</c> is the input side. The host validates and applies it (and records undo).</summary>
public readonly struct ConnectionRequest
{
    public readonly string FromNode, FromPort, ToNode, ToPort;
    public ConnectionRequest(string fromNode, string fromPort, string toNode, string toPort)
    {
        FromNode = fromNode; FromPort = fromPort; ToNode = toNode; ToPort = toPort;
    }
}

/// <summary>A free-floating note (e.g. a yellow sticky) drawn on the canvas. Host owns the list.</summary>
public sealed class GraphSticky
{
    public string Id = "";
    public string Text = "";
    public Float2 Position;
    public Float2 Size = new(190, 130);
    public Color? Color;
    public object? UserData;
}

/// <summary>Wire dragged from a port and released on empty canvas. <paramref name="sourceIsOutput"/>
/// says whether the drag started at an output (so the host filters its create menu to nodes that
/// have a compatible input, or vice-versa).</summary>
public delegate void DropWireHandler(Float2 graphPos, string sourceNode, string sourcePort, bool sourceIsOutput);

/// <summary>Snapshot of the current selection, handed to <c>OnSelectionChanged</c>.</summary>
public readonly struct GraphSelection
{
    public readonly IReadOnlyList<GraphNode> Nodes;
    public readonly IReadOnlyList<GraphConnection> Edges;
    public readonly IReadOnlyList<GraphGroup> Groups;
    public readonly IReadOnlyList<GraphSticky> Stickies;
    public GraphSelection(IReadOnlyList<GraphNode> nodes, IReadOnlyList<GraphConnection> edges, IReadOnlyList<GraphGroup> groups, IReadOnlyList<GraphSticky> stickies)
    {
        Nodes = nodes; Edges = edges; Groups = groups; Stickies = stickies;
    }
    public bool IsEmpty => Nodes.Count == 0 && Edges.Count == 0 && Groups.Count == 0 && Stickies.Count == 0;
}

/// <summary>
/// Host-held handle for driving a node graph programmatically. Bind it with
/// <see cref="NodeGraphBuilder.Controller"/>. Read <see cref="Pan"/> / <see cref="Zoom"/> / the
/// selection lists at any time (the widget refreshes them each frame); issue commands
/// (<see cref="FrameAll"/>, <see cref="FocusNode"/>, <see cref="SelectNodes"/>, …) which the widget
/// applies on its next draw. Commands are one-shot — set once, they fire the next frame and clear.
/// </summary>
public sealed class NodeGraphController
{
    // ── Live state (widget writes each frame; host reads) ──
    public float Zoom { get; internal set; } = 1f;
    public Float2 Pan { get; internal set; }
    public IReadOnlyList<string> SelectedNodes { get; internal set; } = Array.Empty<string>();
    public IReadOnlyList<string> SelectedGroups { get; internal set; } = Array.Empty<string>();
    public IReadOnlyList<string> SelectedStickies { get; internal set; } = Array.Empty<string>();

    // ── Pending commands (host sets; widget consumes) ──
    internal Float2? _setPan; internal float? _setZoom;
    internal Float2? _centerOn;
    internal string? _focusNode;
    internal int _frame;                 // 0 none, 1 all, 2 selection
    internal List<string>? _selectNodes; internal bool _selectAdditive;
    internal bool _clearSelect;

    /// <summary>Set pan (graph-space origin offset in px) and zoom directly.</summary>
    public void SetView(Float2 pan, float zoom) { _setPan = pan; _setZoom = zoom; }
    public void SetZoom(float zoom) { _setZoom = zoom; }
    /// <summary>Pan so this graph-space point sits at the centre of the viewport.</summary>
    public void CenterOn(Float2 graphPoint) { _centerOn = graphPoint; }
    /// <summary>Centre + zoom so the given node fills the viewport (never zooms past 1x).</summary>
    public void FocusNode(string nodeId) { _focusNode = nodeId; }
    /// <summary>Fit all nodes/groups/stickies into the viewport.</summary>
    public void FrameAll() { _frame = 1; }
    /// <summary>Fit the current selection into the viewport.</summary>
    public void FrameSelection() { _frame = 2; }
    /// <summary>Replace (or, additive, extend) the node selection.</summary>
    public void SelectNodes(IEnumerable<string> nodeIds, bool additive = false) { _selectNodes = nodeIds.ToList(); _selectAdditive = additive; }
    public void ClearSelection() { _clearSelect = true; }
}

/// <summary>
/// Fluent builder for a generic, host-agnostic node graph. The widget is a pure view + intent
/// emitter: it never mutates the graph. Every edit is raised as an event (with enough data to
/// build an undo step); the host applies it to its own model and records undo. Nodes are real
/// Paper elements; grid, wires and ports are drawn on the canvas from the same pan/zoom used to
/// place the nodes, so everything stays frame-perfect.
/// </summary>
public sealed class NodeGraphBuilder
{
    // Cosmetic / interaction constants not worth a per-theme field.
    private const float BodyPadTop = 8f;
    private const float BodyPadBottom = 10f;
    private const float PortLabelPadX = 15f;
    private const float PortHitR = 9f;         // port grab radius
    private const float TopBotSpacing = 26f;
    private const float PillH = 20f;
    private const float WireHitDist = 8f;      // wire click tolerance

    // Metrics cached from theme.Metrics each frame (see ReadMetrics). All graph-space; scaled by zoom.
    private float _headerH, _portRowH, _nodeRounding, _titleFont, _portFont, _portDotR;
    private float _gridSpacing, _wireThick, _minZoom, _maxZoom, _lodFull, _lodHeader;

    private void ReadMetrics()
    {
        var m = _theme.Metrics;
        // Node cards reuse the shared metrics; only graph-specific values come from the Graph* fields.
        _headerH = m.HeaderHeight; _portRowH = m.RowHeight;
        _nodeRounding = m.Rounding; _titleFont = m.FontSize; _portFont = m.FontSizeSmall;
        _portDotR = m.GraphPortRadius; _gridSpacing = m.GraphGridSpacing; _wireThick = m.GraphWireThickness;
        _minZoom = m.GraphMinZoom; _maxZoom = m.GraphMaxZoom; _lodFull = m.GraphLodFull; _lodHeader = m.GraphLodHeader;
    }

    private enum Detail { Block, Header, Full }
    private enum DragMode { None, MoveNodes, Marquee, Connect, MoveGroup, ResizeGroup, MovePoint, MoveSticky, ResizeSticky }

    private readonly Paper _paper;
    private readonly string _id;
    private readonly OrigamiTheme _theme;

    private float _width = 480f, _height = 320f;
    private IReadOnlyList<GraphNode> _nodes = Array.Empty<GraphNode>();
    private IReadOnlyList<GraphConnection> _connections = Array.Empty<GraphConnection>();
    private IReadOnlyList<GraphGroup> _groups = Array.Empty<GraphGroup>();
    private IReadOnlyList<GraphSticky> _stickies = Array.Empty<GraphSticky>();
    private bool _showGrid = true;
    private Float2? _initPan;
    private float? _initZoom;
    private NodeGraphController? _controller;

    private Action<GraphSelection>? _onSelectionChanged;
    private Action<IReadOnlyList<GraphNode>, Float2>? _onNodesMoved;
    private Action<ConnectionRequest>? _onConnect;
    private Func<ConnectionRequest, bool>? _onValidate;
    private Action<GraphSelection>? _onDelete;
    private Action<Float2>? _onBackgroundContext;
    private Action<GraphNode, Float2>? _onNodeContext;
    private Action<IReadOnlyList<GraphNode>, Float2>? _onNodesContext; // multi-select node right-click
    private Action<GraphNode>? _onNodeDoubleClick;
    private DropWireHandler? _onDropWireEmpty;
    private Action<GraphGroup, IReadOnlyList<GraphNode>, Float2>? _onGroupMoved;
    private Action<GraphGroup, Float2, Float2>? _onGroupResized; // group, newPos, newSize
    private Action<GraphGroup, string>? _onGroupRenamed;
    private Action<GraphGroup, Float2>? _onGroupContext;
    private Action<GraphSticky, Float2>? _onStickyMoved;        // sticky, delta
    private Action<GraphSticky, Float2, Float2>? _onStickyResized;
    private Action<GraphSticky, string>? _onStickyEdited;
    private Action<GraphSticky, Float2>? _onStickyContext;
    private Action<GraphConnection, int, Float2>? _onWireAddPoint;    // wire, insert index, pos
    private Action<GraphConnection, int>? _onWireRemovePoint;         // wire, index
    private Action<GraphConnection, int, Float2>? _onWirePointMoved;  // wire, index, new pos (commit)

    internal NodeGraphBuilder(Paper paper, string id, OrigamiTheme theme)
    {
        _paper = paper ?? throw new ArgumentNullException(nameof(paper));
        _id = id ?? throw new ArgumentNullException(nameof(id));
        _theme = theme ?? throw new ArgumentNullException(nameof(theme));
    }

    // ── Fluent config ──
    public NodeGraphBuilder Size(float width, float height) { _width = width; _height = height; return this; }
    public NodeGraphBuilder Nodes(IReadOnlyList<GraphNode> nodes) { _nodes = nodes ?? throw new ArgumentNullException(nameof(nodes)); return this; }
    public NodeGraphBuilder Connections(IReadOnlyList<GraphConnection> connections) { _connections = connections ?? throw new ArgumentNullException(nameof(connections)); return this; }
    public NodeGraphBuilder Groups(IReadOnlyList<GraphGroup> groups) { _groups = groups ?? throw new ArgumentNullException(nameof(groups)); return this; }
    public NodeGraphBuilder Stickies(IReadOnlyList<GraphSticky> stickies) { _stickies = stickies ?? throw new ArgumentNullException(nameof(stickies)); return this; }
    public NodeGraphBuilder Grid(bool show = true) { _showGrid = show; return this; }
    /// <summary>Initial pan (graph-space origin offset, in pixels) and zoom, applied only on the first
    /// frame; the user's pan/zoom persists afterward. Use to frame the graph when it opens.</summary>
    public NodeGraphBuilder InitialView(Float2 pan, float zoom) { _initPan = pan; _initZoom = zoom; return this; }
    /// <summary>Bind a host-held <see cref="NodeGraphController"/> for programmatic view/selection control
    /// and to read the current pan/zoom/selection.</summary>
    public NodeGraphBuilder Controller(NodeGraphController controller) { _controller = controller; return this; }

    /// <summary>Selection changed (nodes and/or wires). Host shows properties / highlights.</summary>
    public NodeGraphBuilder OnSelectionChanged(Action<GraphSelection> handler) { _onSelectionChanged = handler; return this; }
    /// <summary>A move gesture committed: the given nodes should shift by <c>delta</c> (graph space).
    /// Fired once on release — record it as a single undo step.</summary>
    public NodeGraphBuilder OnNodesMoved(Action<IReadOnlyList<GraphNode>, Float2> handler) { _onNodesMoved = handler; return this; }
    /// <summary>User dropped a wire on a compatible port. Host adds the connection + records undo.</summary>
    public NodeGraphBuilder OnConnect(Action<ConnectionRequest> handler) { _onConnect = handler; return this; }
    /// <summary>Optional live validation while dragging a wire — return false to reject the drop (and
    /// show a red preview). When set, an invalid drop does NOT fire <c>OnConnect</c>.</summary>
    public NodeGraphBuilder OnValidateConnection(Func<ConnectionRequest, bool> predicate) { _onValidate = predicate; return this; }
    /// <summary>Delete the current selection (nodes + wires + groups + stickies). Host removes them + records undo.</summary>
    public NodeGraphBuilder OnDeleteSelection(Action<GraphSelection> handler) { _onDelete = handler; return this; }
    /// <summary>Right-click on empty canvas (graph-space position) — host opens a create menu.</summary>
    public NodeGraphBuilder OnBackgroundContext(Action<Float2> handler) { _onBackgroundContext = handler; return this; }
    /// <summary>Right-click on a single node (that node becomes the selection if it wasn't selected).</summary>
    public NodeGraphBuilder OnNodeContext(Action<GraphNode, Float2> handler) { _onNodeContext = handler; return this; }
    /// <summary>Right-click while 2+ nodes are selected — host adds e.g. a "Create Group" item.</summary>
    public NodeGraphBuilder OnNodesContext(Action<IReadOnlyList<GraphNode>, Float2> handler) { _onNodesContext = handler; return this; }
    public NodeGraphBuilder OnNodeDoubleClick(Action<GraphNode> handler) { _onNodeDoubleClick = handler; return this; }
    /// <summary>Wire dragged from a port and released on empty canvas — host may open a filtered create menu.</summary>
    public NodeGraphBuilder OnDropWireInEmpty(DropWireHandler handler) { _onDropWireEmpty = handler; return this; }

    // ── Group edits (host applies + records undo) ──
    /// <summary>A group was dragged: move the group and the given member nodes by <c>delta</c>.</summary>
    public NodeGraphBuilder OnGroupMoved(Action<GraphGroup, IReadOnlyList<GraphNode>, Float2> handler) { _onGroupMoved = handler; return this; }
    public NodeGraphBuilder OnGroupResized(Action<GraphGroup, Float2, Float2> handler) { _onGroupResized = handler; return this; }
    public NodeGraphBuilder OnGroupRenamed(Action<GraphGroup, string> handler) { _onGroupRenamed = handler; return this; }
    public NodeGraphBuilder OnGroupContext(Action<GraphGroup, Float2> handler) { _onGroupContext = handler; return this; }

    // ── Sticky note edits ──
    public NodeGraphBuilder OnStickyMoved(Action<GraphSticky, Float2> handler) { _onStickyMoved = handler; return this; }
    public NodeGraphBuilder OnStickyResized(Action<GraphSticky, Float2, Float2> handler) { _onStickyResized = handler; return this; }
    /// <summary>Sticky text edited inline (double-click to edit; commits on Escape / click-away).</summary>
    public NodeGraphBuilder OnStickyEdited(Action<GraphSticky, string> handler) { _onStickyEdited = handler; return this; }
    public NodeGraphBuilder OnStickyContext(Action<GraphSticky, Float2> handler) { _onStickyContext = handler; return this; }

    // ── Wire control points (reroutes) ──
    /// <summary>Right-clicked an empty part of a wire — insert a reroute point at <c>index</c> / <c>pos</c>.</summary>
    public NodeGraphBuilder OnWireAddPoint(Action<GraphConnection, int, Float2> handler) { _onWireAddPoint = handler; return this; }
    /// <summary>Right-clicked a reroute point — remove control point <c>index</c>.</summary>
    public NodeGraphBuilder OnWireRemovePoint(Action<GraphConnection, int> handler) { _onWireRemovePoint = handler; return this; }
    /// <summary>A reroute point drag committed — control point <c>index</c> moved to <c>pos</c> (fired once on release).</summary>
    public NodeGraphBuilder OnWirePointMoved(Action<GraphConnection, int, Float2> handler) { _onWirePointMoved = handler; return this; }

    // ═══════════════════════════════════════════════════════════════════
    //  Per-frame state (persisted on the container element)
    // ═══════════════════════════════════════════════════════════════════

    private sealed class GraphState
    {
        public float PanX, PanY, Zoom = 1f;
        public bool ViewInit;                          // first-frame InitialView applied
        public float ScreenX, ScreenY;                 // container top-left, from OnPostLayout
        public readonly HashSet<string> SelNodes = new();
        public readonly HashSet<string> SelEdges = new();  // by EdgeKey
        public readonly HashSet<string> SelGroups = new(); // by group id
        public readonly HashSet<string> SelStickies = new(); // by sticky id
        public DragMode Mode;
        public Float2 DragOffset;                       // MoveNodes / MoveGroup accumulator (graph space)
        public Float2 MarqueeStart;                     // graph space
        public string? ConnNode, ConnPort;              // Connect source
        public bool ConnFromOutput;
        public string? ActiveGroup;                     // group being moved/resized
        public string? ActiveSticky;                    // sticky being moved/resized
        public Float2 ResizeSize;                       // live size while resizing (graph space)
        public string? RenamingGroup;                   // group whose title is being edited
        public string? EditingSticky;                   // sticky whose text is being edited
        public string RenameBuffer = "";                // shared text-edit buffer (group title / sticky body)
        public string? ActiveWire;                      // EdgeKey of the wire whose control point is dragging
        public int ActivePoint;                         // control point index being dragged
        public Float2 PointOffset;                      // live offset (graph space) of the dragged point
    }

    // A resolved node box + its port anchors for this frame (effective = includes live drag offset).
    private struct PortSlot { public GraphPort Port; public bool IsOutput; public PortSide Side; public Float2 Anchor; }
    private struct NodeLayout
    {
        public GraphNode Node;
        public Float2 Pos; public float W, H;
        public List<PortSlot> Ports;
    }

    private static string EdgeKey(GraphConnection c) => $"{c.FromNode}{c.FromPort}{c.ToNode}{c.ToPort}";

    // ═══════════════════════════════════════════════════════════════════
    //  Show
    // ═══════════════════════════════════════════════════════════════════

    public void Show()
    {
        ReadMetrics();
        var ink = _theme.Ink;
        Color canvasBg = _theme.Neutral.C100;
        Color nodeBg = _theme.Popover;
        Color borderSoft = _theme.BorderSoft;
        Color accentDefault = _theme.Primary.C500;
        FontFile? font = _theme.Font;
        FontFile? semi = _theme.SemiBold ?? _theme.Font;

        var container = _paper.Box(_id)
            .Width(_width).Height(_height)
            .Rounded(_theme.Metrics.ContainerRounding)
            .BorderColor(borderSoft).BorderWidth(1f)
            .Clip();
        var handle = container._handle;

        var st = _paper.GetElementStorage<GraphState>(handle, "state", null!);
        if (st == null) { st = new GraphState(); _paper.SetElementStorage(handle, "state", st); }
        if (!st.ViewInit)
        {
            st.ViewInit = true;
            if (_initZoom.HasValue) st.Zoom = Math.Clamp(_initZoom.Value, _minZoom, _maxZoom);
            if (_initPan.HasValue) { st.PanX = _initPan.Value.X; st.PanY = _initPan.Value.Y; }
        }

        // Index + reconcile selection against the current (host-owned) model.
        var byId = new Dictionary<string, GraphNode>(_nodes.Count);
        foreach (var n in _nodes) byId[n.Id] = n;
        st.SelNodes.RemoveWhere(nid => !byId.ContainsKey(nid));
        var edgeKeys = new HashSet<string>();
        foreach (var c in _connections) edgeKeys.Add(EdgeKey(c));
        st.SelEdges.RemoveWhere(k => !edgeKeys.Contains(k));
        var groupById = new Dictionary<string, GraphGroup>(_groups.Count);
        foreach (var g in _groups) groupById[g.Id] = g;
        st.SelGroups.RemoveWhere(gid => !groupById.ContainsKey(gid));
        if (st.ActiveGroup != null && !groupById.ContainsKey(st.ActiveGroup)) { st.ActiveGroup = null; st.Mode = DragMode.None; }
        if (st.RenamingGroup != null && !groupById.ContainsKey(st.RenamingGroup)) st.RenamingGroup = null;
        var stickyById = new Dictionary<string, GraphSticky>(_stickies.Count);
        foreach (var sk in _stickies) stickyById[sk.Id] = sk;
        st.SelStickies.RemoveWhere(sid => !stickyById.ContainsKey(sid));
        if (st.ActiveSticky != null && !stickyById.ContainsKey(st.ActiveSticky)) { st.ActiveSticky = null; st.Mode = DragMode.None; }
        if (st.EditingSticky != null && !stickyById.ContainsKey(st.EditingSticky)) st.EditingSticky = null;

        // Cursor-anchored zoom (on the container so wheel bubbling up from a node still zooms).
        container.OnScroll(st, (s, e) =>
        {
            float nz = Math.Clamp(s.Zoom * MathF.Exp(e.Delta * 0.14f), _minZoom, _maxZoom);
            float lx = (float)(e.PointerPosition.X - e.ElementRect.Min.X);
            float ly = (float)(e.PointerPosition.Y - e.ElementRect.Min.Y);
            s.PanX = lx - (lx - s.PanX) / s.Zoom * nz;
            s.PanY = ly - (ly - s.PanY) / s.Zoom * nz;
            s.Zoom = nz;
        });

        // Middle-mouse pan (Paper drag is left-only), and keyboard, scoped to when the graph is hovered.
        bool overGraph = _paper.PointerPos.X >= st.ScreenX && _paper.PointerPos.X <= st.ScreenX + _width
                      && _paper.PointerPos.Y >= st.ScreenY && _paper.PointerPos.Y <= st.ScreenY + _height;
        bool editing = st.RenamingGroup != null || st.EditingSticky != null;
        if (overGraph && _paper.IsPointerDown(PaperMouseBtn.Middle) && !editing)
        {
            st.PanX += _paper.PointerDelta.X;
            st.PanY += _paper.PointerDelta.Y;
        }
        if (editing) HandleTextEdit(st);
        else if (overGraph && !_paper.WantsCaptureKeyboard) HandleKeyboard(st, byId);

        // Resolve node boxes + port anchors once (effective positions include the live drag offset).
        var layouts = new Dictionary<string, NodeLayout>(_nodes.Count);
        foreach (var n in _nodes)
            layouts[n.Id] = BuildLayout(n, EffectivePos(n, st));

        // Apply host commands (frame/focus/select/view) now that layouts (content bounds) are known.
        if (_controller != null) ApplyController(st, layouts, byId);

        float zoom = st.Zoom;
        Detail detail = zoom >= _lodFull ? Detail.Full : zoom >= _lodHeader ? Detail.Header : Detail.Block;

        using (container.Enter())
        {
            var snap = new Snapshot
            {
                St = st,
                Zoom = zoom,
                PanX = st.PanX,
                PanY = st.PanY,
                Nodes = _nodes,
                Connections = _connections,
                Layouts = layouts,
                ById = byId,
                ShowGrid = _showGrid,
                GridSpacing = _gridSpacing,
                WireThick = _wireThick,
                GridMinor = ToC32(borderSoft, 0.5f),
                GridMajor = ToC32(borderSoft, 1f),
                WireDefault = ToC32(accentDefault, 0.85f),
                WireSelected = ToC32(_theme.Primary.C700, 1f),
                DotRing = ToC32(nodeBg, 1f),
                DotFill = ToC32(ink.C300, 1f),
                Accent = accentDefault,
                Marquee = ToC32(accentDefault, 0.9f),
                MarqueeFill = ToC32(accentDefault, 0.14f),
            };

            // Background: grid only, plus the pan/marquee/select/context surface.
            var bg = _paper.Box($"{_id}_bg")
                .PositionType(PositionType.SelfDirected).Left(0).Top(0)
                .Width(UnitValue.Percentage(100)).Height(UnitValue.Percentage(100))
                .BackgroundColor(canvasBg)
                .Cursor(PaperCursor.Default)
                // Origin for screen<->graph mapping: use the canvas surface itself so clicks line up
                // with the rendered nodes/wires exactly (independent of the container's border/padding).
                .OnPostLayout((h, r) => { st.ScreenX = (float)r.Min.X; st.ScreenY = (float)r.Min.Y; });
            WireBackgroundEvents(bg, st, byId, layouts);
            using (bg.Enter())
                _paper.Draw((canvas, rect) => PaintGridPass(canvas, rect, in snap));

            // Group boxes: behind the wires and nodes (only their title bar is interactive).
            foreach (var g in _groups)
                DrawGroup(g, st, accentDefault, borderSoft, ink.C500, font, semi);

            // Wires: above the group fills, below the node cards.
            var wireLayer = _paper.Box($"{_id}_wires")
                .PositionType(PositionType.SelfDirected).Left(0).Top(0)
                .Width(UnitValue.Percentage(100)).Height(UnitValue.Percentage(100))
                .IsNotInteractable();
            using (wireLayer.Enter())
                _paper.Draw((canvas, rect) => PaintWiresPass(canvas, rect, in snap));

            // Sticky notes: over the wires, below the node cards.
            foreach (var sk in _stickies)
                DrawSticky(sk, st, font);

            // Node cards.
            foreach (var n in _nodes)
                DrawNode(layouts[n.Id], st, detail, nodeBg, borderSoft, ink.C500, ink.C300, accentDefault, font, semi);

            // Interactive port sockets (above nodes so they are the drag source/targets).
            foreach (var n in _nodes)
                DrawPorts(layouts[n.Id], st, snap);

            // Wire reroute control points (drag to move, right-click to remove).
            foreach (var c in _connections)
                DrawControlPoints(c, st, accentDefault);

            // Foreground overlay: marquee box + in-progress wire (both follow the live cursor).
            var overlay = _paper.Box($"{_id}_ovl")
                .PositionType(PositionType.SelfDirected).Left(0).Top(0)
                .Width(UnitValue.Percentage(100)).Height(UnitValue.Percentage(100))
                .IsNotInteractable();
            using (overlay.Enter())
                _paper.Draw((canvas, rect) => PaintForeground(canvas, rect, in snap));
        }

        if (_controller != null) WriteControllerState(st);
    }

    // ═══════════════════════════════════════════════════════════════════
    //  Programmatic control (NodeGraphController)
    // ═══════════════════════════════════════════════════════════════════

    private void ApplyController(GraphState st, Dictionary<string, NodeLayout> layouts, Dictionary<string, GraphNode> byId)
    {
        var c = _controller!;
        if (c._clearSelect) { ClearSelection(st); FireSelection(st); c._clearSelect = false; }
        if (c._selectNodes != null)
        {
            if (!c._selectAdditive) ClearSelection(st);
            foreach (var id in c._selectNodes) if (byId.ContainsKey(id)) st.SelNodes.Add(id);
            FireSelection(st); c._selectNodes = null;
        }
        if (c._setZoom.HasValue) { st.Zoom = Math.Clamp(c._setZoom.Value, _minZoom, _maxZoom); c._setZoom = null; }
        if (c._setPan.HasValue) { st.PanX = c._setPan.Value.X; st.PanY = c._setPan.Value.Y; c._setPan = null; }
        if (c._centerOn.HasValue) { CenterGraphPoint(st, c._centerOn.Value); c._centerOn = null; }
        if (c._focusNode != null) { if (layouts.TryGetValue(c._focusNode, out var l)) FrameRect(st, l.Pos, new Float2(l.W, l.H), capAtOne: true); c._focusNode = null; }
        if (c._frame != 0) { FrameContent(st, layouts, all: c._frame == 1); c._frame = 0; }
    }

    private void WriteControllerState(GraphState st)
    {
        var c = _controller!;
        c.Zoom = st.Zoom; c.Pan = new Float2(st.PanX, st.PanY);
        c.SelectedNodes = st.SelNodes.ToList();
        c.SelectedGroups = st.SelGroups.ToList();
        c.SelectedStickies = st.SelStickies.ToList();
    }

    private void CenterGraphPoint(GraphState st, Float2 g)
    {
        st.PanX = _width * 0.5f - g.X * st.Zoom;
        st.PanY = _height * 0.5f - g.Y * st.Zoom;
    }

    // Fit a graph-space rect (pos, size) into the viewport with padding; centre it.
    private void FrameRect(GraphState st, Float2 pos, Float2 size, bool capAtOne)
    {
        const float pad = 60f;
        float bw = Math.Max(1f, size.X) + pad * 2f, bh = Math.Max(1f, size.Y) + pad * 2f;
        float zoom = Math.Clamp(Math.Min(_width / bw, _height / bh), _minZoom, capAtOne ? Math.Min(1f, _maxZoom) : _maxZoom);
        st.Zoom = zoom;
        CenterGraphPoint(st, new Float2(pos.X + size.X * 0.5f, pos.Y + size.Y * 0.5f));
    }

    private void FrameContent(GraphState st, Dictionary<string, NodeLayout> layouts, bool all)
    {
        float minX = float.MaxValue, minY = float.MaxValue, maxX = float.MinValue, maxY = float.MinValue;
        bool any = false;
        void Add(Float2 p, Float2 s) { minX = Math.Min(minX, p.X); minY = Math.Min(minY, p.Y); maxX = Math.Max(maxX, p.X + s.X); maxY = Math.Max(maxY, p.Y + s.Y); any = true; }

        foreach (var l in layouts.Values)
            if (all || st.SelNodes.Contains(l.Node.Id)) Add(l.Pos, new Float2(l.W, l.H));
        foreach (var g in _groups)
            if (all || st.SelGroups.Contains(g.Id)) Add(g.Position, g.Size);
        foreach (var sk in _stickies)
            if (all || st.SelStickies.Contains(sk.Id)) Add(sk.Position, sk.Size);

        if (any) FrameRect(st, new Float2(minX, minY), new Float2(maxX - minX, maxY - minY), capAtOne: false);
    }

    private Float2 EffectivePos(GraphNode n, GraphState st)
    {
        if (st.Mode == DragMode.MoveNodes && st.SelNodes.Contains(n.Id)) return n.Position + st.DragOffset;
        if (st.Mode == DragMode.MoveGroup && st.ActiveGroup != null
            && groupOfId(st.ActiveGroup) is { } g && NodeInGroup(n, g)) return n.Position + st.DragOffset;
        return n.Position;

        GraphGroup? groupOfId(string id) { foreach (var gg in _groups) if (gg.Id == id) return gg; return null; }
    }

    // Spatial membership: a node belongs to a group if its centre lies inside the group's rect.
    private static bool NodeInGroup(GraphNode n, GraphGroup g)
    {
        // Uses the node's own width; height is approximate but the centre test is robust enough.
        float cx = n.Position.X + n.Width * 0.5f, cy = n.Position.Y + 40f;
        return cx >= g.Position.X && cx <= g.Position.X + g.Size.X
            && cy >= g.Position.Y && cy <= g.Position.Y + g.Size.Y;
    }

    private List<GraphNode> MembersOf(GraphGroup g)
    {
        var list = new List<GraphNode>();
        foreach (var n in _nodes) if (NodeInGroup(n, g)) list.Add(n);
        return list;
    }

    // Group rect after applying any live move/resize drag.
    private static (Float2 pos, Float2 size) GroupEffective(GraphGroup g, GraphState st)
    {
        if (st.ActiveGroup == g.Id)
        {
            if (st.Mode == DragMode.MoveGroup) return (g.Position + st.DragOffset, g.Size);
            if (st.Mode == DragMode.ResizeGroup) return (g.Position, st.ResizeSize);
        }
        return (g.Position, g.Size);
    }

    // ═══════════════════════════════════════════════════════════════════
    //  Layout
    // ═══════════════════════════════════════════════════════════════════

    private NodeLayout BuildLayout(GraphNode n, Float2 pos)
    {
        List<(GraphPort p, bool o)> left = new(), right = new(), top = new(), bottom = new();
        foreach (var p in n.Inputs) Bucket(p, false).Add((p, false));
        foreach (var p in n.Outputs) Bucket(p, true).Add((p, true));

        List<(GraphPort, bool)> Bucket(GraphPort p, bool o)
        {
            var side = p.Side ?? (o ? PortSide.Right : PortSide.Left);
            return side switch { PortSide.Left => left, PortSide.Right => right, PortSide.Top => top, _ => bottom };
        }

        static PortSlot Slot((GraphPort p, bool o) e, PortSide side, Float2 a)
            => new() { Port = e.p, IsOutput = e.o, Side = side, Anchor = a };

        // Pill (relay/reroute): no header; ports distributed evenly over the full capsule edges.
        if (n.Pill)
        {
            int lr = Math.Max(left.Count, right.Count);
            float ph = Math.Max(PillH, lr * 16f + 6f);
            int tbP = Math.Max(top.Count, bottom.Count);
            float pw = Math.Max(n.Width, Math.Max(44f, tbP > 0 ? tbP * TopBotSpacing + 16f : 44f));
            var pports = new List<PortSlot>(left.Count + right.Count + top.Count + bottom.Count);
            for (int i = 0; i < left.Count; i++) pports.Add(Slot(left[i], PortSide.Left, new Float2(pos.X, pos.Y + (i + 0.5f) / left.Count * ph)));
            for (int i = 0; i < right.Count; i++) pports.Add(Slot(right[i], PortSide.Right, new Float2(pos.X + pw, pos.Y + (i + 0.5f) / right.Count * ph)));
            for (int i = 0; i < top.Count; i++) pports.Add(Slot(top[i], PortSide.Top, new Float2(pos.X + (i + 0.5f) / top.Count * pw, pos.Y)));
            for (int i = 0; i < bottom.Count; i++) pports.Add(Slot(bottom[i], PortSide.Bottom, new Float2(pos.X + (i + 0.5f) / bottom.Count * pw, pos.Y + ph)));
            return new NodeLayout { Node = n, Pos = pos, W = pw, H = ph, Ports = pports };
        }

        int rowsLR = Math.Max(left.Count, right.Count);
        float bodyH = rowsLR > 0
            ? BodyPadTop + rowsLR * _portRowH + BodyPadBottom
            : (top.Count > 0 || bottom.Count > 0 ? 22f : BodyPadBottom);
        float h = _headerH + bodyH;

        int topBot = Math.Max(top.Count, bottom.Count);
        float w = Math.Max(n.Width, topBot > 0 ? topBot * TopBotSpacing + 24f : 0f);

        var ports = new List<PortSlot>(left.Count + right.Count + top.Count + bottom.Count);
        for (int i = 0; i < left.Count; i++)
            ports.Add(Slot(left[i], PortSide.Left, new Float2(pos.X, pos.Y + _headerH + BodyPadTop + (i + 0.5f) * _portRowH)));
        for (int i = 0; i < right.Count; i++)
            ports.Add(Slot(right[i], PortSide.Right, new Float2(pos.X + w, pos.Y + _headerH + BodyPadTop + (i + 0.5f) * _portRowH)));
        for (int i = 0; i < top.Count; i++)
            ports.Add(Slot(top[i], PortSide.Top, new Float2(pos.X + (i + 0.5f) * (w / top.Count), pos.Y)));
        for (int i = 0; i < bottom.Count; i++)
            ports.Add(Slot(bottom[i], PortSide.Bottom, new Float2(pos.X + (i + 0.5f) * (w / bottom.Count), pos.Y + h)));

        return new NodeLayout { Node = n, Pos = pos, W = w, H = h, Ports = ports };
    }

    private static bool TryAnchor(NodeLayout l, string portId, bool output, out Float2 anchor)
    {
        foreach (var s in l.Ports)
            if (s.IsOutput == output && s.Port.Id == portId) { anchor = s.Anchor; return true; }
        anchor = default; return false;
    }

    // ═══════════════════════════════════════════════════════════════════
    //  Node rendering (Paper elements)
    // ═══════════════════════════════════════════════════════════════════

    private void DrawNode(NodeLayout l, GraphState st, Detail detail,
        Color nodeBg, Color borderSoft, Color titleCol, Color portLabelCol, Color accentDefault,
        FontFile? font, FontFile? semi)
    {
        var node = l.Node;
        Color accent = node.Accent ?? accentDefault;
        bool selected = st.SelNodes.Contains(node.Id);
        float zoom = st.Zoom;

        float sx = l.Pos.X * zoom + st.PanX;
        float sy = l.Pos.Y * zoom + st.PanY;
        float w = l.W * zoom, h = l.H * zoom;
        float headerH = _headerH * zoom;
        float rounding = _nodeRounding * zoom;

        if (node.Pill) { DrawPill(l, st, node, accent, selected, sx, sy, w, h, titleCol, semi); return; }

        var card = _paper.Column($"{_id}_n_{node.Id}")
            .PositionType(PositionType.SelfDirected).Left(sx).Top(sy)
            .Width(w).Height(h)
            .Rounded(rounding)
            .BackgroundColor(nodeBg)
            .BorderColor(selected ? accent : borderSoft).BorderWidth(selected ? 1.6f : 1f)
            .Clip()
            .Cursor(PaperCursor.Grab).CursorDragging(PaperCursor.Grabbing);
        WireNodeEvents(card, node, st);
        if (selected) card.Glow(0, 0, 14f, 1f, WithA(accent, 90));

        using (card.Enter())
        {
            // Header strip. At Block LOD the whole card is tinted and text is dropped.
            var header = _paper.Row($"{_id}_n_{node.Id}_h")
                .Width(UnitValue.Percentage(100)).Height(detail == Detail.Block ? h : headerH)
                .BackgroundColor(WithA(accent, detail == Detail.Block ? 70 : 40))
                .RoundedTop(rounding).IsNotInteractable();
            if (detail == Detail.Block) header.Rounded(rounding);

            using (header.Padding(10f * zoom, 10f * zoom, 0, 0).Enter())
            {
                if (node.Icon != null && detail != Detail.Block)
                {
                    var icon = node.Icon; float isz = 14f * zoom;
                    using (_paper.Box($"{_id}_n_{node.Id}_ico").Width(isz).Height(headerH).Margin(0, 7f * zoom, 0, 0).IsNotInteractable().Enter())
                        _paper.Draw((canvas, rr) =>
                        {
                            float ix = (float)(rr.Min.X + (rr.Size.X - isz) * 0.5f), iy = (float)(rr.Min.Y + (rr.Size.Y - isz) * 0.5f);
                            icon.Draw(canvas, new Rect(ix, iy, ix + isz, iy + isz), accent);
                        });
                }
                if (detail != Detail.Block && semi != null)
                    _paper.Box($"{_id}_n_{node.Id}_t").Width(UnitValue.Stretch()).Height(headerH)
                        .Text(node.Title, semi).FontSize(_titleFont * zoom)
                        .TextColor(titleCol).Alignment(TextAlignment.MiddleLeft).TextTruncate().IsNotInteractable();
            }

            // Body: input/output labels (only at Full LOD; Left/Right ports carry labels).
            if (detail == Detail.Full && font != null)
            {
                var lefts = l.Ports.Where(p => p.Side == PortSide.Left).ToList();
                var rights = l.Ports.Where(p => p.Side == PortSide.Right).ToList();
                int rows = Math.Max(lefts.Count, rights.Count);
                if (rows > 0)
                    using (_paper.Column($"{_id}_n_{node.Id}_body").Width(UnitValue.Percentage(100)).Height(UnitValue.Stretch())
                        .Padding(0, 0, BodyPadTop * zoom, BodyPadBottom * zoom).IsNotInteractable().Enter())
                        for (int i = 0; i < rows; i++)
                            using (_paper.Row($"{_id}_n_{node.Id}_r{i}").Width(UnitValue.Percentage(100)).Height(_portRowH * zoom).Enter())
                            {
                                _paper.Box($"{_id}_n_{node.Id}_r{i}_in").Width(UnitValue.Stretch()).Height(UnitValue.Percentage(100))
                                    .Margin(PortLabelPadX * zoom, 0, 0, 0)
                                    .Text(i < lefts.Count ? lefts[i].Port.Label : "", font).FontSize(_portFont * zoom)
                                    .TextColor(portLabelCol).Alignment(TextAlignment.MiddleLeft).TextTruncate();
                                _paper.Box($"{_id}_n_{node.Id}_r{i}_out").Width(UnitValue.Stretch()).Height(UnitValue.Percentage(100))
                                    .Margin(0, PortLabelPadX * zoom, 0, 0)
                                    .Text(i < rights.Count ? rights[i].Port.Label : "", font).FontSize(_portFont * zoom)
                                    .TextColor(portLabelCol).Alignment(TextAlignment.MiddleRight).TextTruncate();
                            }
            }
        }
    }

    // ── Pill node (relay / reroute): a small solid capsule, no text ──
    private void DrawPill(NodeLayout l, GraphState st, GraphNode node, Color accent, bool selected,
        float sx, float sy, float w, float h, Color titleCol, FontFile? semi)
    {
        var pill = _paper.Box($"{_id}_n_{node.Id}")
            .PositionType(PositionType.SelfDirected).Left(sx).Top(sy).Width(w).Height(h)
            .Rounded(h * 0.5f)
            .BackgroundColor(WithA(accent, 235))
            .BorderColor(selected ? _theme.Ink.C700 : WithA(accent, 255)).BorderWidth(selected ? 1.8f : 1f)
            .Cursor(PaperCursor.Grab).CursorDragging(PaperCursor.Grabbing);
        WireNodeEvents(pill, node, st);
        if (selected) pill.Glow(0, 0, 12f, 1f, WithA(accent, 110));
    }

    // Sticky rect after applying any live move/resize drag.
    private static (Float2 pos, Float2 size) StickyEffective(GraphSticky sk, GraphState st)
    {
        if (st.ActiveSticky == sk.Id)
        {
            if (st.Mode == DragMode.MoveSticky) return (sk.Position + st.DragOffset, sk.Size);
            if (st.Mode == DragMode.ResizeSticky) return (sk.Position, st.ResizeSize);
        }
        return (sk.Position, sk.Size);
    }

    // ── Sticky note: a movable / resizable / editable coloured note (drawn over wires, under nodes) ──
    private void DrawSticky(GraphSticky sk, GraphState st, FontFile? font)
    {
        float zoom = st.Zoom;
        var (pos, size) = StickyEffective(sk, st);
        Color fill = sk.Color ?? _theme.Amber.C400;
        Color textCol = Color.FromArgb(255, 46, 38, 16);
        bool selected = st.SelStickies.Contains(sk.Id);
        bool editing = st.EditingSticky == sk.Id;
        string sid = sk.Id;

        float sx = pos.X * zoom + st.PanX, sy = pos.Y * zoom + st.PanY;
        float w = size.X * zoom, h = size.Y * zoom;
        float rounding = 6f * zoom;

        var note = _paper.Column($"{_id}_sk_{sid}")
            .PositionType(PositionType.SelfDirected).Left(sx).Top(sy).Width(w).Height(h)
            .Rounded(rounding).Clip()
            .BackgroundColor(fill)
            .BorderColor(selected ? _theme.Ink.C700 : WithA(textCol, 55)).BorderWidth(selected ? 2f : 1f)
            .Cursor(PaperCursor.Grab).CursorDragging(PaperCursor.Grabbing)
            .Padding(10f * zoom, 10f * zoom, 8f * zoom, 8f * zoom)
            .DropShadow(0, 3f * zoom, 9f * zoom, 0, Color.FromArgb(90, 0, 0, 0));
        note.OnClick(st, (s, e) => ClickSelectSticky(s, sk));
        note.OnDragStart(st, (s, e) =>
        {
            if (s.RenamingGroup != null || (s.EditingSticky != null && s.EditingSticky != sid)) CommitTextEdit(s);
            if (!s.SelStickies.Contains(sid)) SelectOnly(s, s.SelStickies, sid);
            s.Mode = DragMode.MoveSticky; s.ActiveSticky = sid; s.DragOffset = Float2.Zero;
        });
        note.OnDragging(st, (s, e) => { if (s.Mode == DragMode.MoveSticky) s.DragOffset += new Float2(e.Delta.X / s.Zoom, e.Delta.Y / s.Zoom); });
        note.OnDragEnd(st, (s, e) =>
        {
            if (s.Mode == DragMode.MoveSticky && (s.DragOffset.X != 0 || s.DragOffset.Y != 0)) _onStickyMoved?.Invoke(sk, s.DragOffset);
            s.Mode = DragMode.None; s.ActiveSticky = null; s.DragOffset = Float2.Zero;
        });
        note.OnDoubleClick(st, (s, e) => { s.RenamingGroup = null; s.EditingSticky = sid; s.RenameBuffer = sk.Text; });
        note.OnRightClick(st, (s, e) =>
        {
            if (!s.SelStickies.Contains(sid)) SelectOnlySticky(s, sk);
            _onStickyContext?.Invoke(sk, ScreenToGraph(s, e.PointerPosition));
        });
        if (selected) note.Glow(0, 0, 12f, 1f, WithA(fill, 120));

        using (note.Enter())
        {
            if (font != null)
            {
                string body = editing ? st.RenameBuffer + (_paper.Pulse(1.1f) > 0.5f ? "|" : "") : sk.Text;
                _paper.Box($"{_id}_sk_{sid}_t").Width(UnitValue.Stretch()).Height(UnitValue.Stretch())
                    .Text(body, font).FontSize(Math.Max(8f, 11.5f * zoom))
                    .TextColor(textCol).Alignment(TextAlignment.Left).Wrap(TextWrapMode.Wrap).IsNotInteractable();
            }
        }

        // Resize grip (bottom-right corner).
        float hs = Math.Max(11f, 15f * zoom);
        var grip = _paper.Box($"{_id}_skz_{sid}")
            .PositionType(PositionType.SelfDirected).Left(sx + w - hs).Top(sy + h - hs).Width(hs).Height(hs)
            .Cursor(PaperCursor.ResizeNWSE);
        grip.OnDragStart(st, (s, e) => { s.Mode = DragMode.ResizeSticky; s.ActiveSticky = sid; s.ResizeSize = sk.Size; });
        grip.OnDragging(st, (s, e) =>
        {
            if (s.Mode == DragMode.ResizeSticky)
                s.ResizeSize = new Float2(Math.Max(110f, s.ResizeSize.X + e.Delta.X / s.Zoom), Math.Max(70f, s.ResizeSize.Y + e.Delta.Y / s.Zoom));
        });
        grip.OnDragEnd(st, (s, e) =>
        {
            if (s.Mode == DragMode.ResizeSticky) _onStickyResized?.Invoke(sk, sk.Position, s.ResizeSize);
            s.Mode = DragMode.None; s.ActiveSticky = null;
        });
        Color32 skGrip = ToC32(textCol, 0.5f);
        using (grip.Enter())
            _paper.Draw((canvas, rr) => PaintCornerGrip(canvas, rr, skGrip));
    }

    // ── Group box: frame (pass-through) + interactive title bar + resize handle ──
    private void DrawGroup(GraphGroup g, GraphState st, Color accentDefault, Color borderSoft, Color titleCol, FontFile? font, FontFile? semi)
    {
        float zoom = st.Zoom;
        var (gpos, gsize) = GroupEffective(g, st);
        Color accent = g.Color ?? accentDefault;
        bool selected = st.SelGroups.Contains(g.Id);
        bool renaming = st.RenamingGroup == g.Id;
        string gid = g.Id;

        float sx = gpos.X * zoom + st.PanX, sy = gpos.Y * zoom + st.PanY;
        float w = gsize.X * zoom, h = gsize.Y * zoom;
        float titleH = Math.Max(18f, 26f * zoom);
        float rounding = 8f * zoom;

        // Frame — non-interactive so nodes/wires/bg inside stay usable.
        _paper.Box($"{_id}_g_{gid}")
            .PositionType(PositionType.SelfDirected).Left(sx).Top(sy).Width(w).Height(h)
            .Rounded(rounding)
            .BackgroundColor(WithA(accent, 20))
            .BorderColor(selected ? accent : WithA(accent, 110)).BorderWidth(selected ? 2f : 1.4f)
            .IsNotInteractable();

        // Title bar — the group's drag/select/rename/context handle.
        var title = _paper.Row($"{_id}_gt_{gid}")
            .PositionType(PositionType.SelfDirected).Left(sx).Top(sy).Width(w).Height(titleH)
            .RoundedTop(rounding).Padding(9f * zoom, 9f * zoom, 0, 0)
            .BackgroundColor(WithA(accent, selected ? 85 : 50))
            .Cursor(PaperCursor.Grab).CursorDragging(PaperCursor.Grabbing);
        title.OnClick(st, (s, e) => ClickSelectGroup(s, g));
        title.OnDragStart(st, (s, e) =>
        {
            if (s.RenamingGroup != null || s.EditingSticky != null) CommitTextEdit(s);
            if (!s.SelGroups.Contains(gid)) SelectOnlyGroup(s, g);
            s.Mode = DragMode.MoveGroup; s.ActiveGroup = gid; s.DragOffset = Float2.Zero;
        });
        title.OnDragging(st, (s, e) => { if (s.Mode == DragMode.MoveGroup) s.DragOffset += new Float2(e.Delta.X / s.Zoom, e.Delta.Y / s.Zoom); });
        title.OnDragEnd(st, (s, e) =>
        {
            if (s.Mode == DragMode.MoveGroup && (s.DragOffset.X != 0 || s.DragOffset.Y != 0))
                _onGroupMoved?.Invoke(g, MembersOf(g), s.DragOffset);
            s.Mode = DragMode.None; s.ActiveGroup = null; s.DragOffset = Float2.Zero;
        });
        title.OnDoubleClick(st, (s, e) => { s.RenamingGroup = gid; s.RenameBuffer = g.Title; });
        title.OnRightClick(st, (s, e) =>
        {
            if (!s.SelGroups.Contains(gid)) SelectOnlyGroup(s, g);
            _onGroupContext?.Invoke(g, ScreenToGraph(s, e.PointerPosition));
        });

        using (title.Enter())
        {
            if (semi != null)
            {
                string text = renaming ? st.RenameBuffer + (_paper.Pulse(1.1f) > 0.5f ? "|" : "") : g.Title;
                _paper.Box($"{_id}_gtt_{gid}").Width(UnitValue.Stretch()).Height(UnitValue.Percentage(100))
                    .Text(text, semi).FontSize(_titleFont * zoom)
                    .TextColor(renaming ? _theme.Ink.C700 : titleCol).Alignment(TextAlignment.MiddleLeft)
                    .TextTruncate().IsNotInteractable();
            }
        }

        // Resize handle (bottom-right corner).
        float hs = Math.Max(11f, 15f * zoom);
        var grip = _paper.Box($"{_id}_grz_{gid}")
            .PositionType(PositionType.SelfDirected).Left(sx + w - hs).Top(sy + h - hs).Width(hs).Height(hs)
            .Cursor(PaperCursor.ResizeNWSE);
        grip.OnDragStart(st, (s, e) => { s.Mode = DragMode.ResizeGroup; s.ActiveGroup = gid; s.ResizeSize = g.Size; });
        grip.OnDragging(st, (s, e) =>
        {
            if (s.Mode == DragMode.ResizeGroup)
                s.ResizeSize = new Float2(Math.Max(140f, s.ResizeSize.X + e.Delta.X / s.Zoom), Math.Max(90f, s.ResizeSize.Y + e.Delta.Y / s.Zoom));
        });
        grip.OnDragEnd(st, (s, e) =>
        {
            if (s.Mode == DragMode.ResizeGroup) _onGroupResized?.Invoke(g, g.Position, s.ResizeSize);
            s.Mode = DragMode.None; s.ActiveGroup = null;
        });
        Color32 gGrip = ToC32(WithA(accent, 150), 1f);
        using (grip.Enter())
            _paper.Draw((canvas, rr) => PaintCornerGrip(canvas, rr, gGrip));
    }

    // A small angle glyph in the bottom-right of a resize handle.
    private static void PaintCornerGrip(Canvas canvas, Rect rr, Color32 col)
    {
        float x2 = (float)rr.Max.X - 2f, y2 = (float)rr.Max.Y - 2f, e2 = (float)Math.Min(rr.Size.X, rr.Size.Y) - 3f;
        canvas.SaveState(); canvas.SetStrokeColor(col); canvas.SetStrokeWidth(1.4f);
        canvas.BeginPath(); canvas.MoveTo(x2 - e2, y2); canvas.LineTo(x2, y2); canvas.LineTo(x2, y2 - e2); canvas.Stroke();
        canvas.RestoreState();
    }

    // Interactive socket per port (drag-source & drop-target), drawn on top of the node.
    private void DrawPorts(NodeLayout l, GraphState st, Snapshot snap)
    {
        float zoom = st.Zoom, ox = st.ScreenX, oy = st.ScreenY;
        float hit = Math.Max(9f, PortHitR * zoom);
        float dotR = _portDotR * zoom;
        foreach (var slot in l.Ports)
        {
            var s = slot;
            // container-local position: anchor is graph space; local = graph*zoom + pan.
            float lx = s.Anchor.X * zoom + st.PanX;
            float ly = s.Anchor.Y * zoom + st.PanY;

            string nodeId = l.Node.Id;
            var pb = _paper.Box($"{_id}_p_{nodeId}_{(s.IsOutput ? "o" : "i")}_{s.Port.Id}")
                .PositionType(PositionType.SelfDirected).Left(lx - hit).Top(ly - hit)
                .Width(hit * 2).Height(hit * 2).Cursor(PaperCursor.Crosshair);
            pb.OnDragStart(st, (state, e) =>
            {
                state.Mode = DragMode.Connect;
                state.ConnNode = nodeId; state.ConnPort = s.Port.Id; state.ConnFromOutput = s.IsOutput;
            });
            pb.OnDragEnd(st, (state, e) => EndConnect(state, snap.Layouts));

            bool hov = _paper.IsElementHovered(pb._handle.Data.ID);
            Color pc = s.Port.Color ?? snap.Accent;
            PortShape shape = s.Port.Shape;
            using (pb.Enter())
                _paper.Draw((canvas, rr) =>
                {
                    float cx = (float)(rr.Min.X + rr.Size.X * 0.5f), cy = (float)(rr.Min.Y + rr.Size.Y * 0.5f);
                    float r = dotR;
                    var fill = ToC32(pc, 1f);
                    if (hov) canvas.CircleFilled(cx, cy, r + 3.5f, ToC32(pc, 0.35f));
                    if (shape == PortShape.Arrow) PaintArrow(canvas, cx, cy, r + 0.5f, s.Side, fill, snap.DotRing);
                    else { canvas.CircleFilled(cx, cy, r + 1.5f, snap.DotRing); canvas.CircleFilled(cx, cy, r, fill); }
                });
        }
    }

    // A draggable dot per wire control point; right-click removes it.
    private void DrawControlPoints(GraphConnection c, GraphState st, Color accentDefault)
    {
        var cps = c.ControlPoints;
        if (cps.Count == 0) return;
        float zoom = st.Zoom, hit = Math.Max(7f, 6.5f * zoom);
        string key = EdgeKey(c);
        Color32 fill = ToC32(c.Color ?? accentDefault, 1f), ring = ToC32(_theme.Popover, 1f);

        for (int i = 0; i < cps.Count; i++)
        {
            int idx = i;
            Float2 gp = (st.Mode == DragMode.MovePoint && st.ActiveWire == key && st.ActivePoint == i) ? cps[i] + st.PointOffset : cps[i];
            float lx = gp.X * zoom + st.PanX, ly = gp.Y * zoom + st.PanY;

            var box = _paper.Box($"{_id}_cp_{key}_{idx}")
                .PositionType(PositionType.SelfDirected).Left(lx - hit).Top(ly - hit).Width(hit * 2).Height(hit * 2)
                .Cursor(PaperCursor.Grab).CursorDragging(PaperCursor.Grabbing);
            box.OnDragStart(st, (s, e) => { s.Mode = DragMode.MovePoint; s.ActiveWire = key; s.ActivePoint = idx; s.PointOffset = Float2.Zero; });
            box.OnDragging(st, (s, e) => { if (s.Mode == DragMode.MovePoint) s.PointOffset += new Float2(e.Delta.X / s.Zoom, e.Delta.Y / s.Zoom); });
            box.OnDragEnd(st, (s, e) =>
            {
                if (s.Mode == DragMode.MovePoint && (s.PointOffset.X != 0 || s.PointOffset.Y != 0) && idx < c.ControlPoints.Count)
                    _onWirePointMoved?.Invoke(c, idx, c.ControlPoints[idx] + s.PointOffset);
                s.Mode = DragMode.None; s.ActiveWire = null;
            });
            box.OnRightClick(st, (s, e) => _onWireRemovePoint?.Invoke(c, idx));

            bool hov = _paper.IsElementHovered(box._handle.Data.ID);
            using (box.Enter())
                _paper.Draw((canvas, rr) =>
                {
                    float cx = (float)(rr.Min.X + rr.Size.X * 0.5f), cy = (float)(rr.Min.Y + rr.Size.Y * 0.5f);
                    float r = hov ? 5.5f : 4.5f;
                    canvas.CircleFilled(cx, cy, r + 2f, ring);
                    canvas.CircleFilled(cx, cy, r, fill);
                });
        }
    }

    // ═══════════════════════════════════════════════════════════════════
    //  Interaction wiring
    // ═══════════════════════════════════════════════════════════════════

    private void WireNodeEvents(ElementBuilder card, GraphNode node, GraphState st)
    {
        card.OnClick(st, (state, e) => ClickSelectNode(state, node));
        card.OnDragStart(st, (state, e) =>
        {
            if (!state.SelNodes.Contains(node.Id)) SelectOnlyNode(state, node);
            state.Mode = DragMode.MoveNodes;
            state.DragOffset = Float2.Zero;
        });
        card.OnDragging(st, (state, e) =>
        {
            if (state.Mode == DragMode.MoveNodes)
                state.DragOffset += new Float2(e.Delta.X / state.Zoom, e.Delta.Y / state.Zoom);
        });
        card.OnDragEnd(st, (state, e) =>
        {
            if (state.Mode == DragMode.MoveNodes && (state.DragOffset.X != 0 || state.DragOffset.Y != 0))
            {
                var moved = _nodes.Where(n => state.SelNodes.Contains(n.Id)).ToList();
                _onNodesMoved?.Invoke(moved, state.DragOffset);
            }
            state.Mode = DragMode.None; state.DragOffset = Float2.Zero;
        });
        card.OnRightClick(st, (state, e) =>
        {
            // Keep an existing multi-selection if the clicked node is part of it; else select just this one.
            if (!state.SelNodes.Contains(node.Id)) SelectOnlyNode(state, node);
            var pos = ScreenToGraph(state, e.PointerPosition);
            if (state.SelNodes.Count > 1 && _onNodesContext != null)
                _onNodesContext(_nodes.Where(n => state.SelNodes.Contains(n.Id)).ToList(), pos);
            else
                _onNodeContext?.Invoke(node, pos);
        });
        card.OnDoubleClick(st, (state, e) => _onNodeDoubleClick?.Invoke(node));
    }

    private void WireBackgroundEvents(ElementBuilder bg, GraphState st, Dictionary<string, GraphNode> byId, Dictionary<string, NodeLayout> layouts)
    {
        bg.OnDragStart(st, (state, e) =>
        {
            state.Mode = DragMode.Marquee;
            state.MarqueeStart = ScreenToGraph(state, e.PointerPosition);
        });
        bg.OnDragEnd(st, (state, e) =>
        {
            if (state.Mode == DragMode.Marquee)
            {
                Float2 end = ScreenToGraph(state, e.PointerPosition);
                MarqueeSelect(state, layouts, state.MarqueeStart, end, Additive());
            }
            state.Mode = DragMode.None;
        });
        bg.OnClick(st, (state, e) => BackgroundClick(state, layouts, ScreenToGraph(state, e.PointerPosition), Additive()));
        bg.OnRightClick(st, (state, e) =>
        {
            Float2 gp = ScreenToGraph(state, e.PointerPosition);
            // Right-click on a wire drops a reroute point there; on empty canvas opens the create menu.
            if (_onWireAddPoint != null && HitTestWire(state, layouts, gp, out var wire, out int seg))
                _onWireAddPoint(wire!, seg, gp);
            else
                _onBackgroundContext?.Invoke(gp);
        });
    }

    private bool HitTestWire(GraphState st, Dictionary<string, NodeLayout> layouts, Float2 graphPos, out GraphConnection? wire, out int seg)
    {
        wire = null; seg = 0;
        float best = WireHitDist / Math.Max(st.Zoom, 0.001f);
        foreach (var c in _connections)
        {
            if (!layouts.TryGetValue(c.FromNode, out var lf) || !layouts.TryGetValue(c.ToNode, out var lt)) continue;
            if (!TryAnchor(lf, c.FromPort, true, out var a) || !TryAnchor(lt, c.ToPort, false, out var b)) continue;
            float d = WireDistGraph(a, EffectiveCPs(c, st), b, DirOf(lf, c.FromPort, true), DirOf(lt, c.ToPort, false), graphPos, out int s);
            if (d < best) { best = d; wire = c; seg = s; }
        }
        return wire != null;
    }

    private void EndConnect(GraphState state, Dictionary<string, NodeLayout> layouts)
    {
        if (state.Mode != DragMode.Connect || state.ConnNode == null || state.ConnPort == null) { state.Mode = DragMode.None; return; }
        var (hitNode, hitPort, hitOut) = HitTestPort(state, _paper.PointerPos, layouts);
        if (hitNode != null && hitOut != state.ConnFromOutput && hitNode != state.ConnNode)
        {
            var req = state.ConnFromOutput
                ? new ConnectionRequest(state.ConnNode, state.ConnPort, hitNode, hitPort!)
                : new ConnectionRequest(hitNode, hitPort!, state.ConnNode, state.ConnPort);
            if (_onValidate == null || _onValidate(req)) _onConnect?.Invoke(req); // validator is authoritative
        }
        else if (hitNode == null)
        {
            _onDropWireEmpty?.Invoke(ScreenToGraph(state, _paper.PointerPos), state.ConnNode, state.ConnPort, state.ConnFromOutput);
        }
        state.Mode = DragMode.None; state.ConnNode = null; state.ConnPort = null;
    }

    // Nearest port to a screen point, reusing this frame's resolved layouts (no rebuild).
    private (string? node, string? port, bool output) HitTestPort(GraphState st, Float2 screen, Dictionary<string, NodeLayout> layouts)
    {
        float zoom = st.Zoom, best = Math.Max(11f, PortHitR * zoom + 3f);
        string? bn = null, bp = null; bool bo = false;
        foreach (var l in layouts.Values)
            foreach (var s in l.Ports)
            {
                float px = st.ScreenX + s.Anchor.X * zoom + st.PanX;
                float py = st.ScreenY + s.Anchor.Y * zoom + st.PanY;
                float d = Dist(px, py, (float)screen.X, (float)screen.Y);
                if (d < best) { best = d; bn = l.Node.Id; bp = s.Port.Id; bo = s.IsOutput; }
            }
        return (bn, bp, bo);
    }

    private void HandleKeyboard(GraphState st, Dictionary<string, GraphNode> byId)
    {
        if (_paper.IsKeyPressed(PaperKey.Delete) || _paper.IsKeyPressed(PaperKey.Backspace))
        {
            if (!SelectionEmpty(st)) _onDelete?.Invoke(BuildSelection(st));
        }
        else if (_paper.IsKeyPressed(PaperKey.Escape))
        {
            st.Mode = DragMode.None; st.ConnNode = null; st.RenamingGroup = null; st.EditingSticky = null;
            if (!SelectionEmpty(st)) { ClearSelection(st); FireSelection(st); }
        }
        else if (Ctrl() && _paper.IsKeyPressed(PaperKey.A))
        {
            ClearSelection(st);
            foreach (var n in _nodes) st.SelNodes.Add(n.Id);
            FireSelection(st);
        }
    }

    private static bool SelectionEmpty(GraphState st) => st.SelNodes.Count == 0 && st.SelEdges.Count == 0 && st.SelGroups.Count == 0 && st.SelStickies.Count == 0;
    private static void ClearSelection(GraphState st) { st.SelNodes.Clear(); st.SelEdges.Clear(); st.SelGroups.Clear(); st.SelStickies.Clear(); }

    // Self-contained inline text editor (avoids TextField focus plumbing): drains typed chars while active.
    // Serves both group-title rename (single line, Enter commits) and sticky text (multi-line, Enter = newline).
    private void HandleTextEdit(GraphState st)
    {
        bool sticky = st.EditingSticky != null;
        while (_paper.InputString.Count > 0)
        {
            char ch = _paper.InputString.Dequeue();
            if (!char.IsControl(ch)) st.RenameBuffer += ch;
        }
        if (_paper.IsKeyPressedOrRepeating(PaperKey.Backspace) && st.RenameBuffer.Length > 0)
            st.RenameBuffer = st.RenameBuffer.Substring(0, st.RenameBuffer.Length - 1);
        if (_paper.IsKeyPressed(PaperKey.Enter))
        {
            if (sticky) st.RenameBuffer += '\n';
            else CommitTextEdit(st);
        }
        else if (_paper.IsKeyPressed(PaperKey.Escape)) CommitTextEdit(st);
    }

    private void CommitTextEdit(GraphState st)
    {
        if (st.RenamingGroup != null)
        {
            var g = _groups.FirstOrDefault(x => x.Id == st.RenamingGroup);
            string t = st.RenameBuffer.Trim();
            if (g != null && t.Length > 0 && t != g.Title) _onGroupRenamed?.Invoke(g, t);
        }
        else if (st.EditingSticky != null)
        {
            var sk = _stickies.FirstOrDefault(x => x.Id == st.EditingSticky);
            if (sk != null && st.RenameBuffer != sk.Text) _onStickyEdited?.Invoke(sk, st.RenameBuffer);
        }
        st.RenamingGroup = null; st.EditingSticky = null;
    }

    // ── selection helpers ──
    private bool Additive() => Shift() || Ctrl();
    private bool Shift() => _paper.IsKeyDown(PaperKey.LeftShift) || _paper.IsKeyDown(PaperKey.RightShift);
    private bool Ctrl() => _paper.IsKeyDown(PaperKey.LeftControl) || _paper.IsKeyDown(PaperKey.RightControl);
    private void MaybeCommitEdit(GraphState st) { if (st.RenamingGroup != null || st.EditingSticky != null) CommitTextEdit(st); }

    // Additive (Shift/Ctrl) toggles the id in its set; otherwise replaces the whole selection.
    private void ClickSelect(GraphState st, HashSet<string> set, string id)
    {
        MaybeCommitEdit(st);
        if (Additive()) { if (!set.Remove(id)) set.Add(id); }
        else { ClearSelection(st); set.Add(id); }
        FireSelection(st);
    }

    private void SelectOnly(GraphState st, HashSet<string> set, string id) { ClearSelection(st); set.Add(id); FireSelection(st); }

    private void SelectOnlyNode(GraphState st, GraphNode n) => SelectOnly(st, st.SelNodes, n.Id);
    private void SelectOnlyGroup(GraphState st, GraphGroup g) => SelectOnly(st, st.SelGroups, g.Id);
    private void SelectOnlySticky(GraphState st, GraphSticky sk) => SelectOnly(st, st.SelStickies, sk.Id);
    private void ClickSelectNode(GraphState st, GraphNode n) => ClickSelect(st, st.SelNodes, n.Id);
    private void ClickSelectGroup(GraphState st, GraphGroup g) => ClickSelect(st, st.SelGroups, g.Id);
    private void ClickSelectSticky(GraphState st, GraphSticky sk) => ClickSelect(st, st.SelStickies, sk.Id);

    private void BackgroundClick(GraphState st, Dictionary<string, NodeLayout> layouts, Float2 graphPos, bool additive)
    {
        MaybeCommitEdit(st);
        // Wire hit-test: nearest wire (through its control points) within threshold.
        string? hitKey = null; float best = WireHitDist / Math.Max(st.Zoom, 0.001f);
        foreach (var c in _connections)
        {
            if (!layouts.TryGetValue(c.FromNode, out var lf) || !layouts.TryGetValue(c.ToNode, out var lt)) continue;
            if (!TryAnchor(lf, c.FromPort, true, out var a) || !TryAnchor(lt, c.ToPort, false, out var b)) continue;
            float d = WireDistGraph(a, EffectiveCPs(c, st), b, DirOf(lf, c.FromPort, true), DirOf(lt, c.ToPort, false), graphPos, out _);
            if (d < best) { best = d; hitKey = EdgeKey(c); }
        }

        if (hitKey != null)
        {
            if (additive) { if (!st.SelEdges.Remove(hitKey)) st.SelEdges.Add(hitKey); }
            else { ClearSelection(st); st.SelEdges.Add(hitKey); }
            FireSelection(st);
        }
        else if (!additive && !SelectionEmpty(st))
        {
            ClearSelection(st); FireSelection(st);
        }
    }

    private void MarqueeSelect(GraphState st, Dictionary<string, NodeLayout> layouts, Float2 a, Float2 b, bool additive)
    {
        float minX = Math.Min(a.X, b.X), maxX = Math.Max(a.X, b.X);
        float minY = Math.Min(a.Y, b.Y), maxY = Math.Max(a.Y, b.Y);
        if (!additive) ClearSelection(st);
        foreach (var kv in layouts)
        {
            var l = kv.Value;
            bool inside = l.Pos.X < maxX && l.Pos.X + l.W > minX && l.Pos.Y < maxY && l.Pos.Y + l.H > minY;
            if (inside) st.SelNodes.Add(l.Node.Id);
        }
        // Wires: selected when the routed path passes through the marquee rect.
        foreach (var c in _connections)
        {
            if (!layouts.TryGetValue(c.FromNode, out var lf) || !layouts.TryGetValue(c.ToNode, out var lt)) continue;
            if (!TryAnchor(lf, c.FromPort, true, out var ga) || !TryAnchor(lt, c.ToPort, false, out var gb)) continue;
            foreach (var (p, _) in SampleWireGraph(ga, EffectiveCPs(c, st), gb, DirOf(lf, c.FromPort, true), DirOf(lt, c.ToPort, false)))
                if (p.X >= minX && p.X <= maxX && p.Y >= minY && p.Y <= maxY) { st.SelEdges.Add(EdgeKey(c)); break; }
        }
        FireSelection(st);
    }

    private GraphSelection BuildSelection(GraphState st) => new(
        _nodes.Where(n => st.SelNodes.Contains(n.Id)).ToList(),
        _connections.Where(c => st.SelEdges.Contains(EdgeKey(c))).ToList(),
        _groups.Where(g => st.SelGroups.Contains(g.Id)).ToList(),
        _stickies.Where(s => st.SelStickies.Contains(s.Id)).ToList());

    private void FireSelection(GraphState st) => _onSelectionChanged?.Invoke(BuildSelection(st));

    private static Float2 ScreenToGraphS(GraphState st, float sx, float sy)
        => new Float2((sx - st.ScreenX - st.PanX) / st.Zoom, (sy - st.ScreenY - st.PanY) / st.Zoom);
    private Float2 ScreenToGraph(GraphState st, Float2 screen) => ScreenToGraphS(st, (float)screen.X, (float)screen.Y);

    // ═══════════════════════════════════════════════════════════════════
    //  Canvas passes
    // ═══════════════════════════════════════════════════════════════════

    private struct Snapshot
    {
        public GraphState St;
        public float Zoom, PanX, PanY;
        public IReadOnlyList<GraphNode> Nodes;
        public IReadOnlyList<GraphConnection> Connections;
        public Dictionary<string, NodeLayout> Layouts;
        public Dictionary<string, GraphNode> ById;
        public bool ShowGrid;
        public float GridSpacing, WireThick;
        public Color32 GridMinor, GridMajor, WireDefault, WireSelected, DotRing, DotFill, Marquee, MarqueeFill;
        public Color Accent;
    }

    private void PaintGridPass(Canvas canvas, Rect rect, in Snapshot s)
    {
        if (!s.ShowGrid) return;
        PaintGrid(canvas, (float)rect.Min.X, (float)rect.Min.Y, (float)rect.Size.X, (float)rect.Size.Y, in s);
    }

    private void PaintWiresPass(Canvas canvas, Rect rect, in Snapshot s)
    {
        float ox = (float)rect.Min.X, oy = (float)rect.Min.Y;
        foreach (var c in s.Connections)
        {
            if (!s.Layouts.TryGetValue(c.FromNode, out var lf) || !s.Layouts.TryGetValue(c.ToNode, out var lt)) continue;
            if (!TryAnchor(lf, c.FromPort, true, out var ga) || !TryAnchor(lt, c.ToPort, false, out var gb)) continue;
            Float2 a = ToScreen(ga, ox, oy, in s), b = ToScreen(gb, ox, oy, in s);
            bool sel = s.St.SelEdges.Contains(EdgeKey(c));
            int da = DirOf(lf, c.FromPort, true), db = DirOf(lt, c.ToPort, false);
            Color32 col = sel ? s.WireSelected : (c.Color.HasValue ? ToC32(c.Color.Value, 0.85f) : s.WireDefault);

            float wt = s.WireThick, wSel = s.WireThick * 1.75f, wGlow = s.WireThick * 3.5f;
            var cps = EffectiveCPs(c, s.St);
            if (cps.Count == 0)
            {
                if (sel) PaintWire(canvas, a, b, da, db, ToC32(s.Accent, 0.28f), wGlow);
                PaintWire(canvas, a, b, da, db, col, sel ? wSel : wt);
            }
            else
            {
                var samples = SampleWireGraph(ga, cps, gb, da, db);
                var pts = new List<Float2>(samples.Count);
                foreach (var t in samples) pts.Add(ToScreen(t.p, ox, oy, in s));
                if (sel) PaintWirePath(canvas, pts, ToC32(s.Accent, 0.28f), wGlow);
                PaintWirePath(canvas, pts, col, sel ? wSel : wt);
            }
        }
    }

    private void PaintForeground(Canvas canvas, Rect rect, in Snapshot s)
    {
        float ox = (float)rect.Min.X, oy = (float)rect.Min.Y;
        var st = s.St;

        if (st.Mode == DragMode.Marquee)
        {
            Float2 a = ToScreen(st.MarqueeStart, ox, oy, in s);
            float cx = (float)_paper.PointerPos.X, cy = (float)_paper.PointerPos.Y;
            float x = Math.Min(a.X, cx), y = Math.Min(a.Y, cy), mw = Math.Abs(cx - a.X), mh = Math.Abs(cy - a.Y);
            canvas.RectFilled(x, y, mw, mh, s.MarqueeFill);
            canvas.SaveState(); canvas.SetStrokeColor(s.Marquee); canvas.SetStrokeWidth(1f);
            canvas.BeginPath(); canvas.Rect(x, y, mw, mh); canvas.Stroke(); canvas.RestoreState();
        }
        else if (st.Mode == DragMode.Connect && st.ConnNode != null && st.ConnPort != null
                 && s.Layouts.TryGetValue(st.ConnNode, out var ln)
                 && TryAnchor(ln, st.ConnPort, st.ConnFromOutput, out var ga))
        {
            Float2 a = ToScreen(ga, ox, oy, in s);
            Float2 b = new((float)_paper.PointerPos.X, (float)_paper.PointerPos.Y);
            var (tn, tp, to) = HitTestPort(st, _paper.PointerPos, s.Layouts);
            bool valid = tn != null && to != st.ConnFromOutput && tn != st.ConnNode && ValidateHit(st, tn, tp!, to);
            Color32 col = tn == null ? ToC32(s.Accent, 0.8f) : (valid ? ToC32(_theme.Green.C500, 1f) : ToC32(_theme.Red.C500, 1f));
            PortSide srcSide = SideOf(ln, st.ConnPort, st.ConnFromOutput);
            PaintWire(canvas, a, b, DirForSide(srcSide), st.ConnFromOutput ? -1 : 1, col, 2.5f);
            canvas.CircleFilled(b.X, b.Y, 4f, col);
        }
    }

    private bool ValidateHit(GraphState st, string node, string port, bool output)
    {
        if (_onValidate == null) return true;
        var req = st.ConnFromOutput
            ? new ConnectionRequest(st.ConnNode!, st.ConnPort!, node, port)
            : new ConnectionRequest(node, port, st.ConnNode!, st.ConnPort!);
        return _onValidate(req);
    }

    private static PortSide SideOf(NodeLayout l, string portId, bool output)
    {
        foreach (var s in l.Ports) if (s.IsOutput == output && s.Port.Id == portId) return s.Side;
        return output ? PortSide.Right : PortSide.Left;
    }
    private static int DirOf(NodeLayout l, string portId, bool output) => DirForSide(SideOf(l, portId, output));
    // Tangent direction (x-component sign) for a side, used to shape the bezier handle.
    private static int DirForSide(PortSide side) => side switch { PortSide.Left => -1, PortSide.Right => 1, _ => 0 };

    private static void PaintGrid(Canvas canvas, float ox, float oy, float w, float h, in Snapshot s)
    {
        float spacing = s.GridSpacing * s.Zoom;
        if (spacing < 7f) return;
        canvas.SaveState(); canvas.SetStrokeWidth(1f);
        float startX = ox + Mod(s.PanX, spacing); int col = (int)MathF.Floor(-s.PanX / spacing) - 1;
        for (float x = startX - spacing; x < ox + w + spacing; x += spacing, col++)
        { canvas.SetStrokeColor(col % 5 == 0 ? s.GridMajor : s.GridMinor); canvas.BeginPath(); canvas.MoveTo(x, oy); canvas.LineTo(x, oy + h); canvas.Stroke(); }
        float startY = oy + Mod(s.PanY, spacing); int row = (int)MathF.Floor(-s.PanY / spacing) - 1;
        for (float y = startY - spacing; y < oy + h + spacing; y += spacing, row++)
        { canvas.SetStrokeColor(row % 5 == 0 ? s.GridMajor : s.GridMinor); canvas.BeginPath(); canvas.MoveTo(ox, y); canvas.LineTo(ox + w, y); canvas.Stroke(); }
        canvas.RestoreState();
    }

    // Bezier handles that leave each endpoint along its port's axis (horizontal or vertical).
    private static (Float2 c1, Float2 c2) BezierHandles(Float2 a, Float2 b, int dirA, int dirB)
    {
        if (dirA == 0 || dirB == 0)
        {
            float k = Math.Clamp(MathF.Abs(b.Y - a.Y) * 0.5f, 24f, 160f);
            return (new Float2(a.X, a.Y + (b.Y > a.Y ? k : -k)), new Float2(b.X, b.Y + (b.Y > a.Y ? -k : k)));
        }
        float kk = Math.Clamp(MathF.Abs(b.X - a.X) * 0.5f, 24f, 160f);
        return (new Float2(a.X + dirA * kk, a.Y), new Float2(b.X + dirB * kk, b.Y));
    }

    // Direct (no control point) wire as one smooth cubic bezier.
    private static void PaintWire(Canvas canvas, Float2 a, Float2 b, int dirA, int dirB, Color32 color, float width)
    {
        var (c1, c2) = BezierHandles(a, b, dirA, dirB);
        canvas.SaveState();
        canvas.SetStrokeColor(color); canvas.SetStrokeWidth(width); canvas.SetStrokeCap(EndCapStyle.Round);
        canvas.BeginPath(); canvas.MoveTo(a.X, a.Y); canvas.BezierCurveTo(c1.X, c1.Y, c2.X, c2.Y, b.X, b.Y); canvas.Stroke();
        canvas.RestoreState();
    }

    // Stroke a pre-sampled screen-space polyline (used for wires routed through control points).
    private static void PaintWirePath(Canvas canvas, List<Float2> pts, Color32 color, float width)
    {
        if (pts.Count < 2) return;
        canvas.SaveState();
        canvas.SetStrokeColor(color); canvas.SetStrokeWidth(width); canvas.SetStrokeCap(EndCapStyle.Round); canvas.SetStrokeJoint(JointStyle.Round);
        canvas.BeginPath(); canvas.MoveTo(pts[0].X, pts[0].Y);
        for (int i = 1; i < pts.Count; i++) canvas.LineTo(pts[i].X, pts[i].Y);
        canvas.Stroke();
        canvas.RestoreState();
    }

    // Sample the wire path in graph space, tagging each sample with the segment index it belongs to
    // (segment k is the span between control point k-1 and k, so a click on segment k inserts at index k).
    private static List<(Float2 p, int seg)> SampleWireGraph(Float2 a, IReadOnlyList<Float2> cps, Float2 b, int dirA, int dirB)
    {
        var outp = new List<(Float2, int)>();
        if (cps.Count == 0)
        {
            var (c1, c2) = BezierHandles(a, b, dirA, dirB);
            for (int i = 0; i <= 20; i++) outp.Add((Bezier(a, c1, c2, b, i / 20f), 0));
            return outp;
        }
        var f = new List<Float2>(cps.Count + 2) { a };
        f.AddRange(cps); f.Add(b);
        for (int k = 0; k < f.Count - 1; k++)
        {
            Float2 p0 = f[Math.Max(0, k - 1)], p1 = f[k], p2 = f[k + 1], p3 = f[Math.Min(f.Count - 1, k + 2)];
            Float2 cc1 = p1 + (p2 - p0) * (1f / 6f), cc2 = p2 - (p3 - p1) * (1f / 6f);
            for (int i = (k == 0 ? 0 : 1); i <= 10; i++) outp.Add((Bezier(p1, cc1, cc2, p2, i / 10f), k));
        }
        return outp;
    }

    private static List<Float2> EffectiveCPs(GraphConnection c, GraphState st)
    {
        var cps = c.ControlPoints;
        if (st.Mode == DragMode.MovePoint && st.ActiveWire == EdgeKey(c) && st.ActivePoint >= 0 && st.ActivePoint < cps.Count)
        {
            var list = new List<Float2>(cps);
            list[st.ActivePoint] = list[st.ActivePoint] + st.PointOffset;
            return list;
        }
        return cps;
    }

    // Nearest distance (graph space) from p to the wire, and the segment index of the closest point.
    private static float WireDistGraph(Float2 a, IReadOnlyList<Float2> cps, Float2 b, int dirA, int dirB, Float2 p, out int seg)
    {
        var samples = SampleWireGraph(a, cps, b, dirA, dirB);
        float best = float.MaxValue; seg = 0;
        for (int i = 1; i < samples.Count; i++)
        {
            float d = DistToSeg(p, samples[i - 1].p, samples[i].p);
            if (d < best) { best = d; seg = samples[i].seg; }
        }
        return best;
    }

    private static void PaintArrow(Canvas canvas, float cx, float cy, float r, PortSide side, Color32 fill, Color32 ring)
    {
        canvas.CircleFilled(cx, cy, r + 1.5f, ring); // halo so it reads on any background
        float s = r * 1.15f;
        (float dx, float dy) = side switch
        {
            PortSide.Left => (1f, 0f),
            PortSide.Right => (1f, 0f),
            PortSide.Top => (0f, 1f),
            _ => (0f, 1f),
        };
        canvas.SaveState(); canvas.SetFillColor(fill); canvas.BeginPath();
        if (dx != 0) { canvas.MoveTo(cx - s, cy - s); canvas.LineTo(cx + s, cy); canvas.LineTo(cx - s, cy + s); }
        else { canvas.MoveTo(cx - s, cy - s); canvas.LineTo(cx, cy + s); canvas.LineTo(cx + s, cy - s); }
        canvas.ClosePath(); canvas.Fill(); canvas.RestoreState();
    }

    // ═══════════════════════════════════════════════════════════════════
    //  Geometry / color helpers
    // ═══════════════════════════════════════════════════════════════════

    private static Float2 ToScreen(Float2 graph, float ox, float oy, in Snapshot s)
        => new Float2(ox + graph.X * s.Zoom + s.PanX, oy + graph.Y * s.Zoom + s.PanY);

    private static float Dist(float ax, float ay, float bx, float by) { float dx = ax - bx, dy = ay - by; return MathF.Sqrt(dx * dx + dy * dy); }

    private static Float2 Bezier(Float2 a, Float2 c1, Float2 c2, Float2 b, float t)
    {
        float u = 1 - t;
        float w0 = u * u * u, w1 = 3 * u * u * t, w2 = 3 * u * t * t, w3 = t * t * t;
        return new Float2(w0 * a.X + w1 * c1.X + w2 * c2.X + w3 * b.X, w0 * a.Y + w1 * c1.Y + w2 * c2.Y + w3 * b.Y);
    }

    private static float DistToSeg(Float2 p, Float2 a, Float2 b)
    {
        float vx = b.X - a.X, vy = b.Y - a.Y, wx = p.X - a.X, wy = p.Y - a.Y;
        float c1 = vx * wx + vy * wy; if (c1 <= 0) return Dist(p.X, p.Y, a.X, a.Y);
        float c2 = vx * vx + vy * vy; if (c2 <= c1) return Dist(p.X, p.Y, b.X, b.Y);
        float t = c1 / c2; return Dist(p.X, p.Y, a.X + t * vx, a.Y + t * vy);
    }

    private static float Mod(float a, float m) { float r = a % m; return r < 0 ? r + m : r; }
    private static Color WithA(Color c, int a) => Color.FromArgb(a, c.R, c.G, c.B);
    private static Color32 ToC32(Color c, float alphaScale)
        => new Color32(c.R, c.G, c.B, (byte)Math.Clamp((int)MathF.Round(c.A * alphaScale), 0, 255));
}
