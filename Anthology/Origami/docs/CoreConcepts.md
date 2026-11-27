# Core Concepts

Origami's widgets (buttons, fields, tables, docking, and the rest under `Widgets/`) all sit on
top of a small set of shared systems: theming, metrics, icons, drag-and-drop, and the field
drawer registry used by the property grid. This doc covers those foundations - read it before
building a new widget or embedding Origami in a host application.

## Theming

`OrigamiTheme` is the complete visual config for a widget tree: a set of 7-stop color ramps
(`Neutral`, `Primary`, `Blue`, `Red`, `Green`, `Amber`, plus the `Ink` foreground ramp), semantic
surface colors (`Glass`, `Popover`, `BorderSoft`, `BorderStrong`, `Shadow`), an `OrigamiMetrics`
block, an `OrigamiIcons` set, and font faces (`Font`, `FontMedium`, `FontSemiBold`, `FontBold`,
`FontMono`).

Widgets never read a ramp directly - they resolve an `OrigamiVariant` (`Default`, `Primary`,
`Success`, `Warning`, `Danger`, `Info`, `Subtle`) to a ramp via `theme.Get(variant)`. `Default`
and `Subtle` both map to `Neutral`; a widget distinguishes them by treating `Subtle`'s low ramp
stops as transparent rather than by picking a different ramp.

`Origami` is the static entry point. It holds the active theme (`Origami.Current`) and every
widget factory method (`Origami.Button(...)`, `Origami.Slider(...)`, etc.) reads `Current` when
constructing its builder.

```csharp
Origami.SetTheme(myHostTheme);

using (Origami.PushTheme(gameTheme))
{
    Origami.Button(paper, "play", "Play").Primary().Show();
}
```

Call `SetTheme` once to replace the root theme globally - pass `transitionSeconds > 0` to
smoothly lerp colors and metrics to the new theme instead of snapping. When a transition is
running you must call `Origami.TickTransition(dt)` (or `Origami.BeginFrame`, which calls it for
you) once per frame or the lerp never advances. Font and icon references are not lerpable and
snap to the target immediately.

`PushTheme` scopes a theme to a region without touching the root - handy when a game view is
rendered inside an editor that has its own theme. The stack is per-thread and the returned
`IDisposable` pops on `Dispose()`; always pair it with a `using` block. If you forget to pop, the
override leaks into everything drawn afterward on that thread.

If no host ever calls `SetTheme`, Origami falls back to `OrigamiTheme.CreateDefaults()` - the
built-in "Nebula" palette (frosted magenta-violet glass over a dark void).

## Metrics & Variants

`OrigamiMetrics` (`theme.Metrics`) holds every sizing constant widgets share: corner radii
(`Rounding`, `ContainerRounding`, `SmallRounding`), heights (`RowHeight`, `HeaderHeight`,
`CompactHeight`), spacing/padding scales, font sizes, docking metrics (`TabBarHeight`,
`SplitterSize`, ...), and node-graph metrics (`GraphPortRadius`, `GraphGridSpacing`, ...). Widgets
pull from this instead of hardcoding pixel values so a host can retheme density globally by
swapping one `OrigamiMetrics` instance.

`OrigamiVariant` is the semantic style enum widgets accept (most builders expose `.Variant(...)`
plus shorthands like `.Primary()`, `.Danger()`, `.Success()`). It carries meaning, not a specific
color - the same `Danger` variant renders differently per theme because it's resolved through
`theme.Get(variant)` at draw time.

`OrigamiRamp` is the 7-stop color container (`C100` darkest through `C700` lightest) used for
every ramp on the theme. `OrigamiRamp.LerpColor` and `OrigamiRamp.Lerp` are the primitives theme
transitions are built from; you'll reach for `LerpColor` if you write a custom widget that needs
to interpolate its own color during a hover/press animation.

## Icons

Widget chrome (chevrons, close buttons, checkboxes, sort carets, ...) is drawn through
`IOrigamiIcon`, not hardcoded glyphs, so a host can swap the whole icon set for their own vector
or icon-font set. `OrigamiIcons` (`theme.Icons`) holds one named slot per chrome icon (e.g.
`ChevronDown`, `Close`, `CheckboxOn`) and defaults to Origami's built-in `OrigamiIconSet` (SVG
path data in a 16x16 viewBox). Set any slot to `null` to have that widget draw no icon at all.

Three `IOrigamiIcon` implementations ship out of the box:

- `SvgIcon` - strokes or fills SVG path data (`M`/`L`/`H`/`V`/`C`/`A`/`Z`) in a 16x16 viewBox.
- `FontIcon` - draws a single glyph from a `FontFile` (e.g. a Font Awesome face).
- `IconAction` - a raw `Action<Canvas, Rect, Color, float>` callback, the escape hatch for
  anything else.

