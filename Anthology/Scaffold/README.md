# Prowl.Scaffold

A persistent, rendering-independent layout engine for the Prowl Game Engine ecosystem. Scaffold owns
a tree of boxes and solves their geometry; it draws nothing and knows nothing about your renderer.
Keep the tree alive between frames and it only recomputes what actually changed.

## Features

- **Layout modes**
  - Rows and columns, with wrapping and reverse order
  - Overlay containers, where children share one content box
  - Equal-width grids with content-height rows
  - Absolute children anchored by left/right/top/bottom

- **Sizing**
  - Composite `Length`: pixels + percentage + auto-content, with grow and shrink weights
  - Weighted grow, and shrink weighted by base size, both clamped to min/max
  - Pixel or percentage padding, margins and size bounds
  - Grow-weighted margins, for centering and pushing without spacer elements
  - Aspect ratio, deriving one auto axis from a definite one

- **Placement**
  - Six justification modes along the layout axis
  - Per-container and per-child cross-axis alignment, including stretch
  - Item and line gaps

- **Incremental**
  - Edits dirty only the affected node and its ancestors; clean subtrees are reused
  - A clean root relays out in O(1)
  - Steady-state layout allocates zero managed bytes
  - `LayoutStatistics` reports what was actually measured, arranged and reused

## Usage

```csharp
using Prowl.Scaffold;

var tree = new LayoutTree(capacity: 1024);
var root = tree.Create(Style.Default with { Layout = LayoutMode.Row, Gap = 8 });
var sidebar = tree.Create(Style.Default with { Width = 240, Height = Length.Stretch() }, root);
var body = tree.Create(Style.Default with { Width = Length.Stretch(), Height = Length.Stretch() }, root);

tree.Layout(root, new Size(1280, 720));
Rect bounds = tree.GetWorldRect(body);
```

`Style.Default` starts with auto dimensions, no shrink, unlimited maxima and column flow. Note that
`default(Style)` is a valid zero-sized style, not the defaults.

### Keeping the tree between frames

Hold onto the tree and the node handles. `SetStyle`, `SetMeasure`, `PlaceAfter`, `Create` and
`Remove` dirty the nodes they affect. Declaring a style equal to the current one is a no-op, so
rebuilding your styles every frame costs nothing when the values did not move.

```csharp
tree.SetStyle(sidebar, tree.GetStyle(sidebar) with { Width = 280 });
tree.Layout(root, new Size(1280, 720));
```

Scaffold cannot see your strings or font metrics, so tell it when content changed:
`MarkDirty(node)` for one node, `MarkSubtreeDirty(node)` for a whole subtree after a font or DPI
change. Repeated invalidations coalesce at an already-dirty ancestor.

### Measuring intrinsic content

An auto-sized node asks a callback for its content size. The callback receives the node, the
available content space and a caller-supplied context.

```csharp
tree.SetMeasure(label, (node, available, context) => MeasureText((string)context!, available.Width), text);
```

Callbacks must return finite nonnegative sizes, must not mutate the tree or call `Layout`
recursively, and should be pure with respect to their constraints.

The context is opaque to Scaffold, so it cannot tell when the content behind a callback changed.
Register once and call `MarkDirty` for updates; that keeps the node's measurement cached. Calling
`SetMeasure` again always invalidates, which makes re-registering a safe fallback when you are not
sure. Either way the cost is bounded to that node's ancestor path, not the tree.

### Reading results back

`GetRect` returns a parent-local rectangle. `GetWorldRect` composes origins up to the root, at a
cost of O(depth). To hand a whole subtree to a renderer in one pass, use `CopyWorldRects` with a
buffer you own and reuse.

```csharp
var results = new LayoutResult[tree.Count]; // allocate once, outside the hot path
int count = tree.CopyWorldRects(root, results);
for (int i = 0; i < count; i++)
{
    NodeId node = results[i].Node;
    Rect bounds = results[i].WorldRect;
}
```

Entries come back in parent-first hierarchy order. Hidden nodes are included with zero rectangles.
Size the buffer with `GetSubtreeCount`, or just `tree.Count`; an undersized buffer is rejected
before anything is written.

## Notes and limits

- Sizes are border-box. Padding reduces content space, and absolute anchors use the parent's
  content box.
- The root's declared width and height are overridden by the viewport passed to `Layout`.
- Percentages resolve against the available parent content size, including during intrinsic
  measurement. Auto/percentage feedback uses that available size rather than a CSS fixed-point solve.
- Size bounds, padding and margins are `Length` values, so a percentage there replaces the default
  rather than adding to it: `MaxWidth = Length.Percentage(50)` means exactly half. Bounds and
  padding take pixels and percentages only; a grow weight is meaningful on a margin, where it turns
  the edge into a flexible spacer.
- A stretching child contributes its natural size while its parent's cross axis is still being
  derived from content, so an empty spacer cannot inflate an auto-sized container.
- Grid fills each cell's width subject to min/max. It is not CSS grid: there are no tracks or spans.
- Reverse changes flow order, not writing direction.
- Ordinary rows are linear in child count, but constrained flex freezes bound violations and
  redistributes, so a pathological line can reach O(k squared). A dirty child can force its siblings
  to be revisited to resolve shared space.
- Trees are single-threaded and limited to 256 levels, checked before insertion or reparenting.
  Handles reject foreign owners and removed slots.
- Layout is not transactional. If a measure callback throws, read nothing until a later `Layout`
  succeeds.
- Creating the tree, growing capacity and your own delegates and contexts allocate; steady-state
  layout does not. `Reserve` preallocates node storage.

## Verification

```sh
dotnet test Scaffold/Tests/Scaffold.Tests.csproj -c Release
```
