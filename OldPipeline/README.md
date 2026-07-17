# Old Render Pipeline (pre-Graphite) - Feature Inventory

This directory is a reference snapshot of Prowl's render pipeline as it existed on `main`
(commit `285ca4da`) immediately before the Graphite/Slang rewrite. It is **not** part of the
build. It exists so the new pipeline can be built from scratch with better architecture while
still being able to look up exactly how the old pipeline implemented any given feature.

Layout mirrors the live tree:

- `OldPipeline/Prowl.Runtime/Rendering/**` - C# pipeline code
- `OldPipeline/Prowl.Runtime/Components/Lights/**`, `MeshRenderer.cs`, `SkinnedMeshRenderer.cs`,
  `LineRenderer.cs` - renderer/light components
- `OldPipeline/Prowl.Runtime/MeshRenderable.cs`, `SkinnedMeshRenderable.cs` - `IRenderable` adapters
- `OldPipeline/Prowl.Runtime/Assets/Defaults/*.shader`, `*.glsl` - all standalone GLSL shader
  source and includes

Every entry below is `[what it does] -> [where to find it]`.

## 1. Core Pipeline / Render Graph

- **Two-stage forward pipeline with explicit render stages.** Effects register for
  `RenderStage.AfterOpaques` (has opaque depth/normals/color, used by SSR/GTAO/volumetric fog) or
  `RenderStage.PostProcess` (final image chain: tonemap, bloom, DOF, AA, motion blur).
  `Rendering/RenderStage.cs`, `Rendering/DefaultRenderPipeline.cs` (`GatherImageEffects`,
  `ExecuteImageEffects`, `Internal_Render`).
- **Frame order:** pre-cull -> camera snapshot/global uniforms -> collect+cull renderables ->
  pre-render -> light system reconcile -> shadow atlas clear+render -> unified prepass (depth +
  view-space normals + motion/roughness/metallic MRT) -> opaque forward pass -> AfterOpaques
  effects -> transparents (back-to-front) -> world-space UI -> PostProcess effects -> gizmos ->
  final blit -> overlay UI -> backbuffer reset. `Rendering/DefaultRenderPipeline.cs::Internal_Render`.
- **Unified G-buffer-lite prepass.** One MRT pass writes scene depth, view-space normals
  (Color4b), and motion vectors + roughness/metallic packed into a Short4 target, replacing
  separate depth-prepass/motion-vector passes. `Rendering/DefaultRenderPipeline.cs` lines ~262-296.
- **`IRenderable` / `IRenderableLight` abstraction.** Anything drawable implements
  `GetMaterial`, `GetCullingData`, `GetRenderingData` (returns mesh/model/instance data);
  anything that lights implements `GetForwardLightData`. `Rendering/RenderPipeline.cs`.
- **Frustum culling.** Per-renderable world AABB computed once per frame and shared between the
  main cull and every shadow-cascade cull (`EnsureWorldBounds`); culled mask stored as a `bool[]`
  keyed by index instead of a `HashSet` for O(1) reads. `Rendering/RenderPipeline.cs::CullRenderables`,
  `EnsureWorldBounds`.
- **Sorting.** Front-to-back for opaques (early-Z), back-to-front for transparents, sorted by
  squared distance (no sqrt) with cached `Comparison<T>` delegates to avoid per-call allocation.
  `Rendering/RenderPipeline.cs::SortRenderables`.
- **Batching / draw-call reduction.** `DrawRenderables` groups renderables into batches keyed by
  `(materialHash, passIndex, mesh)`; state (shader variant, raster state, material uniforms,
  mesh keywords) is bound once per batch, and per-object transform uniforms are set only inside
  the batch loop. Batch/index-list pool objects are reused across frames (encode is sequential).
  `Rendering/RenderPipeline.cs::DrawRenderables` (phase 1 build batches, phase 2 draw).
