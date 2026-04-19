// This file is part of the Prowl Game Engine
// Licensed under the MIT License. See the LICENSE file in the project root for details.

using System;
using System.Collections.Generic;

using Prowl.Echo;
using Prowl.Editor.Docking;
using Prowl.Editor.Widgets;
using Prowl.PaperUI;
using Prowl.PaperUI.Events;
using Prowl.PaperUI.LayoutEngine;
using Prowl.Runtime;
using Prowl.Runtime.GraphTools;
using Prowl.Runtime.GraphTools.ShaderGraphs.Nodes;
using Prowl.Vector;

namespace Prowl.Editor.GraphTools;

/// <summary>
/// Generic node-graph editor window. Hosts any <see cref="Graph"/> subclass and routes
/// rendering through <see cref="GraphRendering"/> + <see cref="MinimapRenderer"/>.
/// Phase 2: pan / zoom / draw. Phase 3 will add interaction (drag, connect, select).
/// </summary>
[EditorWindow("Tools/Graph Editor")]
public class GraphEditorWindow : DockPanel
{
    private Graph? _graph;
    private GraphCanvasView? _view;

    /// <summary>Latest canvas-Box screen rect captured during draw — used by input handlers.</summary>
    private Rect _canvasScreenRect;

    // ─── Interaction state ────────────────────────────────────────────────────────────
    private enum DragMode { None, MoveNodes, MarqueeSelect, ConnectingWire }
    private DragMode _dragMode = DragMode.None;

    /// <summary>Reference to a port on a node — used as the "from" endpoint of a connecting drag.</summary>
    private struct PortRef
    {
        public Guid NodeId;
        public string PortName;
        public PortDirection Direction;
        public Type DataType;
    }

    private PortRef? _dragSourcePort;
    private Float2? _dragWireEndGraph;

    // Node-creation popup state.
    private bool _creationMenuOpen;
    private Float2 _creationMenuLocal;    // popup top-left in canvas-Box-local pixels
    private Float2 _creationMenuGraph;    // graph-space point where new node lands
    private string _creationFilter = "";

    /// <summary>Active marquee corners in graph space (start at drag start, end follows mouse).</summary>
    private Float2? _marqueeStartGraph;
    private Float2? _marqueeEndGraph;
    /// <summary>Captured at marquee start: do we add to existing selection (Ctrl/Shift) or replace.</summary>
    private bool _marqueeAdditive;

    /// <summary>Per-node positions captured at drag-start, used to register one undo step per drag.</summary>
    private Dictionary<Guid, Float2>? _dragMoveStartPositions;

    /// <summary>Mouse button currently held — captured on press so drag handlers know intent.</summary>
    private PaperMouseBtn _pressedButton = PaperMouseBtn.Unknown;

    /// <summary>Selected nodes (Guids).</summary>
    private readonly HashSet<Guid> _selectedNodes = new();

    /// <summary>Selected wires (Edge Guids).</summary>
    private readonly HashSet<Guid> _selectedEdges = new();

    /// <summary>Node currently under the cursor (recomputed each frame).</summary>
    private Guid? _hoveredNode;

    /// <summary>Port currently under the cursor (recomputed each frame). Highlighted while
    /// dragging a wire it would target.</summary>
    private (Guid nodeId, string portName, PortDirection direction)? _hoveredPort;

    public override string Title => _graph != null
        ? $"Graph — {_graph.Name}"
        : "Graph Editor";

    public override string Icon => EditorIcons.DiagramProject;

    public GraphEditorWindow() { }

    /// <summary>Open a floating editor window bound to the given graph asset.</summary>
    public static void OpenFor(Graph graph)
    {
        var panel = new GraphEditorWindow
        {
            _graph = graph,
            _view = new GraphCanvasView(graph),
        };
        EditorApplication.Instance?.OpenPanelInstance(panel, 1100, 720);
    }

    public override void OnGUI(Paper paper, float width, float height)
    {
        var font = EditorTheme.DefaultFont;
        if (font == null) return;

        if (_graph == null)
        {
            EditorGUI.Label(paper, "graph_empty",
                "No graph loaded. Open this window from a graph asset's inspector.");
            return;
        }
        _view ??= new GraphCanvasView(_graph);

        DrawToolbar(paper);
        DrawCanvas(paper, font);

        // Per-frame, post-layout: update hover and process keyboard shortcuts that only
        // apply when the canvas is focused (mouse over it).
        UpdateHoverAndShortcuts(paper);
    }

    /// <summary>
    /// Convert global Paper pointer position to graph space, hit-test against nodes,
    /// and handle hotkeys (Delete) that should only fire when the user is in the canvas.
    /// </summary>
    private void UpdateHoverAndShortcuts(Paper paper)
    {
        if (_view == null || _graph == null) return;
        if (!_canvasScreenRect.Contains(paper.PointerPos))
        {
            _hoveredNode = null; _hoveredPort = null;
            return;
        }

        // Middle-mouse pan — Paper's drag events only fire for left-mouse, so middle-button
        // pan is polled manually (same pattern as EditorCamera). Anywhere on the canvas,
        // including over nodes; matches the convention used by the Scene viewport.
        // Use Paper's PointerDelta so the pan delta is in Paper-logical space (matches the
        // canvas it's panning).
        if (Input.GetMouseButton(2))
            _view.PanBy(paper.PointerDelta);

        var graphMouse = ScreenToGraph(paper.PointerPos);
        var portHit = HitTestPort(graphMouse, out var portNode);
        _hoveredPort = (portHit.HasValue && portNode != null)
            ? ((Guid, string, PortDirection)?) (portNode.Id, portHit.Value.port.Name, portHit.Value.port.Direction)
            : null;
        _hoveredNode = HitTestNode(graphMouse)?.Id;

        // Shortcuts — only active while the canvas is hovered (so they don't steal typing
        // in the Inspector / blackboard). All IDs are registered in BuiltInShortcuts.
        // Undo/Redo are handled globally (Global/Undo, Global/Redo) → Editor.Undo.
        // Mutations below register via Undo.RegisterAction so global Ctrl+Z reaches them.
        if (ShortcutManager.IsPressed("GraphEditor/Delete"))
            DeleteSelected();
        else if (ShortcutManager.IsPressed("GraphEditor/Save"))
            SaveGraph();
        else if (ShortcutManager.IsPressed("GraphEditor/SelectAll"))
            SelectAll();
        else if (ShortcutManager.IsPressed("GraphEditor/Copy"))
            CopySelection();
        else if (ShortcutManager.IsPressed("GraphEditor/Paste"))
            PasteAt(graphMouse);
        else if (ShortcutManager.IsPressed("GraphEditor/Duplicate"))
            DuplicateSelection();
        else if (ShortcutManager.IsPressed("GraphEditor/FrameSelection"))
            FrameSelectionOrAll();

        if (_creationMenuOpen && Input.GetKeyDown(KeyCode.Escape))
            CloseCreationMenu();

        // While the popup is open as a wire-drop continuation, keep the in-progress wire
        // visually following the cursor so the user can see what they're connecting.
        if (_creationMenuOpen && _dragSourcePort.HasValue)
            _dragWireEndGraph = ScreenToGraph(paper.PointerPos);
    }

