---
name: prowl-paper
description: Paper, the immediate mode UI library Prowl uses for OnGui game UI and the editor. Use when writing any non trivial Paper code - layouts, sizing, styling, hover and press states, transitions and animation, events and focus, text fields, scrolling, popups and layers, custom canvas drawing - or when Paper UI lays out, clicks or animates unexpectedly.
---

# Paper UI

Paper is immediate mode with a flexbox style layout engine. Read the prowl-ui skill first for where Paper fits in a Prowl game. This skill is the library itself.

```csharp
using Prowl.PaperUI;               // Paper, GuiProp, Layer, PaperCursor, PaperKey, LayoutType, PositionType
using Prowl.PaperUI.Events;        // ClickEvent, DragEvent, ScrollEvent, KeyEvent
using Prowl.PaperUI.LayoutEngine;  // UnitValue, ElementHandle
using Prowl.Scribe;                // FontFile, TextWrapMode
using Prowl.Vector;                // Color, Rect, Float2, Easing
using Prowl.Quill;                 // Canvas, in paper.Draw
using TextAlignment = Prowl.PaperUI.TextAlignment;   // Prowl.Scribe has one too
```

## The frame model

- Each frame Prowl calls `BeginFrame`, then every component's `OnGui(Paper paper)`, then `EndFrame`. EndFrame does styles and transitions, layout, events, then rendering.
- **The element tree is rebuilt from nothing every frame.** An element not declared this frame does not exist, and its stored state, transitions and text field contents are deleted.
- **Every event callback runs at the end of the frame**, after your whole `OnGui` returned and after layout. Callbacks only change state. The next frame's `OnGui` reads that state. Copy loop variables before capturing them (`int index = i;`).
- Keep UI state (open flags, selection, scroll offsets, drag state) in your component's fields.

## Ids

- `paper.Box(string id, int intId = 0)`, `Row(...)`, `Column(...)`, `Grid(...)`, `Overlay(...)` return a fluent builder.
- The real id hashes the parent's id, the `PushID` scope, the string, the int and **the caller's line number**. The same id twice in one frame throws "Element already exists with this ID".
- In a loop, pass the index: `paper.Box("row", i)`.
- A helper method that calls `paper.Box("button")` collides when called twice under one parent, since the line is the same. Give it an `int id` parameter or vary the string.
- **Every component's `OnGui` shares one Paper at the root**, so two instances of the same component collide. Wrap the body in `paper.PushID(InstanceID)` and `paper.PopID()`.
- Hover, focus, storage and transitions follow the id across frames. An id built from a changing value loses all of them.

## Hierarchy

`using (builder.Enter()) { ... }` makes the element the parent of everything declared inside. `paper.CurrentParent` is the current parent's handle.

## Layout

Defaults: `Width` and `Height` are `Stretch`, the layout is a **Column**, margins and padding are 0, children align to the start.

Units (`UnitValue`):

- A plain number is pixels: `.Width(200)`.
- `UnitValue.Stretch(weight)` takes a share of the leftover space. `UnitValue.Percentage(p)` is a percent of the parent's inner size. `UnitValue.Auto` fits the content.
- They combine: `UnitValue.Percentage(50) - UnitValue.Pixels(8)`.

How sizing works:

- Along the layout direction, every child first gets its pixel, percent and content size. The leftover space is split between stretch weights, clamped to min and max. With too little space, children overflow. Nothing clips unless `.Clip()`.
- Across the layout direction, a stretch child fills the parent. Others keep their size and follow `AlignItems` or `AlignSelf`.
- **`AlignItems(LayoutAlignment.Center)` and `JustifyContent(LayoutJustification.Center)` do nothing to stretch children**, because they already fill the space. Give children a pixel or Auto size first.
- **Inside an Auto sized container, a pure stretch child counts as 0 and collapses.** In a `Height(UnitValue.Auto)` column, give every child a pixel height or `Height(UnitValue.Auto)`.
- Sizes include padding. Padding shrinks the area for text and children.
- `MinWidth`, `MaxWidth`, `MinHeight`, `MaxHeight` and `Padding` accept only pixels and percent. Stretch or Auto there throws during EndFrame.
- `AspectRatio(w / h)` derives one Auto axis from the other.

