# State and Storage

Immediate mode throws away the element tree every frame, so anything that needs
to persist — a scroll offset, a text cursor, an animation's running value — has
to live somewhere other than a local variable in your UI-building code. Paper
gives you per-element key/value storage for exactly this.

## Per-element storage

```csharp
T GetElementStorage<T>(string key, T defaultValue = default)             // on CurrentParent
T GetElementStorage<T>(ElementHandle el, string key, T defaultValue = default)
void SetElementStorage<T>(string key, T value)                            // on CurrentParent
void SetElementStorage<T>(ElementHandle el, string key, T value)
bool HasElementStorage(ElementHandle el, string key)

T GetRootStorage<T>(string key)       // global storage on the root element — persists across all elements/frames
void SetRootStorage<T>(string key, T value)
```

Storage is keyed by the element's stable ID (see
[element identity](elements-and-hierarchy.md#element-identity)), so it survives
across frames as long as you keep recreating an element with the same ID.
**When an element isn't recreated in a given frame, its storage is automatically
dropped at end of frame** — you never have to manually clean up state for
elements that disappeared from the tree (a closed panel, a filtered-out list
item, etc.).

This is exactly the mechanism `TextField`/`TextArea` use for cursor/selection/
scroll state (see [Text and Input Fields](text-and-input-fields.md)) and that
the [animation primitives](styling-and-animation.md#imperative-animation-primitives-paperanimationcs)
use to persist their running values between frames.

## ID scoping

```csharp
void PushID(string id)
void PushID(int id)
void PopID()
```

Pushes/pops an extra salt onto the ID stack used when computing element
identity. Useful inside a reusable component function that's called multiple
times per frame, so its internal elements don't collide across calls without
having to thread an explicit `intID` through every child element by hand.

## Worked example: a scroll view built from primitives

Paper has no built-in scrolling widget — `Samples/Shared/ScrollView.cs` builds
one entirely from public primitives, and is a good template for any custom
stateful widget: a `SelfDirected` content column offset by a stored scroll
position, clipped to the outer view, with an `OnScroll` handler that updates the
stored offset and an `OnPostLayout` callback that measures the actual content
height (needed to clamp the max scroll and size an optional scrollbar thumb).

```csharp
public static IDisposable Begin(Paper paper, string id, float width, float height, ...)
{
    ElementHandle outerHandle = default;

    var outer = paper.Box(id)
        .Size(width, height)
        .Clip()
        .OnScroll(e => {
            float scroll = paper.GetElementStorage(outerHandle, "scrollY", 0f);
            float contentH = paper.GetElementStorage(outerHandle, "contentH", height);
            float maxScroll = MathF.Max(0, contentH - height);
            scroll = MathF.Max(0, MathF.Min(maxScroll, scroll - e.Delta * ScrollSpeed));
            paper.SetElementStorage(outerHandle, "scrollY", scroll);
        });

    var outerDisposable = outer.Enter();
    outerHandle = paper.CurrentParent;
    float scrollY = paper.GetElementStorage(outerHandle, "scrollY", 0f);

    var content = paper.Column($"{id}_content")
        .PositionType(PositionType.SelfDirected)
        .Position(0, -scrollY)
        .Width(width - ScrollBarWidth)
        .Height(UnitValue.Auto);

    content.OnPostLayout((h, rect) => {
        paper.SetElementStorage(outerHandle, "contentH", (float)rect.Size.Y);
    });

    var contentDisposable = content.Enter();
    return new ScrollViewScope(paper, id, outerHandle, outerDisposable, contentDisposable, width, height, scrollY);
}
```

Usage: `using (ScrollView.Begin(paper, "myScroll", width, height)) { /* content */ }`
— the returned `IDisposable`'s `Dispose()` closes the content scope and, if the
measured content overflowed, draws a track + draggable thumb scrollbar
positioned from the same stored `scrollY`/`contentH` values.

The pattern generalizes to any custom widget: store whatever state you need
under stable keys on the widget's own element, read it back at the top of the
next frame's build, and let Paper's per-frame cleanup handle disposal when the
widget stops being created.