- **GPU instancing.** Renderables that return non-null `InstanceData[]` from `GetRenderingData`
  get their own batch and are drawn with `DrawIndexedInstanced` against a shared per-mesh
  instance VAO/buffer; mesh keyword `GPU_INSTANCING` is toggled around the draw.
  `Rendering/RenderPipeline.cs::DrawInstancedRenderablePass`, `Rendering/InstanceData.cs`.
- **Motion-vector history.** Previous-frame model matrices tracked per stable object ID in a
  dictionary, garbage-collected every 120 frames for ids not seen that frame.
  `Rendering/RenderPipeline.cs::TrackModelMatrix`, `CleanupUnusedModelMatrices`.
- **GrabTexture / scene-color read-back.** A shader pass can request the current framebuffer
  color (and optionally depth) blitted into a temporary RT and exposed as a global texture
  (used by e.g. refraction), scoped to just that batch's draws via CB-ordered
  `SetGlobalTexture`/`ClearGlobalTexture`. `Rendering/RenderPipeline.cs::DrawRenderables`
  (grabRT handling), `Assets/Defaults/Refraction.shader`.
- **Mesh keyword auto-configuration.** Per-batch, shader keywords `HAS_NORMALS`, `HAS_TANGENTS`,
  `HAS_UV`, `HAS_UV2`, `HAS_COLORS`, `HAS_BONEINDICES`, `HAS_BONEWEIGHTS`, `SKINNED`,
  `BLENDSHAPES` are derived from the mesh's actual vertex attributes.
  `Rendering/RenderPipeline.cs::DrawRenderables`/`DrawInstancedRenderablePass`.
- **Editor grid + gizmos + gizmo icons injected as regular renderables/draws.**
  `Rendering/DefaultRenderPipeline.cs::EnsureGridResources`, `RenderGizmos`; shaders
  `Assets/Defaults/Grid.shader`, `Gizmos.shader`, `GizmoIcon.shader`.
- **Skybox rendering** (Procedural / SolidColor / Gradient / Material modes), driven by the
  scene's `Skybox` settings and the selected directional light's direction for the procedural
  sun. `Rendering/DefaultRenderPipeline.cs::RenderSkybox`; shaders `ProceduralSkybox.shader`,
  `GradientSkybox.shader`, `CubemapSkybox.shader`.
- **Screen-space + world-space UI.** UI render items are collected, stable-sorted by canvas
  hierarchy `SortKey`, and drawn through a memoized shader-pass lookup (`RenderOrder=UI` tag).
  World-space canvases draw with camera VP alongside transparents; screen-space canvases use an
  orthographic projection and are skipped in scene view (drawn world-space there instead).
  `Rendering/DefaultRenderPipeline.cs::RenderUIQueue`, `RenderUIWorld`, `DrawUIItems`.
- **RenderStats instrumentation.** Per-frame counters for renderables/culled, batches, lights by
  type, shadow casters, image effects, and pass timing (shadow/color/postfx).
  `Rendering/RenderStats.cs`.

## 2. Global Uniforms / Camera Setup

- **Per-frame global UBO.** Camera position, projection params, screen params, time (`_Time`,
  `_SinTime`, `_CosTime`, delta time), and view/projection matrices are packed into one buffer
  and uploaded once per camera via `GlobalUniforms.Upload()`.
  `Rendering/RenderPipeline.cs::SetupGlobalUniforms`, `AssignCameraMatrices`,
  `Rendering/GlobalUniforms.cs`.
- **Precomputed inverse matrices (`prowl_MatIP`, `prowl_MatIVP`).** Inverse-projection and
  inverse-view-projection are computed once per camera on the CPU and uploaded, instead of every
  fragment shader that reconstructs view/world position from depth (GTAO, SSR, fog, shadow
  reprojection) calling `inverse()` per-pixel. `Rendering/RenderPipeline.cs::AssignCameraMatrices`.
