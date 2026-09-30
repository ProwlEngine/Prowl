# Ambient and Distance Fog

Source: `Scene.FogParams`, `Scene.AmbientLightParams` in [`Resources/Scene.cs`](../../Prowl.Runtime/Resources/Scene.cs),
`UploadFogUniforms` / `UploadAmbientUniforms` in [`Rendering/DefaultRenderPipeline.cs`](../../Prowl.Runtime/Rendering/DefaultRenderPipeline.cs),
`CalculateAmbient` / `ApplyFog` in [`Assets/Defaults/Lighting.glsl`](../../Prowl.Runtime/Assets/Defaults/Lighting.glsl).
Edited in the editor Environment panel ([editor overlays](../scene-rendering/editor-overlays.md)).

## Ambient

`Scene.Ambient`:

| Field | Default |
|---|---|
| `Mode` | `Uniform` (or `Hemisphere`) |
| `Strength` | 1 |
| `Color` | (0.43, 0.55, 0.65) |
| `SkyColor` / `GroundColor` | (0.3, 0.3, 0.4) / (0.2, 0.2, 0.2) |

Uploaded as `_AmbientMode` (vec2 one-hot: uniform, hemisphere), `_AmbientColor`, `_AmbientSkyColor`,
`_AmbientGroundColor`, `_AmbientStrength`.

`CalculateAmbient(N) = _AmbientColor * mode.x + mix(ground, sky, N.y * 0.5 + 0.5) * mode.y`. Callers multiply by
`_AmbientStrength` (Standard through `CalculateGI` mode 0, Terrain and Grass directly). It is used as both the diffuse
and the specular ambient source (see [Standard family](../shaders/standard-shader-family.md)).

## Distance fog

`Scene.Fog`:

| Field | Default |
|---|---|
| `Mode` | `ExponentialSquared` (`Off`, `Linear`, `Exponential`, `ExponentialSquared`) |
| `Color` | (0.5, 0.5, 0.5) |
| `Start` / `End` | 20 / 100 |
| `Density` | 0.01 |

Uploaded each camera render:
- `_FogColor`
- `_FogParams = (density / 1.2011224, density / ln 2, -1 / (end - start), end / (end - start))` (range guarded)
- `_FogStates` = one-hot `(linear, exp, exp2)`; all zero means off.

`ApplyFog(color, worldPos)`: distance to camera `d`;
`f = (d * p.z + p.w) * linear + exp2(-d * p.y) * exp + exp2(-(d * p.x)^2) * exp2`; returns
`mix(_FogColor, color, saturate(f))`. The constants turn `exp2` into `exp(-density d)` and
`exp(-(density d)^2)` (Unity-style fog factors).

Applied by Standard/Unlit forward, Terrain, Grass, Sprite. Not applied by particles, UI, or text meshes.
`Line.shader` calls a stale `ApplyFog(float, vec3)` overload that does not exist and does not include `Lighting`
(see [known issues](../reference/known-issues.md)).
Independent from [volumetric fog](../post-processing/volumetric-fog.md).

## Rebuild notes

- Fog is per-fragment in every lit shader rather than a post pass, so transparents fog correctly.
- There is no sky/fog interaction: the skybox is not fogged.
