# 3D Gizmos

The `Prowl.OrigamiUI.Gizmo` namespace is a viewport-manipulation gizmo system for 3D editors:
move/rotate/scale handles (`TransformGizmo`) and an orientation view cube (`ViewManipulatorGizmo`).
Unlike the widgets under `Origami/Widgets`, these are not exposed through `Origami.*` static
factories — they are plain classes you construct and drive yourself from your viewport's
input and render loop. Both draw through `GizmoDraw3D`, which projects 3D points into a Quill
`Canvas` using a view-projection matrix.

## TransformGizmo

The move/rotate/scale handle set for a single selected object. You choose which handles exist
via a `[Flags] TransformGizmoMode`, feed it the camera and target transform once per frame, call
`Update` with a picking ray to get drag deltas, and call `Draw` to render it.

![Screenshot: TransformGizmo](images/gizmos/transformgizmo.png)

```csharp
var gizmo = new TransformGizmo(TransformGizmoMode.Universal);

// Once per frame, before picking/drawing:
gizmo.UpdateCamera(viewportRect, viewMatrix, projMatrix, camUp, camForward, camRight, camPosition);
gizmo.SetTransform(selectedObject.Position, selectedObject.Rotation, selectedObject.Scale);

var ray = ComputePickRay(mouseScreenPos, viewMatrix, projMatrix, viewportRect);
gizmo.IsMouseDown = mouseDown;
gizmo.IsMouseUp = mouseUp;

var result = gizmo.Update(ray, mouseScreenPos, blockPicking: isOverOtherUI);
if (result is { } r)
{
    if (r.TranslationDelta is { } t) selectedObject.Position += t;
    if (r.RotationAxis is { } axis && r.RotationDelta is { } angle)
        selectedObject.Rotation = Quaternion.FromAxisAngle(axis, angle) * selectedObject.Rotation;
    if (r.Scale is { } s) selectedObject.Scale = s;
}

gizmo.Draw(canvas);
```

- `TransformGizmoMode` flags: `TranslateX/Y/Z`, `TranslateXY/XZ/YZ` (plane handles), `TranslateView`
  (screen-space plane), `RotateX/Y/Z`, `RotateView`, `ScaleX/Y/Z`, `ScaleUniform`, `Arcball`, plus
  presets `Translate`, `Rotate`, `ScaleAll`, `Universal`.
- `Orientation` (`Global` / `Local`) controls whether axis handles follow the object's rotation.
- `Snapping`, `SnapDistance`, `SnapAngle` enable grid/angle snapping while dragging.
- `IsMouseDown` / `IsMouseUp` / `IsShiftDown` are read each `Update` call — set them from your
  input state before calling it.
- `GizmoResult` reports whichever of translation, rotation, or scale changed this frame — check
  which nullable fields are set rather than assuming a fixed shape per mode.
- `IsOver` is true while a handle is hovered or being dragged; use it to suppress camera-orbit
  input so dragging a handle doesn't also spin the camera.

Notes: `SetMode` rebuilds the internal sub-gizmo list, so prefer constructing one `TransformGizmo`
per selection type and calling `SetMode` when the active handle set changes, rather than
recreating it every frame. `Update` and `Draw` must be called with the same camera/transform state
set via `UpdateCamera`/`SetTransform` earlier that frame.

## ViewManipulatorGizmo

The orientation cube typically drawn in a scene view's top-right corner. Click a face to snap the
camera to that axis; click the surrounding circle to toggle ortho/perspective (the toggle itself
is left to the caller — the gizmo only reports the hover/click).

![Screenshot: ViewManipulatorGizmo](images/gizmos/viewmanipulatorgizmo.png)

```csharp
var viewCube = new ViewManipulatorGizmo();
viewCube.SetRect(new Rect(new Float2(viewportWidth - 90, 10), new Float2(80, 80)));
viewCube.SetCamera(camera.Forward, camera.Up);

bool clicked = viewCube.Update(canvas, mouseScreenPos, mouseClicked, blockPicking: isOverOtherUI,
    out Float3 newForward);
if (clicked)
    camera.SnapTo(newForward);

if (viewCube.IsOver)
    suppressCameraOrbitInput = true;
```

- `Update` both draws the cube and returns whether a face was clicked this call — there is no
  separate draw step.
