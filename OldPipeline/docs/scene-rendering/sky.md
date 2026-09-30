# Sky

Source: `RenderSkybox` and the clear-flag switch in
[`Rendering/DefaultRenderPipeline.cs`](../../Prowl.Runtime/Rendering/DefaultRenderPipeline.cs),
`Scene.SkyboxParams` in [`Resources/Scene.cs`](../../Prowl.Runtime/Resources/Scene.cs),
[`ProceduralSkybox.shader`](../../Prowl.Runtime/Assets/Defaults/ProceduralSkybox.shader),
[`GradientSkybox.shader`](../../Prowl.Runtime/Assets/Defaults/GradientSkybox.shader),
[`CubemapSkybox.shader`](../../Prowl.Runtime/Assets/Defaults/CubemapSkybox.shader),
[`SkyDome.obj`](../../Prowl.Runtime/Assets/Defaults/SkyDome.obj).

## When the sky draws

Only for `CameraClearFlags.Skybox`, inside the `ColorPass` CB, after the prepass depth copy and before opaques:
1. Clear color to `Scene.Skybox.SolidColor` if the sky mode is `SolidColor`, else to `camera.ClearColor`.
2. `RenderSkybox` draws `SkyDome.obj` (imported once from embedded resources with the model importer) with the mode's
   material via `cmd.DrawMesh`.

The sky is not written into the prepass (depth 1, zero motion) and is not fogged.

## Modes (`Scene.Skybox`)

| Mode | Material | Inputs |
|---|---|---|
| `Procedural` (default) | `Skybox/Procedural` | `_SunDir` = first directional light's `GetLightDirection()` (fallback `normalize(0.5, -0.7, 0.5)`) |
| `SolidColor` | none | clear color only |
| `Gradient` | `Skybox/Gradient` | `_TopColor`, `_BottomColor`, `_Exponent` from `GradientTop/Bottom/Exponent` |
| `Material` | `CustomMaterial` or procedural fallback | anything, e.g. `Skybox/Cubemap` |

Defaults: `SolidColor (0.2, 0.3, 0.5)`, `GradientTop (0.4, 0.6, 0.9)`, `GradientBottom (0.8, 0.8, 0.7)`, exponent 1.

## Procedural atmosphere

Vertex shader (per dome vertex, not per pixel): strip translation from the view matrix, project with
`prowlSkyProjection()` (a 60-degree perspective substitute under orthographic cameras), force `z = w` (far plane).
Single-scattering Rayleigh + Mie atmosphere ray march (the widely used "glsl-atmosphere" formulation): 16 primary and
8 secondary steps, planet radius 6371 km, atmosphere 6471 km, observer at 6372 km, sun intensity 22,
Rayleigh `(5.5, 13.0, 22.4) e-6`, Mie `21e-6`, scale heights 8 km / 1.2 km, Mie g 0.758.
Fragment: interpolated sky color + a hard sun disc (`smoothstep(0.996, 0.9965, dot(dir, sun))`) + exposure
`1 - exp(-color)`.

## Gradient and cubemap

Gradient: `t = pow(saturate(dir.y * 0.5 + 0.5), exponent)`, `mix(bottom, top, t)`. Cubemap: six 2D face textures
(`_CubeRight/Left/Top/Bottom/Front/Back`), `_Tint`, `_Exposure`; face chosen from the direction. Both render the dome
with `Cull Front`, `ZTest LEqual`, `ZWrite Off`.

## Rebuild notes

- The procedural sky is evaluated per vertex, so dome tessellation limits quality; the sun is always the first
  directional light.
- No sky cubemap is generated, so the sky does not light the scene (ambient is a separate constant).
