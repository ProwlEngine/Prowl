# Known Issues, Quirks and Dead Code

Found while documenting the snapshot. None of these were fixed; they are here so the rebuild does not port them by
accident. Severity is a judgment call: **bug** (wrong output), **dead** (unused code/data), **quirk** (works but
surprising or fragile).

## Lighting and shadows

| # | Kind | Issue | Where |
|---|---|---|---|
| L1 | dead | `FogLight` component is never read; volumetric fog scatters from all BVH lights by type toggle. Its fields (intensity multiplier, color override, fog shadows, scattering bias) do nothing. | [`FogLight.cs`](../../Prowl.Runtime/Components/Lights/FogLight.cs), [`VolumetricFogEffect.cs`](../../Prowl.Runtime/Rendering/Image%20Effects/VolumetricFogEffect.cs) |
| L2 | dead | `_BRDFLut` is loaded and bound every render but no shader samples it. | [`BRDFLutGenerator.cs`](../../Prowl.Runtime/Rendering/BRDFLutGenerator.cs) |
| L3 | dead | `Light.ShadowSlot` is never written (always -1); the real slot lives in BVH slot data. | [`Light.cs`](../../Prowl.Runtime/Components/Lights/Light.cs) |
| L4 | quirk | Camera `CullingMask` does not filter lights (argument ignored in `Reconcile`). | [`SceneLightSystem.cs`](../../Prowl.Runtime/Rendering/SceneLightSystem.cs) |
| L5 | bug | Static lights are never updated after registration; editing a static light's color/intensity/range has no effect until it is re-added. | `SceneLightSystem.Reconcile` |
| L6 | bug | The shadow atlas allocator is reset once per window frame (`Game.cs`), not per camera. The editor renders several cameras per frame (game view, scene view, previews), each reserving fresh tiles; the atlas can run out and lights silently lose shadows. | [`ShadowAtlas.cs`](../../Prowl.Runtime/Rendering/ShadowAtlas.cs) |
| L7 | quirk | Atlas free rectangles are never merged; `lightID` parameter unused. | `ShadowAtlas.ReserveTiles` |
| L8 | quirk | Only the first directional light in collection order is used (shading, shadows, sky sun, god rays). | `Reconcile`, `RenderSkybox`, `CinematicEffects.GetSunDirection` |
| L9 | quirk | Directional cascade depth is a fixed `+-distance/2` slab; tall occluders get clipped. No cascade blending or distance fade. | `DirectionalLight.GetShadowMatrix`, `Lighting.glsl` |
| L10 | quirk | Closest-4 shadow caster selection has no hysteresis (shadows pop when ranks swap). | `Reconcile` |
| L11 | quirk | `PBR.glsl` comment says alpha = perceptualRoughness^2, but the code uses alpha = roughness. | `DistributionGGX` |

## Pipeline / batching

| # | Kind | Issue | Where |
|---|---|---|---|
| P1 | bug | Motion history (`prowl_PrevObjectToWorld`) is keyed only by object id on the shared pipeline instance; with multiple cameras per frame later cameras see zero object motion. | `RenderPipeline.TrackModelMatrix` |
| P2 | bug | Motion vectors ignore skinning/morphs (prepass current position is unskinned) and instancing (no per-instance previous matrix). TAA/motion blur ghost on characters, particles, grass. | `StandardCore.glsl`, `DrawInstancedRenderablePass` |
| P3 | quirk | Mesh-derived keywords (`HAS_*`, `SKINNED`, `BLENDSHAPES`, `GPU_INSTANCING`) are written onto the shared material every batch. | `DrawRenderables` |
| P4 | quirk | Opaques are drawn in batch order, not front-to-back (only transparents are distance sorted). | `Internal_Render` |
| P5 | quirk | `PropertyState.ClearGlobals()` after every camera wipes any globals set outside the pipeline. | `DefaultRenderPipeline.Render` |
| P6 | quirk | Transparent Standard/Unlit variants have no prepass and no shadow caster pass. | `Standard*Transparent*.shader` |
| P7 | quirk | Grab texture is copied per batch, not per frame. | `DrawRenderables` |
| P8 | dead | `ShaderPass` stores the `Fallback` asset name but nothing uses it. | [`ShaderPass.cs`](../../Prowl.Runtime/Rendering/Shaders/ShaderPass.cs) |
| P9 | quirk | Unlit prepass writes roughness 0 / metallic 0 while its comment calls it "perfectly rough". | `StandardCore.glsl` |
| P10 | quirk | `Camera.Viewrect` is not implemented (commented out). | [`Camera.cs`](../../Prowl.Runtime/Components/Camera.cs) |
| P11 | bug | Camera gizmo uses `float aspect = 1280 / 720` (integer division = 1). | `Camera.DrawGizmos` |

