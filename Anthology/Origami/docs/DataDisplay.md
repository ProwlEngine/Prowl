# Data Display

Widgets for showing structured or tabular data: tables, trees, property grids, charts, flame graphs, node graphs and image comparisons.

## Table

A bordered data grid with typed columns, sortable headers, selection, and optional fixed-height virtualized scrolling for long lists.

![Screenshot: Table](images/datadisplay/table.png)

```csharp
Origami.Table(paper, "assets-table", selectedRow, i => selectedRow = i)
    .Column("Name", flex: 2f, sortable: true)
    .Column("Type", flex: 1f)
    .Column("Size", flex: 1f, align: TextAlignment.MiddleRight)
    .Row().Cell("Player.cs", ink).Cell("Script", ink).CellRight("4.2 KB", ink)
    .Row().Cell("Texture.png", ink).Cell("Image", ink).CellRight("512 KB", ink)
    .Show();
```

- `Scroll(width, height)` fixes the table size with an internal scrolling body (header stays pinned); add `.Virtualize()` for long lists so only visible rows are laid out
- `Sort(activeColumn, ascending, onSortColumn)` wires a directional caret and click-to-sort on sortable columns
- `MultiSelect()` + `IsSelected(...)` + `OnSelectModified(...)` for ctrl/shift multi-row selection instead of the default single `selectedIndex`
- `OnRowActivate(...)` / `OnRowContext(...)` for double-click and right-click row actions
- `CellContent(draw)` + `RowCount(n)` replace `.Row().Cell(...)` data with fully custom per-cell drawing (badges, inline editors, etc.)

Notes: `Virtualize()` requires `Scroll(...)` to be set first, since it needs a fixed viewport to compute the visible row range.

## Tree

A virtualized hierarchical list with expand/collapse, checkboxes, drag-drop, rename, and context menus. Nodes are supplied as a flat, depth-first list; the widget manages expand state internally.

![Screenshot: Tree](images/datadisplay/tree.png)

```csharp
var nodes = new List<TreeNode>
{
    new() { Id = "root", Label = "Scene", Depth = 0, HasChildren = true, DefaultExpanded = true },
    new() { Id = "cam",  Label = "Main Camera", Depth = 1, IsLeaf = true },
    new() { Id = "player", Label = "Player", Depth = 1, IsLeaf = true },
};

Origami.Tree(paper, "hierarchy", 260, 400)
    .Nodes(nodes)
    .IsSelected(n => n.Id == selectedId)
    .OnSelect(e => selectedId = e.Node.Id)
    .Show();
```

- `Checkboxes()`, `MultiSelect()`, `Reorderable()` toggle common tree features
- `OnExpandChanged(...)` for lazy-loading children when a node opens
- `CanDrag(...)` / `OnDragStart(...)` / `CanDrop(...)` / `OnDrop(...)` for drag-and-drop reparenting
- `OnRenamed(...)` commits an inline rename (set `node.IsRenaming = true` to enter rename mode)
- `CustomRowContent(...)` overrides row content entirely while the tree still draws the caret and checkbox

Notes: children of a collapsed parent must be omitted from the `Nodes` list by the caller — the tree does not filter them out itself.

## PropertyGrid

Reflection-driven field editor for plain objects, similar to a Unity/Godot inspector. Renders one row per serializable field, recursing into nested objects, lists, and enums, with pluggable per-type field drawers.

![Screenshot: PropertyGrid](images/datadisplay/propertygrid.png)

```csharp
var config = new PropertyGridConfig();
config.Drawers.Register<Color>(new ColorFieldDrawer());

Origami.PropertyGrid(paper, "inspector", selectedObject, config)
    .OnChanged(obj => MarkDirty(obj))
    .Show();
```

- Pass an `IReadOnlyList<object>` instead of a single target to edit a multi-selection; fields that differ across targets are flagged as mixed and edits apply to all
- `PropertyGridConfig.Drawers` registers `FieldDrawer`s per type; `Handlers` registers `AttributeHandler`s per attribute (e.g. `[Range]`, `[Header]`); `CustomEditors` replaces whole nested-object rendering
- `ExpandByDefault(true)` starts nested objects and list entries open instead of collapsed
- `Overrides(HashSet<string>)` highlights prefab-style field overrides with an accent dot

