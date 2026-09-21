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
    /// <summary>Hover text for the socket, e.g. what the port expects.</summary>
    public string? Tooltip;

    /// <summary>
    /// An empty socket standing in for the port that does not exist yet: the way a node takes a list of
    /// inputs, or a sub graph grows a new result. It draws as a hollow plus, and a wire dropped on it
    /// (or dragged from it) arrives as a connection request with <see cref="ConnectionRequest.ToPlaceholder"/>
    /// or <see cref="ConnectionRequest.FromPlaceholder"/> set, for the host to turn into a real port.
    /// </summary>
    public bool IsPlaceholder;
    public object? UserData;

    public GraphPort() { }
    public GraphPort(string id, string label) { Id = id; Label = label; }
}

/// <summary>
/// A small chip on a node's header: an error marker, a state name, a count. Hovering it shows
/// <see cref="Tooltip"/>, which is where the detail of a problem belongs.
/// </summary>
public sealed class GraphBadge
{
    public string Text = "";
    public IOrigamiIcon? Icon;
    public Color? Color;
    public string? Tooltip;
    /// <summary>A richer tooltip than plain text, drawn by the host. Wins over <see cref="Tooltip"/>.</summary>
    public TooltipContent? Content;

    public GraphBadge() { }
    public GraphBadge(string text, Color? color = null, string? tooltip = null) { Text = text; Color = color; Tooltip = tooltip; }
}

/// <summary>
/// Handed to <see cref="GraphNode.Body"/> so a node can draw its own content: fields, sliders, a
/// blend space, a preview. The call happens inside an element already sized and placed for the body,
/// so the content only has to fill it.
/// </summary>
/// <remarks>
/// The card is laid out at graph scale and zoomed by a transform, so anything placed inside, any
/// Origami widget included, is sized as if the graph were at 100% and follows the zoom on its own,
/// pointer input and all. Element ids are scoped to the node already, so plain names do not clash
/// with another node's.
/// </remarks>
public sealed class NodeBodyContext
{
    internal NodeBodyContext(Paper paper, GraphNode node, float zoom, Color accent, bool selected)
    {
        Paper = paper; Node = node; Zoom = zoom; Accent = accent; Selected = selected;
    }

    public Paper Paper { get; }
    public GraphNode Node { get; }
    /// <summary>Current graph zoom, for deciding how much detail is worth drawing. Sizes do not need it.</summary>
    public float Zoom { get; }
    public Color Accent { get; }
    public bool Selected { get; }

    /// <summary>
    /// A length in the body's own units. The card's transform does the zooming, so this is the value
    /// unchanged; it stays so bodies written against the old per-length scaling keep working.
    /// </summary>
    public float S(float value) => value;

    /// <summary>
    /// Marks an element as owning its drags: a slider, a field, a chart. Without it a drag inside the
    /// body bubbles up and moves the node instead of working the control. Clicks still bubble, so
    /// clicking a control selects its node, and a plain drag on the body's background moves the node.
    /// </summary>
    public ElementBuilder Control(ElementBuilder element)
    {
        element.StopDragPropagation();
        return element;
    }

    /// <summary>
    /// An element id for this node's body. Ids are already scoped to the node, so the same suffix on two
    /// nodes stays distinct.
    /// </summary>
    public string Id(string suffix) => suffix;
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

    /// <summary>Hover text for the whole node.</summary>
    public string? Tooltip;

    /// <summary>A chip on the header: an error, a warning, a live value.</summary>
    /// <summary>
    /// An outline for a state the host wants seen, such as running, drawn with a soft glow of the same
    /// colour. Selection still takes the outline while the node is selected.
    /// </summary>
    public Color? Outline;

    /// <summary>Draws the card faded back, for a node the graph is not reaching right now.</summary>
    public bool Dimmed;

    public GraphBadge? Badge;
    /// <summary>More chips beside <see cref="Badge"/>, in order, such as a warning next to an output marker.</summary>
    public List<GraphBadge> Badges = new();

    /// <summary>
    /// Draws the node's own content below the port rows. Set <see cref="BodyHeight"/> to the space it
    /// needs; without it the node is a title and its port labels, as before.
    /// </summary>
    public Action<NodeBodyContext>? Body;

    /// <summary>Height reserved for <see cref="Body"/>, in graph space.</summary>
    public float BodyHeight;

    /// <summary>Shows a chevron in the header that raises <c>OnNodeToggleCollapsed</c>.</summary>
    public bool Collapsible;

    /// <summary>Draw the header only. Ports stay live, spread down the header's edges.</summary>
    public bool Collapsed;

    /// <summary>
    /// The node cannot be dragged or deleted, and a group never carries it along. For the fixed parts
    /// of a graph: the boundary cards of a sub graph, a root output, anything the user may wire but
    /// must not move or remove.
    /// </summary>
    public bool Pinned;
}

/// <summary>A directed wire from an output port to an input port, referenced by id.</summary>
public sealed class GraphConnection
{
    public string FromNode = "";
    public string FromPort = "";
    public string ToNode = "";
    public string ToPort = "";
    public Color? Color;

    /// <summary>
    /// Identity for selection. Left empty the wire is keyed by its endpoints, so two wires between the
    /// same pair of ports cannot be told apart: give parallel wires an id.
    /// </summary>
    public string Id = "";

    /// <summary>Multiplies the drawn width. Carry a weight or a strength on it.</summary>
    public float Thickness = 1f;

    /// <summary>Sends dots travelling along the wire, to show something flowing through it.</summary>
    public bool Flow;

    /// <summary>How fast the flow dots travel, in wire lengths per second.</summary>
    public float FlowSpeed = 1f;
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

    /// <summary>The output end was a placeholder, so the host makes a real output before wiring it.</summary>
    public readonly bool FromPlaceholder;

    /// <summary>The input end was a placeholder, so the host makes a real input before wiring it.</summary>
    public readonly bool ToPlaceholder;

    public ConnectionRequest(string fromNode, string fromPort, string toNode, string toPort,
        bool fromPlaceholder = false, bool toPlaceholder = false)
    {
        FromNode = fromNode; FromPort = fromPort; ToNode = toNode; ToPort = toPort;
        FromPlaceholder = fromPlaceholder; ToPlaceholder = toPlaceholder;
    }

    /// <summary>True when either end still has to be created.</summary>
    public bool NeedsPort => FromPlaceholder || ToPlaceholder;
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
    internal const float BodyPadTop = 8f;
    internal const float BodyPadBottom = 10f;
    private const float PortLabelPadX = 15f;
    private const float PortHitR = 9f;         // port grab radius
    internal const float TopBotSpacing = 26f;
    internal const float PillH = 20f;
    internal const float CollapsedPortSpacing = 15f;  // room per port down a folded node's edge
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
    private bool _readOnly;
    private Float2? _initPan;
    private float? _initZoom;
    private NodeGraphController? _controller;
    private float _snapStep;
    private bool _allowSelfConnections;
    private bool _showMinimap;
    private float _minimapW = 190f, _minimapH = 120f;

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
    private Action<GraphNode>? _onToggleCollapsed;
    private Action<GraphConnection>? _onDisconnect;

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
    /// <summary>Disable node/group/sticky/wire editing (move, resize, connect, delete, rename) while
    /// keeping pan, zoom, and selection active — a view-only inspection mode.</summary>
    public NodeGraphBuilder ReadOnly(bool readOnly = true) { _readOnly = readOnly; return this; }
    /// <summary>Initial pan (graph-space origin offset, in pixels) and zoom, applied only on the first
    /// frame; the user's pan/zoom persists afterward. Use to frame the graph when it opens.</summary>
    public NodeGraphBuilder InitialView(Float2 pan, float zoom) { _initPan = pan; _initZoom = zoom; return this; }
    /// <summary>Bind a host-held <see cref="NodeGraphController"/> for programmatic view/selection control
    /// and to read the current pan/zoom/selection.</summary>
    public NodeGraphBuilder Controller(NodeGraphController controller) { _controller = controller; return this; }
    /// <summary>Snap dragged nodes to a grid of this size (graph space). 0 turns snapping off.</summary>
    public NodeGraphBuilder SnapToGrid(float step) { _snapStep = MathF.Max(0f, step); return this; }
    /// <summary>Let a wire run from a node back into itself, leaving the decision to the validator.</summary>
    public NodeGraphBuilder AllowSelfConnections(bool allow = true) { _allowSelfConnections = allow; return this; }
    /// <summary>Show a minimap in the corner. Click or drag it to move the view.</summary>
    public NodeGraphBuilder Minimap(bool show = true, float width = 190f, float height = 120f)
    { _showMinimap = show; _minimapW = width; _minimapH = height; return this; }

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