Spacing and argument order (not CSS order):

- `Padding(left, right, top, bottom)`, `Padding(all)`, `Padding(horizontal, vertical)`. `Margin` takes the same orders.
- `Left`, `Right`, `Top`, `Bottom` set **margins**, not positions. `Position(left, top)` sets the left and top margins.
- A stretch margin is a flexible spacer: `.Margin(UnitValue.Stretch())` centres a fixed size element.
- `Gap(px)` is between children. `LineGap(px)` is between wrapped lines and grid rows.

Containers:

- `LayoutType(LayoutType.Row)`, or use `paper.Row`. `ReverseLayout()`. `WrapContent()` wraps onto new lines.
- `JustifyContent(Start|Center|End|SpaceBetween|SpaceAround|SpaceEvenly)` and `AlignItems(Start|Center|End|Stretch)`.
- Grid: `paper.Grid("g").Columns(3)`. Rows take the tallest child, so grid children need a pixel or Auto height.
- Overlay: `paper.Overlay("o")` stacks children on the same box.
- `Visible(false)` keeps the element (and its state) but it takes no space, does not draw and is not hit.

Free placement:

- `PositionType(PositionType.SelfDirected)` takes an element out of the flow and places it in its parent's content box (the screen for top level elements).
- `AnchorLeft/Right/Top/Bottom(px)` are pixel offsets from that box. Anchoring both edges of an axis sets the size on that axis.
- Anchors take pixels, percentages of that box, or both: `AnchorLeft(UnitValue.Percentage(50, -100))` is 50 percent minus 100 pixels. Stretch or Auto anchors throw. Stretch margins also centre a fixed size element.
- `ClampToScreen()` keeps a popup on screen.

## Styling

- Colors are `Prowl.Vector.Color` with floats 0 to 1. **Integer arguments pick the byte overload**, so `new Color(1, 0, 0, 1)` is nearly black. Write `1f`.
- `BackgroundColor`, `BorderColor`, `BorderWidth`, `Rounded(r)` or `Rounded(tl, tr, br, bl)`, `BoxShadow(x, y, blur, spread, color)`, `BackdropBlur(radius)`, `Opacity(0..1)` (for the whole subtree).
- Gradients replace the background color: `BackgroundLinearGradient(x1, y1, x2, y2, c1, c2)` with coordinates as fractions 0 to 1 of the element, also radial and box gradients.
- `BackgroundImage(texture)` and `Image(texture, tint, rotation, pivot, ImageScaleMode.Fit)` take a Prowl `Texture2D`. The rotation is in degrees.
- Text: `.Text(string, FontFile)`, `FontSize` (default 16), `TextColor` (default white), `Alignment(TextAlignment.MiddleCenter)`, `Wrap(TextWrapMode.Wrap)` (default no wrap), `TextTruncate()`, `LineHeight`, `LetterSpacing`. Plain `Left`, `Center` and `Right` alignments are top aligned, use the `Middle` ones to centre vertically. Text is not clipped unless `.Clip()`.
- Rich text: `.Text(text, font).RichText()` turns on tags like `<b>bold</>`, `<i>`, `<#f80>colour</>`, `<size 1.5>`, `<wave>`. Pass bold, italic and mono fonts to `RichText` for those styles.
- Transforms are visual only and do not move layout: `Translate(x, y)`, `Scale(s)`, `Rotate(degrees)`, `TransformOrigin(0.5f, 0.5f)`.
- `Cursor(PaperCursor.Pointer)` sets the mouse cursor while over the element.

State blocks style an element only while a state is on:

```csharp
.BackgroundColor(idle)
.Hovered.BackgroundColor(hover).End()
.Active.BackgroundColor(pressed).End()
.Focused.BorderColor(accent).End()
.If(selected).BackgroundColor(accent).End()
```

- **Always close a block with `.End()`**, otherwise every later setter is part of the block.
- Hovered is on for every ancestor of the hovered element too. States come from the previous frame.
- Other blocks: `ParentFocused`, `Breakpoint(name)`, `MinViewportWidth(px)`, `Portrait`, `Landscape`.

