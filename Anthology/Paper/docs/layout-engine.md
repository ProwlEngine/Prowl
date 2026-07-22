# Layout Engine

Paper's layout engine is a constraint-based, flexbox-like solver in the style of
[Morphorm](https://github.com/vizia/morphorm), generalized so "row" and "column"
are just "main axis" and "cross axis" relative to each container's own
`LayoutType` — the same recursive algorithm handles both directions.

## `UnitValue`

Every size/position property (`Width`, `Height`, `Left`, `Right`, `Top`, `Bottom`,
`Padding*`, `Child*`, `RowBetween`/`ColBetween`, `Min*`/`Max*`) is a `UnitValue`.
It's a struct with four float components that compose arithmetically:

```
resolved = Px + Pct% * parentValue + Grow * (share of leftover space) + AutoFactor * contentSize
```

Factory helpers (also exposed on `Paper` and `ElementBuilder`):

```csharp
UnitValue.Pixels(float value)                      // Paper.Pixels(value)
UnitValue.Percentage(float value, float pxOffset=0) // Paper.Percent(value, pixelOffset)
UnitValue.Stretch(float factor = 1f)                // Paper.Stretch(factor)
UnitValue.Auto                                      // Paper.Auto
UnitValue.StretchOne                                // Stretch(1) — used as the default for parameterless Child*/RowBetween/ColBetween
UnitValue.ZeroPixels
```

Implicit conversions exist from `int`/`float` to `UnitValue` (as pixels), and
`+`/`-`/`*`//` operators combine values component-wise — e.g. `Stretch(1) + Pixels(10)`
or `Percent(50) - Pixels(8)` both work as you'd expect. `UnitValue.Lerp` (used by
property transitions) interpolates all four components independently.

Predicates: `IsFixed` (no grow/auto — a plain px/percent value), `IsAuto`,
`IsStretch`, `IsPixels`, `IsPercentage`, `HasGrow`, `HasAuto`.

**The root element's `Width`/`Height` must be pure pixel values** — the solver
throws `"Root element must have fixed width"` otherwise, since there's no parent
to resolve a percentage/stretch/auto against.

## Sizing

```csharp
.Size(UnitValue both)
.Size(UnitValue width, UnitValue height)
.Width(...) .Height(...)
.MinWidth(...) .MaxWidth(...) .MinHeight(...) .MaxHeight(...)
.AspectRatio(float ratio) // width/height
```

- **Fixed** (`Pixels`/`Percentage`): resolves directly against the parent's inner
  (padding-excluded) size.
- **`Stretch(factor)`**: competes with sibling stretch values for whatever main-
  or cross-axis space is left over after fixed/auto siblings are sized, weighted
  by `factor`. Resolved via an iterative "distribute → clamp to min/max → freeze
  over/under-shot items → redistribute among the rest → repeat" pass (the
  standard flexbox conflict-resolution algorithm), run separately for the main
  axis and the cross axis.
- **`Auto`**: sized from content. If the element has an explicit `ContentSizer`
  (`ElementBuilder.ContentSizer(...)`), that's used; otherwise, if the element has
  text, its measured text size is used. A container with `Auto` and children
  sizes to the sum/max of its (already-laid-out) children plus padding.
- **`AspectRatio`**: if exactly one axis is `Auto`, it's derived from the other
  and the ratio. If both are `Auto`, the ratio is applied against whichever axis
  the parent constrains. If either axis stretches, aspect ratio is resolved in a
  pass *after* stretch resolution instead.

Because container auto-sizing depends on children, and children's stretch sizing
depends on the container's resolved size, a single element can be laid out
(`DoLayout` invoked on it) multiple times within one frame as the solver works
through these passes. Anything you hook into layout — a `ContentSizer` callback,
or reading `LayoutWidth`/`LayoutHeight` from a callback mid-frame — should expect
to be invoked more than once with different candidate constraints before the
final size settles.

## Position

```csharp
.PositionType(PositionType.ParentDirected)  // default: positioned by the parent's layout flow
.PositionType(PositionType.SelfDirected)    // "absolute": positioned by its own Left/Top, independent of siblings
.Left(...) .Right(...) .Top(...) .Bottom(...)
.Position(UnitValue left, UnitValue top)
```

