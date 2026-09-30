# Camera

Source: [`Components/Camera.cs`](../../Prowl.Runtime/Components/Camera.cs) (also defines `ImageEffect` and
`CameraClearFlags`), `RenderPipeline.CameraSnapshot` and `ViewerData` in
[`Rendering/RenderPipeline.cs`](../../Prowl.Runtime/Rendering/RenderPipeline.cs) /
[`Rendering/DefaultRenderPipeline.cs`](../../Prowl.Runtime/Rendering/DefaultRenderPipeline.cs).

## Serialized fields

| Field | Default | Meaning |
|---|---|---|
| `Effects` | `[]` | ordered `List<ImageEffect>`; order within a stage is execution order |
| `ClearFlags` | `Skybox` | `Nothing`, `SolidColor`, `Depth`, `Skybox` |
| `ClearColor` | black | used by `SolidColor`, and by `Skybox` when the scene sky mode is not `SolidColor` |
| `CullingMask` | `Everything` | layer mask for renderables (lights ignore it, see [scene light system](../lighting/scene-light-system.md)) |
| `ProjectionMode` | `Perspective` | or `Orthographic` |
| `FieldOfView` | 60 | vertical degrees, clamped to [1, 179] on use |
| `OrthographicSize` | 5 | half height; width follows aspect |
| `NearClipPlane` / `FarClipPlane` | 0.1 / 100 | |
| `Depth` | -1 | sort key for multi-camera rendering |
| `Pipeline` | null | per-camera override; null = `DefaultRenderPipeline.Default` |
| `Target` | null | render texture asset; null = `RenderingData.FallbackTarget` or backbuffer |
| `HDR` | false | scene color `Short4` instead of `Color4b` |
| `RenderScale` | 1 | clamped [0.1, 2]; `PixelWidth/Height = target size * scale` |
| `ShadowFocus` | null | transform directional cascades center on (third-person games) |

`Viewrect` is declared commented-out as "Not Implemented".

## Per-frame state (`UpdateRenderData`)

Called once at the top of `Internal_Render`:

1. Resolve target: `Target` if valid, else the fallback; size from it or from the window framebuffer.
2. `PixelWidth/Height = max(1, size * clamp(RenderScale))`.
3. Aspect recomputed unless the user set `Aspect` (`_customAspect`).
4. `ProjectionMatrix` recomputed unless user-set; `NonJitteredProjectionMatrix` recomputed unless user-set.
5. `ViewMatrix = CreateLookTo(position, forward, up)`.

Orthographic uses `CreateOrtho(size * 2 * aspect, size * 2, near, far)` (a fix: passing `size` for both squashed
non-square targets). Perspective uses `CreatePerspectiveFov`. Projections are DirectX-style (clip z in `[0, w]`),
which is why shaders remap depth with `depth * 2 - 1` (see `screenDepthToNDC` in [includes](../shaders/includes.md)).

## Jitter split (TAA support)

The camera keeps two projections:

- `ProjectionMatrix` - what rasterization uses. TAA writes a jittered copy in `OnPreCull`.
- `NonJitteredProjectionMatrix` - clean; TAA sets it to the pre-jitter matrix. Used for motion vectors and for
  `SavePreviousViewProjectionMatrix()` (`prev = nonJitteredProj * view`, stored at end of frame).

`HasPreviousViewProjectionMatrix` is false on the first frame after `OnEnable` or `ResetMotionHistory()`; the pipeline
then uses the current non-jittered VP as the previous one so motion reads zero. TAA's `OnPostRender` calls
`ResetProjectionMatrix()` so picking and gizmos never see a jittered matrix.

## Image effect lifecycle hooks

`UpdateImageEffectLifecycle(currentlyActive)` diffs the set of effects rendered this frame against last frame and calls
`OnDisable()` on anything that dropped out (disabled, removed, hot-swapped). `Camera.OnDisable` calls `OnDisable` on all
of them. Details in the [image effect framework](../post-processing/image-effect-framework.md).

## Snapshot types

- `CameraSnapshot` (struct, built after `OnPreCull` so it captures the jittered projection): scene, position, shadow
  focus position, basis vectors, culling mask, clear flags, clip planes, pixel size, aspect, view, view inverse,
  projection, non-jittered projection, previous VP, has-previous flag, world frustum (`Frustum.FromMatrix(P * V)`).
- `ViewerData` (struct): position, forward, up, right, pixel size, view and projection. Passed to every
  `IRenderable.GetRenderingData` so renderables can billboard or LOD per viewer (shadow passes pass the light as viewer).

## Utilities

- `ScreenPointToRay(point, size)` unprojects through the non-jittered projection.
- `GetShadowFocusPosition()` returns `ShadowFocus.Position` if the focus is set and its GameObject is alive, else the
  camera position (tested by `CameraShadowFocusTests`).
- `DrawGizmos` draws the camera icon and frustum lines (note: `float aspect = 1280 / 720` is integer division = 1).

## Rebuild notes

- The camera owns render-pipeline state (jitter, previous VP, effect lifecycle). A render graph might move these into a
  per-camera "view state" object owned by the pipeline instead.
- The jitter/non-jitter split must be preserved for any TAA-style effect and for correct motion vectors.
