# Editor Overlays and Editor-Side Rendering

Source: `EnsureGridResources`, `RenderGizmos` in
[`Rendering/DefaultRenderPipeline.cs`](../../Prowl.Runtime/Rendering/DefaultRenderPipeline.cs);
[`Grid.shader`](../../Prowl.Runtime/Assets/Defaults/Grid.shader),
[`Gizmos.shader`](../../Prowl.Runtime/Assets/Defaults/Gizmos.shader),
[`GizmoIcon.shader`](../../Prowl.Runtime/Assets/Defaults/GizmoIcon.shader);
editor: [`GUI/PreviewRenderer.cs`](../../Prowl.Editor/GUI/PreviewRenderer.cs),
[`GUI/Panels/EnvironmentPanel.cs`](../../Prowl.Editor/GUI/Panels/EnvironmentPanel.cs),
[`GUI/CustomEditors/AssetEditors/RenderTextureAssetEditor.cs`](../../Prowl.Editor/GUI/CustomEditors/AssetEditors/RenderTextureAssetEditor.cs).
`Debug.GetGizmoDrawData()` / `GetGizmoIcons()` (gizmo batching) are core.

## Editor grid (`RenderingData.DisplayGrid`)

A 1000x1000 quad (`+-500`, UVs in world units, normal +Y) is built once and injected as an ordinary `MeshRenderable`
into the renderables list, translated to the camera's rounded XZ, layer 0. It therefore goes through culling and the
transparents pass (`RenderOrder=Transparent`, alpha blend, `ZTest LEqual`, `ZWrite Off`).

Shader: anti-aliased grid lines from UV derivatives (the "pristine grid" technique: line width clamped to the pixel
footprint, fades to the average coverage when lines get sub-pixel), primary (1) and secondary (0.25) spacing,
`_LineWidth` 0.02, distance falloff `_Falloff` 1.5 / `_MaxDist` 500, and a depth comparison against
`_CameraDepthTexture` (0.5% of linear depth) to handle intersection with scene geometry. Default color
`(0.5, 0.5, 0.5, 0.3)`.

## Gizmos (`RenderingData.DisplayGizmos`)

Encoded into the `FinalBlit` CB after PostProcess, before the final blit, into scene color (so they are not tonemapped
or anti-aliased):
- `Debug.GetGizmoDrawData()` returns a batched wire mesh and a solid mesh; both drawn with `Hidden/Gizmos`
  (`ZTest Always`, alpha blend).
- Icons (`Debug.DrawIcon`, e.g. camera and light icons): one full-screen-quad draw per icon with `Hidden/Gizmo Icon`,
  `_IconCenter`, `_IconScale`, `_IconColor`, `_MainTex`; the shader billboards the quad and dims occluded icons
  (color x0.5, alpha x0.3) by comparing against `_CameraDepthTexture`.

Components contribute through `DrawGizmos` / `DrawGizmosSelected` (lights, camera frustum, fog volumes, probes, ...).

## Asset previews (`PreviewRenderer`)

Builds an isolated `Scene` with a camera (`ClearFlags.Skybox`, or `SolidColor` transparent for alpha thumbnails) and a
`DirectionalLight`, places the subject (mesh + material, prefab hierarchy, or a material on a sphere), orbits with
drag/scroll, supports animation scrubbing for rigged subjects and bone-to-screen projection, and renders with
`pipeline.Render(camera, { DisplayGrid = ShowGrid, FallbackTarget = rt })`. Used by inspectors and the thumbnail
generator.

## Environment panel

Tabs over the open scene's settings: Skybox (mode, colors, exponent, custom material), Fog (mode, color, start/end,
density), Ambient (mode, strength, colors), Lightmapping (resolution, quality, environment, advanced options,
bake/clear buttons driving `LightmapBakeService`). See [sky](sky.md), [ambient and fog](../lighting/ambient-and-fog.md),
[baked GI](../lighting/baked-gi.md).

## RenderTexture asset editor

Inspector for `.rendertexture` assets: edits the description stored in the file, rewrites and reimports it, which
rebuilds GPU resources for every camera targeting it; per-asset edit state. (Render textures themselves are core.)

## Rebuild notes

- Grid injection as a regular renderable is convenient but puts editor content through game culling/sorting.
- Gizmos drawn into HDR scene color before the final blit are then gamma-unaware if no tonemapper runs.