```csharp
var theme = Origami.Current;
theme.Icons.Trash = new FontIcon(myIconFont, "");
```

Buttons and similar widgets separate two icon inputs: a plain glyph string (drawn as text in the
current font, e.g. `.LeadingIcon("+")`) versus an `IOrigamiIcon` object (`.LeadingIcon(icon)`),
which is a full vector/font-backed icon tinted to the label color. Pick the glyph overload only
when you actually have a font glyph to render as text; otherwise use the `IOrigamiIcon` overload.

## Drag & Drop

`DragDrop` is a single global drag operation - one drag at a time, process-wide. Define a payload
by subclassing `DragPayload` (override `DisplayName` for the ghost label, optionally `Icon`), then:

```csharp
public sealed class NodePayload : DragPayload
{
    public string NodeId;
    public override string DisplayName => NodeId;
}

// on the drag source:
if (isPressedAndMoved)
    DragDrop.StartDrag(new NodePayload { NodeId = id });

// on a drop target:
var dropped = DragDrop.AcceptDrop<NodePayload>(isHovered);
if (dropped != null) MoveNode(dropped.NodeId);
```

`AcceptDrop<T>` only returns non-null on the exact frame the mouse is released while hovering a
valid target (`IsDropFrame`) - the payload is consumed (set back to null) the moment a target
accepts it. The payload deliberately survives one extra frame after release so drop targets can
check for it in the same frame order they check hover state; `DragDrop.Update` clears it on the
frame after that if nobody claimed it. Press Escape mid-drag to cancel outright.

`Origami.BeginFrame`/`EndFrame` already call `DragDrop.Update` and `DragDrop.DrawVisual` for you -
you only need to call `StartDrag`/`AcceptDrop` from widget code, never the per-frame plumbing.

## Field Drawers

The property grid (`Widgets/PropertyGrid.cs`) renders arbitrary object fields by dispatching on
`Type` to a `FieldDrawer`. `FieldDrawerRegistry.Register<T>(drawer)` maps a type to a drawer
instance; lookup falls back through base types and then interfaces if no exact match exists.

`BuiltInFieldDrawers.Register(registry)` wires up drawers for every primitive
(`bool`, `int`, `float`, `string`, ...), the vector types (`Float2/3/4`, `Double2/3/4`,
`Int2/3/4`), and `Prowl.Vector.Color` - each one is a thin one-liner that calls the matching
Origami widget factory (e.g. `IntDrawer` just calls `Origami.NumericField<int>(...)`). Register
your own `FieldDrawer` for a custom type to make the property grid render it automatically:

```csharp
public class MyAssetDrawer : FieldDrawer
{
    public override void Draw(Paper paper, string id, object? value, Type fieldType,
        Action<object?> onChange, int depth)
        => Origami.TextField(paper, id, ((MyAsset?)value)?.Name ?? "", _ => { }).Show();
}

registry.Register<MyAsset>(new MyAssetDrawer());
```

## Math Expressions

`MathParser` (internal) is a small arithmetic evaluator - `+ - * / ^ ( )` plus the constants
`pi`, `e`, `tau` - that `NumericField` uses to let users type expressions like `2*3+1` or
`360/16` directly into a number field instead of a plain literal. It's not part of the public
API; it's mentioned here only so it's clear where that behavior comes from when you see a
numeric field accept `"180/2"` as a value.

## Frame Lifecycle

Two calls bracket every frame that uses Origami:

- `Origami.BeginFrame(paper, deltaSeconds)` - call before drawing any widgets. Advances an
  in-progress theme transition (see Theming above).
- `Origami.EndFrame(paper)` - call after all user widgets for the frame are drawn. Renders the
  overlay systems that must paint on top of everything else: the drag-drop ghost, context menus,
  modals, toasts, and tooltips.

Skipping `EndFrame` means modals, toasts, and tooltips triggered that frame never actually
render, since those systems queue their content during the frame and only draw it here.

Two other global switches live on `Origami` and are worth knowing about: `Origami.IsReadOnly`
(set via `BeginReadOnly()`/`EndReadOnly()`, nestable) forces every widget drawn inside the scope
into a disabled state, and `Origami.DropShadowsEnabled` / `Origami.GlowsEnabled` gate the two
box-shadow "intents" (`OrigamiShadow.DropShadow` / `OrigamiShadow.Glow`) so a host can turn off
either effect class globally (e.g. for a low-power or reduced-motion preference) without every
widget needing its own toggle.
