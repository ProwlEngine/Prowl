# Layout Engine

Paper does not solve layout itself. It builds an element tree each frame, hands the geometry to
[Scaffold](../../Scaffold) as a persistent layout tree, and copies the resulting rectangles back
onto its elements. Scaffold is a standalone package with no rendering dependency, so the rules
documented here are Scaffold's rules expressed through Paper's style system.

Every layout property is an ordinary Paper style property, which means all of them animate through
`Transition`, inherit through `InheritStyle`, and work inside state variants like `Hovered`. There
is no separate geometry object and no second way to declare a size.

Layout is also cached between frames. Re-declaring identical geometry costs nothing, and changing
only paint properties does not recompute anything.

## `UnitValue`

Sizes, margins, padding and anchors are `UnitValue`, a struct whose components compose
arithmetically:

```
resolved = Px + Pct% * parentValue + Grow * (share of leftover) + AutoFactor * contentSize
```

Factory helpers, also exposed on `Paper` and `ElementBuilder`:

```csharp
UnitValue.Pixels(float value)                       // Paper.Pixels(value)
UnitValue.Percentage(float value, float pxOffset=0) // Paper.Percent(value, pixelOffset)
UnitValue.Stretch(float factor = 1f)                // Paper.Stretch(factor)
UnitValue.Auto                                      // Paper.Auto
UnitValue.ZeroPixels
```

Implicit conversions exist from `int` and `float` as pixels, and `+`, `-`, `*` and `/` combine
values component-wise, so `Stretch(1) + Pixels(10)` and `Percent(50) - Pixels(8)` both work.
`UnitValue.Lerp` interpolates every component independently, which is what transitions use.

Predicates: `IsFixed`, `IsAuto`, `IsStretch`, `IsPixels`, `IsPercentage`, `HasGrow`, `HasAuto`.

## The property set

Paper's layout properties map one to one onto Scaffold's `Style`:

| Paper | Purpose |
| --- | --- |
| `Width`, `Height` | the element's own size |
| `MinWidth`/`MaxWidth`, `MinHeight`/`MaxHeight` | bounds, in pixels or percent of the parent |
| `Padding`, `PaddingLeft/Right/Top/Bottom` | inset applied to this element's content box |
| `Margin`, `Left`/`Right`/`Top`/`Bottom` | this element's own outer spacing |
| `Gap`, `LineGap` | space between children, and between wrapped lines or grid rows |
| `AnchorLeft/Right/Top/Bottom` | anchors for self-directed elements |
| `AspectRatio` | width divided by height |

Size bounds and padding accept pixels and percentages only. A grow, auto or shrink component there
is rejected with `NotSupportedException` rather than silently dropped, because an intrinsic or
flexible value belongs on a size or on a spacer element.

A percentage bound is the whole bound, not an addition to the default. `MaxWidth(Percent(50))`
means exactly half the parent.

## Containers

```csharp
paper.Row("toolbar")            // main axis is horizontal
paper.Column("sidebar")         // main axis is vertical
paper.Grid("cards").Columns(3)  // equal-width columns, rows size to their tallest item
paper.Overlay("stack")          // children share one content box
```

All four take `(stringID, intID = 0, lineID)`, so `Columns(n)` is a separate call rather than a
positional argument that would collide with the id.

## Spacing and alignment

Three distinct jobs, three properties:

- **`Padding`** insets the container's content box. Children cannot escape it, and percentage
  sizes resolve inside it.
- **`Gap`** separates adjacent children along the layout direction. **`LineGap`** separates wrapped
  lines and grid rows.
- **`Margin`** is an individual element's own outer spacing. `Auto` means no margin.

```csharp
using (paper.Column("panel").Padding(12).Gap(8).Enter())
{
    paper.Box("a").Height(24);
    paper.Box("b").Height(24);   // 8px below "a", 12px in from the panel edges
}
```

For alignment, `AlignItems` sets the cross-axis alignment of a container's children, `AlignSelf`
overrides it for one child, and `JustifyContent` distributes leftover space along the main axis.
`ReverseLayout` flips flow order without changing declaration order.

`LayoutJustification.Fill` grows every item on a line to fill it, and applies to wrapping
containers.

Grow margins are the other way to position a single element: an edge declared as `Stretch()`
becomes a flexible spacer, so `Left(Stretch()).Right(Stretch())` centers without a wrapper, and
`Left(Stretch())` alone pushes the element to the right.

Cross-axis stretch resolves against the container's content size once that size is known. While a
container's cross axis is still being derived from its content, a stretching child contributes its
natural size, so an empty spacer cannot inflate an auto-sized parent.

## Sizing

`Auto` sizes to content, which for a text element means its measured text and for a container means
its children.

`AspectRatio` is **width divided by height** and derives one auto axis from a definite one. It does
not override two explicit axes, and it is absolute rather than relative to the container's
direction.

`ContentSizer` supplies intrinsic size for custom content:

```csharp
.ContentSizer(callback)             // runs every frame, the safe default
.ContentSizer(callback, revision)   // cached, re-runs when the revision changes
.ContentSizer(width, height)        // fixed, cached automatically
```

Callbacks must return finite nonnegative sizes and must not touch layout while measuring.

## Positioning

`PositionType.ParentDirected` is the default and participates in flow, where `Left`/`Top`/`Right`/
`Bottom` act as margins.

`PositionType.SelfDirected` takes the element out of flow and positions it with the anchor
properties, measured from the parent's content box. `Auto` means unanchored, and anchoring both
edges of an axis stretches the element across it:

```csharp
paper.Box("footer")
    .PositionType(PositionType.SelfDirected)
    .AnchorLeft(12).AnchorRight(12).AnchorBottom(8)
    .Height(32);
```

## Invalidation

Paper tracks text, fonts, dimensions, hierarchy and content revisions itself, and re-layouts on
resolution and DPI changes without being told. Registering a fallback font invalidates too, since
it changes which glyphs resolve.

For content Paper cannot see, such as a cached `ContentSizer` whose data changed or a `FontFile`
mutated in place, invalidate explicitly:

```csharp
paper.MarkLayoutDirty();          // the current element, inside its Enter scope
paper.MarkLayoutDirty(id);        // by persistent element id
paper.MarkSubtreeLayoutDirty();   // the current element and its descendants
paper.MarkAllLayoutDirty();       // everything, e.g. after a global font change
```

The parameterless forms read the entered element, the same convention `Paper.Draw` and the element
storage helpers use. Entering an element establishes that context; it does not stop it being a leaf.

`paper.LayoutStatistics` reports how many nodes were measured, arranged and reused last frame,
which is the quickest way to confirm caching is working.