    /// <summary>The collapse chevron on a node was clicked. Host flips <see cref="GraphNode.Collapsed"/>.</summary>
    public NodeGraphBuilder OnNodeToggleCollapsed(Action<GraphNode> handler) { _onToggleCollapsed = handler; return this; }

    /// <summary>
    /// A wire was pulled off its input port. The host removes it; if the drag then lands on another
    /// port, <c>OnConnect</c> follows with the new one. Without this handler a connected input starts a
    /// fresh wire instead of moving the existing one.
    /// </summary>
    public NodeGraphBuilder OnDisconnect(Action<GraphConnection> handler) { _onDisconnect = handler; return this; }

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
        public Float2 DragOffset;                       // MoveNodes / MoveGroup accumulator (graph space), snapped
        public Float2 RawDrag;                          // the same accumulator before snapping
        public string? DetachWire;                      // EdgeKey of the wire a re-target drag pulled off
        public Float2 MarqueeStart;                     // graph space
        public string? ConnNode, ConnPort;              // Connect source
        public bool ConnFromOutput;
        public string? ActiveGroup;                     // group being moved/resized
        public string? ActiveSticky;                    // sticky being moved/resized
        public Float2 ResizeSize;                       // live size while resizing (graph space)
        public string? RenamingGroup;                   // group whose title is being edited
        public string? EditingSticky;                   // sticky whose text is being edited
        public string RenameBuffer = "";                // shared text-edit buffer (group title / sticky body)
        public double EditStarted = double.NegativeInfinity; // when the current edit began, so its own click cannot commit it
        public string? ActiveWire;                      // EdgeKey of the wire whose control point is dragging
        public int ActivePoint;                         // control point index being dragged
        public Float2 PointOffset;                      // live offset (graph space) of the dragged point
        public readonly List<(float x, float y, float w, float h, Color32 col)> MinimapBoxes = new();
    }

    // A resolved node box + its port anchors for this frame (effective = includes live drag offset).
    private struct PortSlot { public GraphPort Port; public bool IsOutput; public PortSide Side; public Float2 Anchor; }
    private struct NodeLayout
    {
        public GraphNode Node;
        public Float2 Pos; public float W, H;
        // Ports are ordered left, right, top, bottom, so the counts are all a reader needs.
        public int LeftCount, RightCount;
        public List<PortSlot> Ports;
    }

    // Pushes a scope onto Paper's id stack so element ids inside it only have to be unique locally.
    private readonly struct IdScope : IDisposable
    {
        private readonly Paper _paper;
        public IdScope(Paper paper, string id) { _paper = paper; paper.PushID(id); }
        public void Dispose() => _paper.PopID();
    }

    private IdScope Scope(string id) => new(_paper, id);

    // A separator no id can contain, so "a"+"bc" and "ab"+"c" stay different wires.
    private static string EdgeKey(GraphConnection c)
        => c.Id.Length > 0 ? c.Id : string.Concat(c.FromNode, "\u0001", c.FromPort, "\u0001", c.ToNode, "\u0001", c.ToPort);

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
        foreach (var n in _nodes)
        {
            if (!byId.TryAdd(n.Id, n))
                throw new InvalidOperationException($"Two nodes share the id '{n.Id}'. Node ids have to be unique.");
        }
        st.SelNodes.RemoveWhere(nid => !byId.ContainsKey(nid));

        // Wire selection is resolved to the wires themselves once, so the paint pass never builds a key.
        HashSet<GraphConnection>? selectedWires = null;
        if (st.SelEdges.Count > 0)
        {
            var live = new HashSet<string>();
            selectedWires = new HashSet<GraphConnection>();
            foreach (var c in _connections)
            {
                string key = EdgeKey(c);
                live.Add(key);
                if (st.SelEdges.Contains(key)) selectedWires.Add(c);
            }
            st.SelEdges.RemoveWhere(k => !live.Contains(k));
        }
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
            float lx = (float)e.PointerPosition.X - s.ScreenX;
            float ly = (float)e.PointerPosition.Y - s.ScreenY;
            s.PanX = lx - (lx - s.PanX) / s.Zoom * nz;
            s.PanY = ly - (ly - s.PanY) / s.Zoom * nz;
            s.Zoom = nz;
        });

        // Middle-mouse pan (Paper drag is left-only), and keyboard, scoped to when the graph is hovered.
        bool overGraph = _paper.IsElementHovered(handle.Data.ID)
                      && _paper.PointerPos.X >= st.ScreenX && _paper.PointerPos.X <= st.ScreenX + _width
                      && _paper.PointerPos.Y >= st.ScreenY && _paper.PointerPos.Y <= st.ScreenY + _height;
        bool editing = st.RenamingGroup != null || st.EditingSticky != null;
        if (overGraph && _paper.IsPointerDown(PaperMouseBtn.Middle) && !editing)
        {
            st.PanX += _paper.PointerDelta.X;
            st.PanY += _paper.PointerDelta.Y;
        }
        if (editing && _paper.WantsCaptureKeyboard) CommitTextEdit(st);
        else if (editing) HandleTextEdit(st);
        else if (overGraph && !_paper.WantsCaptureKeyboard) HandleKeyboard(st);

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
                Connections = _connections,
                Layouts = layouts,
                SelectedWires = selectedWires,
                ShowGrid = _showGrid,
                GridSpacing = _gridSpacing,
                WireThick = _wireThick,
                GridMinor = ToC32(borderSoft, 0.5f),
                GridMajor = ToC32(borderSoft, 1f),
                WireDefault = ToC32(accentDefault, 0.85f),
                WireSelected = ToC32(_theme.Primary.C700, 1f),
                DotRing = ToC32(nodeBg, 1f),
                Accent = accentDefault,
                Marquee = ToC32(accentDefault, 0.9f),
                MarqueeFill = ToC32(accentDefault, 0.14f),
                Time = (float)_paper.Time,
            };

            if (st.Mode == DragMode.Connect && st.ConnNode != null && st.ConnPort != null)
            {
                var (tn, tp, to) = HitTestPort(st, _paper.PointerPos, layouts);
                snap.ConnHitNode = tn;
                snap.ConnHitValid = tn != null && to != st.ConnFromOutput
                    && (tn != st.ConnNode || _allowSelfConnections) && ValidateHit(st, tn, tp!, to);
            }

            // Background: grid only, plus the pan/marquee/select/context surface.
            var bg = _paper.Box("bg")
                .PositionType(PositionType.SelfDirected).Left(0).Top(0)
                .Width(UnitValue.Percentage(100)).Height(UnitValue.Percentage(100))
                .BackgroundColor(canvasBg)
                .Cursor(PaperCursor.Default)
                // Origin for screen<->graph mapping: use the canvas surface itself so clicks line up
                // with the rendered nodes/wires exactly (independent of the container's border/padding).
                .OnPostLayout((h, r) => { st.ScreenX = (float)r.Min.X; st.ScreenY = (float)r.Min.Y; });
            WireBackgroundEvents(bg, st, layouts);
            using (bg.Enter())
                _paper.Draw((canvas, rect) => PaintGridPass(canvas, rect, in snap));

            // Group boxes: behind the wires and nodes (only their title bar is interactive).
            foreach (var g in _groups)
                DrawGroup(g, st, detail, accentDefault, borderSoft, ink.C500, font, semi);

            // Wires: above the group fills, below the node cards.
            var wireLayer = _paper.Box("wires")
                .PositionType(PositionType.SelfDirected).Left(0).Top(0)
                .Width(UnitValue.Percentage(100)).Height(UnitValue.Percentage(100))
                .IsNotInteractable();
            using (wireLayer.Enter())
                _paper.Draw((canvas, rect) => PaintWiresPass(canvas, rect, in snap));

            // Sticky notes: over the wires, below the node cards.
            foreach (var sk in _stickies)
                DrawSticky(sk, st, detail, font);

            // Node cards.
            foreach (var n in _nodes)
                DrawNode(layouts[n.Id], st, detail, nodeBg, borderSoft, ink.C500, ink.C300, accentDefault, font, semi);

            // Interactive port sockets (above nodes so they are the drag source/targets). Zoomed out
            // far enough that a socket is not drawn, it is not in the way either.
            if (detail != Detail.Block)
            {
                foreach (var n in _nodes)
                    DrawPorts(layouts[n.Id], st, snap);

                // Wire reroute control points (drag to move, right-click to remove).
                foreach (var c in _connections)
                    DrawControlPoints(c, st, accentDefault);
            }

            // Minimap: the whole graph in the corner, with the viewport marked. Click or drag to move.
            if (_showMinimap) DrawMinimap(st, layouts, accentDefault, borderSoft, ink.C300);

            // Foreground overlay: marquee box + in-progress wire (both follow the live cursor).
            var overlay = _paper.Box("overlay")
                .PositionType(PositionType.SelfDirected).Left(0).Top(0)
                .Width(UnitValue.Percentage(100)).Height(UnitValue.Percentage(100))
                .IsNotInteractable();
            using (overlay.Enter())
                _paper.Draw((canvas, rect) => PaintForeground(canvas, rect, in snap));
        }

        if (_controller != null) WriteControllerState(st);
    }

    // ═══════════════════════════════════════════════════════════════════
    //  Minimap
    // ═══════════════════════════════════════════════════════════════════

    /// <summary>
    /// A scaled down plan of the graph in the bottom right corner, with a box showing what the viewport
    /// currently covers. Clicking or dragging inside it moves the view there.
    /// </summary>
    private void DrawMinimap(GraphState st, Dictionary<string, NodeLayout> layouts, Color accent, Color border, Color dim)
    {
        if (!TryContentBounds(layouts, out Float2 min, out Float2 size)) return;

        float pad = 8f;
        float mw = Math.Min(_minimapW, _width * 0.4f), mh = Math.Min(_minimapH, _height * 0.4f);
        float left = _width - mw - pad, top = _height - mh - pad;

        // One scale for both axes keeps the plan's proportions.
        float scale = Math.Min((mw - 10f) / Math.Max(size.X, 1f), (mh - 10f) / Math.Max(size.Y, 1f));
        float offX = left + (mw - size.X * scale) * 0.5f;
        float offY = top + (mh - size.Y * scale) * 0.5f;

        var panel = _paper.Box("minimap")
            .PositionType(PositionType.SelfDirected).Left(left).Top(top).Width(mw).Height(mh)
            .Rounded(_theme.Metrics.Rounding)
            .BackgroundColor(WithA(_theme.Neutral.C100, 225))
            .BorderColor(border).BorderWidth(1f)
            .Cursor(PaperCursor.Pointer)
            .Tooltip("Click to move the view");

        void MoveViewTo(Float2 pointer)
        {
            float gx = min.X + ((float)pointer.X - st.ScreenX - offX) / scale;
            float gy = min.Y + ((float)pointer.Y - st.ScreenY - offY) / scale;
            CenterGraphPoint(st, new Float2(gx, gy));
        }

        panel.OnClick(st, (state, e) => MoveViewTo(e.PointerPosition));
        panel.OnDragging(st, (state, e) => MoveViewTo(e.PointerPosition));

        // The plan is static per frame, so it is painted rather than built from elements.
        var boxes = st.MinimapBoxes;
        boxes.Clear();
        foreach (var g in _groups)
        {
            var (gp, gs) = GroupEffective(g, st);
            boxes.Add((gp.X, gp.Y, gs.X, gs.Y, ToC32(g.Color ?? accent, 0.18f)));
        }
        foreach (var sk in _stickies)
        {
            var (sp, ss) = StickyEffective(sk, st);
            boxes.Add((sp.X, sp.Y, ss.X, ss.Y, ToC32(sk.Color ?? _theme.Amber.C400, 0.5f)));
        }
        foreach (var l in layouts.Values)
        {
            Color col = l.Node.Accent ?? accent;
            float alpha = st.SelNodes.Contains(l.Node.Id) ? 1f : 0.75f;
            boxes.Add((l.Pos.X, l.Pos.Y, l.W, l.H, ToC32(col, alpha)));
        }

        Color32 viewCol = ToC32(dim, 0.9f);
        float zoom = st.Zoom;
        using (panel.Enter())
            _paper.Draw((canvas, rr) =>
            {
                float bx = (float)rr.Min.X + (offX - left), by = (float)rr.Min.Y + (offY - top);
                foreach (var b in boxes)
                    canvas.RectFilled(bx + (b.x - min.X) * scale, by + (b.y - min.Y) * scale,
                        Math.Max(1.5f, b.w * scale), Math.Max(1.5f, b.h * scale), b.col);

                // Viewport: the graph-space rectangle the canvas is showing right now.
                float vx = -st.PanX / zoom, vy = -st.PanY / zoom;
                float vw = _width / zoom, vh = _height / zoom;
                canvas.SaveState();
                canvas.SetStrokeColor(viewCol); canvas.SetStrokeWidth(1.2f);
                canvas.BeginPath();
                canvas.Rect(bx + (vx - min.X) * scale, by + (vy - min.Y) * scale, vw * scale, vh * scale);
                canvas.Stroke();
                canvas.RestoreState();
            });
    }

    private bool TryContentBounds(Dictionary<string, NodeLayout> layouts, out Float2 min, out Float2 size)
    {
        float minX = float.MaxValue, minY = float.MaxValue, maxX = float.MinValue, maxY = float.MinValue;
        bool any = false;
        void Add(Float2 p, Float2 s)
        {
            minX = Math.Min(minX, p.X); minY = Math.Min(minY, p.Y);
            maxX = Math.Max(maxX, p.X + s.X); maxY = Math.Max(maxY, p.Y + s.Y); any = true;
        }

        foreach (var l in layouts.Values) Add(l.Pos, new Float2(l.W, l.H));
        foreach (var g in _groups) Add(g.Position, g.Size);
        foreach (var sk in _stickies) Add(sk.Position, sk.Size);

        min = new Float2(minX, minY);
        size = any ? new Float2(Math.Max(maxX - minX, 1f), Math.Max(maxY - minY, 1f)) : Float2.Zero;
        return any;
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
        c.SelectedNodes = Refill(c.SelectedNodes, st.SelNodes);
        c.SelectedGroups = Refill(c.SelectedGroups, st.SelGroups);
        c.SelectedStickies = Refill(c.SelectedStickies, st.SelStickies);

        static IReadOnlyList<string> Refill(IReadOnlyList<string> current, HashSet<string> source)
        {
            if (current is not List<string> list) list = new List<string>(source.Count);
            else list.Clear();
            foreach (string id in source) list.Add(id);
            return list;
        }
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
        if (n.Pinned) return n.Position;
        if (st.Mode == DragMode.MoveNodes && st.SelNodes.Contains(n.Id)) return n.Position + st.DragOffset;
        if (st.Mode == DragMode.MoveGroup && st.ActiveGroup != null
            && groupOfId(st.ActiveGroup) is { } g && NodeInGroup(n, g)) return n.Position + st.DragOffset;
        return n.Position;

        GraphGroup? groupOfId(string id) { foreach (var gg in _groups) if (gg.Id == id) return gg; return null; }
    }

    // Spatial membership: a node belongs to a group if its centre lies inside the group's rect.
    private bool NodeInGroup(GraphNode n, GraphGroup g)
    {
        float cx = n.Position.X + MeasuredWidth(n) * 0.5f, cy = n.Position.Y + MeasuredHeight(n) * 0.5f;
        return cx >= g.Position.X && cx <= g.Position.X + g.Size.X
            && cy >= g.Position.Y && cy <= g.Position.Y + g.Size.Y;
    }

    private List<GraphNode> MembersOf(GraphGroup g)
    {
        var list = new List<GraphNode>();
        foreach (var n in _nodes) if (!n.Pinned && NodeInGroup(n, g)) list.Add(n);
        return list;
    }

    /// <summary>The card size a node will lay out to, before it is placed.</summary>
    private float MeasuredHeight(GraphNode n) => NodeGraphPreview.MeasureHeight(n, _theme.Metrics);

    private float MeasuredWidth(GraphNode n) => NodeGraphPreview.MeasureWidth(n, _theme.Metrics);

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
            return new NodeLayout { Node = n, Pos = pos, W = pw, H = ph, LeftCount = left.Count, RightCount = right.Count, Ports = pports };
        }

        float h = MeasuredHeight(n);
        int topBot = Math.Max(top.Count, bottom.Count);
        float w = Math.Max(n.Width, topBot > 0 ? topBot * TopBotSpacing + 24f : 0f);

        // Collapsed: the card is its header, so the side ports spread down its edges.
        if (n.Collapsed)
        {
            var folded = new List<PortSlot>(left.Count + right.Count + top.Count + bottom.Count);
            for (int i = 0; i < left.Count; i++) folded.Add(Slot(left[i], PortSide.Left, new Float2(pos.X, pos.Y + (i + 0.5f) / left.Count * h)));
            for (int i = 0; i < right.Count; i++) folded.Add(Slot(right[i], PortSide.Right, new Float2(pos.X + w, pos.Y + (i + 0.5f) / right.Count * h)));
            for (int i = 0; i < top.Count; i++) folded.Add(Slot(top[i], PortSide.Top, new Float2(pos.X + (i + 0.5f) * (w / top.Count), pos.Y)));
            for (int i = 0; i < bottom.Count; i++) folded.Add(Slot(bottom[i], PortSide.Bottom, new Float2(pos.X + (i + 0.5f) * (w / bottom.Count), pos.Y + h)));
            return new NodeLayout { Node = n, Pos = pos, W = w, H = h, LeftCount = left.Count, RightCount = right.Count, Ports = folded };
        }

        var ports = new List<PortSlot>(left.Count + right.Count + top.Count + bottom.Count);
        for (int i = 0; i < left.Count; i++)
            ports.Add(Slot(left[i], PortSide.Left, new Float2(pos.X, pos.Y + _headerH + BodyPadTop + (i + 0.5f) * _portRowH)));
        for (int i = 0; i < right.Count; i++)
            ports.Add(Slot(right[i], PortSide.Right, new Float2(pos.X + w, pos.Y + _headerH + BodyPadTop + (i + 0.5f) * _portRowH)));
        for (int i = 0; i < top.Count; i++)
            ports.Add(Slot(top[i], PortSide.Top, new Float2(pos.X + (i + 0.5f) * (w / top.Count), pos.Y)));
        for (int i = 0; i < bottom.Count; i++)
            ports.Add(Slot(bottom[i], PortSide.Bottom, new Float2(pos.X + (i + 0.5f) * (w / bottom.Count), pos.Y + h)));

        return new NodeLayout { Node = n, Pos = pos, W = w, H = h, LeftCount = left.Count, RightCount = right.Count, Ports = ports };
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

        // The card keeps its top left corner where the view puts it and is laid out at graph scale, then
        // zoomed about that corner. Everything inside is sized as at 100%, which is what lets ordinary
        // widgets live in a card: the transform scales them and Paper maps the pointer back for them.
        float sx = l.Pos.X * zoom + st.PanX;
        float sy = l.Pos.Y * zoom + st.PanY;
        float w = l.W, h = l.H;
        float headerH = _headerH;
        float rounding = _nodeRounding;

        // Outlines and glows stay the same on screen at any zoom, so they are divided back out.
        float hairline = 1f / zoom;

        using var scope = Scope(node.Id);
        if (node.Pill) { DrawPill(st, node, accent, selected, sx, sy, w, h, zoom); return; }

        var card = _paper.Column("card")
            .PositionType(PositionType.SelfDirected).Left(sx).Top(sy)
            .Width(w).Height(h)
            .TransformOrigin(0, 0).Scale(zoom)
            .Rounded(rounding)
            .BackgroundColor(nodeBg)
            .BorderColor(selected ? accent : node.Outline ?? borderSoft)
            .BorderWidth((selected || node.Outline.HasValue ? 1.6f : 1f) * hairline)
            .Clip()
            .Cursor(PaperCursor.Grab).CursorDragging(PaperCursor.Grabbing);
        WireNodeEvents(card, node, st);
        if (!string.IsNullOrEmpty(node.Tooltip)) card.Tooltip(node.Title, node.Tooltip!);
        if (selected) card.Glow(0, 0, 14f * hairline, hairline, WithA(accent, 90));
        else if (node.Outline is { } outline) card.Glow(0, 0, 18f * hairline, hairline, WithA(outline, 60));

        using (card.Enter())
        {
            // Header strip. At Block LOD the whole card is tinted and text is dropped.
            bool folded = node.Collapsed || detail == Detail.Block;
            var header = _paper.Row("header")
                .Width(UnitValue.Percentage(100)).Height(folded ? h : headerH)
                .BackgroundColor(WithA(accent, detail == Detail.Block ? 70 : 40))
                .RoundedTop(rounding).IsNotInteractable();
            if (folded) header.Rounded(rounding);

            using (header.Padding(10f, 10f, 0, 0).Enter())
            {
                if (node.Icon != null && detail != Detail.Block)
                {
                    var icon = node.Icon; float isz = 14f;
                    using (_paper.Box("icon").Width(isz).Height(headerH).Margin(0, 7f, 0, 0).IsNotInteractable().Enter())
                        _paper.Draw((canvas, rr) =>
                        {
                            float ix = (float)(rr.Min.X + (rr.Size.X - isz) * 0.5f), iy = (float)(rr.Min.Y + (rr.Size.Y - isz) * 0.5f);
                            icon.Draw(canvas, new Rect(ix, iy, ix + isz, iy + isz), accent);
                        });
                }
                if (detail != Detail.Block && semi != null)
                    _paper.Box("title").Width(UnitValue.Stretch()).Height(headerH)
                        .Text(node.Title, semi).FontSize(_titleFont)
                        .TextColor(titleCol).Alignment(TextAlignment.MiddleLeft).TextTruncate().IsNotInteractable();

                if (detail == Detail.Full)
                {
                    if (node.Badge != null) DrawBadge(node.Badge, "badge", headerH, accent, titleCol, font);
                    for (int i = 0; i < node.Badges.Count; i++)
                        DrawBadge(node.Badges[i], "badge" + i, headerH, accent, titleCol, font);
                }

                if (detail == Detail.Full && node.Collapsible)
                    DrawCollapseToggle(node, st, headerH, portLabelCol);
            }

            // Body: input/output labels (only at Full LOD; Left/Right ports carry labels).
            if (detail == Detail.Full && !node.Collapsed && font != null)
            {
                int rows = Math.Max(l.LeftCount, l.RightCount);
                if (rows > 0)
                    // With a custom body below, the rows take exactly their own height rather than
                    // stretching, so the body keeps the space the layout reserved for it.
                    using (_paper.Column("body").Width(UnitValue.Percentage(100))
                        .Height(node.Body != null && node.BodyHeight > 0f
                            ? BodyPadTop + rows * _portRowH
                            : UnitValue.Stretch())
                        .Padding(0, 0, BodyPadTop, node.Body != null ? 0 : BodyPadBottom).IsNotInteractable().Enter())
                        for (int i = 0; i < rows; i++)
                            using (_paper.Row("row", i).Width(UnitValue.Percentage(100)).Height(_portRowH).Enter())
                            {
                                _paper.Box("in", i).Width(UnitValue.Stretch()).Height(UnitValue.Percentage(100))
                                    .Margin(PortLabelPadX, 0, 0, 0)
                                    .Text(i < l.LeftCount ? l.Ports[i].Port.Label : "", font).FontSize(_portFont)
                                    .TextColor(portLabelCol).Alignment(TextAlignment.MiddleLeft).TextTruncate();
                                _paper.Box("out", i).Width(UnitValue.Stretch()).Height(UnitValue.Percentage(100))
                                    .Margin(0, PortLabelPadX, 0, 0)
                                    .Text(i < l.RightCount ? l.Ports[l.LeftCount + i].Port.Label : "", font).FontSize(_portFont)
                                    .TextColor(portLabelCol).Alignment(TextAlignment.MiddleRight).TextTruncate();
                            }

                if (node.Body != null && node.BodyHeight > 0f)
                    using (_paper.Box("bodyhost").Width(UnitValue.Percentage(100)).Height(node.BodyHeight)
                        .Margin(0, 0, 0, BodyPadBottom).Enter())
                        node.Body(new NodeBodyContext(_paper, node, zoom, accent, selected));
            }

            // Paper has no per element opacity, so a faded card is the card with the canvas laid over it.
            if (node.Dimmed)
                _paper.Box("dim").PositionType(PositionType.SelfDirected).Left(0).Top(0)
                    .Width(UnitValue.Percentage(100)).Height(UnitValue.Percentage(100))
                    .BackgroundColor(WithA(_theme.Neutral.C100, 150)).IsNotInteractable();
        }
    }

    // A chip at the right of the header: a live value, a state, or a problem with the detail on hover.
    private void DrawBadge(GraphBadge badge, string id, float headerH, Color accent, Color titleCol, FontFile? font)
    {
        Color fill = badge.Color ?? accent;
        float h = headerH - 10f;
        var chip = _paper.Row(id)
            .Height(h).Margin(6f, 0, UnitValue.Stretch(), UnitValue.Stretch())
            .Rounded(_theme.Metrics.SmallRounding)
            .BackgroundColor(WithA(fill, 55))
            .BorderColor(WithA(fill, 150)).BorderWidth(1f);
        if (badge.Content != null) chip.Tooltip(badge.Content);
        else if (!string.IsNullOrEmpty(badge.Tooltip)) chip.Tooltip(badge.Tooltip!);

        // An icon on its own is a square, which is what lets a warning sit small beside a labelled chip.
        bool iconOnly = badge.Icon != null && badge.Text.Length == 0;
        if (iconOnly) chip.Width(h);

        using (chip.Padding(iconOnly ? 0 : 5f, iconOnly ? 0 : 5f, 0, 0).Enter())
        {
            if (badge.Icon is { } icon)
            {
                float isz = 10f;
                using (_paper.Box(id + "icon").Width(iconOnly ? UnitValue.Stretch() : isz).Height(UnitValue.Percentage(100)).IsNotInteractable().Enter())
                    _paper.Draw((canvas, rr) =>
                    {
                        float ix = (float)(rr.Min.X + (rr.Size.X - isz) * 0.5f), iy = (float)(rr.Min.Y + (rr.Size.Y - isz) * 0.5f);
                        icon.Draw(canvas, new Rect(ix, iy, ix + isz, iy + isz), fill);
                    });
            }

            if (badge.Text.Length > 0 && font != null)
                _paper.Box(id + "text").Height(UnitValue.Percentage(100))
                    .Margin(badge.Icon != null ? 4f : 0, 0, 0, 0)
                    .Text(badge.Text, font).FontSize(_portFont * 0.92f)
                    .TextColor(titleCol).Alignment(TextAlignment.MiddleCenter).IsNotInteractable();
        }
    }

    // The fold chevron. The host owns the flag, so the widget only reports the click.
    private void DrawCollapseToggle(GraphNode node, GraphState st, float headerH, Color iconCol)
    {
        float size = 14f;
        bool collapsed = node.Collapsed;
        var toggle = _paper.Box("fold")
            .Width(size).Height(headerH).Margin(5f, 0, 0, 0)
            .Cursor(PaperCursor.Pointer)
            .Tooltip(collapsed ? "Expand" : "Collapse");
        toggle.OnClick(st, (state, e) => _onToggleCollapsed?.Invoke(node));

        Color32 col = ToC32(iconCol, 1f);
        using (toggle.Enter())
            _paper.Draw((canvas, rr) =>
            {
                float cx = (float)(rr.Min.X + rr.Size.X * 0.5f), cy = (float)(rr.Min.Y + rr.Size.Y * 0.5f);
                float e = size * 0.28f;
                canvas.SaveState(); canvas.SetStrokeColor(col); canvas.SetStrokeWidth(1.4f);
                canvas.BeginPath();
                if (collapsed) { canvas.MoveTo(cx - e, cy - e); canvas.LineTo(cx + e, cy); canvas.LineTo(cx - e, cy + e); }
                else { canvas.MoveTo(cx - e, cy - e * 0.6f); canvas.LineTo(cx, cy + e * 0.6f); canvas.LineTo(cx + e, cy - e * 0.6f); }
                canvas.Stroke(); canvas.RestoreState();
            });
    }

    // ── Pill node (relay / reroute): a small solid capsule, no text. A capsule has no header, so a
    // badge and a collapse toggle have nowhere to go; the tooltip still works.
    private void DrawPill(GraphState st, GraphNode node, Color accent, bool selected,
        float sx, float sy, float w, float h, float zoom)
    {
        float hairline = 1f / zoom;
        var pill = _paper.Box("pill")
            .PositionType(PositionType.SelfDirected).Left(sx).Top(sy).Width(w).Height(h)
            .TransformOrigin(0, 0).Scale(zoom)
            .Rounded(h * 0.5f)
            .BackgroundColor(WithA(accent, 235))
            .BorderColor(selected ? _theme.Ink.C700 : WithA(accent, 255)).BorderWidth((selected ? 1.8f : 1f) * hairline)
            .Cursor(PaperCursor.Grab).CursorDragging(PaperCursor.Grabbing);
        WireNodeEvents(pill, node, st);
        if (!string.IsNullOrEmpty(node.Tooltip)) pill.Tooltip(node.Title, node.Tooltip!);
        if (selected) pill.Glow(0, 0, 12f * hairline, hairline, WithA(accent, 110));
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
    private void DrawSticky(GraphSticky sk, GraphState st, Detail detail, FontFile? font)
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
        float rounding = _theme.Metrics.Rounding * zoom;

        using var scope = Scope(sid);
        var note = _paper.Column("note")
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
            if (_readOnly) return;
            if (s.RenamingGroup != null || (s.EditingSticky != null && s.EditingSticky != sid)) CommitTextEdit(s);
            if (!s.SelStickies.Contains(sid)) SelectOnly(s, s.SelStickies, sid);
            s.Mode = DragMode.MoveSticky; s.ActiveSticky = sid; s.DragOffset = Float2.Zero; s.RawDrag = Float2.Zero;
        });
        note.OnDragging(st, (s, e) =>
        {
            if (s.Mode != DragMode.MoveSticky) return;
            s.RawDrag += new Float2(e.Delta.X / s.Zoom, e.Delta.Y / s.Zoom);
            s.DragOffset = SnapDelta(sk.Position, s.RawDrag);
        });
        note.OnDragEnd(st, (s, e) =>
        {
            if (s.Mode == DragMode.MoveSticky && (s.DragOffset.X != 0 || s.DragOffset.Y != 0)) _onStickyMoved?.Invoke(sk, s.DragOffset);
            s.Mode = DragMode.None; s.ActiveSticky = null; s.DragOffset = Float2.Zero; s.RawDrag = Float2.Zero;
        });
        note.OnDoubleClick(st, (s, e) =>
        {
            if (_readOnly) return;
            s.RenamingGroup = null; s.EditingSticky = sid; s.RenameBuffer = sk.Text; s.EditStarted = _paper.Time;
        });
        note.OnRightClick(st, (s, e) =>
        {
            if (!s.SelStickies.Contains(sid)) SelectOnlySticky(s, sk);
            _onStickyContext?.Invoke(sk, ScreenToGraph(s, e.PointerPosition));
        });
        if (selected) note.Glow(0, 0, 12f, 1f, WithA(fill, 120));

        using (note.Enter())
        {
            if (font != null && detail != Detail.Block)
            {
                string body = editing ? st.RenameBuffer + (_paper.Pulse(1.1f) > 0.5f ? "|" : "") : sk.Text;
                _paper.Box("text").Width(UnitValue.Stretch()).Height(UnitValue.Stretch())
                    .Text(body, font).FontSize(Math.Max(5f, 11.5f * zoom))
                    .TextColor(textCol).Alignment(TextAlignment.Left).Wrap(TextWrapMode.Wrap).IsNotInteractable();
            }
        }

        // Resize grip (bottom-right corner).
        float hs = Math.Max(8f, 15f * zoom);
        var grip = _paper.Box("grip")
            .PositionType(PositionType.SelfDirected).Left(sx + w - hs).Top(sy + h - hs).Width(hs).Height(hs)
            .Cursor(PaperCursor.ResizeNWSE);
        grip.OnDragStart(st, (s, e) => { if (_readOnly) return; s.Mode = DragMode.ResizeSticky; s.ActiveSticky = sid; s.ResizeSize = sk.Size; });
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
    private void DrawGroup(GraphGroup g, GraphState st, Detail detail, Color accentDefault, Color borderSoft, Color titleCol, FontFile? font, FontFile? semi)
    {
        float zoom = st.Zoom;
        var (gpos, gsize) = GroupEffective(g, st);
        Color accent = g.Color ?? accentDefault;
        bool selected = st.SelGroups.Contains(g.Id);
        bool renaming = st.RenamingGroup == g.Id;
        string gid = g.Id;

        float sx = gpos.X * zoom + st.PanX, sy = gpos.Y * zoom + st.PanY;
        float w = gsize.X * zoom, h = gsize.Y * zoom;
        float titleH = Math.Max(5f, _headerH * zoom);
        float rounding = _theme.Metrics.ContainerRounding * zoom;

        using var scope = Scope(gid);

        // Frame — non-interactive so nodes/wires/bg inside stay usable.
        _paper.Box("frame")
            .PositionType(PositionType.SelfDirected).Left(sx).Top(sy).Width(w).Height(h)
            .Rounded(rounding)
            .BackgroundColor(WithA(accent, 20))
            .BorderColor(selected ? accent : WithA(accent, 110)).BorderWidth(selected ? 2f : 1.4f)
            .IsNotInteractable();

        // Title bar — the group's drag/select/rename/context handle.
        var title = _paper.Row("title")
            .PositionType(PositionType.SelfDirected).Left(sx).Top(sy).Width(w).Height(titleH)
            .RoundedTop(rounding).Padding(9f * zoom, 9f * zoom, 0, 0)
            .BackgroundColor(WithA(accent, selected ? 85 : 50))
            .Cursor(PaperCursor.Grab).CursorDragging(PaperCursor.Grabbing);
        title.OnClick(st, (s, e) => ClickSelectGroup(s, g));
        title.OnDragStart(st, (s, e) =>
        {
            if (_readOnly) return;
            if (s.RenamingGroup != null || s.EditingSticky != null) CommitTextEdit(s);
            if (!s.SelGroups.Contains(gid)) SelectOnlyGroup(s, g);
            s.Mode = DragMode.MoveGroup; s.ActiveGroup = gid; s.DragOffset = Float2.Zero; s.RawDrag = Float2.Zero;
        });
        title.OnDragging(st, (s, e) =>
        {
            if (s.Mode != DragMode.MoveGroup) return;
            s.RawDrag += new Float2(e.Delta.X / s.Zoom, e.Delta.Y / s.Zoom);
            s.DragOffset = SnapDelta(g.Position, s.RawDrag);
        });
        title.OnDragEnd(st, (s, e) =>
        {
            if (s.Mode == DragMode.MoveGroup && (s.DragOffset.X != 0 || s.DragOffset.Y != 0))
                _onGroupMoved?.Invoke(g, MembersOf(g), s.DragOffset);
            s.Mode = DragMode.None; s.ActiveGroup = null; s.DragOffset = Float2.Zero; s.RawDrag = Float2.Zero;
        });
        title.OnDoubleClick(st, (s, e) =>
        {
            if (_readOnly) return;
            s.RenamingGroup = gid; s.RenameBuffer = g.Title; s.EditStarted = _paper.Time;
        });
        title.OnRightClick(st, (s, e) =>
        {
            if (!s.SelGroups.Contains(gid)) SelectOnlyGroup(s, g);
            _onGroupContext?.Invoke(g, ScreenToGraph(s, e.PointerPosition));
        });

        using (title.Enter())
        {
            // Zoomed far out the title is unreadable anyway, and drawing it only adds noise.
            if (semi != null && detail != Detail.Block)
            {
                string text = renaming ? st.RenameBuffer + (_paper.Pulse(1.1f) > 0.5f ? "|" : "") : g.Title;
                _paper.Box("titletext").Width(UnitValue.Stretch()).Height(UnitValue.Percentage(100))
                    .Text(text, semi).FontSize(_titleFont * zoom)
                    .TextColor(renaming ? _theme.Ink.C700 : titleCol).Alignment(TextAlignment.MiddleLeft)
                    .TextTruncate().IsNotInteractable();
            }
        }

        // Resize handle (bottom-right corner).
        float hs = Math.Max(8f, 15f * zoom);
        var grip = _paper.Box("grip")
            .PositionType(PositionType.SelfDirected).Left(sx + w - hs).Top(sy + h - hs).Width(hs).Height(hs)
            .Cursor(PaperCursor.ResizeNWSE);
        grip.OnDragStart(st, (s, e) => { if (_readOnly) return; s.Mode = DragMode.ResizeGroup; s.ActiveGroup = gid; s.ResizeSize = g.Size; });
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
        float zoom = st.Zoom;
        // The hit box follows the zoom rather than holding a floor: a fixed one covers the whole card
        // once the graph is small, and swallows the drag and marquee gestures used to rearrange it.
        float hit = Math.Max(4f, PortHitR * zoom);
        float dotR = _portDotR * zoom;
        using var scope = Scope(l.Node.Id);
        foreach (var slot in l.Ports)
        {
            var s = slot;
            // container-local position: anchor is graph space; local = graph*zoom + pan.
            float lx = s.Anchor.X * zoom + st.PanX;
            float ly = s.Anchor.Y * zoom + st.PanY;

            var pb = _paper.Box(s.Port.Id, s.IsOutput ? 1 : 0)
                .PositionType(PositionType.SelfDirected).Left(lx - hit).Top(ly - hit)
                .Width(hit * 2).Height(hit * 2).Cursor(PaperCursor.Crosshair);
            if (!string.IsNullOrEmpty(s.Port.Tooltip)) pb.Tooltip(s.Port.Tooltip!);
            else if (s.Port.Label.Length > 0) pb.Tooltip(s.Port.Label);
            pb.OnDragStart(st, (state, e) =>
            {
                if (_readOnly) return;
                state.Mode = DragMode.Connect;
                state.DetachWire = null;

                // Dragging a connected input picks the existing wire up by its far end, so the wire moves
                // instead of a second one being started.
                if (!s.IsOutput && !s.Port.IsPlaceholder && _onDisconnect != null && FindWireInto(l.Node.Id, s.Port.Id) is { } wire)
                {
                    state.DetachWire = EdgeKey(wire);
                    state.ConnNode = wire.FromNode; state.ConnPort = wire.FromPort; state.ConnFromOutput = true;
                    return;
                }

                state.ConnNode = l.Node.Id; state.ConnPort = s.Port.Id; state.ConnFromOutput = s.IsOutput;
            });
            pb.OnDragEnd(st, (state, e) => EndConnect(state, snap.Layouts));

            bool hov = _paper.IsElementHovered(pb._handle.Data.ID);
            Color pc = s.Port.Color ?? snap.Accent;
            PortShape shape = s.Port.Shape;
            bool placeholder = s.Port.IsPlaceholder;
            using (pb.Enter())
                _paper.Draw((canvas, rr) =>
                {
                    float cx = (float)(rr.Min.X + rr.Size.X * 0.5f), cy = (float)(rr.Min.Y + rr.Size.Y * 0.5f);
                    float r = dotR;
                    var fill = ToC32(pc, 1f);
                    if (hov) canvas.CircleFilled(cx, cy, r + 3.5f, ToC32(pc, 0.35f));
                    if (placeholder) PaintPlaceholder(canvas, cx, cy, r + 1f, fill, snap.DotRing);
                    else if (shape == PortShape.Arrow) PaintArrow(canvas, cx, cy, r + 0.5f, s.Side, fill, snap.DotRing);
                    else { canvas.CircleFilled(cx, cy, r + 1.5f, snap.DotRing); canvas.CircleFilled(cx, cy, r, fill); }
                });
        }
    }

    // A draggable dot per wire control point; right-click removes it.
    private void DrawControlPoints(GraphConnection c, GraphState st, Color accentDefault)
    {
        var cps = c.ControlPoints;
        if (cps.Count == 0) return;
        float zoom = st.Zoom, hit = Math.Max(4f, 6.5f * zoom);
        string key = EdgeKey(c);
        using var scope = Scope(key);
        Color32 fill = ToC32(c.Color ?? accentDefault, 1f), ring = ToC32(_theme.Popover, 1f);

        for (int i = 0; i < cps.Count; i++)
        {
            int idx = i;
            Float2 gp = (st.Mode == DragMode.MovePoint && st.ActiveWire == key && st.ActivePoint == i) ? cps[i] + st.PointOffset : cps[i];
            float lx = gp.X * zoom + st.PanX, ly = gp.Y * zoom + st.PanY;

            var box = _paper.Box("cp", idx)
                .PositionType(PositionType.SelfDirected).Left(lx - hit).Top(ly - hit).Width(hit * 2).Height(hit * 2)
                .Cursor(PaperCursor.Grab).CursorDragging(PaperCursor.Grabbing);
            box.OnDragStart(st, (s, e) => { if (_readOnly) return; s.Mode = DragMode.MovePoint; s.ActiveWire = key; s.ActivePoint = idx; s.PointOffset = Float2.Zero; });
            box.OnDragging(st, (s, e) => { if (s.Mode == DragMode.MovePoint) s.PointOffset += new Float2(e.Delta.X / s.Zoom, e.Delta.Y / s.Zoom); });
            box.OnDragEnd(st, (s, e) =>
            {
                if (s.Mode == DragMode.MovePoint && (s.PointOffset.X != 0 || s.PointOffset.Y != 0) && idx < c.ControlPoints.Count)
                    _onWirePointMoved?.Invoke(c, idx, c.ControlPoints[idx] + s.PointOffset);
                s.Mode = DragMode.None; s.ActiveWire = null;
            });
            box.OnRightClick(st, (s, e) => { if (!_readOnly) _onWireRemovePoint?.Invoke(c, idx); });

            bool hov = _paper.IsElementHovered(box._handle.Data.ID);
            float pointR = Math.Max(1.5f, (hov ? 5.5f : 4.5f) * zoom);
            using (box.Enter())
                _paper.Draw((canvas, rr) =>
                {
                    float cx = (float)(rr.Min.X + rr.Size.X * 0.5f), cy = (float)(rr.Min.Y + rr.Size.Y * 0.5f);
                    canvas.CircleFilled(cx, cy, pointR + Math.Max(1f, 2f * zoom), ring);
                    canvas.CircleFilled(cx, cy, pointR, fill);
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
            if (_readOnly || node.Pinned) return;
            if (!state.SelNodes.Contains(node.Id)) SelectOnlyNode(state, node);
            state.Mode = DragMode.MoveNodes;
            state.DragOffset = Float2.Zero;
            state.RawDrag = Float2.Zero;
        });
        card.OnDragging(st, (state, e) =>
        {
            if (state.Mode != DragMode.MoveNodes) return;
            // The card is zoomed by its transform, so the event already reports the drag in graph units.
            state.RawDrag += e.Delta;
            state.DragOffset = SnapDelta(node.Position, state.RawDrag);
        });
        card.OnDragEnd(st, (state, e) =>
        {
            if (state.Mode == DragMode.MoveNodes && (state.DragOffset.X != 0 || state.DragOffset.Y != 0))
            {
                var moved = _nodes.Where(n => state.SelNodes.Contains(n.Id) && !n.Pinned).ToList();
                _onNodesMoved?.Invoke(moved, state.DragOffset);
            }
            state.Mode = DragMode.None; state.DragOffset = Float2.Zero; state.RawDrag = Float2.Zero;
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

    private void WireBackgroundEvents(ElementBuilder bg, GraphState st, Dictionary<string, NodeLayout> layouts)
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
            if (!_readOnly && _onWireAddPoint != null && HitTestWire(state, layouts, gp, out var wire, out int seg))
                _onWireAddPoint(wire!, seg, gp);
            else if (!_readOnly)
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
        if (state.Mode != DragMode.Connect || state.ConnNode == null || state.ConnPort == null)
        {
            state.Mode = DragMode.None; state.DetachWire = null; return;
        }

        GraphConnection? detached = state.DetachWire is { } key ? FindWire(key) : null;
        var (hitNode, hitPort, hitOut) = HitTestPort(state, _paper.PointerPos, layouts);

        if (hitNode != null && hitOut != state.ConnFromOutput && (hitNode != state.ConnNode || _allowSelfConnections))
        {
            bool sourcePlaceholder = IsPlaceholderPort(state.ConnNode, state.ConnPort, state.ConnFromOutput);
            bool hitPlaceholder = IsPlaceholderPort(hitNode, hitPort!, hitOut);
            var req = state.ConnFromOutput
                ? new ConnectionRequest(state.ConnNode, state.ConnPort, hitNode, hitPort!, sourcePlaceholder, hitPlaceholder)
                : new ConnectionRequest(hitNode, hitPort!, state.ConnNode, state.ConnPort, hitPlaceholder, sourcePlaceholder);
            if (_onValidate == null || _onValidate(req))
            {
                // Re-targeting: the old wire goes first, so the host never briefly holds both.
                if (detached != null) _onDisconnect?.Invoke(detached);
                _onConnect?.Invoke(req); // validator is authoritative
            }
        }
        else if (hitNode == null)
        {
            // A wire pulled off a port and dropped on empty canvas is simply removed.
            if (detached != null) _onDisconnect?.Invoke(detached);
            else _onDropWireEmpty?.Invoke(ScreenToGraph(state, _paper.PointerPos), state.ConnNode, state.ConnPort, state.ConnFromOutput);
        }

        state.Mode = DragMode.None; state.ConnNode = null; state.ConnPort = null; state.DetachWire = null;
    }

    // Snapping puts the dragged thing on the grid; anything dragged with it keeps its relative offset.
    private Float2 SnapDelta(Float2 anchor, Float2 raw)
    {
        if (!(_snapStep > 0f)) return raw;
        float x = MathF.Round((anchor.X + raw.X) / _snapStep) * _snapStep - anchor.X;
        float y = MathF.Round((anchor.Y + raw.Y) / _snapStep) * _snapStep - anchor.Y;
        return new Float2(x, y);
    }

    private bool IsPlaceholderPort(string nodeId, string portId, bool output)
    {
        foreach (var n in _nodes)
        {
            if (n.Id != nodeId) continue;
            foreach (var p in output ? n.Outputs : n.Inputs)
                if (p.Id == portId) return p.IsPlaceholder;
            return false;
        }
        return false;
    }

    private GraphConnection? FindWireInto(string nodeId, string portId)
    {
        foreach (var c in _connections)
            if (c.ToNode == nodeId && c.ToPort == portId) return c;
        return null;
    }

    private GraphConnection? FindWire(string key)
    {
        foreach (var c in _connections)
            if (EdgeKey(c) == key) return c;
        return null;
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

    private void HandleKeyboard(GraphState st)
    {
        if (_paper.IsKeyPressed(PaperKey.Delete) || _paper.IsKeyPressed(PaperKey.Backspace))
        {
            if (!_readOnly && !SelectionEmpty(st)) _onDelete?.Invoke(BuildSelection(st, forEdit: true));
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
    // A double click reaches the click handler too, so an edit is safe from clicks for a moment after
    // it opens: without this the caret appears and the edit closes on the very same gesture.
    private void MaybeCommitEdit(GraphState st)
    {
        if (st.RenamingGroup == null && st.EditingSticky == null) return;
        if (_paper.Time - st.EditStarted < 0.25) return;
        CommitTextEdit(st);
    }

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

    private GraphSelection BuildSelection(GraphState st, bool forEdit = false) => new(
        _nodes.Where(n => st.SelNodes.Contains(n.Id) && !(forEdit && n.Pinned)).ToList(),
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
        public IReadOnlyList<GraphConnection> Connections;
        public Dictionary<string, NodeLayout> Layouts;
        public bool ShowGrid;
        public float GridSpacing, WireThick;
        public HashSet<GraphConnection>? SelectedWires;
        public Color32 GridMinor, GridMajor, WireDefault, WireSelected, DotRing, Marquee, MarqueeFill;
        public Color Accent;
        public float Time;
        // Resolved before the frame is painted, so the host's validator is never called from a draw.
        public string? ConnHitNode;
        public bool ConnHitValid;
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
            bool sel = s.SelectedWires != null && s.SelectedWires.Contains(c);
            int da = DirOf(lf, c.FromPort, true), db = DirOf(lt, c.ToPort, false);
            Color32 col = sel ? s.WireSelected : (c.Color.HasValue ? ToC32(c.Color.Value, 0.85f) : s.WireDefault);

            float scale = float.IsFinite(c.Thickness) ? Math.Clamp(c.Thickness, 0.1f, 8f) : 1f;
            float wt = s.WireThick * scale, wSel = wt * 1.75f, wGlow = wt * 3.5f;
            var cps = EffectiveCPs(c, s.St);
            if (cps.Count == 0)
            {
                if (sel) PaintWire(canvas, a, b, da, db, ToC32(s.Accent, 0.28f), wGlow);
                PaintWire(canvas, a, b, da, db, col, sel ? wSel : wt);
                if (c.Flow) PaintFlowOnCurve(canvas, a, b, da, db, PortColor(lf, c.FromPort, true, in s), PortColor(lt, c.ToPort, false, in s), wt, s.Zoom, s.Time * c.FlowSpeed);
            }
            else
            {
                var samples = SampleWireGraph(ga, cps, gb, da, db);
                var pts = new List<Float2>(samples.Count);
                foreach (var t in samples) pts.Add(ToScreen(t.p, ox, oy, in s));
                if (sel) PaintWirePath(canvas, pts, ToC32(s.Accent, 0.28f), wGlow);
                PaintWirePath(canvas, pts, col, sel ? wSel : wt);
                if (c.Flow) PaintFlowOnPath(canvas, pts, PortColor(lf, c.FromPort, true, in s), PortColor(lt, c.ToPort, false, in s), wt, s.Zoom, s.Time * c.FlowSpeed);
            }
        }
    }

    // Dots running from the output end to the input end, so an active wire reads at a glance.
    private const int FlowDots = 3;

    // A dot carries its ports' own colours, so a wire shows what is travelling along it.
    private static Color PortColor(NodeLayout l, string portId, bool output, in Snapshot s)
    {
        foreach (var slot in l.Ports)
            if (slot.IsOutput == output && slot.Port.Id == portId)
                return slot.Port.Color ?? s.Accent;
        return s.Accent;
    }

    // Dots belong to the graph, so they grow and shrink with it rather than holding a screen size.
    private static float FlowRadius(float width, float zoom) => Math.Max(1.4f, (width * 0.5f + 2.2f) * zoom);

    // Reused each frame so the flow costs no allocations.
    private static readonly List<Float2> s_flowPts = new(FlowSamples + 1);
    private const int FlowSamples = 24;

    private static void PaintFlowOnCurve(Canvas canvas, Float2 a, Float2 b, int dirA, int dirB, Color from, Color to, float width, float zoom, float time)
    {
        var (c1, c2) = BezierHandles(a, b, dirA, dirB);
        s_flowPts.Clear();
        for (int i = 0; i <= FlowSamples; i++)
            s_flowPts.Add(CubicAt(a, c1, c2, b, i / (float)FlowSamples));
        PaintFlowDots(canvas, s_flowPts, from, to, width, zoom, time);
    }

    private static void PaintFlowOnPath(Canvas canvas, List<Float2> pts, Color from, Color to, float width, float zoom, float time)
        => PaintFlowDots(canvas, pts, from, to, width, zoom, time);

    /// <summary>
    /// Spaces the dots by distance along the wire rather than by curve parameter. A cubic crawls near
    /// its ends and races through the middle, so stepping the parameter evenly makes every dot look
    /// like it is speeding up as it travels.
    /// </summary>
    private static void PaintFlowDots(Canvas canvas, IReadOnlyList<Float2> pts, Color from, Color to, float width, float zoom, float time)
    {
        if (pts.Count < 2) return;

        float total = 0f;
        for (int i = 1; i < pts.Count; i++)
            total += Dist(pts[i - 1].X, pts[i - 1].Y, pts[i].X, pts[i].Y);
        if (total <= 0.001f) return;

        float radius = FlowRadius(width, zoom);
        for (int d = 0; d < FlowDots; d++)
        {
            float fraction = Frac(time * 0.5f + d / (float)FlowDots);
            Float2 p = PointAtDistance(pts, fraction * total);
            // The dot takes the output port's colour and arrives in the input port's.
            canvas.CircleFilled(p.X, p.Y, radius, ToC32(MixColor(from, to, fraction), 1f));
        }
    }

    private static Color MixColor(Color a, Color b, float t)
    {
        float f = Math.Clamp(t, 0f, 1f);
        return Color.FromArgb(255,
            (int)(a.R + (b.R - a.R) * f),
            (int)(a.G + (b.G - a.G) * f),
            (int)(a.B + (b.B - a.B) * f));
    }

    private static Float2 PointAtDistance(IReadOnlyList<Float2> pts, float distance)
    {
        float walked = 0f;
        for (int i = 1; i < pts.Count; i++)
        {
            float seg = Dist(pts[i - 1].X, pts[i - 1].Y, pts[i].X, pts[i].Y);
            if (walked + seg >= distance && seg > 0.0001f)
            {
                float f = (distance - walked) / seg;
                return new Float2(pts[i - 1].X + (pts[i].X - pts[i - 1].X) * f, pts[i - 1].Y + (pts[i].Y - pts[i - 1].Y) * f);
            }
            walked += seg;
        }
        return pts[^1];
    }

    private static float Frac(float v) => v - MathF.Floor(v);

    private static Float2 CubicAt(Float2 p0, Float2 p1, Float2 p2, Float2 p3, float t)
    {
        float u = 1f - t;
        float w0 = u * u * u, w1 = 3f * u * u * t, w2 = 3f * u * t * t, w3 = t * t * t;
        return new Float2(
            p0.X * w0 + p1.X * w1 + p2.X * w2 + p3.X * w3,
            p0.Y * w0 + p1.Y * w1 + p2.Y * w2 + p3.Y * w3);
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
            Color32 col = s.ConnHitNode == null ? ToC32(s.Accent, 0.8f)
                : (s.ConnHitValid ? ToC32(_theme.Green.C500, 1f) : ToC32(_theme.Red.C500, 1f));
            PortSide srcSide = SideOf(ln, st.ConnPort, st.ConnFromOutput);
            PaintWire(canvas, a, b, DirForSide(srcSide), st.ConnFromOutput ? -1 : 1, col, 2.5f);
            canvas.CircleFilled(b.X, b.Y, 4f, col);
        }
    }

    private bool ValidateHit(GraphState st, string node, string port, bool output)
    {
        if (_onValidate == null) return true;
        bool sourcePlaceholder = IsPlaceholderPort(st.ConnNode!, st.ConnPort!, st.ConnFromOutput);
        bool hitPlaceholder = IsPlaceholderPort(node, port, output);
        var req = st.ConnFromOutput
            ? new ConnectionRequest(st.ConnNode!, st.ConnPort!, node, port, sourcePlaceholder, hitPlaceholder)
            : new ConnectionRequest(node, port, st.ConnNode!, st.ConnPort!, hitPlaceholder, sourcePlaceholder);
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

    // An open socket: a ring with a plus in it, so it reads as "drop one here" rather than a live port.
    private static void PaintPlaceholder(Canvas canvas, float cx, float cy, float r, Color32 fill, Color32 hole)
    {
        canvas.CircleFilled(cx, cy, r + 1.5f, hole);
        canvas.CircleFilled(cx, cy, r, fill);
        canvas.CircleFilled(cx, cy, Math.Max(0.5f, r - 1.4f), hole);

        float arm = Math.Max(1f, r - 1.2f);
        canvas.SaveState();
        canvas.SetStrokeColor(fill);
        canvas.SetStrokeWidth(Math.Max(0.9f, r * 0.34f));
        canvas.BeginPath(); canvas.MoveTo(cx - arm, cy); canvas.LineTo(cx + arm, cy); canvas.Stroke();
        canvas.BeginPath(); canvas.MoveTo(cx, cy - arm); canvas.LineTo(cx, cy + arm); canvas.Stroke();
        canvas.RestoreState();
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
