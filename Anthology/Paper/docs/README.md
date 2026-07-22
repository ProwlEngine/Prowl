# Paper Documentation

Paper is an immediate-mode UI library: every frame you rebuild the UI tree from
scratch by calling fluent builder methods (`Paper.Box(...)`, `.BackgroundColor(...)`,
`.OnClick(...)`, etc.). Paper diffs nothing — it recomputes layout and style every
frame and throws the tree away at the end of it. Elements that persist state across
frames (a text field's cursor position, a scroll offset) do so through explicit
per-element storage, not through retained objects.

This directory groups the API by core concept rather than by source file. Start
with **Getting Started**, then read whichever concept pages match what you're
building.

## Pages

1. [Getting Started](getting-started.md) — initialization, the frame lifecycle
   (`BeginFrame`/`EndFrame`), hooking up a renderer backend, DPI scaling.
2. [Elements and Hierarchy](elements-and-hierarchy.md) — `Box`/`Row`/`Column`,
   building the tree with `Enter()`/`using`, element identity, `MoveToRoot`,
   layers, visibility, focusability, and other per-element behavior flags.
3. [Layout Engine](layout-engine.md) — `UnitValue` (pixels/percent/stretch/auto),
   sizing, margins, padding, the alignment recipes, flex-wrap, and how the
   constraint solver actually works.
4. [Styling and Animation](styling-and-animation.md) — the `GuiProp` style
   system, state-driven styles (`Hovered`/`Active`/`Focused`/`If`), named/reusable
   styles, style inheritance, property transitions, the imperative animation
   primitives, and easing functions.
5. [Rendering and Custom Drawing](rendering-and-drawing.md) — the per-element
   render pipeline (shadow → backdrop blur → background → border → text →
   children), gradients, box shadows, images, custom shaders, and drawing raw
   vector graphics onto an element with `Draw`/`DrawForeground`.
6. [Events and Input](events-and-input.md) — feeding host input into Paper,
   hover/active/focus state queries, event bubbling, `HookToParent`, cursors,
   and every event type (`ClickEvent`, `DragEvent`, `ScrollEvent`, `FocusEvent`,
   `KeyEvent`, `TextInputEvent`).
7. [Text and Input Fields](text-and-input-fields.md) — plain text, Markdown,
   tagged rich text (with built-in text animations), and the `TextField`/
   `TextArea` controls.
8. [State and Storage](state-and-storage.md) — per-element persistent storage
   (`GetElementStorage`/`SetElementStorage`), ID scoping (`PushID`/`PopID`), and
   the patterns used to build stateful custom widgets (worked example: a
   scrollable view built entirely from primitives).
9. [DevTools](devtools.md) — the built-in console/inspector/profiler overlay.

## Not covered here

`Paper/Paper/OldPaper/` and `Paper/Paper/Extras/WindowManager.cs` are dead code —
every line in both is commented out. They are not part of the current public API
and are omitted from these docs.