- The circle click (ortho/perspective toggle) is only hover-detected here; `Update` returns
  `false` for it, so drive the actual toggle from `IsOver` plus your own click check if you need
  a distinct action for the circle versus a face.

## Sub-gizmos (ISubGizmo)

Each individual handle on a `TransformGizmo` (one axis arrow, one plane square, one rotation ring)
is an `ISubGizmo`. `TransformGizmo` owns a flat list of these and dispatches picking/update/draw to
whichever one is hovered or focused; you generally don't touch this interface directly, but it's
the extension point if you need a custom handle type.

```csharp
public interface ISubGizmo
{
    bool Pick(Ray ray, Float2 screenPos, out float t);
    GizmoResult? Update(Ray ray, Float2 screenPos);
    void Draw(Canvas canvas);
    void SetFocused(bool focused);
}
```

- Built-in implementations: `TranslationSubGizmo`, `RotationSubGizmo`, `ScaleSubGizmo` (all in
  `SubGizmos.cs`).
- `Pick` is called first each frame to test hit against the ray; the first hit becomes hovered,
  and a mouse-down promotes it to focused. Once focused, only that sub-gizmo's `Update` runs until
  mouse-up.
- `TranslationSubGizmo` and `ScaleSubGizmo` both work over `TransformKind.Axis` or
  `TransformKind.Plane`; `RotationSubGizmo` is always an arc around one axis (or the view axis).

## GizmoDraw3D

The shared 3D-to-2D drawing helper both gizmo types use internally. It keeps a viewport/MVP stack
so nested draw calls (e.g. per-sub-gizmo local transforms) can push/pop matrices with `using`
scopes, then projects shapes (arcs, quads, arrows, polylines, polygons) into 2D and draws them on
a Quill `Canvas`.

![Screenshot: GizmoDraw3D](images/gizmos/gizmodraw3d.png)

```csharp
var draw3D = new GizmoDraw3D();
draw3D.Begin(canvas, viewportRect, viewProjectionMatrix);

using (draw3D.Matrix(viewProjectionMatrix * localTransform))
{
    draw3D.Circle(radius: 1.0f, new Stroke3D { Color = Color32.White, Thickness = 2f });
    draw3D.LineSegment(Float3.Zero, Float3.UnitX, new Stroke3D { Color = Color32.Red, Thickness = 2f });
}
```

- `Begin(canvas, viewport, mvp)` resets both stacks and is the usual entry point per frame;
  `Viewport(rect)` / `Matrix(mvp)` push scoped overrides via `IDisposable`.
- Drawing methods (`Arc`, `Circle`, `Quad`, `FilledCircle`, `LineSegment`, `Arrow`, `Polygon`,
  `Polyline`, `Sector`) all operate in the local space defined by the current MVP — points are
  given in that space, not screen space.
- `TransformGizmo.Draw3D` is a ready-made instance already wired to the gizmo's own
  view-projection; you rarely need to construct `GizmoDraw3D` yourself outside a custom sub-gizmo.

## GizmoUtils

Static math/picking helpers shared by the gizmo and sub-gizmo implementations: ray-plane and
ray-ray intersection, arrow/plane geometry layout, snap rounding, per-axis colors, and
`WorldToScreen` projection.

![Screenshot: GizmoUtils](images/gizmos/gizmoutils.png)

```csharp
var screenPos = GizmoUtils.WorldToScreen(viewport, viewProjection * model, worldPoint);
if (screenPos is { } p)
    canvas.CircleFilled(p.X, p.Y, 4f, Color32.Yellow, 16);
```

- `GizmoColor(gizmo, focused, direction)` gives the standard X/Y/Z axis colors (red/green/blue)
  used consistently across both gizmo types.
- `AxisSign` flips a handle to whichever side faces the camera, so arrows never point away from
  the viewer.
- `PickArrow` / `PickPlane` / `PickCircle` are the hit-tests sub-gizmos call from their own `Pick`.
- `WorldToScreen` assumes a column-major MVP (`mvp * vec`), matching Prowl's `Float4x4` convention
  — if you're porting math from a row-major source, transpose first.

Notes: most of `GizmoUtils` is intended for building custom sub-gizmos or drawing your own
handles consistent with the built-in ones; ordinary usage of `TransformGizmo` /
`ViewManipulatorGizmo` never needs to call it directly.