- **Jitter-free motion vectors alongside TAA jitter.** A non-jittered VP
  (`PROWL_MATRIX_VP_NONJITTERED`) is uploaded separately from the (possibly TAA-jittered)
  projection, so the prepass can compute correct motion vectors while the color pass still
  rasterizes with the jittered projection. `Rendering/RenderPipeline.cs::SetupGlobalUniforms`.
- **Fog and ambient uniforms** uploaded once per frame from `Scene.Fog` /
  `Scene.Ambient` (linear/exponential/exponential-squared fog modes; uniform/hemisphere ambient).
  `Rendering/DefaultRenderPipeline.cs::UploadFogUniforms`, `UploadAmbientUniforms`.

## 3. Lighting System

- **Forward+-style light culling via dual BVH.** Two bounding-volume hierarchies per scene
  (static for non-moving lights, dynamic for everything else); point + spot lights only (the
  directional light is handled separately). Each light is a leaf with a tight AABB (sphere of
  its range) and a loose AABB (tight expanded 25%) so small moves refit one leaf without
  rebuilding. Tree is a stackless DFS "rope" layout (`Hit`/`Miss` links) so the shader walks it
  without a stack. Top-down median-split build (O(N log N)), longest-centroid-extent split axis,
  quickselect partition. `Rendering/LightBVH.cs`.
- **BVH -> GPU texture mirror.** Each BVH's light data (5 texels/light) and node data (2
  texels/node) are packed into RGBA32F textures that grow by doubling and are updated with a
  single `TexSubImage` per dirty row range per frame (not a full re-upload).
  `Rendering/LightBVHTextures.cs`. Sampled with `texelFetch` in-shader:
  `Assets/Defaults/LightBVH.glsl`.
- **Per-scene light reconciliation.** `SceneLightSystem.Reconcile` walks the frame's lights once:
  routes baked lights out of the realtime set, picks the single directional light, tracks
  static<->dynamic BVH membership transitions, and picks the closest-N (`MaxShadowCasters = 4`)
  point/spot lights by camera distance to receive shadow-atlas slots (everything else still
  lights surfaces, just unshadowed). `Rendering/SceneLightSystem.cs`.
- **Uniform upload for lighting.** One CommandBuffer per frame uploads: BVH textures + sizes/root
  indices for both trees, the directional light + its cascade matrices/atlas params, and the
  local shadow casters' matrices/atlas params (point: 6 face matrices, spot: 1 matrix), clearing
  only slots that held data last frame. `Rendering/SceneLightSystem.cs::UploadGlobalUniforms`.
- **Shadow atlas.** Single shared depth `RenderTexture` (8192 if the GPU supports it, else 4096),
  packed with a Guillotine bin-packing algorithm (Best-Short-Side-Fit heuristic, split along the
  shorter leftover axis). Sampled as `sampler2DShadow` for hardware 2x2 PCF instead of manual
  compares. `Rendering/ShadowAtlas.cs`.