    // ─── Hit-testing ──────────────────────────────────────────────────────────────────
    /// <summary>
    /// Find the topmost node containing <paramref name="graphPoint"/>, or null. Iterates
    /// in reverse order because later list entries draw on top — last hit wins visually,
    /// so first hit in reverse iteration wins for click handling.
    /// </summary>
    private Node? HitTestNode(Float2 graphPoint)
    {
        if (_graph == null) return null;
        for (int i = _graph.Nodes.Count - 1; i >= 0; i--)
        {
            var n = _graph.Nodes[i];
            if (GraphLayout.GetNodeRect(n).Contains(graphPoint)) return n;
        }
        return null;
    }

    // ─── Toolbar ──────────────────────────────────────────────────────────────────────
    private void DrawToolbar(Paper paper)
    {
        using (paper.Row("graph_toolbar")
            .Height(28)
            .ChildLeft(8).ChildRight(8).RowBetween(8)
            .BackgroundColor(System.Drawing.Color.FromArgb(255, 36, 36, 40))
            .Enter())
        {
            paper.Box("graph_toolbar_title").Width(UnitValue.Auto).Height(28)
                .Text(_graph!.GetType().Name, EditorTheme.DefaultFont!)
                .TextColor(EditorTheme.Ink500)
                .FontSize(EditorTheme.FontSize)
                .Alignment(TextAlignment.MiddleLeft);

            paper.Box("graph_toolbar_spacer").Width(UnitValue.Stretch());

            // Zoom % indicator
            paper.Box("graph_toolbar_zoom").Width(70).Height(28)
                .Text($"{_view!.Zoom * 100:0}%", EditorTheme.DefaultFont!)
                .TextColor(EditorTheme.Ink400)
                .FontSize(EditorTheme.FontSize - 2)
                .Alignment(TextAlignment.MiddleRight);

            EditorGUI.Button(paper, "graph_tb_save", $"{EditorIcons.FloppyDisk}  Save", width: 80)
                .OnValueChanged(_ => SaveGraph());

            EditorGUI.Button(paper, "graph_tb_recenter", "Recenter", width: 90)
                .OnValueChanged(_ => RecenterView());

            // Phase-2 helper: rebuild a fixed demo graph so we can iterate on the renderer.
            // Each click clears + re-seeds so repeated presses don't stack overlapping copies.
            EditorGUI.Button(paper, "graph_tb_seed", "Reset Demo Graph", width: 160)
                .OnValueChanged(_ => SeedSampleNodes());
        }
    }

    private void SaveGraph()
    {
        if (_graph == null) return;
        try
        {
            EditorAssetDatabase.Instance?.SaveAsset(_graph);
            Debug.Log($"Saved {_graph.AssetPath}");
        }
        catch (Exception ex) { Debug.LogError($"Save failed: {ex.Message}"); }
    }


    // ─── Node creation popup ─────────────────────────────────────────────────────────
    private void DrawNodeCreationPopup(Paper paper)
    {
        if (_graph == null) return;

        const float popupW = 280f;
        const float popupH = 360f;

        // Click-outside backdrop — fullscreen invisible Box on the topmost layer that
        // catches clicks outside the popup and closes the menu (standard context UX).
        paper.Box("graph_popup_backdrop")
            .PositionType(PositionType.SelfDirected)
            .Position(-9999, -9999)
            .Size(99999, 99999)
            .Layer(Layer.Topmost)
            .OnClick(_ => CloseCreationMenu());

        using (paper.Column("graph_popup")
            .PositionType(PositionType.SelfDirected)
            .Position(_creationMenuLocal.X, _creationMenuLocal.Y)
            .Width(popupW).Height(popupH)
            .BackgroundColor(System.Drawing.Color.FromArgb(255, 38, 40, 48))
            .BorderColor(System.Drawing.Color.FromArgb(255, 80, 84, 96))
            .BorderWidth(1)
            .Rounded(6)
            .Layer(Layer.Topmost)
            .ClampToScreen()
            .ChildLeft(6).ChildRight(6).ChildTop(6).ChildBottom(6).ColBetween(4)
            .Enter())
        {
            paper.Box("graph_popup_title").Height(20)
                .Text("Add Node", EditorTheme.DefaultFont!)
                .TextColor(EditorTheme.Ink500).FontSize(EditorTheme.FontSize - 1)
                .Alignment(TextAlignment.MiddleLeft);

            EditorGUI.TextField(paper, "graph_popup_search", "", _creationFilter)
                .OnValueChanged(v => _creationFilter = v ?? "");

            // Filtered list of node entries.
            var entries = NodeRegistry.GetForMarker(_graph.NodeMarkerInterface);
            using (paper.Column("graph_popup_list")
                .Width(UnitValue.Stretch()).Height(UnitValue.Stretch())
                .Clip()
                .ColBetween(2)
                .Enter())
            {
                int shown = 0;
                foreach (var reg in entries)
                {
                    // Wire-drop popup: only show nodes that can accept the dragged wire.
                    if (_dragSourcePort.HasValue &&
                        !reg.HasCompatiblePort(_dragSourcePort.Value.DataType, _dragSourcePort.Value.Direction))
                        continue;

                    if (!MatchesFilter(reg, _creationFilter)) continue;
                    if (shown++ >= 50) break; // cap visible entries; future: virtualised scroll list
                    DrawCreationEntry(paper, reg);
                }
                if (shown == 0)
                {
                    paper.Box("graph_popup_empty").Height(20)
                        .Text("(no nodes match)", EditorTheme.DefaultFont!)
                        .TextColor(EditorTheme.Ink400).FontSize(EditorTheme.FontSize - 2)
                        .Alignment(TextAlignment.MiddleLeft);
                }
            }
        }
    }