Notes: fields are discovered via reflection (public fields, or private fields tagged `[SerializeField]`); a field's type needs a registered `FieldDrawer`, a custom editor, or its own serializable fields to render as anything other than a read-only "(unsupported)" note.

## Charts

A family of chart widgets under `Prowl.OrigamiUI.Charts`, all reached through the `Chart` static class: `Chart.CreateCartesian<T>`, `Chart.Histogram<T>`, `Chart.Pie<T>`, `Chart.Donut<T>`, `Chart.Radar<T>`, `Chart.FlameGraph<T>`. Each takes the `Paper`, an id, and an optional `IReadOnlyList<T>` data set, builds a stateless-per-frame builder, and finishes with `.Show()` - pass the full current data set on every call. Every chart type owns its full layout (box, title, legend column) so it never overflows its box, and shares the same chrome: `.Title(...)`, `.Size(w, h)` / `.Width(...)` / `.Height(...)`, `.Padding(...)`, `.Variant(OrigamiVariant)` (or `.Primary()`, `.Success()`, etc. - tints the default palette), `.Legend()` / `.LegendInteractive()` (click a swatch to hide/show what it represents), and `.EmptyLabel(...)` for the placeholder text shown when there's nothing to draw.

![Screenshot: Chart](images/datadisplay/chart.png)

### Cartesian (Line, Bar, Scatter, Bubble)

`Chart.CreateCartesian<T>(paper, id, data)` returns a `CartesianChart<T>` with no marks of its own; plug in one or more modules with `.AddLineChart()`, `.AddBarChart()`, `.AddScatterPlot()`, `.AddBubbleChart()`. Every module shares the chart's x axis, so a line and a bar module added to the same chart line up their marks at the same index. A module call returns the module itself for its own styling; chain back to the owning chart with `.Cartesian` to add another module or set an axis/grid/sampler option.

```csharp
Chart.CreateCartesian<FrameSample>(paper, "fps-chart", frames)
    .X(f => f.Time)
    .YRange(0, 144)
    .Axes()
    .AddLineChart()
        .Y(f => f.Fps).Name("FPS").Color(Color.LimeGreen).Fill()
        .Cartesian
    .AddLineChart()
        .Y(f => f.FrameTimeMs).Name("Frame Time (ms)").Color(Color.OrangeRed)
    .Show();
```

Each module reads its series either from `.Y(selector)` against the chart's `.X(selector)` and shared data set, or from a pre-sampled `.Series(label, color, values)` where the list index is the x value. Per-series styling chains off whichever call added the series last: `.Name(...)`, `.Color(...)`, `.Stroke(...)`, `.StrokeWidth(...)`, `.Fill()`, `.Dashed()` / `.Dotted()`, `.Visible(bool)`.

- `LineModule`: `.Smooth()` / `.Interpolation(CartesianInterpolation)` for a Catmull-Rom curve instead of straight segments
- `BarModule`: `.BarWidth(0..1)` (share of the x unit the whole group of bars occupies, default 0.8) and `.BarGap(0..1)` (spacing between bars in the group, default 0.1); every visible series gets its own bar side by side at each index, growing from zero
- `ScatterModule`: `.MarkerSize(px)` and `.Marker(MarkerShape)` (`Circle`, `Square`, `Triangle`, `Diamond`, `Cross`); no connecting stroke between points
- `BubbleModule`: like Scatter but `.MarkerSize(Func<T, float> selector)` sizes each point's diameter per-item instead of fixing it

