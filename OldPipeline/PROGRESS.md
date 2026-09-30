# Render Pipeline Rebuild - Feature Checklist

Every feature of the old pipeline as a to-do item for the new one. Each item links to the page documenting how the old
pipeline did it. Check an item off when the new pipeline has an equivalent (or tick it with a note if the feature is
deliberately dropped).

Starting point: the in-progress Graphite port has been cut out. The engine core keeps only a minimal pipeline that
clears the camera target and composites UI; nothing below exists yet.

Legend: `[ ]` not started, `[~]` partial, `[x]` done, `[-]` intentionally dropped (say why).

## 0. Foundations

- [ ] Per-camera render entry point with `RenderingData` (gizmos, grid, scene view, skip UI, fallback target) - [frame lifecycle](docs/architecture/frame-lifecycle.md)
- [ ] Multi-camera rendering sorted by `Camera.Depth`, per-camera pipeline override, per-camera error isolation - [frame lifecycle](docs/architecture/frame-lifecycle.md#2-camera-loop)
- [ ] Camera: perspective/orthographic, render scale, HDR flag, clear flags (Skybox, SolidColor, Depth, Nothing), target RT - [camera](docs/architecture/camera.md)
- [ ] Jittered vs non-jittered projection, previous view-projection history - [camera](docs/architecture/camera.md#jitter-split-taa-support)
- [ ] Camera frame/time/matrix constant buffer incl. precomputed inverses - [global uniforms](docs/architecture/global-uniforms.md)
- [ ] Global property block with defined precedence (globals < material < shader defaults < instance) - [global uniforms](docs/architecture/global-uniforms.md#property-precedence-at-draw-time)
- [ ] Renderable / light collection from components (`OnRenderCollect`) - [renderables](docs/architecture/renderables.md)
- [ ] Frustum + layer culling with shared per-frame world bounds - [culling](docs/architecture/culling-sorting-batching.md#culling)
- [ ] Back-to-front transparent sorting (and ideally front-to-back opaques) - [culling](docs/architecture/culling-sorting-batching.md#sorting)
- [ ] Pass selection by shader tags (`RenderOrder`, `LightMode`) with tag sort offsets - [shader format](docs/shaders/shader-format.md#tags-the-pipeline-understands)
- [ ] Material/pass/mesh state batching - [batching](docs/architecture/culling-sorting-batching.md#drawrenderables---batch-build-and-draw)
- [ ] Submesh draws (one material per submesh) - [batching](docs/architecture/culling-sorting-batching.md#drawrenderables---batch-build-and-draw)
- [ ] Mesh-derived shader variants (`HAS_*`, `SKINNED`, `BLENDSHAPES`) - [batching](docs/architecture/culling-sorting-batching.md#drawrenderables---batch-build-and-draw)
- [ ] GPU instancing with per-instance buffer (`InstanceData`) - [instancing](docs/architecture/culling-sorting-batching.md#gpu-instancing)
- [ ] Procedural instancing (`gl_InstanceID`, no buffer) - [instancing](docs/architecture/culling-sorting-batching.md#gpu-instancing)
- [ ] Scene color read-back for shaders (grab texture + depth) - [grab texture](docs/architecture/culling-sorting-batching.md#grab-texture-scene-color-read-back)
- [ ] Per-object previous model matrix history (per camera) - [motion history](docs/architecture/culling-sorting-batching.md#motion-history)
- [ ] Render statistics (draws, batches, culling, lights, effects, timings) - [RenderStats](docs/infrastructure/render-stats.md)

## 1. Render targets and passes

- [ ] HDR / LDR scene color target - [render targets](docs/architecture/render-targets.md)
- [ ] Unified prepass: depth + view normals + motion vectors + roughness/metallic - [render targets](docs/architecture/render-targets.md#unified-prepass-layout)
- [ ] Expose `_CameraDepthTexture`, `_CameraNormalsTexture`, `_CameraMotionVectorsTexture` - [uniform reference](docs/reference/uniform-reference.md#prepass-outputs-globals)
- [ ] Depth prepass feeding opaque early-Z - [frame lifecycle](docs/architecture/frame-lifecycle.md#4-internal_render---exact-order)
- [ ] Opaque forward pass - [frame lifecycle](docs/architecture/frame-lifecycle.md)
- [ ] Transparent forward pass - [frame lifecycle](docs/architecture/frame-lifecycle.md)
- [ ] Final blit to camera target / backbuffer and backbuffer reset - [render targets](docs/architecture/render-targets.md#final-output)

## 2. Shaders

- [ ] Shared shader library: variables, color space, depth/projection helpers (ortho aware), RNG, normal mapping - [includes](docs/shaders/includes.md)
- [ ] Vertex attribute layout, skinning via bone texture, blend shapes via delta textures, instancing helpers - [includes](docs/shaders/includes.md#vertexattributesglsl)
- [ ] Standard lit (opaque, double sided, cutout, cutout double sided, transparent, transparent double sided) - [standard family](docs/shaders/standard-shader-family.md)
- [ ] Standard anisotropic (same 6 variants) - [standard family](docs/shaders/standard-shader-family.md)
- [ ] Unlit (same 6 variants) - [standard family](docs/shaders/standard-shader-family.md)
- [ ] Standard material inputs: albedo, normal (scale), surface (roughness/metallic), occlusion, emission, per-slot UV set, tiling/offset - [standard family](docs/shaders/standard-shader-family.md#material-properties-standard)
- [ ] Parallax occlusion mapping - [BRDF](docs/lighting/shading-brdf.md#parallax-occlusion-mapping)
- [ ] Translucency map + scattering - [BRDF](docs/lighting/shading-brdf.md#translucency-calculatetranslucency)
- [ ] Shader-side sRGB decode for color textures (or switch to sRGB formats) - [standard family](docs/shaders/standard-shader-family.md#color-space)
- [ ] Refraction shader - [inventory](docs/shaders/default-shader-inventory.md)
- [ ] Invalid (magenta) fallback on compile failure - [shader format](docs/shaders/shader-format.md#variants)
- [ ] Default shader + material asset set - [inventory](docs/shaders/default-shader-inventory.md)

## 3. Lighting

- [ ] Light components: directional, point, spot (color, intensity, range, cone, shadow settings, bake mode) - [light components](docs/lighting/light-components.md)
- [ ] Per-scene light system: static/dynamic sets, reconcile per frame - [scene light system](docs/lighting/scene-light-system.md)
- [ ] Light culling structure (old: stackless rope BVH mirrored in float textures) - [light BVH](docs/lighting/light-bvh.md)
- [ ] Incremental updates (loose-bounds refit, dirty-range uploads) - [light BVH](docs/lighting/light-bvh.md#cpu-structure)
- [ ] Single directional light path (or a unified light list) - [overview](docs/lighting/overview.md)
- [ ] Per-camera light layer filtering (never implemented in the old pipeline) - [known issues L4](docs/reference/known-issues.md#lighting-and-shadows)
- [ ] GGX / Smith / Schlick / Disney diffuse BRDF, roughness floor - [BRDF](docs/lighting/shading-brdf.md#isotropic-brdf-metallicroughness)
- [ ] Anisotropic BRDF - [BRDF](docs/lighting/shading-brdf.md#anisotropic-brdf-far-cry-4--ggx-aniso)
- [ ] Specular anti-aliasing - [BRDF](docs/lighting/shading-brdf.md#specular-anti-aliasing)
- [ ] Inverse-square attenuation with smooth range window, spot cone smoothstep - [BRDF](docs/lighting/shading-brdf.md#local-light-evaluation-evaluatelocallight)
- [ ] Particle lights (many unshadowed dynamic point lights) - [light components](docs/lighting/light-components.md#particle-lights)
- [ ] Scene ambient: uniform and hemisphere - [ambient and fog](docs/lighting/ambient-and-fog.md#ambient)
- [ ] Ambient specular approximation (or real IBL) - [standard family](docs/shaders/standard-shader-family.md#fragment-stage---forward-prowlfragment)
- [ ] Distance fog: linear, exp, exp2 - [ambient and fog](docs/lighting/ambient-and-fog.md#distance-fog)
- [ ] BRDF LUT (unused by the old shaders; only needed if IBL is added) - [BRDF](docs/lighting/shading-brdf.md#brdf-lut)

## 4. Shadows

- [ ] Shadow atlas with rectangle packing - [shadows](docs/lighting/shadows.md#shadow-atlas)
- [ ] Directional cascades (1/2/4), texel snapping, shadow focus target - [shadows](docs/lighting/shadows.md#directional-cascades-shader)
- [ ] Point light cube shadows - [shadows](docs/lighting/shadows.md#point-and-spot-shader)
- [ ] Spot light shadows - [shadows](docs/lighting/shadows.md#point-and-spot-shader)
- [ ] Closest-N shadowed local lights - [scene light system](docs/lighting/scene-light-system.md#reconcilelights-shadowfocuspos-cullingmask)
- [ ] Hardware PCF + rotated Poisson soft shadows, per-cascade world-normalized radius - [shadows](docs/lighting/shadows.md#filtering-sampleshadowpcf)
- [ ] Normal offset + slope-scaled bias - [shadows](docs/lighting/shadows.md#directional-cascades-shader)
- [ ] Shadow caster passes for cutout materials and terrain - [standard family](docs/shaders/standard-shader-family.md#fragment-stage---shadow)

## 5. Baked GI

- [ ] Lightmap binding per renderer (RGBM pages, scale/offset, UV2 or UV0) - [baked GI](docs/lighting/baked-gi.md#runtime-selection-lightmapbindingfill)
- [ ] Streaming-safe lightmap fallback - [baked GI](docs/lighting/baked-gi.md#runtime-selection-lightmapbindingfill)
- [ ] Light probes: SH L2 sampling for dynamic objects - [baked GI](docs/lighting/baked-gi.md#light-probes)
- [ ] Probe tetrahedralization and interpolation - [baked GI](docs/lighting/baked-gi.md#light-probes)
- [ ] Editor lightmap bake (Photonic) incl. mixed/baked light modes - [baked GI](docs/lighting/baked-gi.md#bake-pipeline-editor)
- [ ] Lightmap UV2 generation at import - [baked GI](docs/lighting/baked-gi.md#lightmap-uvs)
- [ ] Light probe group authoring tools - [baked GI](docs/lighting/baked-gi.md#light-probes)

## 6. Post-processing

- [ ] Image effect framework: stages, lifecycle hooks, error isolation, scene color replacement - [framework](docs/post-processing/image-effect-framework.md)
- [ ] Tonemapper (9 operators, contrast, saturation, display encode) - [tonemapping](docs/post-processing/tonemapping-and-exposure.md#tonemapper-postprocess-transformstoldr--true)
- [ ] Auto exposure - [exposure](docs/post-processing/tonemapping-and-exposure.md#auto-exposure-postprocess-place-before-bloom-and-tonemapper)
- [ ] Bloom - [bloom](docs/post-processing/bloom.md)
- [ ] FXAA - [AA](docs/post-processing/anti-aliasing.md#fxaa)
- [ ] SMAA (+ LUT assets, no flip) - [AA](docs/post-processing/anti-aliasing.md#smaa-1x-luma-edges)
- [ ] TAA - [AA](docs/post-processing/anti-aliasing.md#taa)
- [ ] GTAO - [GTAO](docs/post-processing/gtao.md)
- [ ] Screen-space reflections - [SSR](docs/post-processing/ssr.md)
- [ ] Volumetric fog + fog volumes - [volumetric fog](docs/post-processing/volumetric-fog.md)
- [ ] Per-light fog controls (`FogLight`, dead in the old pipeline) - [known issues L1](docs/reference/known-issues.md#lighting-and-shadows)
- [ ] Bokeh depth of field - [DoF](docs/post-processing/depth-of-field.md)
- [ ] Motion blur - [motion blur](docs/post-processing/motion-blur.md)
- [ ] Cinematic uber effect: vignette, chromatic aberration, grain, grading, LUT, CAS sharpen, edges, pixelation, god rays - [cinematic](docs/post-processing/cinematic-effects.md)
- [ ] Effect custom inspectors - [framework](docs/post-processing/image-effect-framework.md#editor)

## 7. Scene content

- [ ] MeshRenderer (multi-material submeshes, per-object props cache) - [mesh renderers](docs/scene-rendering/mesh-renderers.md#meshrenderer)
- [ ] SkinnedMeshRenderer (bone paths, dirty-checked skinning, bone texture, bone-derived bounds) - [mesh renderers](docs/scene-rendering/mesh-renderers.md#skinnedmeshrenderer)
- [ ] Blend shapes (active layer weights, in-between frames) - [mesh renderers](docs/scene-rendering/mesh-renderers.md#blend-shapes)
- [ ] Skybox: procedural atmosphere, solid color, gradient, custom material, cubemap - [sky](docs/scene-rendering/sky.md)
- [ ] Terrain: quadtree LOD, heightmap displacement (bilinear/bicubic), 4/8 splat layers, holes, brush preview - [terrain](docs/scene-rendering/terrain-and-vegetation.md#terrain-surface)
- [ ] Procedural grass cascades with wind zones and translucency - [terrain](docs/scene-rendering/terrain-and-vegetation.md#procedural-grass-terraindetailrenderer--terrainscatterglsl--grassshader)
- [ ] Mesh detail scattering - [terrain](docs/scene-rendering/terrain-and-vegetation.md#mesh-details-terrainmeshdetailrenderer)
- [ ] Terrain trees - [terrain](docs/scene-rendering/terrain-and-vegetation.md#trees-terraintreerenderer)
- [ ] Particles: instanced billboards, flipbook UVs - [particles](docs/scene-rendering/particles.md)
- [ ] LineRenderer (and a working line shader) - [lines](docs/scene-rendering/lines-sprites-text.md#linerenderer)
- [ ] SpriteRenderer (secondary textures, sorting order) - [sprites](docs/scene-rendering/lines-sprites-text.md#spriterenderer)
- [ ] TextMeshComponent - [text](docs/scene-rendering/lines-sprites-text.md#textmeshcomponent)
- [ ] World-space UI canvases inside the 3D pass - [UI](docs/scene-rendering/ui-in-pipeline.md)
- [ ] Overlay UI and scene-view world-space canvases - [UI](docs/scene-rendering/ui-in-pipeline.md#surfaces)

## 8. Editor integration

- [ ] Scene view grid - [editor overlays](docs/scene-rendering/editor-overlays.md#editor-grid-renderingdatadisplaygrid)
- [ ] Gizmos and depth-aware gizmo icons - [editor overlays](docs/scene-rendering/editor-overlays.md#gizmos-renderingdatadisplaygizmos)
- [ ] Asset preview renderer / thumbnails - [editor overlays](docs/scene-rendering/editor-overlays.md#asset-previews-previewrenderer)
- [ ] Environment panel (sky, fog, ambient, lightmapping) - [editor overlays](docs/scene-rendering/editor-overlays.md#environment-panel)
- [ ] Render profiler still shows meaningful pass/command names - [command buffers](docs/infrastructure/command-buffers.md)

## 9. Tests to recreate

- [ ] Light structure tests (slot stability, refit vs rebuild, traversal) - [light BVH](docs/lighting/light-bvh.md)
- [ ] Directional shadow matrix texel snapping - [light components](docs/lighting/light-components.md#directionallight)
- [ ] Camera shadow focus resolution - [camera](docs/architecture/camera.md#utilities)
- [ ] SMAA asset presence / shader parse - [AA](docs/post-processing/anti-aliasing.md#smaa-1x-luma-edges)
- [ ] Render collection registry and camera gather - [renderables](docs/architecture/renderables.md#collection-model)

## Do not port

Bugs and dead code catalogued in [known issues](docs/reference/known-issues.md) (broken line shader, shared motion
history, per-window-frame atlas reset, `Scene.Current` lookups in effects, stats timing, and others).