    private void DrawCreationEntry(Paper paper, NodeRegistration reg)
    {
        string id = $"graph_popup_entry_{reg.Type.FullName}";
        using (paper.Row(id).Height(20)
            .ChildLeft(6).ChildRight(6).RowBetween(6)
            .BackgroundColor(System.Drawing.Color.FromArgb(255, 48, 50, 58))
            .Hovered.BackgroundColor(System.Drawing.Color.FromArgb(255, 64, 70, 90)).End()
            .Rounded(3)
            .OnClick(reg, (cap, _) => SpawnNode(cap.Type))
            .Enter())
        {
            paper.Box($"{id}_title").Height(20)
                .Text(reg.Title, EditorTheme.DefaultFont!)
                .TextColor(EditorTheme.Ink500).FontSize(EditorTheme.FontSize - 2)
                .Alignment(TextAlignment.MiddleLeft);

            paper.Box($"{id}_cat").Width(UnitValue.Auto).Height(20)
                .Text(reg.Category, EditorTheme.DefaultFont!)
                .TextColor(EditorTheme.Ink400).FontSize(EditorTheme.FontSize - 3)
                .Alignment(TextAlignment.MiddleRight);
        }
    }

    private void SpawnNode(Type nodeType)
    {
        if (_graph == null) return;
        try
        {
            var node = (Node)Activator.CreateInstance(nodeType)!;
            // If popup was opened from a wire-drop, slide the new node up/left a bit so
            // its first input port roughly meets the cursor.
            var spawnPos = _creationMenuGraph;
            if (_dragSourcePort?.Direction == PortDirection.Output)
                spawnPos -= new Float2(0, GraphLayout.HeaderHeight + GraphLayout.PortRowHeight * 0.5f);
            node.Position = spawnPos;
            _graph.AddNode(node);

            // Register the spawn (must come before AutoConnectFromDrop, which itself
            // pushes a separate Connect Wire action — undo will roll back in reverse).
            var graph = _graph;
            var spawned = node;
            Undo.RegisterAction($"Add Node ({nodeType.Name})",
                undo: () => graph.RemoveNode(spawned.Id),
                redo: () => graph.Nodes.Add(spawned));

            // If popup was opened from a wire-drop, hook up the connection now that the
            // new node has its ports defined.
            if (_dragSourcePort.HasValue)
                AutoConnectFromDrop(node);

            CloseCreationMenu();
        }
        catch (Exception ex) { Debug.LogError($"Failed to spawn {nodeType.Name}: {ex.Message}"); }
    }

    private void CloseCreationMenu()
    {
        _creationMenuOpen = false;
        // Whether the popup was triggered by a wire-drop or a plain right-click, clear
        // the dangling wire state so it stops rendering once the menu closes.
        _dragSourcePort = null;
        _dragWireEndGraph = null;
    }

    private static bool MatchesFilter(NodeRegistration reg, string filter)
    {
        if (string.IsNullOrEmpty(filter)) return true;
        return reg.Title.Contains(filter, StringComparison.OrdinalIgnoreCase)
            || reg.Category.Contains(filter, StringComparison.OrdinalIgnoreCase);
    }

    // ─── Canvas ───────────────────────────────────────────────────────────────────────
    private void DrawCanvas(Paper paper, Prowl.Scribe.FontFile font)
    {
        // Single canvas Box owns the viewport, its events, and its full draw via
        // OnPostLayout → paper.Draw → RenderCanvas. Everything (grid, groups, nodes,
        // wires, drag-overlay, marquee, minimap) is drawn through Quill in one pass —
        // no per-node Paper boxes.
        using (paper.Box("graph_canvas")
            .Width(UnitValue.Stretch())
            .Height(UnitValue.Stretch())
            .Clip()
            .OnScroll(HandleScroll)
            .OnPress(HandlePress)
            .OnRelease(HandleRelease)
            .OnClick(HandleClick)
            .OnRightClick(HandleRightClick)
            .OnDragStart(HandleDragStart)
            .OnDragging(HandleDragging)
            .OnDragEnd(HandleDragEnd)
            .OnPostLayout((handle, rect) =>
            {
                _canvasScreenRect = rect;
                paper.Draw(ref handle, (canvas, r) => RenderCanvas(canvas, r, font));
            })
            .Enter())
        {
            // Popup declared INSIDE the canvas Box so its SelfDirected .Position(x,y) is
            // relative to the canvas origin — we convert the stored screen-pixel cursor
            // position into canvas-local space below. Layer.Topmost + ClampToScreen keep
            // it visible even near the viewport edges.
            if (_creationMenuOpen)
                DrawNodeCreationPopup(paper);
        }
    }

