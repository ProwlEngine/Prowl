# Rendering and Custom Drawing

## Per-element render order

For each visible element, in this order:

1. **Box shadow** (if `BoxShadow.IsVisible`) — drawn as a soft even-odd ring so
   translucent element backgrounds don't reveal the shadow underneath themselves.
2. **Backdrop blur** (if `BackdropBlur > 0`) — blurs whatever was already drawn
   behind the element (frosted-glass effect); requires a renderer backend that
   reports `ICanvasRenderer.SupportsBackdropBlur == true`, otherwise it degrades
   to a flat tinted fill.
3. **Background** — a gradient (if `BackgroundGradient.Type != None`) overrides
   a plain `BackgroundColor`.
4. **Background image** (if `BackgroundImage` is set) — drawn on top of the
   background color/gradient, stretched to the element rect.
5. **Border** (if `BorderWidth > 0` and `BorderColor.A > 0`).
6. **Scissor clip** applied here if `.Clip()` was called, so everything from
   this point on (text, custom draws, children) is clipped to the element rect.
7. **Text** (if any `Paragraph` is set — plain/Markdown/rich text).
8. **Background render commands** (`Paper.Draw(...)`, see below) — before children.
9. **Children**, recursively, in child order (later children draw on top of
   earlier ones), each also respecting its own `Layer`.
10. **Foreground render commands** (`Paper.DrawForeground(...)`) — after all children.

Elements whose whole subtree (bounds grown by border/box-shadow reach) falls
entirely outside the current clip rect are culled — skipped without descending
into children — unless something inside the subtree escapes to a higher
[`Layer`](elements-and-hierarchy.md#layers), in which case culling is skipped for
that branch so the escaping content still renders.

## Custom drawing with `Draw`/`DrawForeground`

```csharp
paper.Draw(Action<Canvas, Rect> renderAction);
paper.Draw(ref ElementHandle handle, Action<Canvas, Rect> renderAction);
paper.DrawForeground(Action<Canvas, Rect> renderAction);
paper.DrawForeground(ref ElementHandle handle, Action<Canvas, Rect> renderAction);
```

The callback receives the live `Prowl.Quill.Canvas` (already transformed into
the element's local space) and the element's final layout `Rect`. Use the
canvas' path/fill/stroke API (`BeginPath`, `MoveTo`, `LineTo`, `Arc`, `Fill`,
`Stroke`, `RoundedRect`, `SetFillColor`, `SetLinearBrush`/`SetRadialBrush`/
`SetBoxBrush`, `DrawImage`, `DrawText`/`DrawLayout`, `SaveState`/`RestoreState`,
`TransformBy`, etc.) to draw arbitrary vector graphics. Custom draw actions are
your own responsibility to `SaveState`/`RestoreState` around if you change canvas
state that shouldn't leak to later draws in the same subtree — Paper does not
wrap each render command for you.

Multiple `Draw`/`DrawForeground` calls on the same element accumulate (each
appends to a list), so you can layer several custom draws.

## Images

```csharp
.Image(object texture, Color32? tint = null, float rotation = 0f, Float2? pivot = null,
       ImageScaleMode scaleMode = ImageScaleMode.Stretch)
```

Draws a texture filling the element's rect (internally implemented as a
background `Draw` call). `ImageScaleMode`:

- `Stretch` — fills the rect, ignoring aspect ratio.
- `Fit` — scales uniformly to fit inside the rect (may letterbox).
- `Fill` — scales uniformly to cover the rect (may crop).

`rotation` is in degrees around `pivot` (normalized 0..1 within the rect,
default center). For a static background image use `.BackgroundImage(texture)`
instead (step 4 in the render order above) — `.Image(...)` is for cases needing
tint/rotation/scale-mode control.

## Custom shaders

```csharp
.CustomShader(object shader, Action<Quill.ShaderUniforms>? setupUniforms = null)
```

Replaces the element's background fill with a backend-specific shader object
(whatever your `ICanvasRenderer` implementation expects). The optional callback
lets you set per-frame uniforms before the shader draws a full-rect fill.

## Textures

Textures are opaque `object`s — created via your renderer's
`ICanvasRenderer.CreateTexture(width, height)` / `SetTextureData(...)`, then
passed straight into `.BackgroundImage(texture)` / `.Image(texture, ...)`. See
[Getting Started](getting-started.md#implementing-a-renderer-backend).

## Text measurement

```csharp
Float2 MeasureText(string text, float pixelSize, FontFile font, float letterSpacing = 0f)
Float2 MeasureText(string text, TextLayoutSettings settings)
TextLayout CreateLayout(string text, TextLayoutSettings settings)
IEnumerable<FontFile> EnumerateSystemFonts()
void AddFallbackFont(FontFile font)
```

Useful inside a `ContentSizer` callback (see [Layout Engine](layout-engine.md))
when an element's auto size should depend on measured text rather than the
built-in `Text`/`Markdown`/`RichText` content sizing.