Shared chart-level options (on `CartesianChart<T>` / any single-geometry Cartesian type):
- `.YRange(min, max)` / `.IncludeZero()` / `.MinSpan(span)` / `.Scale(AxisScale.Linear|Log)` / `.YTicks(count)` control the y axis; `.AutoFit()` recomputes the y range from whatever's currently visible each frame (pairs well with `.LegendInteractive()`)
- `.ValueFormatter(v => ...)` / `.XTickFormatter(i => ...)` / `.XTicks(count)` / `.XLabel(...)` / `.YLabel(...)` / `.Axes(bool)` customize axis text and visibility
- `.GridLines(countY)` / `.GridLines(countX, countY)` for a fixed grid, or `.GridTickLines(ratioY)` / `.GridTickLines(ratioX, ratioY)` to draw N grid lines per axis tick; `.GridLineColor(...)` overrides the color
- `.Sampleable()` turns on a pointer-tracked crosshair with a value popup; `.SampleLineColor(...)` recolors it
- `.Zoomable()` (scroll-wheel, anchored on the pointer) and `.Pannable()` (middle-mouse drag) - which axes they affect depends on the chart type (Scatter/Bubble pan both axes; Line/Bar pan x only)
- `.BackgroundColor(...)` tints the chart's own container

### Histogram

`Chart.Histogram<T>(paper, id, data)` bins one or more groups of raw values against one shared run of bin edges, so every group's bar for a given range sits beside the others inside it. Inherits every Cartesian axis/grid/sampler option above.

```csharp
Chart.Histogram<float>(paper, "latency-hist", latencies)
    .Value(v => v)
    .BinCount(20)
    .XTickFormatter(i => $"{i}ms")
    .Show();
```

- `.Value(selector)` projects the data set into one unlabelled group; add further groups with `.Series(label, color, values)` (each a full set of raw values, not pre-binned)
- `.BinCount(n)` (default 10) or `.BinWidth(w)` (fixes bin size, takes precedence)
- `.Normalize()` plots each bin as a fraction of its own group's total instead of a raw count
- `.Fill(color)` sets the interior color of the most recently added group, separate from its `.Color(...)` accent

### Pie / Donut

`Chart.Pie<T>(paper, id, data)` and `Chart.Donut<T>(paper, id, data)` turn each data item into a wedge whose share of the circle is its value's share of the visible total. A donut is a pie with a hole.

```csharp
Chart.Pie<Category>(paper, "breakdown", categories)
    .Name(c => c.Label)
    .Value(c => c.Amount)
    .ShowPercent()
    .Show();

Chart.Donut<Category>(paper, "breakdown2", categories)
    .Name(c => c.Label)
    .Value(c => c.Amount)
    .InnerRadius(0.6f)
    .Show();
```