    /// <summary>
    /// Single-pass canvas render. Coordinates: <paramref name="screenRect"/> is the
    /// canvas Box's screen-pixel rect. We translate by its origin then apply the view
    /// transform so subsequent draws are in graph space; that lets <see cref="GraphRendering"/>
    /// helpers stay simple.
    /// </summary>
    private void RenderCanvas(Prowl.Quill.Canvas canvas, Rect screenRect, Prowl.Scribe.FontFile font)
    {
        if (_graph == null || _view == null) return;

        // Background fills the whole viewport in untransformed screen pixels.
        GraphRendering.DrawBackground(canvas, screenRect);

        // Compute the visible graph-space rect for the grid LOD math.
        var topLeftGraph = _view.CanvasToGraph(Float2.Zero);
        var bottomRightGraph = _view.CanvasToGraph(
            new Float2((float)screenRect.Size.X, (float)screenRect.Size.Y));
        var visibleGraphRect = new Rect(topLeftGraph.X, topLeftGraph.Y,
                                         bottomRightGraph.X, bottomRightGraph.Y);

        canvas.SaveState();
        canvas.TransformBy(Prowl.Vector.Spatial.Transform2D.CreateTranslation(
            (float)screenRect.Min.X, (float)screenRect.Min.Y));
        _view.ApplyTransform(canvas);

        // Graph-space layers (drawn back-to-front).
        GraphRendering.DrawGrid(canvas, visibleGraphRect, _view.Zoom);
        foreach (var g in _graph.Groups) GraphRendering.DrawGroup(canvas, g);
        foreach (var note in _graph.StickyNotes)
            GraphRendering.DrawStickyNote(canvas, note, _view.Zoom, font);

        // Wires under nodes so the node body covers the wire root for a clean look.
        foreach (var edge in _graph.Edges)
        {
            var srcNode = _graph.FindNode(edge.SourceNodeId);
            var dstNode = _graph.FindNode(edge.TargetNodeId);
            if (srcNode == null || dstNode == null) continue;
            var srcPos = GraphLayout.TryGetPortPosition(srcNode, edge.SourcePortName, PortDirection.Output);
            var dstPos = GraphLayout.TryGetPortPosition(dstNode, edge.TargetPortName, PortDirection.Input);
            if (!srcPos.HasValue || !dstPos.HasValue) continue;

            var srcPort = srcNode.GetOutput(edge.SourcePortName);
            var baseColor = srcPort != null
                ? GraphLayout.GetPortColor(srcPort.DataType)
                : new Color32(170, 170, 170, 255);
            bool selected = _selectedEdges.Contains(edge.Id);
            var color = selected ? new Color32(255, 200, 80, 255) : baseColor;
            float thickness = selected ? 5.0f : 2.5f;
            GraphRendering.DrawWire(canvas, srcPos.Value, dstPos.Value, color, _view.Zoom, thickness);
        }

        foreach (var node in _graph.Nodes)
        {
            bool isSel = _selectedNodes.Contains(node.Id);
            bool isHov = _hoveredNode == node.Id;
            (string portName, PortDirection direction)? hovPort = null;
            if (_hoveredPort.HasValue && _hoveredPort.Value.nodeId == node.Id)
                hovPort = (_hoveredPort.Value.portName, _hoveredPort.Value.direction);
            GraphRendering.DrawNode(canvas, _graph, node, isSel, isHov, hovPort, _view.Zoom, font);
        }

        // In-progress drag wire — graph space; source port position via GraphLayout.
        if (_dragSourcePort.HasValue && _dragWireEndGraph.HasValue)
        {
            var src = _dragSourcePort.Value;
            var srcNode = _graph.FindNode(src.NodeId);
            if (srcNode != null)
            {
                var srcPos = GraphLayout.TryGetPortPosition(srcNode, src.PortName, src.Direction);
                if (srcPos.HasValue)
                {
                    var color = GraphLayout.GetPortColor(src.DataType);
                    if (src.Direction == PortDirection.Output)
                        GraphRendering.DrawDragWire(canvas, srcPos.Value, _dragWireEndGraph.Value, color, _view.Zoom);
                    else
                        GraphRendering.DrawDragWire(canvas, _dragWireEndGraph.Value, srcPos.Value, color, _view.Zoom);
                }
            }
        }

        // Marquee — graph space.
        if (_dragMode == DragMode.MarqueeSelect && _marqueeStartGraph.HasValue && _marqueeEndGraph.HasValue)
        {
            var rect = MakeMarqueeRect(_marqueeStartGraph.Value, _marqueeEndGraph.Value);
            GraphRendering.DrawMarquee(canvas, rect, _view.Zoom);
        }

        canvas.RestoreState();

        // Minimap is screen-space (drawn after restore so it isn't pan/zoomed).
        MinimapRenderer.Draw(canvas, screenRect, _graph, _view);
    }

    // ─── Input handling ──────────────────────────────────────────────────────────────

    private void HandleScroll(ScrollEvent e)
    {
        if (_view == null) return;
        // Scroll up = zoom in. e.Delta is typically ±1; raised to a smooth factor.
        float factor = e.Delta > 0 ? GraphCanvasView.ZoomStep : 1.0f / GraphCanvasView.ZoomStep;
        // ElementEvent.RelativePosition is already mouse-relative-to-element-top-left,
        // i.e. our canvas-local pixel space. Anchor zoom to cursor.
        _view.ZoomBy(factor, e.RelativePosition);
    }

    private void HandlePress(ClickEvent e)
    {
        // Capture which mouse button started this interaction so the upcoming drag handlers
        // can branch on it (DragEvent itself doesn't carry button info).
        _pressedButton = e.Button;
    }

    /// <summary>
    /// Right-click on empty canvas opens the node-creation popup at the cursor;
    /// right-click on a node could open a context menu (Phase 4 — for now no-op).
    /// </summary>
    private void HandleRightClick(ClickEvent e)
    {
        if (_view == null || _graph == null) return;
        var graphPoint = ScreenToGraph(e.PointerPosition);
        if (HitTestNode(graphPoint) != null) return; // node context menu = future work

        _creationMenuOpen = true;
        _creationMenuLocal = e.PointerPosition - new Float2(
            (float)_canvasScreenRect.Min.X, (float)_canvasScreenRect.Min.Y);
        _creationMenuGraph = graphPoint;
        _creationFilter = "";
    }

    private void HandleRelease(ClickEvent e)
    {
        _pressedButton = PaperMouseBtn.Unknown;
    }

    /// <summary>
    /// Fires on a left-click that didn't turn into a drag. Used for plain selection.
    /// Drag-then-release intentionally doesn't trigger this.
    /// </summary>
    private void HandleClick(ClickEvent e)
    {
        if (_view == null || _graph == null) return;
        if (e.Button != PaperMouseBtn.Left) return;

        var graphPoint = ScreenToGraph(e.PointerPosition);
        var hit = HitTestNode(graphPoint);

        bool additive = Input.IsCtrlPressed || Input.IsShiftPressed;

        if (hit != null)
        {
            // Hit a node — selection logic.
            if (additive)
            {
                if (!_selectedNodes.Add(hit.Id)) _selectedNodes.Remove(hit.Id); // toggle
            }
            else
            {
                _selectedNodes.Clear();
                _selectedEdges.Clear();
                _selectedNodes.Add(hit.Id);
            }
            SyncSelectionSystem();
            return;
        }

        // Empty space — but maybe near a wire? Wire hit-test uses a screen-pixel tolerance
        // so the catch zone stays the same regardless of zoom.
        const float wireHitPixels = 6f;
        float toleranceGraph = wireHitPixels / _view.Zoom;
        var wireHit = HitTestWire(graphPoint, toleranceGraph);
        if (wireHit != null)
        {
            if (additive)
            {
                if (!_selectedEdges.Add(wireHit.Id)) _selectedEdges.Remove(wireHit.Id);
            }
            else
            {
                _selectedNodes.Clear();
                _selectedEdges.Clear();
                _selectedEdges.Add(wireHit.Id);
            }
            SyncSelectionSystem();
            return;
        }

        // Truly empty — clear selection unless adding.
        if (!additive)
        {
            _selectedNodes.Clear();
            _selectedEdges.Clear();
            SyncSelectionSystem();
        }
    }

