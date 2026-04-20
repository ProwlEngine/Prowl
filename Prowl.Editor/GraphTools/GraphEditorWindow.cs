// This file is part of the Prowl Game Engine
// Licensed under the MIT License. See the LICENSE file in the project root for details.

using System;
using System.Collections.Generic;

using Prowl.Echo;
using Prowl.Editor.Docking;
using Prowl.Editor.Inspector;
using Prowl.Editor.Widgets;
using Prowl.PaperUI;
using Prowl.PaperUI.Events;
using Prowl.PaperUI.LayoutEngine;
using Prowl.Runtime;
using Prowl.Runtime.GraphTools;
using Prowl.Runtime.GraphTools.ShaderGraphs;
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
    private float _lastWindowHeight;

    // ─── Interaction state ────────────────────────────────────────────────────────────
    private enum DragMode { None, MoveNodes, ResizeNode, MoveStickyNotes, ResizeStickyNote, MoveGroup, ResizeGroup, MarqueeSelect, ConnectingWire }
    private DragMode _dragMode = DragMode.None;

    /// <summary>Resizable node currently being drag-resized + its size at drag-start
    /// (for the undo step). Null unless <see cref="_dragMode"/> is <see cref="DragMode.ResizeNode"/>.</summary>
    private Guid? _resizingNode;
    private Float2 _resizeNodeStartSize;

    /// <summary>Resize handle size (graph-space pixels) at the bottom-right of each sticky / group.</summary>
    private const float StickyResizeHandleSize = 16f;
    /// <summary>Minimum sticky note size — prevents user dragging it to zero.</summary>
    private static readonly Float2 StickyMinSize = new Float2(120, 80);

    /// <summary>Height of the group title bar in graph-space units (matches DrawGroup).</summary>
    private const float GroupTitleHeight = 24f;
    /// <summary>Minimum group size — prevents user dragging it to zero.</summary>
    private static readonly Float2 GroupMinSize = new Float2(140, 80);

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
    // Expanded-category set — top-level category groups (split on '/') whose rows are
    // visible. Persisted across popup open/close so the user doesn't have to re-expand
    // their go-to group every time. Cleared when search is active (flat-list mode).
    private readonly HashSet<string> _expandedCategories = new() { "Output", "Input", "Math" };

    // Sidebar visibility. Shader-graph asset types get a settings panel (blend/cull/
    // queue dropdowns); other graph types don't yet, so the toggle is hidden for them.
    private bool _sidebarOpen = true;
    private float _sidebarWidth = 210f;

    // Preview state — lives on the window so it persists across frames / foldout
    // expansions. Lazy-created the first time the sidebar draws.
    private PreviewRenderer? _preview;
    private Prowl.Runtime.Resources.Material? _lastPreviewMaterial;
    private Prowl.Runtime.Resources.Mesh?     _lastPreviewMesh;
    private Prowl.Runtime.AssetRef<Prowl.Runtime.Resources.Mesh> _previewMesh;

    // Foldout expand/collapse state lives on Paper's element storage via EditorGUI.Foldout —
    // no window-local fields needed.

    /// <summary>Active marquee corners in graph space (start at drag start, end follows mouse).</summary>
    private Float2? _marqueeStartGraph;
    private Float2? _marqueeEndGraph;
    /// <summary>Captured at marquee start: do we add to existing selection (Ctrl/Shift) or replace.</summary>
    private bool _marqueeAdditive;

    /// <summary>Per-node positions captured at drag-start, used to register one undo step per drag.</summary>
    private Dictionary<Guid, Float2>? _dragMoveStartPositions;

    /// <summary>Time (seconds since editor start) of the last left-click on a node, and
    /// which node was clicked — used to detect double-click. Threshold below.</summary>
    private double _lastNodeClickTime = -1;
    private Guid _lastClickedNode;
    private const double DoubleClickThresholdSeconds = 0.4;

    /// <summary>Cursor position in graph space at the moment node-drag began. Combined
    /// with <see cref="_dragMoveStartPositions"/> to compute the *unsnapped* target
    /// position each frame from the absolute cursor — that way snapping is a correction
    /// applied on top of the raw position, not a cumulative shift that fights the cursor
    /// on every small movement.</summary>
    private Float2 _dragMoveStartCursor;

    /// <summary>Mouse button currently held — captured on press so drag handlers know intent.</summary>
    private PaperMouseBtn _pressedButton = PaperMouseBtn.Unknown;

    /// <summary>Selected nodes (Guids).</summary>
    private readonly HashSet<Guid> _selectedNodes = new();

    /// <summary>Selected wires (Edge Guids).</summary>
    private readonly HashSet<Guid> _selectedEdges = new();

    /// <summary>Selected sticky notes (Guids).</summary>
    private readonly HashSet<Guid> _selectedStickyNotes = new();

    /// <summary>Selected groups (Guids). Drag/resize is single-group at a time; selection
    /// routes to Inspector like stickies so Title can be renamed via PropertyGrid.</summary>
    private readonly HashSet<Guid> _selectedGroups = new();

    /// <summary>Mark the graph dirty for auto-recompile. Called by every code path
    /// that mutates Nodes / Edges / Groups / StickyNotes — selection-only changes
    /// don't count. Records the timestamp so the per-frame debounce can decide
    /// whether enough idle time has passed to recompile.</summary>
    private void MarkGraphDirty()
    {
        _isDirtyForRecompile = true;
        _lastChangeTime = Time.UnscaledTotalTime;
    }

    /// <summary>Wrapper around <see cref="Undo.RegisterAction"/> that also flags the
    /// graph dirty for auto-recompile. Every mutation in this window goes through
    /// here so we can't forget — the only places NOT using this are pure selection
    /// updates, which don't change the compiled shader.</summary>
    private void RegisterMutation(string label, Action undo, Action redo)
    {
        Undo.RegisterAction(label, undo, redo);
        MarkGraphDirty();
    }

    /// <summary>Wipe every per-element selection set. Always prefer this over clearing
    /// individual sets so a future selection set added here gets cleared too.</summary>
    private void ClearAllSelection()
    {
        _selectedNodes.Clear();
        _selectedEdges.Clear();
        _selectedStickyNotes.Clear();
        _selectedGroups.Clear();
    }

    /// <summary>Sticky note whose corner is being resized. Null unless <see cref="_dragMode"/> is <see cref="DragMode.ResizeStickyNote"/>.</summary>
    private Guid? _resizingStickyNote;
    private Float2 _resizeStickyStartSize;

    /// <summary>Group being moved or resized. Null unless drag mode is MoveGroup/ResizeGroup.</summary>
    private Guid? _activeGroup;
    private Float2 _groupDragStartPos;
    private Float2 _groupDragStartSize;
    /// <summary>Start positions of every node that was inside the group at drag-start —
    /// moving the group translates all of them so the group + contents move as a unit.</summary>
    private Dictionary<Guid, Float2>? _groupContainedNodeStartPositions;
    private Dictionary<Guid, Float2>? _groupContainedStickyStartPositions;

    /// <summary>Pre-drag positions for sticky notes — same pattern as node drag undo.</summary>
    private Dictionary<Guid, Float2>? _dragStickyStartPositions;

    /// <summary>Alignment guide lines in graph space, populated during a snapped node
    /// drag; rendered by the canvas so users can see which edge they snapped to.</summary>
    private readonly List<(Float2 from, Float2 to)> _alignmentGuides = new();

    /// <summary>Snap spacing when the snap modifier is held with no other-node match.
    /// Matches the visual grid at its base level (see GraphRendering.DrawGrid: baseStep=32).</summary>
    private const float SnapGridSize = 16f;
    /// <summary>Snap-to-other-node tolerance in graph-space pixels (scaled by zoom for
    /// consistent feel — see SnapToolerance()).</summary>
    private const float SnapPixelTolerance = 8f;

    // ─── Auto-recompile ────────────────────────────────────────
    /// <summary>True after any graph mutation; cleared when SaveGraph runs. While true
    /// the auto-recompile timer is counting down towards <see cref="AutoRecompileDelay"/>.</summary>
    private bool _isDirtyForRecompile;
    /// <summary>UnscaledTotalTime of the most recent mutation. Used to debounce: we
    /// only recompile after 1s of editing inactivity so rapid edits don't re-import
    /// every frame.</summary>
    private double _lastChangeTime;
    /// <summary>Toggleable from the toolbar. When off, mutations still mark dirty but
    /// the user has to hit "Compile" (or Save) explicitly. Persisted per-window so
    /// users with strong opinions can disable it once and forget.</summary>
    private bool _autoRecompile = true;
    /// <summary>Seconds of inactivity before auto-recompile fires. Mirrors SF's 1s.</summary>
    private const float AutoRecompileDelay = 1.0f;

    /// <summary>Node currently under the cursor (recomputed each frame).</summary>
    private Guid? _hoveredNode;

    /// <summary>Port currently under the cursor (recomputed each frame). Highlighted while
    /// dragging a wire it would target.</summary>
    private (Guid nodeId, string portName, PortDirection direction)? _hoveredPort;

    /// <summary>Edge under the cursor (recomputed each frame). The wire renderer uses
    /// this to brighten/thicken so users get hover feedback before clicking.</summary>
    private Guid? _hoveredEdge;

    /// <summary>Cut-line gesture state — list of graph-space points sampled while the
    /// user is alt+right-dragging. Wires intersected by any segment of the polyline
    /// are flagged for deletion on release.</summary>
    private List<Float2>? _cutLinePoints;

    /// <summary>Node currently shown in the right-click context menu. Set by
    /// HandleRightClick; consumed by the per-frame context-menu render.</summary>
    private Guid? _contextMenuNode;

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

    public override bool SerializeState(System.Text.Json.Nodes.JsonObject state)
    {
        if (_graph == null || _graph.AssetID == Guid.Empty) return false;
        // Viewport pan/zoom already live on the Graph asset itself — only the asset
        // reference needs to survive an editor restart.
        state["graph"] = _graph.AssetID.ToString();
        return true;
    }

    public override void RestoreState(System.Text.Json.Nodes.JsonObject state)
    {
        string? guidStr = state["graph"]?.GetValue<string>();
        if (!Guid.TryParse(guidStr, out var guid)) return;

        // Resolve via AssetRef so the importer path runs (keeps behavior identical to
        // double-click open). If the asset was deleted since last save we stay empty
        // and the window shows its "No graph loaded" placeholder.
        var graph = new Prowl.Runtime.AssetRef<Graph>(guid).Res;
        if (graph == null) return;

        _graph = graph;
        _view = new GraphCanvasView(graph);
    }

    public override void OnGUI(Paper paper, float width, float height)
    {
        _lastWindowHeight = height;
        var font = EditorTheme.DefaultFont;
        if (font == null) return;

        if (_graph == null)
        {
            EditorGUI.Label(paper, "graph_empty",
                "No graph loaded. Open this window from a graph asset's inspector.");
            return;
        }
        _view ??= new GraphCanvasView(_graph);

        // Fresh validation pass every frame — cheap (O(V+E) for our validators) and
        // means the diagnostic badges always reflect the current state regardless of
        // whether the mutation came from a mouse interaction, Ctrl+Z, or an external
        // Inspector edit. If this becomes a perf concern on very large graphs we can
        // switch to a dirty-flag model.
        Prowl.Runtime.GraphTools.GraphValidatorRegistry.Validate(_graph);

        // Auto-recompile: if the graph is dirty AND the user
        // has finished editing for AutoRecompileDelay seconds AND auto-recompile is
        // on, save the asset. SaveGraph writes the file → the asset DB's file
        // watcher picks it up → importer runs → Shader sub-asset regenerates →
        // any AssetRef holding the old shader re-resolves to the new instance.
        if (_isDirtyForRecompile && _autoRecompile
            && Time.UnscaledTotalTime - _lastChangeTime >= AutoRecompileDelay)
        {
            _isDirtyForRecompile = false;
            SaveGraph();
        }

        // Auto-prune pass — removes IAutoPruneNode nodes that reported they're no
        // longer needed (e.g. dangling RelayNode after a disconnect). Runs before
        // hover/shortcut handling so the user never interacts with a doomed node.
        RunAutoPrune();

        // Shader graphs own their toolbar — Compile/Auto/Recenter live in the sidebar's
        // top strip so the canvas gets the full window height. Other graph types keep
        // the classic top toolbar.
        bool isShaderGraph = _graph is Prowl.Runtime.GraphTools.ShaderGraphs.ShaderGraph;
        if (!isShaderGraph)
        {
            DrawToolbar(paper);
            DrawCanvas(paper, font);
        }
        else if (_sidebarOpen)
        {
            // Sidebar | Canvas — sidebar on the LEFT so it reads like a typical DCC
            // inspector (Maya attribute editor, Unity's shader graph blackboard).
            using (paper.Row("graph_main")
                .Width(UnitValue.Stretch()).Height(UnitValue.Stretch())
                .Enter())
            {
                DrawSidebar(paper, font);
                DrawCanvas(paper, font);
            }
        }
        else
        {
            // Sidebar collapsed — canvas gets the whole window, plus a tiny floating
            // toggle so the user can bring it back without a menu dive.
            DrawCanvas(paper, font);
            DrawSidebarTogglePill(paper);
        }

        // Per-frame, post-layout: update hover and process keyboard shortcuts that only
        // apply when the canvas is focused (mouse over it).
        UpdateHoverAndShortcuts(paper);
    }

    /// <summary>
    /// Collect every <see cref="IAutoPruneNode"/> that reports it should vanish, and
    /// delete the whole batch as one undo step. Runs every frame; usually does
    /// nothing. Not registered as an Undo action when the user's action that caused
    /// the prune (Delete / Disconnect) already registered its own — we register here
    /// so Ctrl+Z still restores the auto-pruned node if the user wants it back.
    /// </summary>
    private void RunAutoPrune()
    {
        if (_graph == null) return;
        List<Node>? doomed = null;
        foreach (var n in _graph.Nodes)
        {
            if (n is IAutoPruneNode ap && ap.ShouldPrune(_graph))
            {
                doomed ??= new List<Node>();
                doomed.Add(n);
            }
        }
        if (doomed == null) return;

        // Also capture edges touching doomed nodes so undo can restore them too.
        var removedEdges = new List<Edge>();
        foreach (var n in doomed)
            foreach (var e in _graph.Edges)
                if ((e.SourceNodeId == n.Id || e.TargetNodeId == n.Id) && !removedEdges.Contains(e))
                    removedEdges.Add(e);

        foreach (var n in doomed) _graph.RemoveNode(n.Id);
        // Drop selection references for any pruned nodes so the Inspector and
        // selection sets stay consistent.
        foreach (var n in doomed) _selectedNodes.Remove(n.Id);

        var graph = _graph;
        var nodesSnapshot = doomed;
        var edgesSnapshot = removedEdges;
        RegisterMutation("Auto-prune",
            undo: () =>
            {
                foreach (var n in nodesSnapshot) graph.Nodes.Add(n);
                foreach (var e in edgesSnapshot) graph.Edges.Add(e);
            },
            redo: () =>
            {
                foreach (var n in nodesSnapshot) graph.RemoveNode(n.Id);
                foreach (var e in edgesSnapshot) graph.Edges.Remove(e);
            });
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

        // Cut-line gesture: alt+right-drag draws a slash through wires. Like the
        // middle-mouse pan above, polled manually because Paper drag events are
        // left-only. Right-click without drag still opens the context menu (handled
        // by HandleRightClick when no cut-line was started).
        if (Input.IsAltPressed && Input.GetMouseButton(1))
        {
            _cutLinePoints ??= new List<Float2>();
            _cutLinePoints.Add(ScreenToGraph(paper.PointerPos));
        }
        else if (_cutLinePoints != null)
        {
            // Released — apply the cuts (if the polyline has at least one segment).
            if (_cutLinePoints.Count >= 2) ApplyCutLine(_cutLinePoints);
            _cutLinePoints = null;
        }

        var graphMouse = ScreenToGraph(paper.PointerPos);
        var portHit = HitTestPort(graphMouse, out var portNode);
        _hoveredPort = (portHit.HasValue && portNode != null)
            ? ((Guid, string, PortDirection)?) (portNode.Id, portHit.Value.port.Name, portHit.Value.port.Direction)
            : null;
        _hoveredNode = HitTestNode(graphMouse)?.Id;
        // Wire hover only when nothing higher-priority is under the cursor — wires
        // sit visually beneath nodes/stickies so highlighting them while a node is
        // also under the cursor would be noise.
        _hoveredEdge = (_hoveredNode == null && _hoveredPort == null)
            ? HitTestWire(graphMouse, 6f / _view.Zoom)?.Id
            : null;

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
        else if (ShortcutManager.IsPressed("GraphEditor/GroupSelection"))
            GroupSelection();

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

    /// <summary>Return the topmost sticky note whose rect contains <paramref name="graphPoint"/>.</summary>
    private StickyNote? HitTestStickyNote(Float2 graphPoint)
    {
        if (_graph == null) return null;
        for (int i = _graph.StickyNotes.Count - 1; i >= 0; i--)
        {
            var s = _graph.StickyNotes[i];
            if (graphPoint.X >= s.Position.X && graphPoint.X <= s.Position.X + s.Size.X &&
                graphPoint.Y >= s.Position.Y && graphPoint.Y <= s.Position.Y + s.Size.Y)
                return s;
        }
        return null;
    }

    /// <summary>True if <paramref name="graphPoint"/> is inside the bottom-right resize handle of <paramref name="sticky"/>.</summary>
    private static bool IsOverStickyResizeHandle(StickyNote sticky, Float2 graphPoint)
    {
        float x0 = sticky.Position.X + sticky.Size.X - StickyResizeHandleSize;
        float y0 = sticky.Position.Y + sticky.Size.Y - StickyResizeHandleSize;
        return graphPoint.X >= x0 && graphPoint.X <= sticky.Position.X + sticky.Size.X
            && graphPoint.Y >= y0 && graphPoint.Y <= sticky.Position.Y + sticky.Size.Y;
    }

    /// <summary>Topmost group whose title bar contains <paramref name="graphPoint"/>.
    /// Only the title bar acts as the interaction surface so clicks on a group's body
    /// pass through to interior nodes — without this, groups would eat every click.</summary>
    private NodeGroup? HitTestGroupTitle(Float2 graphPoint)
    {
        if (_graph == null) return null;
        for (int i = _graph.Groups.Count - 1; i >= 0; i--)
        {
            var g = _graph.Groups[i];
            if (graphPoint.X >= g.Position.X && graphPoint.X <= g.Position.X + g.Size.X &&
                graphPoint.Y >= g.Position.Y && graphPoint.Y <= g.Position.Y + GroupTitleHeight)
                return g;
        }
        return null;
    }

    /// <summary>True if <paramref name="graphPoint"/> is inside the bottom-right resize
    /// handle of <paramref name="group"/>. The corner handle is always grabbable even
    /// when the group's body passes clicks through to interior nodes.</summary>
    private static bool IsOverGroupResizeHandle(NodeGroup group, Float2 graphPoint)
    {
        float x0 = group.Position.X + group.Size.X - StickyResizeHandleSize;
        float y0 = group.Position.Y + group.Size.Y - StickyResizeHandleSize;
        return graphPoint.X >= x0 && graphPoint.X <= group.Position.X + group.Size.X
            && graphPoint.Y >= y0 && graphPoint.Y <= group.Position.Y + group.Size.Y;
    }

    /// <summary>Enumerate nodes whose rect centres fall inside <paramref name="group"/>.
    /// Used to pick up the "contained" set at move-start so the whole cluster translates
    /// together.</summary>
    private IEnumerable<Node> NodesInsideGroup(NodeGroup group)
    {
        if (_graph == null) yield break;
        foreach (var n in _graph.Nodes)
        {
            var rect = GraphLayout.GetNodeRect(n);
            float cx = (float)(rect.Min.X + rect.Max.X) * 0.5f;
            float cy = (float)(rect.Min.Y + rect.Max.Y) * 0.5f;
            if (cx >= group.Position.X && cx <= group.Position.X + group.Size.X &&
                cy >= group.Position.Y && cy <= group.Position.Y + group.Size.Y)
                yield return n;
        }
    }

    private IEnumerable<StickyNote> StickiesInsideGroup(NodeGroup group)
    {
        if (_graph == null) yield break;
        foreach (var s in _graph.StickyNotes)
        {
            float cx = s.Position.X + s.Size.X * 0.5f;
            float cy = s.Position.Y + s.Size.Y * 0.5f;
            if (cx >= group.Position.X && cx <= group.Position.X + group.Size.X &&
                cy >= group.Position.Y && cy <= group.Position.Y + group.Size.Y)
                yield return s;
        }
    }

    /// <summary>True if <paramref name="graphPoint"/> is inside the bottom-right resize
    /// handle of a resizable node. Same 14x14 grip area as the sticky/group renderer.</summary>
    private static bool IsOverNodeResizeHandle(Node node, Float2 graphPoint)
    {
        if (node is not IResizableNode) return false;
        var rect = GraphLayout.GetNodeRect(node);
        float x0 = (float)rect.Max.X - 14f;
        float y0 = (float)rect.Max.Y - 14f;
        return graphPoint.X >= x0 && graphPoint.X <= rect.Max.X
            && graphPoint.Y >= y0 && graphPoint.Y <= rect.Max.Y;
    }

    private NodeGroup? FindGroup(Guid id)
    {
        if (_graph == null) return null;
        foreach (var g in _graph.Groups) if (g.Id == id) return g;
        return null;
    }

    private static NodeGroup? FindGroupIn(Prowl.Runtime.GraphTools.Graph graph, Guid id)
    {
        foreach (var g in graph.Groups) if (g.Id == id) return g;
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

            // Recompile / dirty indicator. Shows "●" + the
            // remaining countdown when waiting on auto-recompile, "✓" when up-to-date.
            string status;
            if (_isDirtyForRecompile && _autoRecompile)
            {
                float remaining = MathF.Max(0f, AutoRecompileDelay - (float)(Time.UnscaledTotalTime - _lastChangeTime));
                status = $"● {remaining:0.0}s";
            }
            else if (_isDirtyForRecompile) status = "● dirty";
            else status = "✓ up-to-date";
            paper.Box("graph_tb_status").Width(80).Height(28)
                .Text(status, EditorTheme.DefaultFont!)
                .TextColor(_isDirtyForRecompile ? EditorTheme.Purple400 : EditorTheme.Ink400)
                .FontSize(EditorTheme.FontSize - 2)
                .Alignment(TextAlignment.MiddleRight);

            EditorGUI.Button(paper, "graph_tb_compile", $"{EditorIcons.WandMagicSparkles}  Compile", width: 100)
                .OnValueChanged(_ => { _isDirtyForRecompile = false; SaveGraph(); });

            // "Auto" toggle — when on, dirty graphs auto-recompile after a short
            // idle window. Off = user has to hit Compile explicitly. (Save and
            // Compile are the same operation now: both write the .shadergraph,
            // which triggers the importer to regenerate the Shader sub-asset.)
            EditorGUI.ToggleButton(paper, "graph_tb_auto", "Auto", _autoRecompile, width: 50)
                .OnValueChanged(v => _autoRecompile = v);

            EditorGUI.Button(paper, "graph_tb_recenter", "Recenter", width: 90)
                .OnValueChanged(_ => RecenterView());

            // Wire-style toggle — cycles Bezier → Linear → Rectilinear → Bezier. Cycling
            // is fine instead of a dropdown since there are only three options.
            string label = $"Wires: {_graph.WireStyle}";
            EditorGUI.Button(paper, "graph_tb_wirestyle", label, width: 150)
                .OnValueChanged(_ => CycleWireStyle());

            // Sidebar toggle is only relevant for shader graphs, and in that mode the
            // toolbar isn't rendered at all — buttons live inside the sidebar itself.
            // So we don't draw a toggle button here.
        }
    }

    // ─── Sidebar (shader-graph settings panel) ────────────────────────────────────────
    /// <summary>Left-hand panel that owns the graph's toolbar, a live preview of the
    /// compiled material, and foldout sections for Properties / Lighting / Blending /
    /// Geometry. Every control mutates through <see cref="RegisterMutation"/> so
    /// auto-recompile and Ctrl+Z work identically to node-graph edits.</summary>
    private void DrawSidebar(Paper paper, Prowl.Scribe.FontFile font)
    {
        if (_graph is not Prowl.Runtime.GraphTools.ShaderGraphs.ShaderGraph sg) return;

        using (paper.Column("graph_sidebar")
            .Width(_sidebarWidth).Height(UnitValue.Stretch())
            .BackgroundColor(System.Drawing.Color.FromArgb(255, 32, 34, 40))
            .BorderColor(System.Drawing.Color.FromArgb(255, 50, 52, 60))
            .BorderWidth(1)
            .ChildLeft(8).ChildRight(8).ChildTop(6).ChildBottom(8).ColBetween(4)
            .Clip()
            .Enter())
        {
            DrawSidebarToolbar(paper, sg);
            DrawSidebarPreviewRow(paper, sg);
            DrawSidebarPreview(paper, sg);

            // ─── Foldouts (scroll view) ───────────────────────────────────────────────
            // ScrollView takes a fixed float height, so compute what's left after the
            // toolbar (26) + preview row (22) + preview square (sidebar - 20) + sidebar
            // vertical padding (14) + inter-row gaps (~12). Floor at 80 so the panel
            // stays usable even on very short windows.
            float fixedConsumed = 26f + 22f + (_sidebarWidth - 20f) + 14f + 12f;
            float scrollH = MathF.Max(80f, _lastWindowHeight - fixedConsumed);
            using (ScrollView.Begin(paper, "sg_foldout_scroll", _sidebarWidth - 16, scrollH))
            {
                // EditorGUI.Foldout owns its expand/collapse state via Paper's element
                // storage — matches the same behaviour used by the Preferences panel, so
                // the chevron clicks and hover states feel identical to the rest of the
                // editor. All four default to collapsed so the preview doesn't scroll
                // off-screen when the panel opens.
                EditorGUI.Foldout(paper, "sg_fold_props", "Properties",
                    () => DrawPropertiesFoldout(paper, sg), defaultValue: false);
                EditorGUI.Foldout(paper, "sg_fold_light", "Lighting",
                    () => DrawLightingFoldout(paper, sg),   defaultValue: false);
                EditorGUI.Foldout(paper, "sg_fold_blend", "Blending",
                    () => DrawBlendingFoldout(paper, sg),   defaultValue: false);
                EditorGUI.Foldout(paper, "sg_fold_geo", "Geometry",
                    () => DrawGeometryFoldout(paper, sg),   defaultValue: false);
            }
        }
    }

    /// <summary>Top strip of the sidebar — replaces the removed window-wide toolbar.
    /// Row 1: Compile / Auto / Recenter / dirty indicator. Compact so the preview gets
    /// all available vertical space.</summary>
    private void DrawSidebarToolbar(Paper paper, Prowl.Runtime.GraphTools.ShaderGraphs.ShaderGraph sg)
    {
        using (paper.Row("sg_tb_row1").Height(26).RowBetween(4).Enter())
        {
            EditorGUI.Button(paper, "sg_tb_compile", $"{EditorIcons.WandMagicSparkles} Compile", width: 90)
                .OnValueChanged(_ => { _isDirtyForRecompile = false; SaveGraph(); });
            EditorGUI.ToggleButton(paper, "sg_tb_auto", "Auto", _autoRecompile, width: 48)
                .OnValueChanged(v => _autoRecompile = v);
            EditorGUI.Button(paper, "sg_tb_recenter", "Recenter", width: 72)
                .OnValueChanged(_ => RecenterView());
            // Status indicator — same ● / ✓ scheme as the old toolbar.
            string status;
            if (_isDirtyForRecompile && _autoRecompile)
            {
                float remaining = MathF.Max(0f, AutoRecompileDelay - (float)(Time.UnscaledTotalTime - _lastChangeTime));
                status = $"● {remaining:0.0}s";
            }
            else if (_isDirtyForRecompile) status = "● dirty";
            else status = "✓";
            paper.Box("sg_tb_status").Width(UnitValue.Stretch()).Height(26)
                .Text(status, EditorTheme.DefaultFont!)
                .TextColor(_isDirtyForRecompile ? EditorTheme.Purple400 : EditorTheme.Ink400)
                .FontSize(EditorTheme.FontSize - 2)
                .Alignment(TextAlignment.MiddleRight);
            // Close-sidebar button so the user can recover screen space without hunting
            // in a menu. Reopens via the DrawSidebarTogglePill floating pill.
            EditorGUI.Button(paper, "sg_tb_hide", EditorIcons.CircleXmark, width: 24)
                .OnValueChanged(_ => _sidebarOpen = false);
        }
    }

    /// <summary>Second row: mesh picker + preview renderer feature toggles. Sits above
    /// the preview texture so adjusting either visibly repaints the render next frame.</summary>
    private void DrawSidebarPreviewRow(Paper paper, Prowl.Runtime.GraphTools.ShaderGraphs.ShaderGraph sg)
    {
        using (paper.Row("sg_tb_row2").Height(22).RowBetween(4).Enter())
        {
            // Mesh AssetRef picker — PropertyGrid.DrawField handles the drag/drop,
            // icon, and selector modal for us. Passing empty label skips the "Mesh"
            // prefix so the picker fills the row width; AssetRefPropertyEditor checks
            // IsNullOrEmpty and omits the label box entirely when so.
            using (paper.Box("sg_mesh_field").Width(UnitValue.Stretch()).Height(22).Enter())
            {
                PropertyGrid.DrawField(paper, "sg_mesh", "",
                    typeof(Prowl.Runtime.AssetRef<Prowl.Runtime.Resources.Mesh>),
                    _previewMesh,
                    newVal =>
                    {
                        if (newVal is Prowl.Runtime.AssetRef<Prowl.Runtime.Resources.Mesh> r) _previewMesh = r;
                        else if (newVal is Prowl.Runtime.Resources.Mesh m) _previewMesh = new Prowl.Runtime.AssetRef<Prowl.Runtime.Resources.Mesh>(m);
                        _lastPreviewMesh = null; // force rebuild
                    }, 0);
            }
            // Grid toggle — PreviewRenderer has a ShowGrid flag; surface it here.
            EditorGUI.ToggleButton(paper, "sg_pv_grid", "Grid",
                _preview?.ShowGrid ?? false, width: 52)
                .OnValueChanged(v => { if (_preview != null) _preview.ShowGrid = v; });
        }
    }

    /// <summary>The preview RenderTexture itself. Lazy-creates PreviewRenderer on first
    /// draw; rebinds subject when the compiled material or chosen mesh changes. Orbit
    /// (drag) and zoom (scroll) are implemented by PreviewRenderer.DrawPreview.</summary>
    private void DrawSidebarPreview(Paper paper, Prowl.Runtime.GraphTools.ShaderGraphs.ShaderGraph sg)
    {
        _preview ??= new PreviewRenderer(180, 180);

        // Default to the built-in Sphere mesh — resolved by deterministic GUID through
        // BuiltInAssets. Set once on first draw and persisted on _previewMesh so the
        // PropertyGrid picker shows "Sphere" rather than "None" out of the box.
        if (_previewMesh.IsExplicitNull)
        {
            var sphereGuid = Prowl.Runtime.BuiltInAssets.GuidForMesh(Prowl.Runtime.Resources.DefaultModel.Sphere);
            _previewMesh = new Prowl.Runtime.AssetRef<Prowl.Runtime.Resources.Mesh>(sphereGuid);
        }

        var material = ResolvePreviewMaterial(sg);
        var mesh = _previewMesh.Res;

        // Subject-rebuild detection: swap when the material or mesh instance changes
        // (identity compare covers sub-asset swap after recompile AND AssetRef swaps).
        bool needsRebuild = !ReferenceEquals(_lastPreviewMaterial, material)
                          || !ReferenceEquals(_lastPreviewMesh, mesh);
        if (needsRebuild && material != null && mesh != null)
        {
            _preview.SetupForMesh(mesh, material);
            _lastPreviewMaterial = material;
            _lastPreviewMesh = mesh;
        }

        // Square preview that fills the sidebar width so it scales with _sidebarWidth
        // tweaks. Height mirrors width so the sphere stays round.
        float w = _sidebarWidth - 20;
        _preview.DrawPreview(paper, "sg_preview", w, w);
    }

    /// <summary>Resolve the compiled Shader sub-asset created by ShaderGraphImporter and
    /// wrap it in a persistent Material so the Properties foldout's override edits apply
    /// live to the preview. Returns null while the graph hasn't been compiled yet
    /// (first-save path) — caller must guard for that.</summary>
    private Prowl.Runtime.Resources.Material? ResolvePreviewMaterial(Prowl.Runtime.GraphTools.ShaderGraphs.ShaderGraph sg)
    {
        if (sg.AssetID == Guid.Empty) return null;
        var entry = EditorAssetDatabase.Instance?.GetEntry(sg.AssetID);
        if (entry?.SubAssets == null || entry.SubAssets.Length == 0) return null;

        // Prefer the sub-asset typed as Shader over name-matching — survives any future
        // rename of the "CompiledShader" slot. Use Runtime.AssetDatabase.Get directly
        // so the loader kicks in when the sub-asset hasn't been cached yet; wrapping
        // via new AssetRef<Shader>(guid).Res sometimes returned null the first frame
        // after a fresh compile, which is what caused the Properties foldout to stay
        // stuck on "compile to see properties".
        Prowl.Runtime.Resources.Shader? shader = null;
        foreach (var sub in entry.SubAssets)
        {
            if (typeof(Prowl.Runtime.Resources.Shader).IsAssignableFrom(sub.Type))
            {
                shader = Prowl.Runtime.AssetDatabase.Get(sub.Guid) as Prowl.Runtime.Resources.Shader;
                if (shader != null) break;
            }
        }
        if (shader == null) return null;

        if (_previewMaterial == null || _previewMaterial.Shader != shader)
        {
            _previewMaterial = new Prowl.Runtime.Resources.Material();
            _previewMaterial.Shader = shader;
        }
        return _previewMaterial;
    }
    private Prowl.Runtime.Resources.Material? _previewMaterial;

    // ─── Foldout body functions ───────────────────────────────────────────────────────

    /// <summary>Properties foldout — material override fields shared with the Material
    /// asset inspector. Reads defaults live from the graph-compiled shader; writes go
    /// to the preview material's override set.</summary>
    private void DrawPropertiesFoldout(Paper paper, Prowl.Runtime.GraphTools.ShaderGraphs.ShaderGraph sg)
    {
        var material = ResolvePreviewMaterial(sg);
        if (material?.Shader == null)
        {
            paper.Box("sg_props_none").Height(20)
                .Text("(compile the graph to see properties)", EditorTheme.DefaultFont!)
                .TextColor(EditorTheme.Ink400).FontSize(EditorTheme.FontSize - 2)
                .Alignment(TextAlignment.MiddleLeft);
            return;
        }
        // No rebuild callback — Material is persistent, MeshRenderer holds the same
        // reference, PropertyState changes upload to GPU uniforms on the next render
        // pass. Earlier versions nulled _lastPreviewMaterial to force a SetupForMesh,
        // which re-ran FitToSubject (resetting orbit) AND rebuilt the GO mid-edit —
        // that was the cause of the "values apply for 1 frame then revert" bug.
        foreach (var p in material.Shader.Properties)
        {
            MaterialPropertyDrawer.DrawPropertyRow(paper, $"sg_prop_{p.Name}", material, p);
        }
    }

    /// <summary>Lighting foldout — master-node lighting mode + per-graph ambient/shadow
    /// toggles. Mode changes rebuild master ports so hidden/visible state refreshes.</summary>
    private void DrawLightingFoldout(Paper paper, Prowl.Runtime.GraphTools.ShaderGraphs.ShaderGraph sg)
    {
        var master = FindMasterNode(sg);
        if (master != null)
        {
            var current = master.Lighting;
            EditorGUI.EnumDropdown(paper, "sg_lighting_mode", "Mode", current)
                .OnValueChanged(v =>
                {
                    if (v == current) return;
                    var cap = master; var before = current;
                    RegisterMutation("Change Lighting Mode",
                        undo: () => { cap.Lighting = before; RebuildMasterPorts(cap); },
                        redo: () => { cap.Lighting = v;      RebuildMasterPorts(cap); });
                    cap.Lighting = v;
                    RebuildMasterPorts(cap);
                });
        }
        EditorGUI.Toggle(paper, "sg_recv_ambient", "Receives Ambient", sg.RenderSettings.ReceivesAmbient)
            .OnValueChanged(v => { var s = sg.RenderSettings; s.ReceivesAmbient = v; MutateSettings(s, "Receives Ambient"); });
        EditorGUI.Toggle(paper, "sg_recv_shadows", "Receives Shadows", sg.RenderSettings.ReceivesShadows)
            .OnValueChanged(v => { var s = sg.RenderSettings; s.ReceivesShadows = v; MutateSettings(s, "Receives Shadows"); });
        EditorGUI.Toggle(paper, "sg_cast_shadows", "Casts Shadows", sg.RenderSettings.CastsShadows)
            .OnValueChanged(v => { var s = sg.RenderSettings; s.CastsShadows = v; MutateSettings(s, "Casts Shadows"); });
    }

    /// <summary>Blending foldout — alpha compositing mode, render queue, and one-click
    /// presets for the three common configurations.</summary>
    private void DrawBlendingFoldout(Paper paper, Prowl.Runtime.GraphTools.ShaderGraphs.ShaderGraph sg)
    {
        EditorGUI.EnumDropdown(paper, "sg_blend", "Blend Mode", sg.RenderSettings.Blend)
            .OnValueChanged(v => { var s = sg.RenderSettings; s.Blend = v; MutateSettings(s, "Blend"); });

        // Custom mode unlocks the raw Src/Dst/Op pickers — matching the parser's
        // { Src X; Dst Y; Mode Z; } block exactly. Hidden for presets since they
        // already bake in a specific factor combination.
        if (sg.RenderSettings.Blend == ShaderBlendMode.Custom)
        {
            EditorGUI.EnumDropdown(paper, "sg_blend_src", "Src Factor", sg.RenderSettings.BlendSrc)
                .OnValueChanged(v => { var s = sg.RenderSettings; s.BlendSrc = v; MutateSettings(s, "Src Factor"); });
            EditorGUI.EnumDropdown(paper, "sg_blend_dst", "Dst Factor", sg.RenderSettings.BlendDst)
                .OnValueChanged(v => { var s = sg.RenderSettings; s.BlendDst = v; MutateSettings(s, "Dst Factor"); });
            EditorGUI.EnumDropdown(paper, "sg_blend_op", "Blend Op", sg.RenderSettings.BlendOp)
                .OnValueChanged(v => { var s = sg.RenderSettings; s.BlendOp = v; MutateSettings(s, "Blend Op"); });
        }

        EditorGUI.EnumDropdown(paper, "sg_queue", "Queue", sg.RenderSettings.Queue)
            .OnValueChanged(v => { var s = sg.RenderSettings; s.Queue = v; MutateSettings(s, "Queue"); });

        using (paper.Row("sg_presets").Height(22).RowBetween(4).Enter())
        {
            EditorGUI.Button(paper, "sg_preset_opaque", "Opaque", width: 70)
                .OnValueChanged(_ => ApplyPreset(ShaderGraphRenderSettings.OpaqueDefaults(), "Opaque Preset"));
            EditorGUI.Button(paper, "sg_preset_transp", "Transparent", width: 90)
                .OnValueChanged(_ => ApplyPreset(ShaderGraphRenderSettings.TransparentDefaults(), "Transparent Preset"));
            EditorGUI.Button(paper, "sg_preset_add", "Additive", width: 70)
                .OnValueChanged(_ => ApplyPreset(ShaderGraphRenderSettings.AdditiveDefaults(), "Additive Preset"));
        }
    }

    /// <summary>Geometry foldout — face culling, winding, depth testing. Kept separate
    /// from Blending so users mentally pair "transparency" (blending) with one section
    /// and "geometry shape" (cull / winding / depth) with another.</summary>
    private void DrawGeometryFoldout(Paper paper, Prowl.Runtime.GraphTools.ShaderGraphs.ShaderGraph sg)
    {
        EditorGUI.EnumDropdown(paper, "sg_cull", "Cull", sg.RenderSettings.Cull)
            .OnValueChanged(v => { var s = sg.RenderSettings; s.Cull = v; MutateSettings(s, "Cull"); });
        EditorGUI.EnumDropdown(paper, "sg_winding", "Winding", sg.RenderSettings.Winding)
            .OnValueChanged(v => { var s = sg.RenderSettings; s.Winding = v; MutateSettings(s, "Winding"); });
        EditorGUI.Toggle(paper, "sg_zwrite", "Z Write", sg.RenderSettings.ZWrite)
            .OnValueChanged(v => { var s = sg.RenderSettings; s.ZWrite = v; MutateSettings(s, "Z Write"); });
        EditorGUI.EnumDropdown(paper, "sg_ztest", "Z Test", sg.RenderSettings.ZTest)
            .OnValueChanged(v => { var s = sg.RenderSettings; s.ZTest = v; MutateSettings(s, "Z Test"); });
    }

    /// <summary>Floating pill shown in the top-left corner when the sidebar is hidden,
    /// so the user can re-open it without scrolling a menu. Matches the closed state's
    /// screen affordance in DCC tools (Substance, Blender) where a collapsed panel still
    /// leaves a handle.</summary>
    private void DrawSidebarTogglePill(Paper paper)
    {
        paper.Box("sg_pill_backdrop")
            .PositionType(PositionType.SelfDirected)
            .Position(8, 36).Size(32, 28)
            .BackgroundColor(System.Drawing.Color.FromArgb(220, 38, 40, 48))
            .Hovered.BackgroundColor(System.Drawing.Color.FromArgb(255, 60, 64, 78)).End()
            .BorderColor(System.Drawing.Color.FromArgb(255, 80, 84, 96))
            .BorderWidth(1).Rounded(5)
            .OnClick(_ => _sidebarOpen = true)
            .Text(EditorIcons.Sliders, EditorTheme.DefaultFont!)
            .TextColor(EditorTheme.Ink500)
            .Alignment(TextAlignment.MiddleCenter);
    }

    /// <summary>Apply a settings-field mutation with proper undo/redo + dirty tracking.
    /// Caller passes the already-mutated struct; we snapshot the previous value for
    /// undo. No-op when the value didn't actually change (dropdowns fire on same-value
    /// re-selects).</summary>
    private void MutateSettings(ShaderGraphRenderSettings after, string label)
    {
        if (_graph is not Prowl.Runtime.GraphTools.ShaderGraphs.ShaderGraph sg) return;
        var before = sg.RenderSettings;
        if (before.Equals(after)) return;
        sg.RenderSettings = after;
        RegisterMutation(label,
            undo: () => sg.RenderSettings = before,
            redo: () => sg.RenderSettings = after);
    }

    private void ApplyPreset(ShaderGraphRenderSettings preset, string label)
    {
        if (_graph is not Prowl.Runtime.GraphTools.ShaderGraphs.ShaderGraph sg) return;
        var before = sg.RenderSettings;
        if (before.Equals(preset)) return;
        sg.RenderSettings = preset;
        RegisterMutation(label,
            undo: () => sg.RenderSettings = before,
            redo: () => sg.RenderSettings = preset);
    }

    /// <summary>Locate the master output node in a shader graph (there should be exactly one).</summary>
    private static Prowl.Runtime.GraphTools.ShaderGraphs.Nodes.PBROutputNode? FindMasterNode(
        Prowl.Runtime.GraphTools.ShaderGraphs.ShaderGraph sg)
    {
        foreach (var n in sg.Nodes)
            if (n is Prowl.Runtime.GraphTools.ShaderGraphs.Nodes.PBROutputNode m) return m;
        return null;
    }

    /// <summary>Force a node's port list to rebuild — call after mutating public fields
    /// that influence which ports are visible (lighting mode toggles hide PBR inputs).
    /// Uses reflection to poke the private _defined flag; cheaper than exposing a public
    /// "invalidate" method and polluting the Node surface for one editor-only concern.</summary>
    private static void RebuildMasterPorts(Prowl.Runtime.GraphTools.Node node)
    {
        var f = typeof(Prowl.Runtime.GraphTools.Node).GetField("_defined",
            System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic);
        f?.SetValue(node, false);
        node.EnsureDefined();
    }

    private void CycleWireStyle()
    {
        if (_graph == null) return;
        var graph = _graph;
        var before = graph.WireStyle;
        var after = before switch
        {
            WireRoutingStyle.Bezier => WireRoutingStyle.Linear,
            WireRoutingStyle.Linear => WireRoutingStyle.Rectilinear,
            _                        => WireRoutingStyle.Bezier,
        };
        graph.WireStyle = after;
        RegisterMutation("Change Wire Style",
            undo: () => graph.WireStyle = before,
            redo: () => graph.WireStyle = after);
    }

    private void SaveGraph()
    {
        if (_graph == null) return;
        if (Application.IsPlaying)
        {
            Toasts.Warning("Can't save during Play Mode", "Exit Play Mode to save this graph.");
            return;
        }
        try
        {
            EditorAssetDatabase.Instance?.SaveAsset(_graph);
            Debug.Log($"Saved {_graph.AssetPath}");
            SaveBatch.Record($"Graph: {_graph.Name ?? "Untitled"}");
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

            // Non-node entries — only shown when no wire is being dropped (a dropped wire
            // is asking for a compatible node, so non-node items make no sense there).
            bool showExtras = !_dragSourcePort.HasValue && string.IsNullOrEmpty(_creationFilter);
            if (showExtras)
                DrawStickyNoteEntry(paper);

            // Filtered list of node entries — grouped under top-level Category token.
            // When a search filter is active we skip grouping entirely (flat list = easier
            // to scan). Groups persist their expanded state across popup open/close via
            // _expandedCategories so the user doesn't re-expand "Math" every time.
            var entries = NodeRegistry.GetForMarker(_graph.NodeMarkerInterface);
            bool flatList = !string.IsNullOrEmpty(_creationFilter);
            using (paper.Column("graph_popup_list")
                .Width(UnitValue.Stretch()).Height(UnitValue.Stretch())
                .Clip()
                .ColBetween(2)
                .Enter())
            {
                // Filter once, share between both render paths. The wire-drop compatibility
                // filter is port-type-driven; the text filter is fuzzy-ish matching on
                // Title and Category.
                var visible = new List<NodeRegistration>();
                foreach (var reg in entries)
                {
                    if (_dragSourcePort.HasValue &&
                        !reg.HasCompatiblePort(_dragSourcePort.Value.DataType, _dragSourcePort.Value.Direction))
                        continue;
                    if (!MatchesFilter(reg, _creationFilter)) continue;
                    visible.Add(reg);
                }

                int shown = 0;
                if (flatList)
                {
                    foreach (var reg in visible)
                    {
                        if (shown++ >= 50) break;
                        DrawCreationEntry(paper, reg);
                    }
                }
                else
                {
                    // Group by top-level category token (before the first '/'). Nested
                    // segments stay attached to the right-aligned category label on each
                    // row, so "Math/Trig" collapses under "Math" but still reads as
                    // "Math/Trig" when expanded.
                    var groups = new SortedDictionary<string, List<NodeRegistration>>(StringComparer.Ordinal);
                    foreach (var reg in visible)
                    {
                        var top = TopLevelCategory(reg.Category);
                        if (!groups.TryGetValue(top, out var list))
                            groups[top] = list = new List<NodeRegistration>();
                        list.Add(reg);
                    }

                    foreach (var (groupName, regs) in groups)
                    {
                        DrawCategoryHeader(paper, groupName, regs.Count);
                        if (!_expandedCategories.Contains(groupName)) continue;

                        foreach (var reg in regs)
                        {
                            if (shown++ >= 200) break;
                            DrawCreationEntry(paper, reg);
                        }
                        if (shown >= 200) break;
                    }
                    shown = visible.Count;
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

    /// <summary>Extra row at the top of the popup for non-node entities. Currently just
    /// Sticky Note; grows as we add groups, comments, etc.</summary>
    private void DrawStickyNoteEntry(Paper paper)
    {
        const string id = "graph_popup_entry_sticky";
        using (paper.Row(id).Height(20)
            .ChildLeft(6).ChildRight(6).RowBetween(6)
            .BackgroundColor(System.Drawing.Color.FromArgb(255, 64, 60, 48))
            .Hovered.BackgroundColor(System.Drawing.Color.FromArgb(255, 80, 74, 58)).End()
            .Rounded(3)
            .OnClick(_ => SpawnStickyNote())
            .Enter())
        {
            paper.Box($"{id}_title").Height(20)
                .Text($"{EditorIcons.NoteSticky}  Sticky Note", EditorTheme.DefaultFont!)
                .TextColor(EditorTheme.Ink500).FontSize(EditorTheme.FontSize - 2)
                .Alignment(TextAlignment.MiddleLeft);
        }
    }

    private void SpawnStickyNote()
    {
        if (_graph == null) return;
        var note = new StickyNote { Position = _creationMenuGraph };
        _graph.StickyNotes.Add(note);

        var graph = _graph;
        RegisterMutation("Add Sticky Note",
            undo: () => graph.StickyNotes.Remove(note),
            redo: () => graph.StickyNotes.Add(note));

        // Auto-select so the Inspector opens for Title/Body editing.
        ClearAllSelection();
        _selectedStickyNotes.Add(note.Id);
        SyncSelectionSystem();

        CloseCreationMenu();
    }

    /// <summary>Render the right-click context menu for the node stored in
    /// <see cref="_contextMenuNode"/>. Built per-frame as a small inline popup so we
    /// don't have to coordinate with ContextMenuHelper's storage on the canvas Box
    /// (the menu's items depend on a runtime field, not on per-element state).</summary>
    private void DrawNodeContextMenu(Paper paper)
    {
        if (_graph == null || !_contextMenuNode.HasValue) return;
        var node = _graph.FindNode(_contextMenuNode.Value);
        if (node == null) { _contextMenuNode = null; return; }

        const float menuW = 200f;

        // Backdrop — click outside dismisses.
        paper.Box("graph_node_ctx_backdrop")
            .PositionType(PositionType.SelfDirected)
            .Position(-9999, -9999).Size(99999, 99999)
            .Layer(Layer.Topmost)
            .OnClick(_ => _contextMenuNode = null);

        using (paper.Column("graph_node_ctx")
            .PositionType(PositionType.SelfDirected)
            .Position(_creationMenuLocal.X, _creationMenuLocal.Y)
            .Width(menuW).Height(UnitValue.Auto)
            .BackgroundColor(System.Drawing.Color.FromArgb(255, 38, 40, 48))
            .BorderColor(System.Drawing.Color.FromArgb(255, 80, 84, 96))
            .BorderWidth(1).Rounded(6)
            .Layer(Layer.Topmost)
            .ClampToScreen()
            .ChildLeft(4).ChildRight(4).ChildTop(4).ChildBottom(4).ColBetween(2)
            .Enter())
        {
            DrawCtxItem(paper, "ctx_dup", $"{EditorIcons.Clone}  Duplicate", () => DuplicateSelection());
            DrawCtxItem(paper, "ctx_disc", $"{EditorIcons.Scissors}  Disconnect All", () => DisconnectNode(node));
            // SubgraphNode-specific: open the referenced asset.
            if (node is SubgraphNode sub && sub.Subgraph.Res != null)
                DrawCtxItem(paper, "ctx_open", $"{EditorIcons.UpRightFromSquare}  Open Subgraph",
                    () => { var inner = sub.Subgraph.Res; if (inner != null) OpenFor(inner); });
            DrawCtxItem(paper, "ctx_del", $"{EditorIcons.Trash}  Delete", DeleteSelected);
        }
    }

    private void DrawCtxItem(Paper paper, string id, string label, Action onClick)
    {
        using (paper.Row(id).Height(22)
            .ChildLeft(8).ChildRight(8)
            .BackgroundColor(System.Drawing.Color.FromArgb(255, 48, 50, 58))
            .Hovered.BackgroundColor(System.Drawing.Color.FromArgb(255, 64, 70, 90)).End()
            .Rounded(3)
            .OnClick(_ => { onClick(); _contextMenuNode = null; })
            .Enter())
        {
            paper.Box($"{id}_lbl").Height(22)
                .Text(label, EditorTheme.DefaultFont!)
                .TextColor(EditorTheme.Ink500).FontSize(EditorTheme.FontSize - 2)
                .Alignment(TextAlignment.MiddleLeft);
        }
    }

    /// <summary>Remove every wire connected to <paramref name="node"/>, registered as one undo step.</summary>
    private void DisconnectNode(Node node)
    {
        if (_graph == null) return;
        var removed = _graph.Edges.FindAll(e => e.SourceNodeId == node.Id || e.TargetNodeId == node.Id);
        if (removed.Count == 0) return;
        foreach (var e in removed) _graph.Edges.Remove(e);
        var graph = _graph;
        RegisterMutation("Disconnect Node",
            undo: () => { foreach (var e in removed) graph.Edges.Add(e); },
            redo: () => { foreach (var e in removed) graph.Edges.Remove(e); });
    }

    /// <summary>Top-level token of a Category path — "Math/Trig" → "Math", "" → "Misc".
    /// Used to group node-creation entries under collapsible headers.</summary>
    private static string TopLevelCategory(string category)
    {
        if (string.IsNullOrEmpty(category)) return "Misc";
        int slash = category.IndexOf('/');
        return slash < 0 ? category : category.Substring(0, slash);
    }

    private void DrawCategoryHeader(Paper paper, string groupName, int count)
    {
        bool expanded = _expandedCategories.Contains(groupName);
        string arrow = expanded ? EditorIcons.ChevronDown : EditorIcons.ChevronRight;
        string id = $"graph_popup_group_{groupName}";
        using (paper.Row(id).Height(20)
            .ChildLeft(4).ChildRight(6).RowBetween(6)
            .BackgroundColor(System.Drawing.Color.FromArgb(255, 44, 46, 54))
            .Hovered.BackgroundColor(System.Drawing.Color.FromArgb(255, 58, 62, 74)).End()
            .Rounded(3)
            .OnClick(_ => ToggleCategoryExpanded(groupName))
            .Enter())
        {
            paper.Box($"{id}_lbl").Height(20)
                .Text($"{arrow}  {groupName}", EditorTheme.DefaultFont!)
                .TextColor(EditorTheme.Ink500).FontSize(EditorTheme.FontSize - 2)
                .Alignment(TextAlignment.MiddleLeft);
            paper.Box($"{id}_cnt").Width(UnitValue.Auto).Height(20)
                .Text(count.ToString(), EditorTheme.DefaultFont!)
                .TextColor(EditorTheme.Ink400).FontSize(EditorTheme.FontSize - 3)
                .Alignment(TextAlignment.MiddleRight);
        }
    }

    private void ToggleCategoryExpanded(string groupName)
    {
        if (!_expandedCategories.Add(groupName)) _expandedCategories.Remove(groupName);
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
            RegisterMutation($"Add Node ({nodeType.Name})",
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
            if (_contextMenuNode.HasValue)
                DrawNodeContextMenu(paper);
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
        foreach (var g in _graph.Groups)
            GraphRendering.DrawGroup(canvas, g, _view.Zoom, font,
                isSelected: _selectedGroups.Contains(g.Id));
        foreach (var note in _graph.StickyNotes)
            GraphRendering.DrawStickyNote(canvas, note, _view.Zoom, font,
                isSelected: _selectedStickyNotes.Contains(note.Id));

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
            bool hovered = _hoveredEdge == edge.Id;
            // Selected wins visually over hover; hover bumps brightness + thickness so
            // users see the click target before clicking.
            var color = selected ? new Color32(255, 200, 80, 255)
                                 : (hovered ? Brighten(baseColor) : baseColor);
            float thickness = selected ? 5.0f : (hovered ? 3.5f : 2.5f);
            GraphRendering.DrawWire(canvas, srcPos.Value, dstPos.Value, color, _view.Zoom, thickness, _graph.WireStyle);
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
                        GraphRendering.DrawDragWire(canvas, srcPos.Value, _dragWireEndGraph.Value, color, _view.Zoom, _graph.WireStyle);
                    else
                        GraphRendering.DrawDragWire(canvas, _dragWireEndGraph.Value, srcPos.Value, color, _view.Zoom, _graph.WireStyle);
                }
            }
        }

        // Marquee — graph space.
        if (_dragMode == DragMode.MarqueeSelect && _marqueeStartGraph.HasValue && _marqueeEndGraph.HasValue)
        {
            var rect = MakeMarqueeRect(_marqueeStartGraph.Value, _marqueeEndGraph.Value);
            GraphRendering.DrawMarquee(canvas, rect, _view.Zoom);
        }

        // Cut-line gesture — render the polyline the user is dragging so it's clear
        // which wires are about to be sliced. Drawn in red so it doesn't blend with
        // wires of any data-type colour.
        if (_cutLinePoints != null && _cutLinePoints.Count >= 2)
        {
            canvas.SetStrokeColor(new Color32(230, 90, 90, 220));
            canvas.SetStrokeWidth(2.0f);
            canvas.BeginPath();
            canvas.MoveTo(_cutLinePoints[0].X, _cutLinePoints[0].Y);
            for (int i = 1; i < _cutLinePoints.Count; i++)
                canvas.LineTo(_cutLinePoints[i].X, _cutLinePoints[i].Y);
            canvas.Stroke();
        }

        // Alignment snap guide lines — only visible while a snapped drag is active.
        if (_dragMode == DragMode.MoveNodes && _alignmentGuides.Count > 0)
        {
            var guideColor = new Color32(255, 200, 80, 180);
            canvas.SetStrokeColor(guideColor);
            canvas.SetStrokeWidth(1.0f);
            foreach (var (from, to) in _alignmentGuides)
            {
                canvas.BeginPath();
                canvas.MoveTo(from.X, from.Y);
                canvas.LineTo(to.X, to.Y);
                canvas.Stroke();
            }
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
        // Suppress the popup if the user just released an alt+right cut-line — a long
        // drag that ends on empty space shouldn't also open the creation menu.
        if (Input.IsAltPressed) return;
        var graphPoint = ScreenToGraph(e.PointerPosition);

        // Right-click on a node opens its context menu instead of the creation popup.
        var nodeHit = HitTestNode(graphPoint);
        if (nodeHit != null)
        {
            // Auto-select so menu actions (Duplicate, Disconnect, Delete) include it.
            if (!_selectedNodes.Contains(nodeHit.Id))
            {
                ClearAllSelection();
                _selectedNodes.Add(nodeHit.Id);
                SyncSelectionSystem();
            }
            _contextMenuNode = nodeHit.Id;
            _creationMenuLocal = e.PointerPosition - new Float2(
                (float)_canvasScreenRect.Min.X, (float)_canvasScreenRect.Min.Y);
            return;
        }

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
            // Double-click on a node? Currently used by SubgraphNode to open its
            // referenced asset; future behaviours (e.g. open per-node inspector window)
            // can hang off the same hook.
            double now = Time.UnscaledTotalTime;
            if (!additive && _lastClickedNode == hit.Id
                && now - _lastNodeClickTime < DoubleClickThresholdSeconds)
            {
                HandleNodeDoubleClick(hit);
                _lastNodeClickTime = -1; // consume so a 3rd click doesn't trigger again
            }
            else
            {
                _lastNodeClickTime = now;
                _lastClickedNode = hit.Id;
            }

            // Hit a node — selection logic.
            if (additive)
            {
                if (!_selectedNodes.Add(hit.Id)) _selectedNodes.Remove(hit.Id); // toggle
            }
            else
            {
                ClearAllSelection();
                _selectedNodes.Add(hit.Id);
            }
            SyncSelectionSystem();
            return;
        }

        // Sticky note hit — same selection model as nodes, so selecting one lands it in
        // the Inspector (so users can edit Title/Body). Checked before wires so stickies
        // on top of a wire capture the click.
        var stickyHit = HitTestStickyNote(graphPoint);
        if (stickyHit != null)
        {
            if (additive)
            {
                if (!_selectedStickyNotes.Add(stickyHit.Id)) _selectedStickyNotes.Remove(stickyHit.Id);
            }
            else
            {
                ClearAllSelection();
                _selectedStickyNotes.Add(stickyHit.Id);
            }
            SyncSelectionSystem();
            return;
        }

        // Group title-bar hit — only the title strip is interactive; clicks on the
        // group's body fall through to wires / empty space.
        var groupHit = HitTestGroupTitle(graphPoint);
        if (groupHit != null)
        {
            if (additive)
            {
                if (!_selectedGroups.Add(groupHit.Id)) _selectedGroups.Remove(groupHit.Id);
            }
            else
            {
                ClearAllSelection();
                _selectedGroups.Add(groupHit.Id);
            }
            SyncSelectionSystem();
            return;
        }

        // Empty space — but maybe near a wire? Wire hit-test uses a screen-pixel tolerance
        // so the catch zone stays the same regardless of zoom.
        const float wireHitPixels = 6f;
        float toleranceGraph = wireHitPixels / _view.Zoom;
        var wireHit = HitTestWire(graphPoint, toleranceGraph, out var closestOnWire);
        if (wireHit != null)
        {
            // Alt+click on a wire splits it with a RelayNode at the click point.
            if (Input.IsAltPressed)
            {
                SplitWireWithRelay(wireHit, closestOnWire);
                return;
            }

            if (additive)
            {
                if (!_selectedEdges.Add(wireHit.Id)) _selectedEdges.Remove(wireHit.Id);
            }
            else
            {
                ClearAllSelection();
                _selectedEdges.Add(wireHit.Id);
            }
            SyncSelectionSystem();
            return;
        }

        // Truly empty — clear selection unless adding.
        if (!additive)
        {
            ClearAllSelection();
            SyncSelectionSystem();
        }
    }

    private Edge? HitTestWire(Float2 graphPoint, float toleranceGraph)
        => HitTestWire(graphPoint, toleranceGraph, out _);

    /// <summary>
    /// Find the nearest wire within <paramref name="toleranceGraph"/> graph-units of
    /// <paramref name="graphPoint"/>, or null. Returns the closest point on the wire via
    /// <paramref name="closestPoint"/> — used by alt+click split to place the relay
    /// exactly on the wire path rather than at the (slightly off) cursor.
    /// </summary>
    private Edge? HitTestWire(Float2 graphPoint, float toleranceGraph, out Float2 closestPoint)
    {
        closestPoint = graphPoint;
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

            float dSq = GraphLayout.DistanceSqToWire(srcPos.Value, dstPos.Value, graphPoint, out var cp);
            if (dSq < bestSq) { bestSq = dSq; bestEdge = edge; closestPoint = cp; }
        }
        return bestEdge;
    }

    /// <summary>
    /// Alt+click on a wire: insert a <see cref="RelayNode"/> at <paramref name="splitAt"/>
    /// and reconnect src→relay→dst. Registered as one undo step.
    /// </summary>
    private void SplitWireWithRelay(Edge edge, Float2 splitAt)
    {
        if (_graph == null) return;
        var srcNode = _graph.FindNode(edge.SourceNodeId);
        var dstNode = _graph.FindNode(edge.TargetNodeId);
        if (srcNode == null || dstNode == null) return;
        var srcPort = srcNode.GetOutput(edge.SourcePortName);
        if (srcPort == null) return;

        // Build the relay typed to match the wire's data type. Position it so its centre
        // lands on the split point (the renderer's rect is 28×20).
        var relay = new RelayNode
        {
            CarriedTypeName = srcPort.DataType.AssemblyQualifiedName ?? typeof(object).AssemblyQualifiedName!,
            Position = new Float2(splitAt.X - 14f, splitAt.Y - 10f),
        };

        var newA = new Edge
        {
            SourceNodeId = edge.SourceNodeId, SourcePortName = edge.SourcePortName,
            TargetNodeId = relay.Id, TargetPortName = "In",
        };
        var newB = new Edge
        {
            SourceNodeId = relay.Id, SourcePortName = "Out",
            TargetNodeId = edge.TargetNodeId, TargetPortName = edge.TargetPortName,
        };
        var removed = edge;

        _graph.Edges.Remove(removed);
        _graph.Nodes.Add(relay);
        _graph.Edges.Add(newA);
        _graph.Edges.Add(newB);

        var graph = _graph;
        RegisterMutation("Insert Relay",
            undo: () =>
            {
                graph.Edges.Remove(newA);
                graph.Edges.Remove(newB);
                graph.RemoveNode(relay.Id);
                graph.Edges.Add(removed);
            },
            redo: () =>
            {
                graph.Edges.Remove(removed);
                graph.Nodes.Add(relay);
                graph.Edges.Add(newA);
                graph.Edges.Add(newB);
            });

        // Selecting the relay lets the user immediately drag to reposition.
        ClearAllSelection();
        _selectedNodes.Add(relay.Id);
        SyncSelectionSystem();
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
            var hit = HitTestNode(graphPoint);
            if (hit != null)
            {
                // Resize handle wins over body drag — same UX as stickies/groups.
                if (hit is IResizableNode rNode && IsOverNodeResizeHandle(hit, graphPoint))
                {
                    _resizingNode = hit.Id;
                    _resizeNodeStartSize = rNode.GetSize();
                    _dragMode = DragMode.ResizeNode;
                    return;
                }

                if (!_selectedNodes.Contains(hit.Id))
                {
                    if (!Input.IsCtrlPressed && !Input.IsShiftPressed) _selectedNodes.Clear();
                    _selectedNodes.Add(hit.Id);
                    SyncSelectionSystem();
                }
                // Capture node positions + cursor at drag start so each frame can
                // recompute absolute (unsnapped) positions. This is also what the
                // single undo step at drag-end uses for before/after.
                _dragMoveStartPositions = new Dictionary<Guid, Float2>();
                foreach (var id in _selectedNodes)
                {
                    var n = _graph.FindNode(id);
                    if (n != null) _dragMoveStartPositions[id] = n.Position;
                }
                _dragMoveStartCursor = graphPoint;
                _dragMode = DragMode.MoveNodes;
                return;
            }

            // Group hit-test — resize handle wins over title-bar move, which wins over
            // passing clicks through to interior nodes. Checked before stickies so a
            // group containing a sticky still acts like a group on the title bar.
            var groupResizeCandidate = (NodeGroup?)null;
            if (_graph != null)
            {
                for (int i = _graph.Groups.Count - 1; i >= 0; i--)
                {
                    if (IsOverGroupResizeHandle(_graph.Groups[i], graphPoint))
                    { groupResizeCandidate = _graph.Groups[i]; break; }
                }
            }
            if (groupResizeCandidate != null)
            {
                _activeGroup = groupResizeCandidate.Id;
                _groupDragStartPos = groupResizeCandidate.Position;
                _groupDragStartSize = groupResizeCandidate.Size;
                _dragMode = DragMode.ResizeGroup;
                return;
            }

            var groupTitleHit = HitTestGroupTitle(graphPoint);
            if (groupTitleHit != null)
            {
                if (!_selectedGroups.Contains(groupTitleHit.Id))
                {
                    if (!Input.IsCtrlPressed && !Input.IsShiftPressed) _selectedGroups.Clear();
                    _selectedGroups.Add(groupTitleHit.Id);
                    SyncSelectionSystem();
                }
                _activeGroup = groupTitleHit.Id;
                _groupDragStartPos = groupTitleHit.Position;
                _groupDragStartSize = groupTitleHit.Size;
                // Capture contained items so the whole cluster translates as a unit.
                _groupContainedNodeStartPositions = new Dictionary<Guid, Float2>();
                foreach (var n in NodesInsideGroup(groupTitleHit))
                    _groupContainedNodeStartPositions[n.Id] = n.Position;
                _groupContainedStickyStartPositions = new Dictionary<Guid, Float2>();
                foreach (var s in StickiesInsideGroup(groupTitleHit))
                    _groupContainedStickyStartPositions[s.Id] = s.Position;
                _dragMode = DragMode.MoveGroup;
                return;
            }

            // Sticky-note hit-test — resize corner wins over body drag so users can grab
            // the bottom-right handle even when body-drag would otherwise capture the event.
            var sticky = HitTestStickyNote(graphPoint);
            if (sticky != null)
            {
                if (IsOverStickyResizeHandle(sticky, graphPoint))
                {
                    _resizingStickyNote = sticky.Id;
                    _resizeStickyStartSize = sticky.Size;
                    _dragMode = DragMode.ResizeStickyNote;
                    return;
                }

                if (!_selectedStickyNotes.Contains(sticky.Id))
                {
                    if (!Input.IsCtrlPressed && !Input.IsShiftPressed) _selectedStickyNotes.Clear();
                    _selectedStickyNotes.Add(sticky.Id);
                    SyncSelectionSystem();
                }
                _dragStickyStartPositions = new Dictionary<Guid, Float2>();
                foreach (var id in _selectedStickyNotes)
                {
                    var s = FindStickyNote(id);
                    if (s != null) _dragStickyStartPositions[id] = s.Position;
                }
                _dragMode = DragMode.MoveStickyNotes;
                return;
            }

            // Left-click on empty space → marquee select.
            _dragMode = DragMode.MarqueeSelect;
            _marqueeStartGraph = graphPoint;
            _marqueeEndGraph = graphPoint;
            _marqueeAdditive = Input.IsCtrlPressed || Input.IsShiftPressed;
        }
    }

    private StickyNote? FindStickyNote(Guid id)
    {
        if (_graph == null) return null;
        foreach (var s in _graph.StickyNotes) if (s.Id == id) return s;
        return null;
    }

    private static StickyNote? FindStickyNoteIn(Prowl.Runtime.GraphTools.Graph graph, Guid id)
    {
        foreach (var s in graph.StickyNotes) if (s.Id == id) return s;
        return null;
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
            case DragMode.ResizeNode:
                if (_resizingNode.HasValue)
                {
                    var n = _graph.FindNode(_resizingNode.Value);
                    if (n is IResizableNode r)
                    {
                        var cursor = ScreenToGraph(e.PointerPosition);
                        var min = r.MinSize;
                        r.SetSize(new Float2(
                            MathF.Max(min.X, cursor.X - n.Position.X),
                            MathF.Max(min.Y, cursor.Y - n.Position.Y)));
                    }
                }
                break;

            case DragMode.MoveStickyNotes:
                {
                    var d = e.Delta / _view.Zoom;
                    foreach (var id in _selectedStickyNotes)
                    {
                        var s = FindStickyNote(id);
                        if (s != null) s.Position += d;
                    }
                }
                break;

            case DragMode.ResizeStickyNote:
                if (_resizingStickyNote.HasValue)
                {
                    var s = FindStickyNote(_resizingStickyNote.Value);
                    if (s != null)
                    {
                        // Resize from bottom-right corner — the cursor's graph position
                        // relative to the note's fixed top-left determines the new size.
                        var cursor = ScreenToGraph(e.PointerPosition);
                        s.Size = new Float2(
                            MathF.Max(StickyMinSize.X, cursor.X - s.Position.X),
                            MathF.Max(StickyMinSize.Y, cursor.Y - s.Position.Y));
                    }
                }
                break;

            case DragMode.MoveGroup:
                if (_activeGroup.HasValue)
                {
                    var g = FindGroup(_activeGroup.Value);
                    if (g != null && _groupContainedNodeStartPositions != null
                        && _groupContainedStickyStartPositions != null)
                    {
                        var d = e.Delta / _view.Zoom;
                        g.Position += d;
                        // Translate every captured member by the same delta. Using
                        // cumulative delta keeps the group + members in sync regardless
                        // of where they started.
                        foreach (var kv in _groupContainedNodeStartPositions)
                        {
                            var n = _graph.FindNode(kv.Key);
                            if (n != null) n.Position += d;
                        }
                        foreach (var kv in _groupContainedStickyStartPositions)
                        {
                            var s = FindStickyNote(kv.Key);
                            if (s != null) s.Position += d;
                        }
                    }
                }
                break;

            case DragMode.ResizeGroup:
                if (_activeGroup.HasValue)
                {
                    var g = FindGroup(_activeGroup.Value);
                    if (g != null)
                    {
                        var cursor = ScreenToGraph(e.PointerPosition);
                        g.Size = new Float2(
                            MathF.Max(GroupMinSize.X, cursor.X - g.Position.X),
                            MathF.Max(GroupMinSize.Y, cursor.Y - g.Position.Y));
                    }
                }
                break;

            case DragMode.MoveNodes:
                {
                    // Absolute positioning: each node = its drag-start position plus
                    // the cursor's graph-space displacement since drag-start. Computing
                    // from absolutes every frame means snapping can be re-evaluated
                    // against the *intended* position instead of accumulating shifts —
                    // when the user moves the cursor past the snap tolerance, the node
                    // pulls away cleanly instead of re-snapping.
                    if (_dragMoveStartPositions == null) break;
                    var cursorNow = ScreenToGraph(e.PointerPosition);
                    var cursorDelta = cursorNow - _dragMoveStartCursor;
                    foreach (var kv in _dragMoveStartPositions)
                    {
                        var n = _graph.FindNode(kv.Key);
                        if (n != null) n.Position = kv.Value + cursorDelta;
                    }
                    _alignmentGuides.Clear();
                    if (IsSnapModifierDown())
                        ApplyAlignmentSnap();
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
                RegisterMutation("Move Nodes",
                    undo: () => { foreach (var kv in starts) { var n = graph.FindNode(kv.Key); if (n != null) n.Position = kv.Value; } },
                    redo: () => { foreach (var kv in ends)   { var n = graph.FindNode(kv.Key); if (n != null) n.Position = kv.Value; } });
            }
            _dragMoveStartPositions = null;
        }

        // Resizable-node drag → one undo step.
        if (_dragMode == DragMode.ResizeNode && _resizingNode.HasValue && _graph != null)
        {
            var graph = _graph;
            var id = _resizingNode.Value;
            var n = graph.FindNode(id);
            if (n is IResizableNode r && !r.GetSize().Equals(_resizeNodeStartSize))
            {
                var before = _resizeNodeStartSize;
                var after = r.GetSize();
                RegisterMutation("Resize Node",
                    undo: () => { var x = graph.FindNode(id); if (x is IResizableNode xr) xr.SetSize(before); },
                    redo: () => { var x = graph.FindNode(id); if (x is IResizableNode xr) xr.SetSize(after); });
            }
            _resizingNode = null;
        }

        // Sticky note move/resize → one undo step per drag.
        if (_dragMode == DragMode.MoveStickyNotes && _dragStickyStartPositions != null && _graph != null)
        {
            var graph = _graph;
            var starts = _dragStickyStartPositions;
            var ends = new Dictionary<Guid, Float2>();
            bool anyMoved = false;
            foreach (var kv in starts)
            {
                var s = FindStickyNote(kv.Key);
                if (s == null) continue;
                ends[kv.Key] = s.Position;
                if (!s.Position.Equals(kv.Value)) anyMoved = true;
            }
            if (anyMoved)
            {
                RegisterMutation("Move Sticky Notes",
                    undo: () => { foreach (var kv in starts) { var s = FindStickyNoteIn(graph, kv.Key); if (s != null) s.Position = kv.Value; } },
                    redo: () => { foreach (var kv in ends)   { var s = FindStickyNoteIn(graph, kv.Key); if (s != null) s.Position = kv.Value; } });
            }
            _dragStickyStartPositions = null;
        }
        else if (_dragMode == DragMode.ResizeStickyNote && _resizingStickyNote.HasValue && _graph != null)
        {
            var graph = _graph;
            var id = _resizingStickyNote.Value;
            var s = FindStickyNoteIn(graph, id);
            if (s != null && !s.Size.Equals(_resizeStickyStartSize))
            {
                var before = _resizeStickyStartSize;
                var after = s.Size;
                RegisterMutation("Resize Sticky Note",
                    undo: () => { var x = FindStickyNoteIn(graph, id); if (x != null) x.Size = before; },
                    redo: () => { var x = FindStickyNoteIn(graph, id); if (x != null) x.Size = after; });
            }
            _resizingStickyNote = null;
        }
        else if (_dragMode == DragMode.MoveGroup && _activeGroup.HasValue && _graph != null
                 && _groupContainedNodeStartPositions != null
                 && _groupContainedStickyStartPositions != null)
        {
            var graph = _graph;
            var id = _activeGroup.Value;
            var g = FindGroupIn(graph, id);
            var nodeStarts = _groupContainedNodeStartPositions;
            var stickyStarts = _groupContainedStickyStartPositions;
            if (g != null && !g.Position.Equals(_groupDragStartPos))
            {
                var groupBefore = _groupDragStartPos;
                var groupAfter = g.Position;
                // Capture final positions of members so redo can replay exactly.
                var nodeEnds = new Dictionary<Guid, Float2>();
                foreach (var kv in nodeStarts)
                {
                    var n = graph.FindNode(kv.Key);
                    if (n != null) nodeEnds[kv.Key] = n.Position;
                }
                var stickyEnds = new Dictionary<Guid, Float2>();
                foreach (var kv in stickyStarts)
                {
                    var s = FindStickyNoteIn(graph, kv.Key);
                    if (s != null) stickyEnds[kv.Key] = s.Position;
                }

                RegisterMutation("Move Group",
                    undo: () =>
                    {
                        var gg = FindGroupIn(graph, id); if (gg != null) gg.Position = groupBefore;
                        foreach (var kv in nodeStarts)   { var n = graph.FindNode(kv.Key); if (n != null) n.Position = kv.Value; }
                        foreach (var kv in stickyStarts) { var s = FindStickyNoteIn(graph, kv.Key); if (s != null) s.Position = kv.Value; }
                    },
                    redo: () =>
                    {
                        var gg = FindGroupIn(graph, id); if (gg != null) gg.Position = groupAfter;
                        foreach (var kv in nodeEnds)   { var n = graph.FindNode(kv.Key); if (n != null) n.Position = kv.Value; }
                        foreach (var kv in stickyEnds) { var s = FindStickyNoteIn(graph, kv.Key); if (s != null) s.Position = kv.Value; }
                    });
            }
            _activeGroup = null;
            _groupContainedNodeStartPositions = null;
            _groupContainedStickyStartPositions = null;
        }
        else if (_dragMode == DragMode.ResizeGroup && _activeGroup.HasValue && _graph != null)
        {
            var graph = _graph;
            var id = _activeGroup.Value;
            var g = FindGroupIn(graph, id);
            if (g != null && !g.Size.Equals(_groupDragStartSize))
            {
                var before = _groupDragStartSize;
                var after = g.Size;
                RegisterMutation("Resize Group",
                    undo: () => { var x = FindGroupIn(graph, id); if (x != null) x.Size = before; },
                    redo: () => { var x = FindGroupIn(graph, id); if (x != null) x.Size = after; });
            }
            _activeGroup = null;
        }

        if (_dragMode == DragMode.MarqueeSelect && _marqueeStartGraph.HasValue && _marqueeEndGraph.HasValue && _graph != null)
        {
            var rect = MakeMarqueeRect(_marqueeStartGraph.Value, _marqueeEndGraph.Value);
            if (!_marqueeAdditive) ClearAllSelection();

            foreach (var n in _graph.Nodes)
                if (GraphLayout.GetNodeRect(n).Intersects(rect))
                    _selectedNodes.Add(n.Id);

            foreach (var s in _graph.StickyNotes)
            {
                var sRect = new Rect(s.Position.X, s.Position.Y,
                                      s.Position.X + s.Size.X, s.Position.Y + s.Size.Y);
                if (sRect.Intersects(rect)) _selectedStickyNotes.Add(s.Id);
            }

            // Groups join the marquee when the title bar overlaps it — body-only hits
            // would be noisy since groups are large and visually passive.
            foreach (var g in _graph.Groups)
            {
                var titleRect = new Rect(g.Position.X, g.Position.Y,
                                          g.Position.X + g.Size.X, g.Position.Y + GroupTitleHeight);
                if (titleRect.Intersects(rect)) _selectedGroups.Add(g.Id);
            }

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
        _alignmentGuides.Clear();
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
        RegisterMutation("Connect Wire",
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

    // ─── Alignment snap ──────────────────────────────────────────────────────────────
    // When Shift is held during a node drag, the primary (most-recently-picked) node's
    // left/centre/right and top/mid/bottom edges look for matches on every other node's
    // edges within a screen-pixel tolerance. If a match is found, the whole selection
    // shifts by the snap correction so the group stays coherent. No match = fall back
    // to grid snap. Guide lines get recorded for the canvas renderer.

    private static bool IsSnapModifierDown() => Input.IsShiftPressed;

    /// <summary>Graph-space snap tolerance scaled by zoom so it feels consistent.</summary>
    private float SnapTolerance() => SnapPixelTolerance / (_view?.Zoom ?? 1f);

    private void ApplyAlignmentSnap()
    {
        if (_graph == null || _selectedNodes.Count == 0) return;

        // Pick the anchor node — last inserted in the selection set iteration; stable
        // enough for drag purposes since HashSet<Guid> enumeration isn't guaranteed
        // ordered, but all selected nodes shift together so the choice of anchor only
        // changes WHICH edges are considered, not the correctness of the snap.
        Node? anchor = null;
        foreach (var id in _selectedNodes)
        {
            anchor = _graph.FindNode(id);
            if (anchor != null) break;
        }
        if (anchor == null) return;

        var aRect = GraphLayout.GetNodeRect(anchor);
        float aL = (float)aRect.Min.X, aR = (float)aRect.Max.X;
        float aT = (float)aRect.Min.Y, aB = (float)aRect.Max.Y;
        float aCx = (aL + aR) * 0.5f, aCy = (aT + aB) * 0.5f;

        float tol = SnapTolerance();
        float bestDx = 0, bestDy = 0;
        float bestAbsDx = tol, bestAbsDy = tol;
        float? snapLineX = null, snapLineY = null;

        // Try each non-selected node's vertical (X) edges against anchor's X edges, and
        // same for Y. "Best" = smallest absolute correction across all pairings.
        foreach (var other in _graph.Nodes)
        {
            if (_selectedNodes.Contains(other.Id)) continue;
            var oRect = GraphLayout.GetNodeRect(other);
            float oL = (float)oRect.Min.X, oR = (float)oRect.Max.X;
            float oT = (float)oRect.Min.Y, oB = (float)oRect.Max.Y;
            float oCx = (oL + oR) * 0.5f, oCy = (oT + oB) * 0.5f;

            foreach (float anchorX in new[] { aL, aCx, aR })
                foreach (float otherX in new[] { oL, oCx, oR })
                {
                    float dx = otherX - anchorX;
                    if (MathF.Abs(dx) < bestAbsDx) { bestAbsDx = MathF.Abs(dx); bestDx = dx; snapLineX = otherX; }
                }
            foreach (float anchorY in new[] { aT, aCy, aB })
                foreach (float otherY in new[] { oT, oCy, oB })
                {
                    float dy = otherY - anchorY;
                    if (MathF.Abs(dy) < bestAbsDy) { bestAbsDy = MathF.Abs(dy); bestDy = dy; snapLineY = otherY; }
                }
        }

        // Grid fallback when no other-node snap found (axis-by-axis).
        if (!snapLineX.HasValue)
        {
            float gridTarget = MathF.Round(aL / SnapGridSize) * SnapGridSize;
            bestDx = gridTarget - aL;
        }
        if (!snapLineY.HasValue)
        {
            float gridTarget = MathF.Round(aT / SnapGridSize) * SnapGridSize;
            bestDy = gridTarget - aT;
        }

        // Shift the whole selection.
        if (bestDx != 0 || bestDy != 0)
        {
            var delta = new Float2(bestDx, bestDy);
            foreach (var id in _selectedNodes)
            {
                var n = _graph.FindNode(id);
                if (n != null) n.Position += delta;
            }
        }

        // Record guide lines at the matched edges so the canvas can render them. Guides
        // span a generous range centred on the anchor — rendered behind nodes, clipped
        // by the canvas.
        if (snapLineX.HasValue)
            _alignmentGuides.Add((new Float2(snapLineX.Value, aT - 200), new Float2(snapLineX.Value, aB + 200)));
        if (snapLineY.HasValue)
            _alignmentGuides.Add((new Float2(aL - 200, snapLineY.Value), new Float2(aR + 200, snapLineY.Value)));
    }

    /// <summary>Apply the cut-line polyline: any wire whose bezier intersects any
    /// segment of the polyline gets removed, registered as one undo step.</summary>
    private void ApplyCutLine(List<Float2> points)
    {
        if (_graph == null || points.Count < 2) return;
        var cuts = new List<Edge>();
        foreach (var edge in _graph.Edges)
        {
            var srcNode = _graph.FindNode(edge.SourceNodeId);
            var dstNode = _graph.FindNode(edge.TargetNodeId);
            if (srcNode == null || dstNode == null) continue;
            var srcPos = GraphLayout.TryGetPortPosition(srcNode, edge.SourcePortName, PortDirection.Output);
            var dstPos = GraphLayout.TryGetPortPosition(dstNode, edge.TargetPortName, PortDirection.Input);
            if (!srcPos.HasValue || !dstPos.HasValue) continue;

            if (CutLineHitsWire(points, srcPos.Value, dstPos.Value))
                cuts.Add(edge);
        }

        if (cuts.Count == 0) return;
        foreach (var e in cuts) _graph.Edges.Remove(e);

        var graph = _graph;
        RegisterMutation("Cut Wires",
            undo: () => { foreach (var e in cuts) graph.Edges.Add(e); },
            redo: () => { foreach (var e in cuts) graph.Edges.Remove(e); });
    }

    /// <summary>True if any segment of the cut polyline crosses the wire's bezier
    /// (sampled). Sample density matches the wire hit-test for consistency.</summary>
    private static bool CutLineHitsWire(List<Float2> cut, Float2 from, Float2 to)
    {
        // Sample the bezier into a polyline.
        float dx = MathF.Abs(to.X - from.X);
        float tangent = MathF.Max(40f, dx * 0.5f);
        Float2 c1 = new Float2(from.X + tangent, from.Y);
        Float2 c2 = new Float2(to.X - tangent, to.Y);
        const int samples = 32;
        Span<Float2> wire = stackalloc Float2[samples + 1];
        for (int i = 0; i <= samples; i++)
        {
            float t = i / (float)samples;
            float u = 1f - t;
            float b0 = u * u * u, b1 = 3 * u * u * t, b2 = 3 * u * t * t, b3 = t * t * t;
            wire[i] = new Float2(
                b0 * from.X + b1 * c1.X + b2 * c2.X + b3 * to.X,
                b0 * from.Y + b1 * c1.Y + b2 * c2.Y + b3 * to.Y);
        }

        for (int i = 0; i < cut.Count - 1; i++)
        {
            for (int j = 0; j < samples; j++)
            {
                if (SegmentsIntersect(cut[i], cut[i + 1], wire[j], wire[j + 1]))
                    return true;
            }
        }
        return false;
    }

    /// <summary>Standard segment-vs-segment intersection test (proper crossing only —
    /// touching endpoints don't count as a hit).</summary>
    private static bool SegmentsIntersect(Float2 a1, Float2 a2, Float2 b1, Float2 b2)
    {
        float d1 = Sign((b2.X - b1.X) * (a1.Y - b1.Y) - (b2.Y - b1.Y) * (a1.X - b1.X));
        float d2 = Sign((b2.X - b1.X) * (a2.Y - b1.Y) - (b2.Y - b1.Y) * (a2.X - b1.X));
        float d3 = Sign((a2.X - a1.X) * (b1.Y - a1.Y) - (a2.Y - a1.Y) * (b1.X - a1.X));
        float d4 = Sign((a2.X - a1.X) * (b2.Y - a1.Y) - (a2.Y - a1.Y) * (b2.X - a1.X));
        return d1 != d2 && d3 != d4;
    }
    private static float Sign(float v) => v > 0 ? 1 : (v < 0 ? -1 : 0);

    /// <summary>Lift a colour toward white by ~30% — used for wire-hover highlight.</summary>
    private static Color32 Brighten(Color32 c)
        => new Color32(
            (byte)Math.Min(255, c.R + 60),
            (byte)Math.Min(255, c.G + 60),
            (byte)Math.Min(255, c.B + 60),
            c.A);

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
        var removedStickies = new List<StickyNote>();

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
        foreach (var id in _selectedStickyNotes)
        {
            var s = FindStickyNote(id);
            if (s != null) removedStickies.Add(s);
        }
        var removedGroups = new List<NodeGroup>();
        foreach (var id in _selectedGroups)
        {
            var g = FindGroup(id);
            if (g != null) removedGroups.Add(g);
        }

        if (removedNodes.Count == 0 && removedEdges.Count == 0 && removedStickies.Count == 0 && removedGroups.Count == 0) return;

        foreach (var n in removedNodes) _graph.RemoveNode(n.Id);
        if (_selectedEdges.Count > 0)
            _graph.Edges.RemoveAll(e => _selectedEdges.Contains(e.Id));
        foreach (var s in removedStickies) _graph.StickyNotes.Remove(s);
        foreach (var g in removedGroups) _graph.Groups.Remove(g);

        var graph = _graph;
        RegisterMutation("Delete Graph Elements",
            undo: () =>
            {
                foreach (var n in removedNodes) graph.Nodes.Add(n);
                foreach (var e in removedEdges) graph.Edges.Add(e);
                foreach (var s in removedStickies) graph.StickyNotes.Add(s);
                foreach (var g in removedGroups) graph.Groups.Add(g);
            },
            redo: () =>
            {
                foreach (var n in removedNodes) graph.RemoveNode(n.Id);
                foreach (var e in removedEdges) graph.Edges.Remove(e);
                foreach (var s in removedStickies) graph.StickyNotes.Remove(s);
                foreach (var g in removedGroups) graph.Groups.Remove(g);
            });

        ClearAllSelection();
        SyncSelectionSystem();
    }

    /// <summary>
    /// Push our internal selection (nodes + sticky notes) to the global
    /// <see cref="Selection"/> system so the Inspector panel shows the selected item(s).
    /// Called whenever selection changes. No-op when nothing is selected — we don't want
    /// to clear the Inspector just because the user clicked an empty part of the canvas.
    /// </summary>
    private void SyncSelectionSystem()
    {
        if (_graph == null) return;
        if (_selectedNodes.Count == 0 && _selectedStickyNotes.Count == 0 && _selectedGroups.Count == 0) return;

        bool first = true;
        foreach (var id in _selectedNodes)
        {
            var n = _graph.FindNode(id);
            if (n == null) continue;
            if (first) { Selection.Select(n); first = false; }
            else Selection.AddToSelection(n);
        }
        foreach (var id in _selectedStickyNotes)
        {
            var s = FindStickyNote(id);
            if (s == null) continue;
            if (first) { Selection.Select(s); first = false; }
            else Selection.AddToSelection(s);
        }
        foreach (var id in _selectedGroups)
        {
            var g = FindGroup(id);
            if (g == null) continue;
            if (first) { Selection.Select(g); first = false; }
            else Selection.AddToSelection(g);
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
        ClearAllSelection();
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
        ClearAllSelection();
        foreach (var n in added) _selectedNodes.Add(n.Id);
        SyncSelectionSystem();

        var graph = _graph;
        RegisterMutation("Paste Graph Elements",
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

    /// <summary>Hook for node-specific double-click behaviour. SubgraphNode opens its
    /// referenced graph asset in a new editor tab; other node types are no-op for now.</summary>
    private void HandleNodeDoubleClick(Node node)
    {
        if (node is SubgraphNode sub)
        {
            var inner = sub.Subgraph.Res;
            if (inner != null) OpenFor(inner);
        }
    }

    /// <summary>Ctrl+G — wrap the current selection (nodes + stickies) in a new
    /// <see cref="NodeGroup"/>. Group is positioned/sized with padding so its title bar
    /// sits above the topmost selected item. Registered as a single undo step.</summary>
    private void GroupSelection()
    {
        if (_graph == null) return;
        if (_selectedNodes.Count == 0 && _selectedStickyNotes.Count == 0) return;

        Float2 min = new Float2(float.MaxValue), max = new Float2(float.MinValue);
        bool any = false;
        foreach (var id in _selectedNodes)
        {
            var n = _graph.FindNode(id);
            if (n == null) continue;
            var r = GraphLayout.GetNodeRect(n);
            min = new Float2(MathF.Min(min.X, (float)r.Min.X), MathF.Min(min.Y, (float)r.Min.Y));
            max = new Float2(MathF.Max(max.X, (float)r.Max.X), MathF.Max(max.Y, (float)r.Max.Y));
            any = true;
        }
        foreach (var id in _selectedStickyNotes)
        {
            var s = FindStickyNote(id);
            if (s == null) continue;
            min = new Float2(MathF.Min(min.X, s.Position.X), MathF.Min(min.Y, s.Position.Y));
            max = new Float2(MathF.Max(max.X, s.Position.X + s.Size.X), MathF.Max(max.Y, s.Position.Y + s.Size.Y));
            any = true;
        }
        if (!any) return;

        // Padding around the contents + extra on top for the title bar.
        const float pad = 16f;
        var group = new NodeGroup
        {
            Position = new Float2(min.X - pad, min.Y - (pad + GroupTitleHeight)),
            Size = new Float2((max.X - min.X) + pad * 2, (max.Y - min.Y) + pad * 2 + GroupTitleHeight),
            Title = "Group",
        };

        _graph.Groups.Add(group);

        var graph = _graph;
        var added = group;
        RegisterMutation("Group Selection",
            undo: () => graph.Groups.Remove(added),
            redo: () => graph.Groups.Add(added));

        // Select the new group so the Inspector opens for Title editing.
        ClearAllSelection();
        _selectedGroups.Add(group.Id);
        SyncSelectionSystem();
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

}