Reusable styles: `static readonly StyleTemplate Card = new StyleTemplate().BackgroundColor(...).Rounded(8);` then `.Style(Card)`. Named styles exist (`paper.DefineStyle`, `.Style("name")`) but allocate on every use.

## Animation

Paper runs on real time, so transitions and the helpers below keep animating while `Time.TimeScale` is 0, and a pause menu can fade in.

- `.Transition(GuiProp.BackgroundColor, seconds, Easing.CubicOut)` animates a property toward whatever value is declared this frame. **Declare it every frame.** Easings are in `Prowl.Vector.Easing`. Width, margins and other layout values animate too.
- **Transforms snap back unless the resting value is declared.** Write `.Scale(1f).Active.Scale(0.95f).End()`, not only the Active part.
- Elements snap on the frame they first appear.
- Helpers on `paper` return animated values: `AnimateBool(open, 0.25f, Easing.CubicInOut)` (0 to 1), `AnimateFloat(target, speed)`, `AnimateSpring`, `AnimateColor`, `OneShot(trigger)`, `Pulse(period)`, `Shake(trigger, intensity, decay, frequency)`. They keep state on the current parent, keyed by the calling line. At the top level of OnGui that parent is the root, shared by every component, so **prefer calling them inside the `Enter` scope of your own element that exists every frame**, and pass `id:` when calling one from a loop or helper.

## Events

| Event | Fires |
| --- | --- |
| `OnPress`, `OnRelease`, `OnHeld` | left button down, up, every held frame. The pressed element keeps getting them when the pointer leaves |
| `OnClick` | release over the element that was pressed, if it did not become a drag |
| `OnDoubleClick`, `OnRightClick` | second press within 0.25s, right button press |
| `OnDragStart`, `OnDragging`, `OnDragEnd` | after 5 pixels of movement. `e.Delta` this frame, `e.TotalDelta` |
| `OnScroll` | `e.Delta` |
| `OnHover`, `OnEnter`, `OnLeave` | pointer over, entering, leaving |
| `OnKeyPressed`, `OnTextInput`, `OnFocusChange` | focused element only |
| `OnPostLayout((handle, rect) => ...)` | the final rect, every frame |