    /// <summary>
    /// Find the nearest wire within <paramref name="toleranceGraph"/> graph-units of
    /// <paramref name="graphPoint"/>, or null. Approximates point-to-bezier distance via
    /// the same 16-sample sweep used elsewhere.
    /// </summary>
    private Edge? HitTestWire(Float2 graphPoint, float toleranceGraph)
    {
        if (_graph == null) return null;
        float bestSq = toleranceGraph * toleranceGraph;
        Edge? bestEdge = null;
        foreach (var edge in _graph.Edges)
        {
            var srcNode = _graph.FindNode(edge.SourceNodeId);
            var dstNode = _graph.FindNode(edge.TargetNodeId);
            if (srcNode == null || dstNode == null) continue;
            var srcPos = GraphLayout.TryGetPortPosition(srcNode, edge.SourcePortName, PortDirection.Output);
            var dstPos = GraphLayout.TryGetPortPosition(dstNode, edge.TargetPortName, PortDirection.Input);
            if (!srcPos.HasValue || !dstPos.HasValue) continue;

            float dSq = GraphLayout.DistanceSqToWire(srcPos.Value, dstPos.Value, graphPoint);
            if (dSq < bestSq) { bestSq = dSq; bestEdge = edge; }
        }
        return bestEdge;
    }

    /// <summary>
    /// Drag began. Decide what kind of drag this is based on the captured button + what's
    /// under the cursor. Stores the mode in <see cref="_dragMode"/> for the duration.
    /// </summary>
    private void HandleDragStart(DragEvent e)
    {
        if (_view == null || _graph == null) return;

        // Pan is handled outside the Paper drag pipeline (Paper only fires drag events for
        // the primary/left button) — see middle-mouse poll in UpdateHoverAndShortcuts.
        if (_pressedButton == PaperMouseBtn.Left)
        {
            var graphPoint = ScreenToGraph(e.StartPosition);

            // Port hit-test wins over node hit-test so users can drag from a port at the
            // very edge of a node without accidentally moving the whole node.
            var portHit = HitTestPort(graphPoint, out var portNode);
            if (portHit.HasValue && portNode != null)
            {
                _dragSourcePort = new PortRef
                {
                    NodeId = portNode.Id,
                    PortName = portHit.Value.port.Name,
                    Direction = portHit.Value.port.Direction,
                    DataType = portHit.Value.port.DataType,
                };
                _dragWireEndGraph = graphPoint;
                _dragMode = DragMode.ConnectingWire;
                return;
            }

            // Left-click on a node → drag-move it (and any other selected nodes).
            // Left-click on empty space → marquee select.
            var hit = HitTestNode(graphPoint);
            if (hit != null)
            {
                if (!_selectedNodes.Contains(hit.Id))
                {
                    if (!Input.IsCtrlPressed && !Input.IsShiftPressed) _selectedNodes.Clear();
                    _selectedNodes.Add(hit.Id);
                    SyncSelectionSystem();
                }
                // Capture node positions before the drag so we can register a single undo
                // step at drag-end with before/after positions.
                _dragMoveStartPositions = new Dictionary<Guid, Float2>();
                foreach (var id in _selectedNodes)
                {
                    var n = _graph.FindNode(id);
                    if (n != null) _dragMoveStartPositions[id] = n.Position;
                }
                _dragMode = DragMode.MoveNodes;
            }
            else
            {
                _dragMode = DragMode.MarqueeSelect;
                _marqueeStartGraph = graphPoint;
                _marqueeEndGraph = graphPoint;
                _marqueeAdditive = Input.IsCtrlPressed || Input.IsShiftPressed;
            }
        }
    }

    /// <summary>
    /// Find a port near the given graph-space point. Returns the port + its parent node,
    /// or null if the point isn't close to any port.
    /// </summary>
    private (Port port, int index)? HitTestPort(Float2 graphPoint, out Node? owningNode)
    {
        owningNode = null;
        if (_graph == null) return null;
        for (int i = _graph.Nodes.Count - 1; i >= 0; i--)
        {
            var n = _graph.Nodes[i];
            var hit = GraphLayout.HitTestPort(n, graphPoint);
            if (hit.HasValue) { owningNode = n; return hit; }
        }
        return null;
    }

    private void HandleDragging(DragEvent e)
    {
        if (_view == null || _graph == null) return;

        switch (_dragMode)
        {
            case DragMode.MoveNodes:
                // Convert screen-pixel delta to graph-space delta (just divide by zoom).
                var graphDelta = e.Delta / _view.Zoom;
                foreach (var id in _selectedNodes)
                {
                    var node = _graph.FindNode(id);
                    if (node != null) node.Position += graphDelta;
                }
                break;

            case DragMode.MarqueeSelect:
                _marqueeEndGraph = ScreenToGraph(e.PointerPosition);
                break;

            case DragMode.ConnectingWire:
                _dragWireEndGraph = ScreenToGraph(e.PointerPosition);
                break;
        }
    }

