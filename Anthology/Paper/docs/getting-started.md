# Getting Started

## Installation

```
dotnet add package Prowl.Paper
```

Paper depends on [Prowl.Quill](https://github.com/ProwlEngine/Prowl.Quill) for
vector-graphics rendering and on `Prowl.Scribe` for text shaping/atlasing. Both
come in transitively.

## Implementing a renderer backend

Paper doesn't talk to a graphics API directly — it draws into a `Prowl.Quill.Canvas`,
and the canvas hands its accumulated vertex/index data to a backend you provide
via `Prowl.Quill.ICanvasRenderer`:

```csharp
public interface ICanvasRenderer : IDisposable
{
    object CreateTexture(uint width, uint height);
    Int2 GetTextureSize(object texture);
    void SetTextureData(object texture, IntRect bounds, byte[] data); // RGBA, 4 bytes/pixel
    void RenderCalls(Canvas canvas, IReadOnlyList<DrawCall> drawCalls);
    bool SupportsBackdropBlur => false; // opt in if your backend can sample the framebuffer
}
```

Textures are opaque `object`s from Paper's point of view — you decide what they
are (a GL texture handle boxed as `object`, a `Texture2D`, etc.) and hand them
back into `.BackgroundImage(texture)` / `.Image(texture, ...)`. The repo ships
working implementations for OpenTK, Raylib, and WebGL/WASM under `Paper/Samples/`
— the OpenTK one is the shortest reference if you're integrating a new backend.

## Creating and running a Paper instance

```csharp
var renderer = new YourCanvasRenderer(...);
var paper = new Paper(renderer, initialWidth, initialHeight, new FontAtlasSettings());

var fontSystem = new FontSystem();
fontSystem.AddFont(File.ReadAllBytes("path/to/font.ttf"));
var myFont = fontSystem.GetFont(24);
```

Each frame:

```csharp
void RenderUI(float deltaTime, float dpiScale)
{
    paper.BeginFrame(deltaTime, dpiScale);

    using (paper.Column("Root").Enter())
    {
        paper.Box("Header").Height(60).Text(Text.Center("Hi", myFont, Color.White));
        // ... rest of the UI ...
    }

    paper.EndFrame();
}
```

`BeginFrame` resets the element hierarchy for the new frame (clears the created-element
set, re-pushes the root). `EndFrame` runs, in order: style resolution, layout,
post-layout callbacks, culling-bounds computation, layered-element collection
(for hit-testing), interaction handling (hover/click/drag/keyboard), rendering
(draining any deferred higher-`Layer` elements in ascending layer order), then
frame cleanup (drops style/storage state for elements that weren't recreated
this frame) and `Canvas.Render()`.

On resize, call `paper.SetResolution(width, height)`.

## Feeding input

Paper doesn't poll any windowing API itself — the host forwards input each frame
(or as events arrive) via a small set of methods on `Paper` (see
[Events and Input](events-and-input.md) for the full list):

```csharp
paper.SetPointerPosition(mouseX, mouseY);
paper.SetPointerState(PaperMouseBtn.Left, mouseX, mouseY, isPointerBtnDown: true, isPointerMove: false);
paper.SetPointerWheel(wheelDeltaY);
paper.SetKeyState(PaperKey.A, isKeyDown: true);
paper.AddInputCharacter(typedText);
```

If you want clipboard support in `TextField`/`TextArea` (Ctrl+C/X/V), implement
`IClipboardHandler` (`GetClipboardText()` / `SetClipboardText(string)`) and call
`paper.SetClipboardHandler(handler)` once at startup — without one, copy/paste
silently no-ops.

## DPI / HiDPi scaling

`Paper.DisplayFramebufferScale` (a `Float2`, default `(1,1)`) is the
physical-pixels-per-logical-pixel ratio. Set it to the host's DPI ratio before
`BeginFrame` (or just pass `dpiScale` into `BeginFrame` each frame, which sets
it for you when `> 0`). Paper uses this to scale vertex output and rasterize
fonts at the right density.

Separately, `paper.ScaleAllSizes(scaleFactor)` multiplies every *default* style
value (default padding, border width, spacing, etc. — the values a fresh element
gets before you set anything) by `scaleFactor`. Call it once at init with the
monitor's DPI ratio if you want your default spacing constants to scale with the
display; it doesn't touch values you explicitly set via the builder.

## DevTools

Set `paper.DevTools.Enabled = true` and press F12 at runtime to open a built-in
console/element-inspector/profiler overlay. See [DevTools](devtools.md).