- Click events carry `PointerPosition`, `RelativePosition` (from the element's top left), `NormalizedPosition`, `ElementRect` and `Button`.
- **Clicks, drags and scrolls bubble to every ancestor.** A button inside a clickable row fires both. Call `e.StopPropagation()`, or `.StopEventPropagation()` on the element.
- **Every element is hit tested and focusable by default**, even with no handlers. `IsNotInteractable()` lets the pointer through that element, but its children stay clickable, so use it on HUD containers. `IsNotFocusable()` opts out of focus. `Opacity(0)` still takes the pointer.
- Handlers add up: calling `.OnClick` twice adds two handlers.
- Tab order: `.TabIndex(0)` makes an element reachable with Tab.
- `paper.SetFocus(handle)`, `paper.ClearFocus()`. Clicking empty space does not clear focus.
- **Paper never blocks Prowl's game input.** `paper.WantsCapturePointer` (pointer over any interactable element) and `paper.WantsCaptureKeyboard` (a text field is focused) only report it. Store the Paper from OnGui in a field and check them in `Update`.

## Widgets

Paper has no buttons, sliders, toggles, dropdowns, tabs, tooltips or scroll views. Build them from boxes, as in the examples below. It does have:

- `.TextField(value, font, onChange, textColor, placeholder, placeholderColor)` and `.TextArea(...)` on any box. Write the new value back to your field in `onChange`. While focused the field keeps its own text. Font size comes from `.FontSize()` and colour from the arguments, `.TextColor()` does nothing. Enter does not submit, add `.OnKeyPressed` for that. `TextInputSettings` adds `MaxLength`, `ReadOnly`, `MaskChar`, `CharFilter` and more.
- `MarkdownBuilder` (namespace `Prowl.PaperUI.Markdown`) renders markdown. Keep the builder in a field.
- F12 toggles Paper's dev tools in a running game.

## Popups and layers

- Siblings draw in order, so later ones are on top and get hit first.
- `.Layer(Layer.Overlay)` or `.Layer(Layer.Topmost)` draws above everything lower and escapes the clipping of ancestors. Children go with it.
- A dropdown or tooltip is a child of its trigger with `PositionType(PositionType.SelfDirected).Position(0, triggerHeight).Layer(Layer.Topmost).ClampToScreen().StopEventPropagation()`, shown while a field says it is open.

## Scrolling

There is no scroll view. Clip a fixed size container, offset a free placed content column by the scroll amount, read its height in `OnPostLayout`, and change the amount in `OnScroll`. See the inventory example.

## Custom drawing

- `paper.Draw((canvas, rect) => ...)` draws on the current parent, behind its children. `paper.DrawForeground` draws over them. At the top level of OnGui it draws behind all UI, so use it inside an element: `using (paper.Box("gauge").Width(96).Height(96).Enter()) paper.Draw(...)`.
- `rect` is the element's box in screen pixels, origin top left, Y down. The canvas already has the element's transform, opacity and clip.
- Wrap changes to the canvas state in `canvas.SaveState()` and `canvas.RestoreState()`.
- Paths: `BeginPath`, `MoveTo`, `LineTo`, `Arc(cx, cy, r, startRadians, endRadians)`, `BezierCurveTo`, `RoundedRect`, `Circle`, `ClosePath`, then `SetFillColor` and `Fill()` (convex shapes only, use `FillComplex()` for concave) or `SetStrokeColor`, `SetStrokeWidth` and `Stroke()`. Angles are radians.
- Shortcuts that skip the path: `RectFilled`, `RoundedRectFilled(x, y, w, h, r, color)`, `CircleFilled(x, y, r, color)`, `PieFilled`.
- `DrawImage(texture, x, y, w, h)`, `DrawText(text, x, y, color, pixelSize, font)`, `MeasureText`.

## Other

- `paper.Width`, `paper.Height`: the screen size in UI pixels. Everything is in logical pixels, Prowl handles DPI, do not scale by it.
- Responsive: `paper.DefineBreakpoint("narrow", Breakpoint.Width(0, 800))`, then `.Breakpoint("narrow")...End()` or `paper.IsBreakpoint("narrow")`.
- Per element storage: `paper.GetElementStorage<T>(key, default)` and `SetElementStorage` on the current parent, deleted when the element is not declared. Read with the exact type you stored. `GetRootStorage` and `SetRootStorage` persist for the whole session but are shared by everything, so prefer fields.
- Every lambda and interpolated string allocates every frame. Fine for a HUD, keep it in mind for long lists.

## Examples

A menu with styled, animated buttons:

```csharp
using System;
using Prowl.PaperUI;
using Prowl.PaperUI.LayoutEngine;
using Prowl.Runtime;
using Prowl.Runtime.Resources;
using Prowl.Scribe;
using Prowl.Vector;
using TextAlignment = Prowl.PaperUI.TextAlignment;

public sealed class MainMenu : Component
{
    private static readonly Color Idle = new(0.18f, 0.20f, 0.26f, 1f);
    private static readonly Color Hover = new(0.26f, 0.30f, 0.40f, 1f);
    private static readonly Color Pressed = new(0.12f, 0.13f, 0.18f, 1f);
    private static readonly Color Accent = new(1f, 0.72f, 0.35f, 1f);
    private int _plays;

    public override void OnGui(Paper paper)
    {
        FontFile? font = FontAsset.LoadDefault().FontFile;
        if (font == null) return;

        paper.PushID(InstanceID);
        using (paper.Column("menu")
            .PositionType(PositionType.SelfDirected)
            .AnchorLeft(40).AnchorTop(40)
            .Width(220).Height(UnitValue.Auto)
            .Gap(8)
            .IsNotInteractable()
            .Enter())
        {
            MenuButton(paper, font, "Play", () => _plays++, 0);
            MenuButton(paper, font, "Quit", () => Debug.Log("Quit"), 1);
        }
        paper.PopID();
    }

    private static void MenuButton(Paper paper, FontFile font, string label, Action onClick, int id)
    {
        paper.Box("button", id)
            .Height(40).Rounded(8)
            .BackgroundColor(Idle).BorderWidth(1).BorderColor(new Color(1f, 1f, 1f, 0.1f))
            .Scale(1f)
            .Hovered.BackgroundColor(Hover).BorderColor(Accent).End()
            .Active.BackgroundColor(Pressed).Scale(0.96f).End()
            .Transition(GuiProp.BackgroundColor, 0.12f)
            .Transition(GuiProp.BorderColor, 0.12f)
            .Transition(GuiProp.ScaleX, 0.08f, Easing.CubicOut)
            .Transition(GuiProp.ScaleY, 0.08f, Easing.CubicOut)
            .Text(label, font).FontSize(16).TextColor(Color.White)
            .Alignment(TextAlignment.MiddleCenter)
            .Cursor(PaperCursor.Pointer)
            .OnClick(_ => onClick());
    }
}
```

A scrolling, selectable list:

```csharp
using System;
using System.Collections.Generic;
using Prowl.PaperUI;
using Prowl.PaperUI.LayoutEngine;
using Prowl.Runtime;
using Prowl.Runtime.Resources;
using Prowl.Scribe;
using Prowl.Vector;
using TextAlignment = Prowl.PaperUI.TextAlignment;

public sealed class InventoryList : Component
{
    private const float ViewHeight = 300f;
    private readonly List<string> _items = new();
    private float _scroll, _contentHeight;
    private int _selected = -1;

    public override void Start()
    {
        for (int i = 0; i < 40; i++) _items.Add($"Item {i + 1}");
    }

    public override void OnGui(Paper paper)
    {
        FontFile? font = FontAsset.LoadDefault().FontFile;
        if (font == null) return;

        using (paper.Column("inventory")
            .PositionType(PositionType.SelfDirected)
            .AnchorRight(20).AnchorTop(20)
            .Width(260).Height(ViewHeight)
            .BackgroundColor(new Color(0f, 0f, 0f, 0.6f)).Rounded(8)
            .Clip()
            .OnScroll(e =>
            {
                float max = MathF.Max(0f, _contentHeight - ViewHeight);
                _scroll = Math.Clamp(_scroll - e.Delta * 30f, 0f, max);
                e.StopPropagation();
            })
            .Enter())
        {
            using (paper.Column("content")
                .PositionType(PositionType.SelfDirected)
                .Top(-_scroll)
                .Height(UnitValue.Auto)
                .Padding(8).Gap(4)
                .OnPostLayout((handle, rect) => _contentHeight = rect.Size.Y)
                .Enter())
            {
                for (int i = 0; i < _items.Count; i++)
                {
                    int index = i;
                    bool selected = index == _selected;
                    paper.Box("row", i)
                        .Height(28).Rounded(4).Padding(8, 8, 0, 0)
                        .BackgroundColor(selected ? new Color(1f, 0.72f, 0.35f, 1f) : new Color(1f, 1f, 1f, 0.06f))
                        .Hovered.BackgroundColor(new Color(1f, 1f, 1f, 0.14f)).End()
                        .Text(_items[i], font).FontSize(14)
                        .TextColor(selected ? new Color(0.05f, 0.05f, 0.08f, 1f) : Color.White)
                        .Alignment(TextAlignment.MiddleLeft)
                        .TextTruncate()
                        .Cursor(PaperCursor.Pointer)
                        .OnClick(_ => _selected = index);
                }
            }
        }
    }
}
```

If the wheel scrolls the wrong way, flip the sign of `e.Delta`.

A text field that keeps its value and submits on Enter, without the game reacting to the typing:

```csharp
using Prowl.PaperUI;
using Prowl.PaperUI.LayoutEngine;
using Prowl.Runtime;
using Prowl.Runtime.Resources;
using Prowl.Scribe;
using Prowl.Vector;
using TextAlignment = Prowl.PaperUI.TextAlignment;

public sealed class NameEntry : Component
{
    private string _name = "";
    private string _submitted = "";
    private Paper? _paper;

    public override void Update()
    {
        bool typing = _paper != null && _paper.WantsCaptureKeyboard;
        if (!typing && Input.GetKeyDown(KeyCode.I)) Debug.Log("Inventory hotkey");
    }

    public override void OnGui(Paper paper)
    {
        _paper = paper;
        FontFile? font = FontAsset.LoadDefault().FontFile;
        if (font == null) return;

        using (paper.Column("name panel")
            .PositionType(PositionType.SelfDirected)
            .AnchorLeft(40).AnchorBottom(40)
            .Width(320).Height(UnitValue.Auto)
            .BackgroundColor(new Color(0f, 0f, 0f, 0.7f)).Rounded(10).Padding(14).Gap(8)
            .Enter())
        {
            paper.Box("name field")
                .Height(34).Rounded(6).Padding(10, 10, 0, 0)
                .BackgroundColor(new Color(1f, 1f, 1f, 0.08f))
                .BorderWidth(1).BorderColor(new Color(1f, 1f, 1f, 0.15f))
                .Focused.BorderColor(new Color(1f, 0.72f, 0.35f, 1f)).End()
                .FontSize(16)
                .Alignment(TextAlignment.MiddleLeft)
                .TabIndex(0)
                .TextField(_name, font, v => _name = v, Color.White, "Enter your name", new Color(1f, 1f, 1f, 0.4f))
                .OnKeyPressed(e =>
                {
                    if (e.Key == PaperKey.Enter)
                    {
                        _submitted = _name;
                        paper.ClearFocus();
                    }
                });

            paper.Box("echo").Height(UnitValue.Auto)
                .Text(_submitted.Length > 0 ? $"Hello {_submitted}" : "Press Enter to submit", font)
                .FontSize(14).TextColor(new Color(0.7f, 0.75f, 0.85f, 1f))
                .Wrap(TextWrapMode.Wrap);
        }
    }
}
```

A panel that slides in with a progress bar, and a centred crosshair:

```csharp
using Prowl.PaperUI;
using Prowl.PaperUI.LayoutEngine;
using Prowl.Runtime;
using Prowl.Runtime.Resources;
using Prowl.Scribe;
using Prowl.Vector;
using TextAlignment = Prowl.PaperUI.TextAlignment;

public sealed class LoadingPanel : Component
{
    private bool _open = true;
    private float _progress;

    public override void Update()
    {
        if (Input.GetKeyDown(KeyCode.F1)) _open = !_open;
        _progress = (_progress + Time.DeltaTime * 0.1f) % 1f;
    }

    public override void OnGui(Paper paper)
    {
        FontFile? font = FontAsset.LoadDefault().FontFile;
        if (font == null) return;

        using (paper.Box("drawer host")
            .PositionType(PositionType.SelfDirected)
            .AnchorLeft(0).AnchorTop(80)
            .Width(300).Height(UnitValue.Auto)
            .IsNotInteractable()
            .Enter())
        {
            float t = paper.AnimateBool(_open, 0.25f, Easing.CubicInOut);
            float shown = paper.AnimateFloat(_progress, 10f);

            using (paper.Column("drawer")
                .Width(280).Height(UnitValue.Auto)
                .TranslateX(-300f * (1f - t))
                .Opacity(t)
                .BackgroundColor(new Color(0.02f, 0.03f, 0.06f, 0.85f))
                .Rounded(0, 10, 10, 0)
                .Padding(14).Gap(8)
                .IsNotInteractable()
                .Enter())
            {
                paper.Box("label").Height(20)
                    .Text($"Loading {shown * 100f:0}%", font).FontSize(14).TextColor(Color.White)
                    .Alignment(TextAlignment.MiddleLeft);

                using (paper.Box("track").Height(10).Rounded(5).BackgroundColor(new Color(1f, 1f, 1f, 0.1f)).Enter())
                    paper.Box("fill").Width(UnitValue.Percentage(shown * 100f)).Rounded(5)
                        .BackgroundColor(new Color(1f, 0.72f, 0.35f, 1f));
            }
        }

        using (paper.Box("crosshair")
            .PositionType(PositionType.SelfDirected)
            .Width(8).Height(8)
            .Margin(UnitValue.Stretch())
            .IsNotInteractable()
            .Enter())
            paper.Draw((canvas, rect) => canvas.CircleFilled(rect.Min.X + 4f, rect.Min.Y + 4f, 3f, Color.White));
    }
}
```