    private void HandleDragEnd(DragEvent e)
    {
        // If a node-move drag is ending, register the start→end positions as a single
        // undo step (only if anything actually moved).
        if (_dragMode == DragMode.MoveNodes && _dragMoveStartPositions != null && _graph != null)
        {
            var graph = _graph;
            var starts = _dragMoveStartPositions;
            var ends = new Dictionary<Guid, Float2>();
            bool anyMoved = false;
            foreach (var kv in starts)
            {
                var n = graph.FindNode(kv.Key);
                if (n == null) continue;
                ends[kv.Key] = n.Position;
                if (!n.Position.Equals(kv.Value)) anyMoved = true;
            }
            if (anyMoved)
            {
                Undo.RegisterAction("Move Nodes",
                    undo: () => { foreach (var kv in starts) { var n = graph.FindNode(kv.Key); if (n != null) n.Position = kv.Value; } },
                    redo: () => { foreach (var kv in ends)   { var n = graph.FindNode(kv.Key); if (n != null) n.Position = kv.Value; } });
            }
            _dragMoveStartPositions = null;
        }

        if (_dragMode == DragMode.MarqueeSelect && _marqueeStartGraph.HasValue && _marqueeEndGraph.HasValue && _graph != null)
        {
            var rect = MakeMarqueeRect(_marqueeStartGraph.Value, _marqueeEndGraph.Value);
            if (!_marqueeAdditive) { _selectedNodes.Clear(); _selectedEdges.Clear(); }

            foreach (var n in _graph.Nodes)
                if (GraphLayout.GetNodeRect(n).Intersects(rect))
                    _selectedNodes.Add(n.Id);

            // Wire hit: any sample point on the bezier inside the marquee rect counts.
            foreach (var edge in _graph.Edges)
            {
                var srcNode = _graph.FindNode(edge.SourceNodeId);
                var dstNode = _graph.FindNode(edge.TargetNodeId);
                if (srcNode == null || dstNode == null) continue;
                var srcPos = GraphLayout.TryGetPortPosition(srcNode, edge.SourcePortName, PortDirection.Output);
                var dstPos = GraphLayout.TryGetPortPosition(dstNode, edge.TargetPortName, PortDirection.Input);
                if (srcPos.HasValue && dstPos.HasValue && WireIntersectsRect(srcPos.Value, dstPos.Value, rect))
                    _selectedEdges.Add(edge.Id);
            }
            SyncSelectionSystem();
        }
        else if (_dragMode == DragMode.ConnectingWire && _dragSourcePort.HasValue && _graph != null)
        {
            var graphPoint = ScreenToGraph(e.PointerPosition);
            bool connected = TryFinaliseConnection(graphPoint);
            if (!connected)
            {
                // Drop on empty space → open creation popup filtered to compatible nodes.
                // Wire state is intentionally NOT cleared here so the in-progress wire keeps
                // rendering while the popup is open; SpawnNode / popup-close will clear it.
                _creationMenuOpen = true;
                _creationMenuLocal = e.PointerPosition - new Float2(
                    (float)_canvasScreenRect.Min.X, (float)_canvasScreenRect.Min.Y);
                _creationMenuGraph = graphPoint;
                _creationFilter = "";
                _dragMode = DragMode.None;
                _marqueeStartGraph = null; _marqueeEndGraph = null;
                return;
            }

            _dragSourcePort = null;
            _dragWireEndGraph = null;
        }

        _dragMode = DragMode.None;
        _marqueeStartGraph = null;
        _marqueeEndGraph = null;
        _dragSourcePort = null;
        _dragWireEndGraph = null;
    }

    /// <summary>
    /// Try to connect the dragged source port to a port found at <paramref name="graphPoint"/>.
    /// Returns true on success.
    /// </summary>
    private bool TryFinaliseConnection(Float2 graphPoint)
    {
        if (_graph == null || !_dragSourcePort.HasValue) return false;
        var targetHit = HitTestPort(graphPoint, out var targetNode);
        if (!targetHit.HasValue || targetNode == null) return false;
        return ConnectPorts(_dragSourcePort.Value, targetNode, targetHit.Value.port);
    }

    /// <summary>
    /// Core connect routine: validates direction (output→input — auto-flipped),
    /// node mismatch, type compatibility; replaces any existing edge on a
    /// single-connection input. Returns true if a wire was created.
    /// </summary>
    private bool ConnectPorts(PortRef source, Node targetNode, Port targetPort)
    {
        if (_graph == null) return false;

        // Same node = invalid (no self-loops). Same direction = invalid.
        if (targetNode.Id == source.NodeId) return false;
        if (targetPort.Direction == source.Direction) return false;

        // Canonicalise to output → input.
        Guid outNodeId, inNodeId;
        string outPortName, inPortName;
        Port outPort, inPort;
        if (source.Direction == PortDirection.Output)
        {
            outNodeId = source.NodeId; outPortName = source.PortName;
            inNodeId = targetNode.Id; inPortName = targetPort.Name;
            var srcNode = _graph.FindNode(source.NodeId);
            outPort = srcNode?.GetOutput(source.PortName)!;
            inPort = targetPort;
        }
        else
        {
            outNodeId = targetNode.Id; outPortName = targetPort.Name;
            inNodeId = source.NodeId; inPortName = source.PortName;
            outPort = targetPort;
            var srcNode = _graph.FindNode(source.NodeId);
            inPort = srcNode?.GetInput(source.PortName)!;
        }

        if (outPort == null || inPort == null) return false;
        if (!GraphLayout.ArePortsCompatible(outPort, inPort)) return false;

        // Capture any displaced edges (single-connection input replacement) so undo can
        // restore them. Then add the new edge.
        List<Edge>? displaced = null;
        if (!inPort.AcceptsMultiple)
        {
            displaced = _graph.Edges.FindAll(e => e.TargetNodeId == inNodeId && e.TargetPortName == inPortName);
            foreach (var d in displaced) _graph.Edges.Remove(d);
        }
        var added = new Edge
        {
            SourceNodeId = outNodeId, SourcePortName = outPortName,
            TargetNodeId = inNodeId, TargetPortName = inPortName,
        };
        _graph.Edges.Add(added);

        var graph = _graph;
        Undo.RegisterAction("Connect Wire",
            undo: () =>
            {
                graph.Edges.Remove(added);
                if (displaced != null) graph.Edges.AddRange(displaced);
            },
            redo: () =>
            {
                if (displaced != null)
                    foreach (var d in displaced) graph.Edges.Remove(d);
                graph.Edges.Add(added);
            });

        return true;
    }

    /// <summary>
    /// After a wire-drop popup spawns a node, find the new node's first port that's
    /// compatible with the dragged source and create the edge.
    /// </summary>
    private void AutoConnectFromDrop(Node newNode)
    {
        if (!_dragSourcePort.HasValue) return;
        var src = _dragSourcePort.Value;
        var neededDir = src.Direction == PortDirection.Output ? PortDirection.Input : PortDirection.Output;
        var ports = neededDir == PortDirection.Input ? newNode.Inputs : newNode.Outputs;
        foreach (var p in ports)
            if (ConnectPorts(src, newNode, p)) return;
    }

    /// <summary>Build a normalized Rect from two corner points (handles dragging in any direction).</summary>
    private static Rect MakeMarqueeRect(Float2 a, Float2 b)
    {
        float minX = MathF.Min(a.X, b.X), maxX = MathF.Max(a.X, b.X);
        float minY = MathF.Min(a.Y, b.Y), maxY = MathF.Max(a.Y, b.Y);
        return new Rect(minX, minY, maxX, maxY);
    }

