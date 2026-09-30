# Default Shader Inventory

Every `.shader` in [`Assets/Defaults/`](../../Prowl.Runtime/Assets/Defaults/). Loaded through `Shader.LoadDefault(DefaultShader.X)`
(enum in core `Resources/DefaultAssets.cs`, not in the snapshot).

## Scene shaders

| Asset | Shader name | Passes (tags) | State | Used by | Doc |
|---|---|---|---|---|---|
| `Standard*.shader` (6) | `Default/[Cutout/|Transparent/]Standard[ Double Sided]` | Forward / Prepass / ShadowCaster | see family doc | default material | [family](standard-shader-family.md) |
| `StandardAnisotropic*.shader` (6) | `Default/Anisotropic/...` | same | | | [family](standard-shader-family.md) |
| `Unlit*.shader` (6) | `Default/[Cutout/|Transparent/]Unlit[ Double Sided]` | same | | | [family](standard-shader-family.md) |
| `Refraction.shader` | `Default/Refraction` | `RenderOrder=Transparent` | Blend Alpha, Cull Back, ZWrite Off, `GrabTexture "_GrabTexture"` | user materials | [culling/batching](../architecture/culling-sorting-batching.md#grab-texture-scene-color-read-back) |
| `Terrain.shader` | `Default/Terrain` | Terrain (Opaque), TerrainShadow (ShadowCaster), TerrainPrepass (Prepass) | Cull Back, ZWrite On | `TerrainComponent` | [terrain](../scene-rendering/terrain-and-vegetation.md) |
| `Grass.shader` | `Default/Grass` | Grass (Opaque), GrassPrepass (Prepass) | Cull Off, no shadow pass | terrain details | [terrain](../scene-rendering/terrain-and-vegetation.md) |
| `Particle.shader` | `Default/Particle` | Transparent | Blend Alpha, Cull Off, ZWrite Off | `ParticleSystemComponent` (`Particle.mat`) | [particles](../scene-rendering/particles.md) |
| `Line.shader` | `Default/Line` | Transparent | Blend Alpha, Cull Off | `LineRenderer` | [lines](../scene-rendering/lines-sprites-text.md) |
| `Sprite.shader` | `Default/Sprite` | Transparent | Blend Alpha, Cull Off, ZWrite Off | `SpriteRenderer` | [sprites](../scene-rendering/lines-sprites-text.md) |
| `DefaultTextMesh.shader` | `UI/Text Mesh` | Transparent | Blend Alpha, Cull Off, ZWrite Off | `TextMeshComponent` | [text](../scene-rendering/lines-sprites-text.md) |
| `DefaultUI.shader` | `UI/Default` | `RenderOrder=UI` | custom blend, ZTest Off | runtime UI images | [UI](../scene-rendering/ui-in-pipeline.md) |
| `DefaultText.shader` | `UI/Text` | `RenderOrder=UI` | custom blend, ZTest Off | runtime UI text | [UI](../scene-rendering/ui-in-pipeline.md) |
| `UI.shader` | `UI/Paper` | UI, BlurDown, BlurUp | ZTest Off | Paper UI renderer (core UI, not the scene pipeline) | - |

## Sky and editor shaders

| Asset | Shader name | Pass / state | Used by | Doc |
|---|---|---|---|---|
| `ProceduralSkybox.shader` | `Skybox/Procedural` | Opaque, Cull None, ZTest Off, ZWrite Off | sky mode Procedural | [sky](../scene-rendering/sky.md) |
| `GradientSkybox.shader` | `Skybox/Gradient` | Cull Front, ZTest LEqual, ZWrite Off | sky mode Gradient | [sky](../scene-rendering/sky.md) |
| `CubemapSkybox.shader` | `Skybox/Cubemap` | Cull Front, ZTest LEqual, ZWrite Off; 6 face textures | custom sky material | [sky](../scene-rendering/sky.md) |
| `Grid.shader` | `Hidden/Grid` | Transparent, Blend Alpha, ZTest LEqual, ZWrite Off | editor grid | [editor overlays](../scene-rendering/editor-overlays.md) |
| `Gizmos.shader` | `Hidden/Gizmos` | Blend Alpha, ZTest Always | gizmo wire/solid meshes | [editor overlays](../scene-rendering/editor-overlays.md) |
| `GizmoIcon.shader` | `Hidden/Gizmo Icon` | Blend Alpha, ZTest Always, depth-dimmed | component icons | [editor overlays](../scene-rendering/editor-overlays.md) |
| `Blit.shader` | `Hidden/Blit` | pass named "Gizmos", Blend Alpha, ZTest Off | `cmd.Blit` default material, final blit | [render targets](../architecture/render-targets.md) |
| `Invalid.shader` | `Hidden/Invalid` | `RenderType=Opaque`, Cull None | compile-failure fallback | [shader format](shader-format.md#variants) |

## Post-process shaders

| Asset | Shader name | Passes (index order) | Effect |
|---|---|---|---|
| `Tonemapper.shader` | `Hidden/Post Process/Tonemapper` | 0 Tonemapper | [tonemapping](../post-processing/tonemapping-and-exposure.md) |
| `AutoExposure.shader` | `Hidden/Post Process/Auto Exposure` | 0 LuminanceExtract, 1 Downsample, 2 Adapt, 3 ApplyExposure | [exposure](../post-processing/tonemapping-and-exposure.md) |
| `Bloom.shader` | `Hidden/Post Process/Bloom` | 0 Threshold, 1 Downsample, 2 Upsample (additive), 3 Composite | [bloom](../post-processing/bloom.md) |
| `FXAA.shader` | `Hidden/Post Process/FXAA` | 0 FXAA | [AA](../post-processing/anti-aliasing.md) |
| `SMAA.shader` (+ `SMAA.glsl`) | `Hidden/Post Process/SMAA` | 0 EdgeDetection, 1 BlendWeights, 2 NeighborhoodBlend | [AA](../post-processing/anti-aliasing.md) |
| `TAA.shader` | `Hidden/Post Process/TAA` | 0 Resolve | [AA](../post-processing/anti-aliasing.md) |
| `GTAO.shader` | `Hidden/Post Process/GTAO` | 0 CalculateGTAO, 1 Blur, 2 Composite, 3 Temporal, 4 DownsampleDepth | [GTAO](../post-processing/gtao.md) |
| `SSR.shader` | `Hidden/Post Process/Screen Space Reflections` | 0 RayCast, 1 SceneBlur, 2 Resolve, 3 Temporal, 4 Reproject, 5 Combine | [SSR](../post-processing/ssr.md) |
| `VolumetricFog.shader` | `Hidden/Post Process/Volumetric Fog` | 0 FogMarch, 1 FogTemporal, 2 FogComposite | [fog](../post-processing/volumetric-fog.md) |
| `BokehDoF.shader` | `Hidden/Post Process/Depth of Field` | 0 CircularHorizMRT, 1 CircularVerticalComposite, 2 DoFCombine | [DoF](../post-processing/depth-of-field.md) |
| `MotionBlur.shader` | `Hidden/Post Process/Motion Blur` | 0 MotionBlur | [motion blur](../post-processing/motion-blur.md) |
| `CinematicEffects.shader` | `Hidden/Post Process/Cinematic Effects` | 0 CinematicEffects | [cinematic](../post-processing/cinematic-effects.md) |

All post-process vertex shaders take clip-space `vertexPosition` from `Mesh.GetFullscreenQuad()` and pass UVs through.

## Default assets that belong to the pipeline

| Asset | Purpose |
|---|---|
| `SkyDome.obj` | sky mesh for all skybox modes |
| `Standard.mat`, `Standard Terrain.mat`, `Grass.mat`, `Particle.mat` | default materials |
| `SMAAAreaTex.bin`, `SMAASearchTex.bin` | raw RGBA8 SMAA lookup tables |
| `../brdf_lut.brdf` | raw RGBA8 256x256 split-sum LUT (generated by `BRDFGen/`) |
| `noise.png` | blue noise for GTAO/SSR |
| `default_white/normal/surface/emission/gray18.png` | default texture slots |
| `grid.png`, `icon_camera.png`, `icon_light.png`, `handle_ui.png` | editor visuals |
