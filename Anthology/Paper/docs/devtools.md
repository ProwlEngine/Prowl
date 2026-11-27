# DevTools

Paper ships a built-in, browser-devtools-style overlay — console, live element
tree inspector, frame profiler, render/input/font-atlas stats — implemented
entirely with the same public `ElementBuilder` API you use for your own UI (it's
a working example of a non-trivial Paper app, if you want a reference for
building complex tooling UI).

## Enabling it

```csharp
paper.DevTools.Enabled = true;  // press F12 at runtime to open/close the panel
paper.DevTools.Font = myFont;   // optional; falls back to the first enumerated system font if unset
```

`Enabled = false` (the default) costs nothing per frame — the F12 check and all
instrumentation hooks short-circuit immediately. Even with `Enabled = true`,
per-phase and per-element timing (`Timing`/`DeepProfiling`) only activate while
the panel is actually open, so leaving `Enabled = true` in a shipped build is
safe.

## Logging

```csharp
paper.Log(string message, PaperDevTools.LogLevel level = PaperDevTools.LogLevel.Info);
// equivalently: paper.DevTools.Log(message, level);

public enum LogLevel { Info, Warning, Error }
```

Appends to the Console tab's ring buffer (capped at 500 entries; consecutive
identical messages are coalesced with a repeat counter instead of duplicating).

## Panels

Once opened (F12), the overlay has six tabs:

- **Console** — the `Log(...)` output.
- **Elements** — a live tree inspector: walks the element hierarchy each frame
  capturing id/parent/depth/subtree size/position/size/layer/tab-index/
  visibility/scissor/layout & position type/text/handlers. Includes a "Pick"
  mode — hover to highlight the app element under the cursor, click to select
  it — with a full-screen highlight overlay.
- **Profiler** — per-phase `EndFrame` timings (Styles/Layout/PostLayout/
  Culling/Layered/Interaction/Render/Upload), a frame-time history, and (when
  "Record" is toggled on in the panel) a per-element render/layout timing
  breakdown.
- **Render** — draw-call/vertex/triangle counts for the frame.
- **Input** — current input state.
- **Atlas** — the font atlas texture.

Beyond `Enabled`, `Font`, and `Log(...)`, the rest of DevTools' behavior
(deep-profiling toggle, panel navigation, element picking) is driven through the
panel UI itself rather than through code — there's no public API to, say, flip
deep-profiling on from a script.