    /// <summary>True if any sample point along the wire's bezier falls inside <paramref name="rect"/>.</summary>
    private static bool WireIntersectsRect(Float2 from, Float2 to, Rect rect)
    {
        float dx = MathF.Abs(to.X - from.X);
        float tangent = MathF.Max(40f, dx * 0.5f);
        Float2 c1 = new Float2(from.X + tangent, from.Y);
        Float2 c2 = new Float2(to.X - tangent, to.Y);
        const int samples = 16;
        for (int i = 0; i <= samples; i++)
        {
            float t = i / (float)samples;
            float u = 1f - t;
            float b0 = u * u * u, b1 = 3 * u * u * t, b2 = 3 * u * t * t, b3 = t * t * t;
            float bx = b0 * from.X + b1 * c1.X + b2 * c2.X + b3 * to.X;
            float by = b0 * from.Y + b1 * c1.Y + b2 * c2.Y + b3 * to.Y;
            if (rect.Contains(new Float2(bx, by))) return true;
        }
        return false;
    }

    private void DeleteSelected()
    {
        if (_graph == null) return;

        // Snapshot what we're about to remove so undo can restore it. Node removal also
        // drops any edges attached to those nodes, which we capture separately.
        var removedNodes = new List<Node>();
        var removedEdges = new List<Edge>();

        foreach (var id in _selectedNodes)
        {
            var n = _graph.FindNode(id);
            if (n == null) continue;
            removedNodes.Add(n);
            // Edges attached to this node are about to be implicitly removed.
            foreach (var e in _graph.Edges)
                if (e.SourceNodeId == id || e.TargetNodeId == id) removedEdges.Add(e);
        }
        foreach (var id in _selectedEdges)
        {
            var e = _graph.Edges.Find(x => x.Id == id);
            if (e != null && !removedEdges.Contains(e)) removedEdges.Add(e);
        }

        if (removedNodes.Count == 0 && removedEdges.Count == 0) return;

        foreach (var n in removedNodes) _graph.RemoveNode(n.Id);
        if (_selectedEdges.Count > 0)
            _graph.Edges.RemoveAll(e => _selectedEdges.Contains(e.Id));

        var graph = _graph;
        Undo.RegisterAction("Delete Graph Elements",
            undo: () =>
            {
                foreach (var n in removedNodes) graph.Nodes.Add(n);
                foreach (var e in removedEdges) graph.Edges.Add(e);
            },
            redo: () =>
            {
                foreach (var n in removedNodes) graph.RemoveNode(n.Id);
                foreach (var e in removedEdges) graph.Edges.Remove(e);
            });

        _selectedNodes.Clear();
        _selectedEdges.Clear();
        SyncSelectionSystem();
    }

    /// <summary>
    /// Push our internal node-selection set to the global <see cref="Selection"/> system so
    /// the Inspector panel shows the selected node(s). Called whenever node selection
    /// changes. No-op when nothing is selected — we don't want to clear the Inspector just
    /// because the user clicked an empty part of the canvas.
    /// </summary>
    private void SyncSelectionSystem()
    {
        if (_graph == null || _selectedNodes.Count == 0) return;
        bool first = true;
        foreach (var id in _selectedNodes)
        {
            var n = _graph.FindNode(id);
            if (n == null) continue;
            if (first) { Selection.Select(n); first = false; }
            else Selection.AddToSelection(n);
        }
    }

    /// <summary>
    /// Convert a Paper screen-pixel point to graph-space coordinates via the current view.
    /// </summary>
    private Float2 ScreenToGraph(Float2 screenPos)
    {
        var canvasLocal = new Float2(
            screenPos.X - (float)_canvasScreenRect.Min.X,
            screenPos.Y - (float)_canvasScreenRect.Min.Y);
        return _view!.CanvasToGraph(canvasLocal);
    }

    // ─── Clipboard & selection ops ───────────────────────────────────────────────────
    // Serialize selected nodes (and the edges entirely within the selection) to the OS
    // clipboard as Echo text, same pattern as GameObjectClipboard so graph copies survive
    // across editor sessions and can be inspected as plain text.

    private const string ClipboardHeader = "ProwlGraphNodes:";

    private void SelectAll()
    {
        if (_graph == null) return;
        _selectedNodes.Clear();
        _selectedEdges.Clear();
        foreach (var n in _graph.Nodes) _selectedNodes.Add(n.Id);
        foreach (var e in _graph.Edges) _selectedEdges.Add(e.Id);
        SyncSelectionSystem();
    }

    private void CopySelection()
    {
        if (_graph == null || _selectedNodes.Count == 0) return;

        var nodesList = EchoObject.NewList();
        foreach (var id in _selectedNodes)
        {
            var n = _graph.FindNode(id);
            if (n == null) continue;
            var echo = Serializer.Serialize(typeof(object), n);
            if (echo != null) nodesList.ListAdd(echo);
        }

        // Only keep edges where BOTH endpoints are in the copied selection — a half-edge
        // pasted into a new graph would dangle.
        var edgesList = EchoObject.NewList();
        foreach (var e in _graph.Edges)
        {
            if (!_selectedNodes.Contains(e.SourceNodeId)) continue;
            if (!_selectedNodes.Contains(e.TargetNodeId)) continue;
            var echo = Serializer.Serialize(typeof(object), e);
            if (echo != null) edgesList.ListAdd(echo);
        }

        var root = EchoObject.NewCompound();
        root["nodes"] = nodesList;
        root["edges"] = edgesList;
        Input.Clipboard = ClipboardHeader + root.WriteToString();
    }