- `.Name(selector)` / `.Value(selector)` project labels and values from the data set; `.ColorFunction((item, index) => color)` overrides the default ramp cycling
- `.StartAngle(degrees)` (default -90, twelve o'clock) / `.Clockwise(bool)` control layout direction
- `.Explode(index)` pulls one slice (by its index in the source data) out along its middle; call once per slice to explode several
- `.InnerRadius(0..0.95)` (Donut only, default 0.6) sizes the hole
- `.SortBy(key, descending)` reorders slices around the circle without changing which item a color function or legend toggle refers to
- `.Labels(bool)` / `.ShowValues()` / `.ShowPercent()` control per-slice text; `.Tooltip(bool)` toggles the hover readout (on by default, since a wedge carries no axis to read a value off)

### Radar

`Chart.Radar<T>(paper, id, data)` spokes one per data item (labelled by `.Name(...)`), with each `.Series(...)` drawn as one closed polygon whose value at spoke *i* comes from index *i*. With no series added, the items' own `.Value(...)` values form a single polygon instead.

```csharp
Chart.Radar<Stat>(paper, "build-stats", stats)
    .Name(s => s.Label)
    .Series("This Build", Color.DodgerBlue, thisBuildValues).Fill()
    .Series("Last Build", Color.Gray, lastBuildValues).Fill(false)
    .YTicks(4)
    .Show();
```

- `.Series(label, color, values)` adds a polygon; `.Color(...)`, `.Fill(bool)` (default on), `.StrokeWidth(px)` style the one just added
- `.YTicks(count)` sets the number of concentric grid rings (default 4); `.Range(min, max)` fixes the value scale instead of deriving it from the visible series
- `.Labels(bool)` toggles the spoke labels around the outside

### FlameGraph

`Chart.FlameGraph<T>(paper, id, data)` lays a tree out as nested horizontal bars: each node's width is its share of the forest total and its row is its depth, so a child sits directly under the parent it came from. Zoom and pan run along the value (horizontal) axis only - rows keep a fixed height and clip against the plot instead of scrolling.

```csharp
Chart.FlameGraph<ProfileNode>(paper, "profiler", new[] { rootNode })
    .Name(n => n.Name)
    .Value(n => n.SelfMs)
    .Children(n => n.Children)
    .ValueFormatter(ms => $"{ms:0.00} ms")
    .Zoomable()
    .Pannable()
    .Show();
```

- `.Name(selector)` / `.Value(selector)` / `.Children(selector)` resolve the tree from the data set passed at construction (one root per top-level item; without `.Children(...)` the set draws as a single flat level). A value that reads as finite and positive is taken as the node's own weight; otherwise the node weighs whatever its children add up to, so a container item with no number of its own still gets a span
- `.ColorFunction((item, depth) => color)` overrides the default per-depth shading
- `.RowHeight(px)` sets the height of one depth level (default 18)
- `.SortBy(key, descending)` orders siblings at every level; `.Highlight(predicate)` dims every node that fails it
- `.OnNodeClick(item => ...)` fires on a cell click; `.Selected(item)` marks one node with the same ring hover uses, independent of the pointer
- `.ShowValues()` / `.ShowPercent()` add to each cell's label text; `.Tooltip(bool)` toggles the hover readout

## NodeGraph

A pannable, zoomable node-based graph editor. Nodes are real Paper elements (so text stays crisp at any zoom); wires, the grid, and port dots are drawn on the canvas from the same transform, keeping wire endpoints frame-perfect against the nodes.

![Screenshot: NodeGraph](images/datadisplay/nodegraph.png)

```csharp
var nodes = new List<GraphNode>
{
    new() { Id = "a", Title = "Input", Position = new Float2(0, 0), Outputs = { new GraphPort("out", "Value") } },
    new() { Id = "b", Title = "Output", Position = new Float2(220, 0), Inputs = { new GraphPort("in", "Value") } },
};
var connections = new List<GraphConnection> { new("a", "out", "b", "in") };

Origami.NodeGraph(paper, "graph", 800, 500)
    .Nodes(nodes)
    .Connections(connections)
    .OnConnect(req => connections.Add(new GraphConnection(req.FromNode, req.FromPort, req.ToNode, req.ToPort)))
    .OnSelectionChanged(sel => selection = sel)
    .Show();
```

- `Groups(...)` / `Stickies(...)` add resizable backdrop groups and sticky notes to the canvas
- `Controller(new NodeGraphController())` gives programmatic control over pan/zoom/selection (`FrameAll()`, `CenterOn(...)`, `FocusNode(...)`)
- `OnValidateConnection(...)` rejects invalid wire connections before `OnConnect` fires
- `OnNodesMoved(...)`, `OnDeleteSelection(...)`, `OnNodeContext(...)` cover the common edit interactions
- `Minimap()` shows the nodes, groups and notes in the corner, click or drag it to move the view
- `SnapToGrid(step)` snaps anything dragged on the canvas (nodes, groups, notes) to the step; wire reroute points move freely
- `AllowSelfConnections()` leaves a node wired to itself to the validator
- `ReadOnly()` shows the graph without letting anything be moved, wired or deleted
- `InitialView(pan, zoom)` sets where the graph opens, once, the first time that widget id is shown
- Node ids have to be unique; two nodes sharing one throws rather than drawing one of them twice

### Node content

A node draws its own body: fields, sliders, a preview, anything Paper can build. Set `BodyHeight` for
the space it needs, and multiply sizes by `ctx.Zoom` so the content scales with the graph.

```csharp
node.BodyHeight = 46f;
node.Body = ctx =>
{
    using (ctx.Paper.Row(ctx.Id("row")).Width(ctx.Paper.Percent(100)).Height(ctx.S(16f)).Enter())
        ctx.Paper.Box(ctx.Id("label")).Text("Scale", font).FontSize(ctx.S(11f));
};
```

Anything inside the body that handles its own input goes through `ctx.Control(...)`, which stops the
event bubbling up: without it a drag on a slider moves the node instead. A drag on the body's
background still moves the node, as it should.

`Badge` puts a chip on the header (an error marker, a live value) with its own hover text, `Tooltip`
on a node or a port explains it, and `Collapsible` adds a fold chevron that raises
`OnNodeToggleCollapsed` for the host to act on. A collapsed node draws its header alone, and its
`Body` is not called at all, so live content costs nothing while it is folded.

`Pill` makes a node a small capsule with no header, for a relay or reroute point. A capsule has room
for its tooltip and its sockets, and nothing else, so `Badge` and `Collapsible` do not apply to one.

`GraphPort.Side` puts a port on any of the four edges and `PortShape.Arrow` draws it as a chevron, so
a left-to-right data graph and a top-down behaviour tree are the same code path. A node with ports
along its top or bottom edge is widened to fit them, which `NodeGraphPreview.MeasureWidth` reports.

### Ports that do not exist yet

A node that takes a list of inputs (a layer stack, a blend space, a sub graph's parameters) carries an
open socket: `GraphPort.IsPlaceholder`. It draws as a hollow plus, and wiring it raises `OnConnect`
with `ToPlaceholder` or `FromPlaceholder` set, so the host creates the real port, names it after
whatever is at the other end, and rewrites the request to use it.

```csharp
node.Inputs.Add(new GraphPort("add", "") { IsPlaceholder = true });

void Connect(ConnectionRequest req)
{
    if (req.NeedsPort && !CreatePort(ref req)) return;
    wires.Add(new GraphConnection(req.FromNode, req.FromPort, req.ToNode, req.ToPort));
}
```

Dragging out of a placeholder works too, so an inputs card can be dragged onto whatever it should
feed and take that port's name.

### Sub graphs

The widget draws one graph at a time, so a sub graph is a second list of nodes the host swaps in.
`OnNodeDoubleClick` is the way in, the host keeps the stack and draws its own breadcrumb to come back,
and whether the inner graph lives in this file or in another asset is never the widget's business.

A sub graph node has no ports of its own: the host derives them from the inner graph, so renaming a
parameter inside renames the port outside. Deriving them runs every frame, so compare before you
rebuild, or every node allocates a fresh set of ports per frame. Mark the inner graph's boundary cards `Pinned`
and they can be wired but not dragged or deleted, and no group carries them off.

The sample's Shader Graph panel does all of this in about a hundred lines: a `GraphDoc` per graph, a
path of ids, ports derived in `SyncSubGraphs`, and `Create Sub Graph` in the multi-select menu, which
moves the selection into a new document and turns every wire crossing the boundary into a parameter.

### A running graph

`GraphConnection.Thickness` scales a wire's width, `Flow` sends dots travelling along it and
`FlowSpeed` sets how fast, so a blend weight or an active path is visible while the graph runs. Hook
`OnDisconnect(...)` and dragging a connected input moves that wire instead of starting a second one.

Two wires between the same pair of ports look identical to the widget, so give each a
`GraphConnection.Id` if a graph allows them: selection, reroute points and `OnDisconnect` all key on
it, and without one the pair is treated as a single wire.

`OnStickyEdited(...)` reports an edited note, and `OnWireAddPoint` / `OnWirePointMoved` /
`OnWireRemovePoint` cover reroute points: right-click a wire to drop one, drag it, right-click it to
take it away.

Notes: the builder does not mutate your node/connection lists itself (aside from what you do in the callbacks) — `OnConnect`, `OnNodesMoved`, and `OnDeleteSelection` all hand back data for the caller to apply.

## ImageDiff

A before/after image comparison with a draggable vertical split bar.

![Screenshot: ImageDiff](images/datadisplay/imagediff.png)

```csharp
Origami.ImageDiff(paper, "diff", beforeTexture, afterTexture)
    .Height(300)
    .SplitPosition(0.5f)
    .Show();
```

- `SplitPosition(0..1)` sets the initial split; the user can then drag the handle
- `BarWidth(...)` / `HandleSize(...)` adjust the divider's visual size

Notes: the `imageA` / `imageB` parameters are typed `object` so the widget stays host-agnostic — the host's render backend is responsible for resolving them to a drawable texture.
