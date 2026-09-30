# Light Components

Source: [`Components/Lights/`](../../Prowl.Runtime/Components/Lights/) -
[`Light.cs`](../../Prowl.Runtime/Components/Lights/Light.cs),
[`DirectionalLight.cs`](../../Prowl.Runtime/Components/Lights/DirectionalLight.cs),
[`PointLight.cs`](../../Prowl.Runtime/Components/Lights/PointLight.cs),
[`SpotLight.cs`](../../Prowl.Runtime/Components/Lights/SpotLight.cs),
[`FogLight.cs`](../../Prowl.Runtime/Components/Lights/FogLight.cs).
Particle lights: [`ParticleSystem/Modules/LightModule.cs`](../../Prowl.Runtime/Components/ParticleSystem/Modules/LightModule.cs).

## `Light` (abstract, `IRenderableLight`)

| Field | Default | Notes |
|---|---|---|
| `Color` | white | linear |
| `Intensity` | 1 | multiplied by 8 in the shader |
| `ShadowStrength` | 1 | multiplies the occluded fraction |
| `ShadowBias` | 0.001 | base for slope-scaled bias |
| `ShadowNormalBias` | 0 | world-space offset along the normal before projection |
| `CastShadows` | true | |
| `ShadowQuality` | Soft | `Hard` = single hardware PCF tap, `Soft` = 8-tap rotated Poisson |
| `BakeMode` | Realtime | `Realtime`, `Mixed` (realtime direct, baked indirect), `Baked` (excluded from realtime) |
| `ShadowSlot` | -1 | runtime, internal set; directional uses cascades instead |

`OnRenderCollect` adds itself to the lights list. `GetLightID() = InstanceID`, `GetLayer() = LayerIndex`,
position/direction from the transform, `DoCastShadows() = CastShadows`. Subclasses implement `GetLightType`,
`RenderShadows(pipeline, shadowFocus, renderables)` and `GetForwardLightData()`.

**Direction convention:** a directional light's `Transform.Forward` points from the surface toward the sun and is used
as-is as the `L` vector in shading; the shadow camera and the gizmo arrow use `-Forward`.

## `DirectionalLight`

| Field | Default | Values |
|---|---|---|
| `ShadowResolution` | 2048 | 512, 1024, 2048, 4096 |
| `Cascades` | Two | One, Two, Four |
| `ShadowDistance` | 70 | world units covered by all cascades |

`RenderShadows`: linear splits `distance_i = ShadowDistance / n * (i + 1)`; resolution `res / (i + 1)` for cascade
`i > 0`; reserve a square atlas tile per cascade; build the matrix with `GetShadowMatrix`; cull with the cascade
frustum; `AssignCameraMatrices(view, proj)`; one CB per cascade drawing `LightMode=ShadowCaster`. Stores
`proj * view` and `atlasParams = (x, y, res, cascadeDistance)`; a failed reservation stores `(-1, -1, 0, dist)`.

`GetShadowMatrix(focus, res, cascadeDistance)`: orthographic `cascadeDistance x cascadeDistance` with depth range
`+-cascadeDistance / 2` (a fixed slab around the focus, not fit to casters, so occluders further than half a cascade
toward the light are clipped). The focus point is projected onto the light basis and **X/Y are snapped to the texel
grid while Z is kept**, which removes shimmering when the camera moves (verified by `DirectionalLightShadowMatrixTests`).

## `PointLight`

`ShadowResolution` 256 (256-2048), `Range` 10. Shadows reserve a `3res x 2res` atlas region laid out as
`[+X][-X][+Y] / [-Y][+Z][-Z]`, render 6 faces with a 90-degree perspective (`near 0.1`, `far = Range`) and fixed
up-vectors, one CB each. Stores 6 `proj * view` matrices and `faceParams = (x, y, res, Range)`.
Gizmo: wire sphere of `Range` when selected.

## `SpotLight`

`ShadowResolution` 512, `Range` 8, `SpotAngle` 45 (outer, degrees), `InnerSpotAngle` 30. Shadow projection is a
perspective with FOV `SpotAngle * 2` (treats `SpotAngle` as a half-angle), `near 0.1`, `far Range`, one tile, one CB.
`atlasParams = (x, y, res, 1)`; `ShadowEnabled = CastShadows && atlasParams.z > 0`.
Note the shading cone uses `cos(SpotAngle)` too, consistent with the half-angle interpretation, while the gizmo draws
`tan(SpotAngle) * Range` as the cone radius.

## `FogLight`

`[RequireComponent(typeof(Light))]` marker with `IntensityMultiplier`, `CastFogShadows`, `UseOverrideColor`,
`OverrideColor`, `ScatteringBias`. **Nothing reads it.** The volumetric fog effect was refactored to scatter from every
BVH light of an enabled type; its own comments say the per-light overrides were removed. See
[volumetric fog](../post-processing/volumetric-fog.md) and [known issues](../reference/known-issues.md).

## Particle lights

`LightModule` on a particle system emits one point light per alive particle through pooled `ParticleLightProxy`
objects (one per particle index, so the dynamic BVH refits the same leaf). Color from particle color (optional) times
module color, intensity optionally fading with normalized lifetime, range optionally scaled by `Size / StartSize`.
Particle lights never cast shadows. See [particles](../scene-rendering/particles.md).

## `ForwardLightData` per type

| Field | Directional | Point | Spot |
|---|---|---|---|
| Range | 0 | `Range` | `Range` |
| Spot angles | 0 | 0 | outer, inner (deg) |
| ShadowEnabled | cast && cascades > 0 | cast && faces rendered | cast && tile reserved |
| Shadow data | 4 cascade matrices + params, count | 6 face matrices + params | 1 matrix + params |

## Rebuild notes

- Shadow rendering is owned by the light component, which calls back into the pipeline (`CullRenderables`,
  `AssignCameraMatrices`, `DrawRenderables`). A render graph should own shadow passes and treat lights as data.
- `Light.ShadowSlot` is declared (and documented as owned by `SceneLightSystem`) but never written; it stays -1. The
  slot shaders actually use lives in the BVH slot data (`LightBVH.SetShadowSlot`).