`SelfDirected` elements are laid out in a separate pass after all
`ParentDirected` children of the same parent are placed — they use the same
margin/size/stretch machinery, but their position is a literal offset from the
parent's padding edge rather than something folded into the main-axis flow.

## Margin, padding, and the alignment recipes

`Margin`/`Left`/`Right`/`Top`/`Bottom` are the element's own outer spacing;
`Padding*` is unconditional inner inset on the parent's content area (children
are always positioned after it, and an `Auto`-sized parent includes it in its
own size).

The interesting part is what happens when a margin is left at its default,
`UnitValue.Auto` ("no preference"):

- If it's the child's **leading** margin and the child is the **first** child,
  the parent's `ChildLeft`/`ChildTop` fills in instead.
- If it's the **trailing** margin and the child is the **last** child, the
  parent's `ChildRight`/`ChildBottom` fills in.
- If it's a trailing margin on a **middle** child *and* the next child's leading
  margin is *also* `Auto`, the parent's `RowBetween`/`ColBetween` fills the gap
  between them instead.
- Otherwise (an `Auto` margin with no matching rule) it resolves to `0`.

Setting a concrete value on a child's own margin opts that side out of all
parent defaulting. This is how CSS-style `justify-content` is expressed — by
putting `Stretch` values into the slots between/around children so the solver
grows those slots to consume leftover space:

| Effect | Recipe (on the parent, children left at default margins) |
|---|---|
| Pack at start (default) | nothing |
| Pack at end | `.ChildLeft()` (or `.ChildTop()` for a column) |
| Center | `.ChildLeft().ChildRight()` (or top/bottom) |
| Space between | `.ColBetween()` (row layout) / `.RowBetween()` (column layout) |
| Space around | `.ChildLeft().ChildRight().ColBetween()` |

`ChildLeft()`/`ChildRight()`/`ChildTop()`/`ChildBottom()`/`RowBetween()`/`ColBetween()`
called with no argument default to `Stretch(1)` (the alignment use case); pass a
pixel value instead for a fixed default margin/gap that individual children can
still override.

Note: `PaddingLeft`/`Right`/`Top`/`Bottom` always mean "my own left/right/top/
bottom," even when this element's `LayoutType` differs from its parent's (e.g. a
`Row` nested in a `Column`) — the engine explicitly remaps the before/after
mapping so padding direction never flips on you based on nesting.

## Flex-wrap

```csharp
.WrapContent(bool wrap = true)
.WrapJustify(WrapJustify justify) // implies WrapContent(true)
```

`WrapJustify`: `Start`, `Center`, `End`, `SpaceBetween`, `SpaceAround`, `Fill`.

When enabled, parent-directed children are greedily packed into lines along the
main axis (always at least one child per line, even if it overflows); an
`Auto`-sized cross dimension on the container grows to fit every line. Each line
is then positioned per `WrapJustify`, and with `Fill` each line's items are
re-laid-out to equally share that line's leftover main-axis space. Use the
container's `RowBetween`/`ColBetween` for both the item gap within a line and
the gap between lines.

**Known limitation:** children should use a fixed or `Auto` size on the *cross*
axis inside a wrap container. A child with `Stretch`/`Grow` on the cross axis is
sized against the whole container's cross extent before lines exist, so it spans
the full container height and visually overlaps the lines below it. Paper logs a
one-time warning per element ID when this happens rather than throwing.

## `ElementHandle`

A lightweight readonly struct: an `Owner` (`Paper` instance) plus an `Index` into
that Paper's per-frame element array. `handle.Data` is a `ref ElementData`
property, so mutating `handle.Data.Foo` mutates the backing element in place.
`IsValid` checks `Owner != null && 0 <= Index < ElementCount`; there's also an
implicit `operator bool` so `if (handle)` works as a validity check.
`GetParentHandle()` returns the parent (or `default` for the root/invalid
handles). Handles are only valid within the frame they were obtained — the
element array is rebuilt every `BeginFrame`.