    /// <summary>
    /// Paste nodes from the clipboard near <paramref name="graphPoint"/>. Assigns fresh
    /// Ids so pasting into the same graph doesn't collide, and remaps edges through the
    /// id map. Registers a single Undo step for the whole paste.
    /// </summary>
    private void PasteAt(Float2 graphPoint)
    {
        if (_graph == null) return;

        string? text = Input.Clipboard;
        if (string.IsNullOrEmpty(text) || !text.StartsWith(ClipboardHeader)) return;
        var root = EchoObject.ReadFromString(text[ClipboardHeader.Length..]);
        if (root == null || root.TagType != EchoType.Compound) return;

        var nodesEcho = root["nodes"];
        var edgesEcho = root["edges"];
        if (nodesEcho == null || nodesEcho.TagType != EchoType.List) return;

        var added = new List<Node>();
        var idMap = new Dictionary<Guid, Guid>(); // old id → new id
        Float2 bboxMin = new Float2(float.MaxValue), bboxMax = new Float2(float.MinValue);

        foreach (var item in nodesEcho.List)
        {
            var n = Serializer.Deserialize<Node>(item);
            if (n == null) continue;
            var oldId = n.Id;
            n.Id = Guid.NewGuid();
            idMap[oldId] = n.Id;
            bboxMin = new Float2(MathF.Min(bboxMin.X, n.Position.X), MathF.Min(bboxMin.Y, n.Position.Y));
            bboxMax = new Float2(MathF.Max(bboxMax.X, n.Position.X), MathF.Max(bboxMax.Y, n.Position.Y));
            added.Add(n);
        }
        if (added.Count == 0) return;

        // Offset so the group's centre lands at the paste point.
        Float2 offset = graphPoint - (bboxMin + bboxMax) * 0.5f;
        foreach (var n in added) n.Position += offset;

        var addedEdges = new List<Edge>();
        if (edgesEcho != null && edgesEcho.TagType == EchoType.List)
        {
            foreach (var item in edgesEcho.List)
            {
                var e = Serializer.Deserialize<Edge>(item);
                if (e == null) continue;
                if (!idMap.TryGetValue(e.SourceNodeId, out var srcNew)) continue;
                if (!idMap.TryGetValue(e.TargetNodeId, out var dstNew)) continue;
                e.Id = Guid.NewGuid();
                e.SourceNodeId = srcNew;
                e.TargetNodeId = dstNew;
                addedEdges.Add(e);
            }
        }

        foreach (var n in added) _graph.Nodes.Add(n);
        foreach (var e in addedEdges) _graph.Edges.Add(e);

        // Select the paste result so users can immediately drag-reposition / delete.
        _selectedNodes.Clear();
        _selectedEdges.Clear();
        foreach (var n in added) _selectedNodes.Add(n.Id);
        SyncSelectionSystem();

        var graph = _graph;
        Undo.RegisterAction("Paste Graph Elements",
            undo: () =>
            {
                foreach (var e in addedEdges) graph.Edges.Remove(e);
                foreach (var n in added) graph.RemoveNode(n.Id);
            },
            redo: () =>
            {
                foreach (var n in added) graph.Nodes.Add(n);
                foreach (var e in addedEdges) graph.Edges.Add(e);
            });
    }

    /// <summary>
    /// Ctrl+D duplicate — copy the current selection then paste with a small offset.
    /// Implemented as copy + paste against the selection's current bounds so it doesn't
    /// teleport to the cursor (users expect duplicate to land near the original).
    /// </summary>
    private void DuplicateSelection()
    {
        if (_graph == null || _selectedNodes.Count == 0) return;
        // Compute selection centre to use as paste anchor (shifted right-down so copies
        // don't render exactly on top of the originals).
        Float2 sum = Float2.Zero; int count = 0;
        foreach (var id in _selectedNodes)
        {
            var n = _graph.FindNode(id);
            if (n == null) continue;
            sum += n.Position;
            count++;
        }
        if (count == 0) return;
        Float2 center = sum / count + new Float2(24, 24);
        CopySelection();
        PasteAt(center);
    }

    /// <summary>F shortcut — frame the selection, or the whole graph if nothing is selected.</summary>
    private void FrameSelectionOrAll()
    {
        if (_graph == null || _view == null) return;

        Float2 min, max;
        bool hasBounds;
        if (_selectedNodes.Count > 0)
        {
            min = new Float2(float.MaxValue);
            max = new Float2(float.MinValue);
            hasBounds = false;
            foreach (var id in _selectedNodes)
            {
                var n = _graph.FindNode(id);
                if (n == null) continue;
                var r = GraphLayout.GetNodeRect(n);
                min = new Float2(MathF.Min(min.X, (float)r.Min.X), MathF.Min(min.Y, (float)r.Min.Y));
                max = new Float2(MathF.Max(max.X, (float)r.Max.X), MathF.Max(max.Y, (float)r.Max.Y));
                hasBounds = true;
            }
        }
        else
        {
            hasBounds = GraphLayout.ComputeGraphBounds(_graph, out min, out max);
        }

        if (!hasBounds) { _view.ResetView(); return; }
        var size = new Float2((float)_canvasScreenRect.Size.X, (float)_canvasScreenRect.Size.Y);
        _view.FrameBounds(min, max, size);
    }

    // ─── Toolbar actions ─────────────────────────────────────────────────────────────
    private void RecenterView()
    {
        if (_graph == null || _view == null) return;
        if (GraphLayout.ComputeGraphBounds(_graph, out var min, out var max))
        {
            var size = new Float2((float)_canvasScreenRect.Size.X, (float)_canvasScreenRect.Size.Y);
            _view.FrameBounds(min, max, size);
        }
        else _view.ResetView();
    }

    private void SeedSampleNodes()
    {
        if (_graph == null) return;

        // Idempotent: wipe whatever's there before seeding so repeated clicks don't pile
        // up duplicate copies on top of each other.
        _graph.Nodes.Clear();
        _graph.Edges.Clear();

        // Layout: nodes are 200px wide, leave ~120px horizontal breathing room between
        // columns so wires have visible curve and labels don't crowd the next node.
        const float colA = 60, colB = 380, colC = 700, colD = 1020;

        var c1 = _graph.AddNode(new FloatConstantNode { Value = 0.5f, Position = new Float2(colA, 60) });
        var c2 = _graph.AddNode(new FloatConstantNode { Value = 2.0f, Position = new Float2(colA, 220) });
        var mul = _graph.AddNode(new MultiplyNode { Position = new Float2(colB, 130) });
        var sin = _graph.AddNode(new SinNode { Position = new Float2(colC, 130) });
        var output = _graph.AddNode(new FragmentOutputNode { Position = new Float2(colD, 60) });

        _graph.Edges.Add(new Edge { SourceNodeId = c1.Id, SourcePortName = "Out",
                                     TargetNodeId = mul.Id, TargetPortName = "A" });
        _graph.Edges.Add(new Edge { SourceNodeId = c2.Id, SourcePortName = "Out",
                                     TargetNodeId = mul.Id, TargetPortName = "B" });
        _graph.Edges.Add(new Edge { SourceNodeId = mul.Id, SourcePortName = "Result",
                                     TargetNodeId = sin.Id, TargetPortName = "X" });
        // Wire sin's output into the FragmentOutput's Smoothness slot to demo a wire
        // travelling some distance (visible bezier).
        _graph.Edges.Add(new Edge { SourceNodeId = sin.Id, SourcePortName = "Result",
                                     TargetNodeId = output.Id, TargetPortName = "Smoothness" });
    }

}
