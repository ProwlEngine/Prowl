# Elements and Hierarchy

## Creating elements

```csharp
ElementBuilder Box(string stringID, int intID = 0, [CallerLineNumber] int lineID = 0)
ElementBuilder Row(string stringID, int intID = 0, [CallerLineNumber] int lineID = 0)    // Box(...).LayoutType(LayoutType.Row)
ElementBuilder Column(string stringID, int intID = 0, [CallerLineNumber] int lineID = 0) // Box(...).LayoutType(LayoutType.Column)
```

`Box` is the one primitive element type — `Row`/`Column` are just `Box` with
`LayoutType` preset. Every call returns an `ElementBuilder`, a fluent API for
configuring that element (style, layout, event handlers, text, behavior flags —
covered in their own doc pages).

### Element identity

An element's identity (used to persist style/animation/storage state across
frames, and to detect accidental duplicate IDs) is:

```
HashCode.Combine(parentElementID, currentIDStackValue, stringID, intID, lineID)
```

`lineID` defaults to the call site's source line number via `[CallerLineNumber]`,
so two `paper.Box("Item")` calls on different lines never collide even with the
same string ID — but the same call site executed twice in the same frame (e.g.
inside a loop) will collide unless you vary `intID` (typically the loop index)
or wrap the loop body in `PushID`/`PopID`. Creating two elements that hash to the
same ID within one frame throws.

```csharp
using (paper.Column("List").Enter())
{
    for (int i = 0; i < items.Count; i++)
        paper.Box("Item", i).Text(items[i].Name, font); // intID disambiguates the loop
}
```

`paper.PushID(string id)` / `PushID(int id)` / `PopID()` push/pop an additional
salt onto the ID stack for the current scope — useful when building a reusable
component function that's called multiple times per frame and you don't want to
thread an explicit `intID` through every child element inside it.

## Building the tree

```csharp
using (paper.Column("MainContainer").BackgroundColor(240, 240, 240).Enter())
{
    paper.Box("Header").Height(60);       // leaf, no children
    using (paper.Row("Content").Enter())  // container with children
    {
        paper.Box("Sidebar").Width(200);
        paper.Box("MainContent");
    }
}
```

`ElementBuilder.Enter()` pushes the element onto Paper's internal parent stack
and returns `this` as an `IDisposable`; disposing (the `using` block ending) pops
it. Elements created without entering (`paper.Box("Sidebar").Width(200);` with no
`using`) are leaves, parented under whatever `CurrentParent` is at that point.
Calling `Enter()` on the element that's already `CurrentParent` throws.

`paper.CurrentParent` (an `ElementHandle`) is the element new children attach to
right now. `paper.FindElementByID(int id)` looks up any element by its computed ID.

### Moving to root

```csharp
public void MoveToRoot()
```

Detaches `CurrentParent` from its current parent and reparents it directly under
the root element. Intended for popups/modals/tooltips that are declared as a
child of whatever's on screen at the time (for convenient scoping/lifetime) but
need to render above everything and not be clipped by an ancestor. Combine with
`.Layer(Layer.Overlay)` (or higher) so it also draws on top.

## Behavior and lifecycle configuration (`ElementBuilder`)

These are the flags/callbacks on `ElementBuilder` that aren't style or layout
properties (those are covered in [Layout Engine](layout-engine.md) and
[Styling and Animation](styling-and-animation.md)):

| Method | Effect |
|---|---|
| `Visible(bool)` | Show/hide the element (and skip its subtree in render/hit-test). |
| `IsNotInteractable()` | Element ignores pointer input; hit-testing still finds its children. |
| `IsNotFocusable()` | Excludes the element from tab focus / `SetFocus`. |
| `TabIndex(int)` | Keyboard tab-navigation order; `-1` (default) excludes it from Tab entirely. |
| `HookToParent()` | Fans out the parent's hover/active/focus/drag *state and events* to this child too — for compound widgets where a wrapper row is what's actually interactive but a label/icon inside it should read the same state. See [Events and Input](events-and-input.md). |
| `StopEventPropagation()` | Prevents click/drag/scroll events from bubbling past this element to its ancestors. |
| `Layer(int)` | Rendering/hit-test tier — see below. |
| `Clip()` | Clips children's rendering (and hit-testing) to this element's bounds. |
| `ClampToScreen()` | After layout, clamps the element's position to stay fully on-screen (with a small margin); children move with it. |
| `Cursor(PaperCursor)` / `CursorDragging(PaperCursor)` | Requested OS cursor shape while hovered / while pressed-dragging. |
| `ContentSizer(Func<float?,float?,(float,float)?>)` / `ContentSizer(w,h)` / `ClearContentSizer()` | Custom size-from-content function for `Auto`-sized elements (see [Layout Engine](layout-engine.md)). |
| `OnPostLayout(Action<ElementHandle, Rect>)` | Callback once this element's final layout rect is known, before rendering. |

### Layers

```csharp
public static class Layer { public const int Base = 0; public const int Overlay = 100; public const int Topmost = 200; }
```

Layers are plain `int`s (named constants spaced 100 apart so you can wedge a
custom tier between them, e.g. `Layer.Overlay + 10`). An element whose `Layer`
is higher than its parent's current rendering layer is *deferred*: it's drawn
after the whole base tree, layers processed in ascending order, so higher layers
always end up on top — and non-`Base` layers are hit-tested independently and
take priority over the base tree (highest layer first), so an overlay always
receives input before whatever's visually underneath it.

## Custom drawing on an element

```csharp
paper.Draw(Action<Canvas, Rect> renderAction);              // on CurrentParent, draws before children
paper.Draw(ref ElementHandle handle, Action<Canvas, Rect>);  // on a specific handle
paper.DrawForeground(Action<Canvas, Rect> renderAction);     // draws after all children
paper.DrawForeground(ref ElementHandle handle, Action<Canvas, Rect>);
```

See [Rendering and Custom Drawing](rendering-and-drawing.md) for what you can do
with the `Canvas` inside the callback, and how these compose with `.Image(...)`
and `.CustomShader(...)`.