## Post-processing

| # | Kind | Issue | Where |
|---|---|---|---|
| E1 | quirk | GTAO multiplies the entire scene color (direct light and emission included), not just ambient. `ApproxMultiBounce` is defined but unused. | [`GTAO.shader`](../../Prowl.Runtime/Assets/Defaults/GTAO.shader) |
| E2 | quirk | SSR adds reflections on top of color that already contains the ambient specular approximation (double specular). Albedo is guessed from the tonemapped scene color. | [`SSR.shader`](../../Prowl.Runtime/Assets/Defaults/SSR.shader) |
| E3 | bug | Volumetric fog volumes and cinematic god-ray sun are found via `Scene.Current`, not the camera's scene (wrong in previews / multi-scene). | `VolumetricFogEffect.UploadFogVolumes`, `CinematicEffects.GetSunDirection` |
| E4 | bug (suspected) | God-ray sun projection multiplies `camera.ViewMatrix * camera.ProjectionMatrix`, the reverse of `P * V` used everywhere else. | `CinematicEffects.OnRenderEffect` |
| E5 | dead | TAA computes `unjitteredUV` and never uses it; no current-sample unjitter. | [`TAA.shader`](../../Prowl.Runtime/Assets/Defaults/TAA.shader) |
| E6 | quirk | Effect order is entirely user-defined; nothing enforces exposure -> bloom -> tonemap -> AA. `TransformsToLDR` is not read. | framework |
| E7 | quirk | No gamma/display transform unless a tonemapper is present. | `Tonemapper.shader` |
| E8 | quirk | Gizmos are drawn into scene color after PostProcess but before the final blit. | `Internal_Render` |

## Content

| # | Kind | Issue | Where |
|---|---|---|---|
| C1 | bug | `Line.shader` is a deferred-era leftover: writes 4 MRT outputs, calls `ApplyFog(float, vec3)` which does not exist, and does not include `Lighting`. It cannot compile against the current includes, so lines render with the `Hidden/Invalid` fallback. | [`Line.shader`](../../Prowl.Runtime/Assets/Defaults/Line.shader) |
| C2 | quirk | `LineRenderer` rebuilds its mesh in `GetRenderingData`, i.e. per pass and per viewer. | [`LineRenderer.cs`](../../Prowl.Runtime/Components/LineRenderer.cs) |
| C3 | dead | `Particle.shader` declares `_SoftParticlesFactor`; soft particles are not implemented. | [`Particle.shader`](../../Prowl.Runtime/Assets/Defaults/Particle.shader) |
| C4 | quirk | `SpriteRenderer.SortingOrder` is emulated with a Z offset on the world matrix. | [`SpriteRenderer.cs`](../../Prowl.Runtime/Components/SpriteRenderer.cs) |
| C5 | quirk | Terrain quadtree LOD has no skirts or edge morphing (cracks between LOD levels). Terrain and grass ignore lightmaps/probes. | [`Terrain.shader`](../../Prowl.Runtime/Assets/Defaults/Terrain.shader) |
| C6 | quirk | `SkinnedMeshRenderer` sets `_MainColor` per draw, overriding the material tint; allocates a `PropertyState` per submesh per frame. | [`SkinnedMeshRenderer.cs`](../../Prowl.Runtime/Components/SkinnedMeshRenderer.cs) |
| C7 | quirk | Light-probe lookup scans all tetrahedra on a miss; single `_lastTet` cache shared by every renderer. | [`LightProbeVolume.cs`](../../Prowl.Runtime/Rendering/LightProbeVolume.cs) |

## Diagnostics

| # | Kind | Issue | Where |
|---|---|---|---|
| D1 | bug | `RenderStats.ColorPassMs` actually measures AfterOpaques effects + transparents + world UI (shared section timer); opaque and prepass encode time is uncounted. | [`RenderStats.cs`](../../Prowl.Runtime/Rendering/RenderStats.cs) |
| D2 | quirk | `RenderStats.BeginFrame/EndFrame` are called only by the editor Game View. | `GameViewPanel` (core) |