- **Directional light cascades.** Up to 4 cascades (1/2/4 configurable), linear split distances,
  resolution halved per cascade beyond the first, each cascade gets its own atlas slot and its
  own submitted CommandBuffer (matrices differ per cascade and can't share a CB).
  `Components/Lights/DirectionalLight.cs::RenderShadows`, `GetShadowMatrix`.
- **Point light shadows.** Cubemap-equivalent: 6 faces packed into the shared 2D atlas (3x2
  grid), one CB per face. `Components/Lights/PointLight.cs`.
- **Spot light shadows.** Single perspective shadow map, one atlas slot.
  `Components/Lights/SpotLight.cs`.
- **PCF soft shadows.** Poisson-disk rotated PCF kernel with a hash-based per-pixel rotation;
  filter radius scaled per cascade by the caller. `Assets/Defaults/Shadow.glsl::SampleShadowPCF`.
- **Baked lighting: lightmaps + light probes.**
  - Lightmaps: per-renderer lightmap index + UV2 scale/offset; async-streamed pages, falls back
    to plain ambient while a page is loading (binding an unloaded page would sRGB-decode a shared
    white texture to blown-out white). `Rendering/LightmapBinding.cs`.
  - Light probes: order-2 (9 coefficient) spherical harmonics per probe
    (`Rendering/SphericalHarmonicsL2.cs`), sampled at runtime via a precomputed tetrahedralization
    of probe positions - point-in-tetrahedron via barycentric coordinates with frame-coherent
    "start from last tet" caching, falling back to inverse-distance-weighted blend of the nearest
    4 probes outside the hull or when the grid degenerates. `Rendering/LightProbeVolume.cs`.
  - `LightBakeMode` (Realtime / Mixed / Baked) controls whether a light contributes direct
    realtime lighting, baked indirect only, or is fully excluded from the realtime set.
    `Components/Lights/Light.cs`.
- **Volumetric fog lighting.** Opt-in `FogLight` component makes a light contribute to
  ray-marched volumetric scattering (with its own intensity multiplier, optional shadow
  sampling, and optional color override), independent of its normal direct lighting.
  `Components/Lights/FogLight.cs`, see section 5 for the effect itself.

## 4. Shading Model

- **PBR / Cook-Torrance BRDF** with a precomputed split-sum BRDF integration LUT (256x256,
  indexed by NdotV/roughness, loaded from an embedded resource, bound globally as `_BRDFLut`).
  `Rendering/BRDFLutGenerator.cs`, `Assets/Defaults/PBR.glsl`.
- **Standard surface shader** (metallic/roughness workflow, normal mapping, emission, GI via
  `CalculateGI` reading `_GIMode` set by `LightmapBinding`). `Assets/Defaults/Standard.shader`,
  `Assets/Defaults/StandardSurface.glsl`, core lighting math in `Assets/Defaults/Lighting.glsl`.
- **Standard Anisotropic** variant (anisotropic specular highlights, e.g. brushed metal/hair).
  `Assets/Defaults/StandardAnisotropic.shader`.
- **Standard Transparent** variant (alpha-blended forward pass, drawn back-to-front).
  `Assets/Defaults/StandardTransparent.shader`.
- **Terrain shader** (multi-layer texture blending). `Assets/Defaults/Terrain.shader`.
- **Refraction shader** using the GrabTexture mechanism (section 1) to sample the
  already-rendered opaque scene behind the surface. `Assets/Defaults/Refraction.shader`.
- **Unlit / sprite / particle / grass / line shaders** for non-PBR content.
  `Assets/Defaults/Unlit.shader`, `Sprite.shader`, `Particle.shader`, `Grass.shader`,
  `Line.shader`.
- **Vertex skinning + blend shapes** driven by the mesh keywords set in section 1
  (`SKINNED`, `BLENDSHAPES`); bone matrices uploaded via a float texture with no uniform array
  size limit. `Components/SkinnedMeshRenderer.cs`, `Assets/Defaults/VertexAttributes.glsl`.
- **Shared shader includes:** `PBR.glsl` (BRDF), `Lighting.glsl` (light loop, BVH traversal
  entry points), `Shadow.glsl` (PCF sampling), `LightBVH.glsl` (BVH texel decode/traversal),
  `ShaderVariables.glsl` (global uniform declarations), `VertexAttributes.glsl` (vertex input
  layout + skinning/blendshape application), `ProwlCG.glsl` / `Random.glsl` / `SimplexNoise4D.glsl`
  / `FastNoiseLite.glsl` (shared math/noise utilities used by fog, grass, particles, etc.).

## 5. Post-Processing / Image Effects

All effects derive from `ImageEffect` and declare a `RenderStage` (`AfterOpaques` or
`PostProcess`, see section 1). Each entry: effect class -> shader -> what it does.

- **Auto Exposure** (`Rendering/Image Effects/AutoExposureEffect.cs`, no dedicated shader listed
  separately from bloom/tonemap chain) - eye adaptation: progressive downsample chain measures
  scene luminance, smoothly adapts a persistent 1x1 RT toward it over time (separate up/down
  adapt speeds), clamped to `MinExposure`. Intended to run before Bloom/Tonemapper.
- **Bloom** (`BloomEffect.cs` -> `Bloom.shader`) - dual-filter (downsample+upsample) blur, much
  cheaper than separable Gaussian/Kawase ping-pong at similar quality; configurable threshold,
  intensity, iteration count.
- **Bokeh Depth of Field** (`BokehDepthOfFieldEffect.cs` -> `BokehDoF.shader`) - auto-focus (from
  depth) or manual focus point, configurable blur strength/radius, runs at a selectable
  downsampled resolution (Full/Half/Quarter/Eighth).
- **Cinematic Effects (uber post shader)** (`CinematicEffects.cs` -> `CinematicEffects.shader`) -
  vignette, chromatic aberration (with barrel distortion), and film grain combined into a single
  pass, each independently toggleable via shader keywords to avoid multiple full-screen passes.
- **FXAA** (`FXAAEffect.cs` -> `FXAA.shader`) - fast approximate AA, configurable edge
  threshold min/max and subpixel quality; blits to a temp RT to avoid read/write aliasing.
- **GTAO** (`GTAOEffect.cs` -> `GTAO.shader`) - Ground-Truth Ambient Occlusion (Activision
  "Practical Realtime Strategies for Accurate Indirect Occlusion"), runs at a selectable
  resolution scale (`EffectResolution`: Full/Half/Quarter) shared with SSR.
- **Motion Blur** (`MotionBlurEffect.cs` -> `MotionBlur.shader`) - per-pixel blur along the
  prepass motion-vector buffer, depth-aware weighting to reduce background bleeding through
  foreground edges, configurable sample count and max radius.
- **Screen-Space Reflections** (`ScreenSpaceReflectionEffect.cs` -> `SSR.shader`) - stochastic
  GGX-importance-sampled ray marching against the prepass depth/normals/roughness/metallic, a
  ray-reuse resolve (4-neighbour reuse, BRDF/pdf-weighted, with tonemap-during-resolve firefly
  suppression) that picks a scene-mip by a roughness cone, and optional temporal reprojection
  with one-bounce feedback. Runs at a selectable ray resolution.
- **SMAA** (`SMAAEffect.cs` -> `SMAA.shader` + `SMAA.glsl`) - Subpixel Morphological AA using
  precomputed Area/Search lookup textures loaded raw from embedded resources (not PNG, to avoid
  forced sRGB + vertical flip corrupting the data) (`Rendering/SMAALookupTextures.cs`); spatial
  alternative to FXAA/TAA with no temporal ghosting, must run after tonemapping, don't stack with
  FXAA/TAA.
- **TAA** (`TAAEffect.cs`, resolves against history using motion vectors) - Halton-sequence
  sub-pixel camera jitter applied to the projection matrix each frame (kept separate from the
  non-jittered VP used for motion vectors, see section 2), accumulates/reprojects previous-frame
  color using history + motion-based neighborhood clamping (`MotionScale`) against ghosting.
- **Tonemapper** (`TonemapperEffect.cs` -> `Tonemapper.shader`) - 9 selectable operators (Melon,
  ACES, ACESSimple, AgX, ReinhardSimple, ReinhardLuma, ReinhardWhitePreserving, RomBinDaHouse,
  Uncharted2), plus contrast/saturation controls; marks `TransformsToLDR = true` so the pipeline
  knows HDR ends here.
- **Volumetric Fog** (`VolumetricFogEffect.cs` -> `VolumetricFog.shader`, runs at
  `RenderStage.AfterOpaques`) - ray-marches per-pixel through global density plus per-volume
  density regions, accumulating Henyey-Greenstein-scattered light from `FogLight`-tagged lights
  (with optional shadow-map sampling per march step), global density/color/scattering/extinction
  controls.

## 6. Renderer / Culling Components

- **`MeshRenderer`** - static mesh with one material per submesh (`Materials` list; `Material`
  property is legacy single-material sugar for `Materials[0]`). `Components/MeshRenderer.cs`.
- **`SkinnedMeshRenderer`** - bones referenced by hierarchy path (`Transform.Find`), resolved
  relative to the renderer's GameObject; bone matrices uploaded via float texture; supports
  multiple materials via submeshes. `Components/SkinnedMeshRenderer.cs`.
- **`SkinnedMeshRenderable`** - `IRenderable` adapter that uses precomputed world-space bounds
  derived from bone positions (not the static bind-pose bounds) so frustum culling is correct
  for deformed meshes. `SkinnedMeshRenderable.cs`.
- **`MeshRenderable`** - plain `IRenderable` adapter wrapping a mesh/material/transform/layer,
  used directly by the pipeline for injected draws like the editor grid.
  `MeshRenderable.cs`.
- **`LineRenderer`** - generates a ribbon mesh from a point list (start/end width, loop,
  per-vertex start/end color gradient, stretch/tile UV modes), implements `IRenderable` directly.
  `Components/LineRenderer.cs`.

## 7. Misc Optimizations (cross-cutting)

- Per-frame world-bounds cache shared across the main cull and every shadow cascade cull instead
  of recomputing bounds per frustum (section 1, `EnsureWorldBounds`).
- `bool[]` culled-mask instead of `HashSet<int>` membership tests during batching/sorting.
- Cached static `Comparison<T>` delegates for sort, avoiding a delegate allocation per
  `SortRenderables` call.
- Batch list / index-list-pool / batch-lookup dictionary reused across frames (encode is
  sequential, never re-entrant), avoiding per-pass dictionary/list allocation.
- Single combined CommandBuffer for all per-frame lighting uniform uploads instead of ~80-100
  one-op rent/submit cycles per camera per frame (`SceneLightSystem.UploadGlobalUniforms`).
- Dirty-row-range tracking (not full re-upload) for both the BVH light-data texture and the BVH
  node texture, and for shadow-slot uniform clearing (only clears slots that held data last
  frame). `Rendering/LightBVH.cs`, `Rendering/LightBVHTextures.cs`,
  `Rendering/SceneLightSystem.cs::UploadLocalShadowSlots`.
- `CollectionsMarshal.AsSpan`/`GetValueRefOrAddDefault` used where indexers or dictionary lookups
  would otherwise copy value-type structs or double-hash (see comment in
  `Rendering/LightmapBinding.cs`).
- Precomputed inverse camera matrices uploaded once per camera instead of per-fragment
  `inverse()` calls (section 2).
- BVH leaf refit-in-place (loose AABB) vs. full tree rebuild, so a light moving a small amount
  only touches one leaf node instead of retriggering the O(N log N) rebuild (section 3).
- Shadow atlas Guillotine packing (Best-Short-Side-Fit) to fit variable-resolution shadow tiles
  (cascades, point-light faces, spot lights) into one shared texture instead of one RT per light.
- Hardware `sampler2DShadow` + PCF instead of manual multi-tap compare-and-average, for both
  performance and quality (section 3).

## Notes for the rebuild

- The directional light is deliberately kept out of both BVHs and uploaded through a dedicated
  uniform path (there's only ever one) - worth deciding early whether the new architecture keeps
  that split or unifies it.
- Per-camera light layer filtering was never implemented in the old system (`SceneLightSystem`
  takes a `cullingMask` parameter but ignores it - see the doc comment on `Reconcile`); the BVH
  is shared across all cameras viewing a scene. If per-camera light layers matter for the new
  pipeline, this needs actual design, not just porting.
- `RenderStage` only has two injection points (`AfterOpaques`, `PostProcess`). Effects that
  needed something in between (e.g. before transparents) didn't have a slot; consider whether
  the new render-graph design should generalize this.
