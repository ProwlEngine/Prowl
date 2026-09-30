# UI Inside the Pipeline

Source: `RenderUIQueue`, `RenderUIWorld`, `DrawUIItems`, `BuildScreenOrtho` in
[`Rendering/DefaultRenderPipeline.cs`](../../Prowl.Runtime/Rendering/DefaultRenderPipeline.cs);
[`DefaultUI.shader`](../../Prowl.Runtime/Assets/Defaults/DefaultUI.shader),
[`DefaultText.shader`](../../Prowl.Runtime/Assets/Defaults/DefaultText.shader).
The runtime UI system itself (`GameCanvas`, `UIRenderTree`, `UIRenderItem`, `UISurface`, layout, input) is core and stays;
this page documents only how the old pipeline drew it. The editor's Paper UI (`UI.shader`, `UI/Paper`) is drawn after
the pipeline by the Paper renderer and is not part of this.

## Surfaces

| Surface | When | Target | Projection |
|---|---|---|---|
| `UISurface.World` | after transparents, before PostProcess | scene color | camera V/P (already current) |
| `UISurface.Overlay` | after the final blit | camera target / backbuffer | screen ortho `(0, w, 0, h, -1000, 1000)`, origin bottom-left |

- Scene view (`RenderingData.IsSceneView`): overlay canvases are also drawn in world space (so they can be seen and
  edited in 3D), and `GameCanvas.ScreenSizeOverride` is set to the viewport size so their world rect matches the
  `UISceneEditor` handles. Overlay UI is skipped in the scene view and when `SkipUI` is set.
- Game view: `GameCanvas.ScreenSizeOverride = (PixelWidth, PixelHeight)` during the overlay pass so design-pixel layout
  matches the ortho projection; restored afterwards (try/finally).
- World-space UI goes through PostProcess (it is tonemapped and anti-aliased with the scene); overlay UI is not.

## Drawing

1. `UIRenderTree.CollectFor(scene, surface, list)` gathers `UIRenderItem`s (they implement `IRenderable`).
2. Stable sort by `UIRenderItem.SortKey` (canvas + hierarchy order).
3. Overlay: `AssignCameraMatrices(Identity, ortho)`; afterwards restore the camera matrices.
4. `DrawUIItems`: memoizes the `RenderOrder=UI` pass index per shader (avoids `GetPassesWithTag` allocation per item),
   skips items without a valid material/mesh, `cmd.DrawMesh(mesh, material, pass, model, props)`; ends with
   `DisableScissor()`.

No GPU scissor: `RectMask` clipping is done per fragment by the UI shaders through per-item `_ClipToLocal`,
`_ClipRect`, `_ClipRadius`, `_ClipSoftness`, `_ClipEnable`, so clips follow rotation/scale and can have rounded, soft
corners.

## Shaders

- `UI/Default`: `RenderOrder=UI`, blend `SrcAlpha / OneMinusSrcAlpha`, `ZTest Off`, `ZWrite Off`, `Cull Off`,
  `_MainTex`, `_MainColor`, tiling/offset, rounded-rect clip.
- `UI/Text`: same state, single-channel SDF atlas with `fwidth`-based antialiasing, same clip.

## Rebuild notes

- The stub pipeline that remains after the nuke still has to composite UI; the world-space pass is the part that needs
  the 3D pipeline (it draws into scene color with depth).
