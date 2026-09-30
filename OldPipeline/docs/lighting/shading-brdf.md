# Shading Model (BRDF)

Source: [`Assets/Defaults/PBR.glsl`](../../Prowl.Runtime/Assets/Defaults/PBR.glsl),
[`Assets/Defaults/Lighting.glsl`](../../Prowl.Runtime/Assets/Defaults/Lighting.glsl),
[`Rendering/BRDFLutGenerator.cs`](../../Prowl.Runtime/Rendering/BRDFLutGenerator.cs),
[`BRDFGen/Program.cs`](../../BRDFGen/Program.cs).

## Isotropic BRDF (metallic/roughness)

| Term | Function | Formula |
|---|---|---|
| NDF | `DistributionGGX(N, H, r)` | `a2 = r^2` (perceptual squared, NOT squared again), `a2 / (pi * (NdotH^2 (a2 - 1) + 1)^2)`; `r` floored to 0.045 |
| Geometry | `GeometrySmith` = `G1(NdotV) * G1(NdotL)` | Schlick-GGX, `k = (r + 1)^2 / 8` (direct-light remap); uses `abs(NdotV)` |
| Fresnel | `FresnelSchlick(LdotH, F0)` | `F0 + (1 - F0) * exp2(-9.28 * LdotH)` (spherical-gaussian approx) |
| Fresnel (indirect) | `FresnelSchlickRoughness` | `F90 = max(1 - r, F0)` (Lazarov) |
| Diffuse | `DisneyDiffuse(NdotV, NdotL, LdotH, r)` | Burley 2012, `fd90 = 0.5 + 2 LdotH^2 r`, exp2 Schlick, divided by pi |

Per light: `kS = F`, `kD = (1 - F)(1 - metallic)`, `specular = NDF G F / (4 NdotV NdotL + 1e-4)`,
`result = (kD * albedo * diffuse + specular) * radiance * NdotL * shadowFactor * ao`. `F0 = mix(0.04, albedo, metallic)`.

Note that the GGX `a2` uses `roughness^2` where `roughness` is already the perceptual value, i.e. alpha = r, a2 = r^2.
The comment in the file says "alpha = perceptualRoughness^2, a2 = alpha", which is not what the code does.

## Anisotropic BRDF (Far Cry 4 / GGX aniso)

`roughnessT = r (1 + aniso)`, `roughnessB = r (1 - aniso)`, `mt = roughnessT^2`, `mb = roughnessB^2` (floored to
`0.045^2`). `DistributionGGXAniso(TdotH, BdotH, NdotH, mt, mb)`, `GeometrySmithAniso` (Smith joint visibility),
`specularTerm = V * D * pi * NdotL`, diffuse `DisneyDiffuse * NdotL`. Tangent/bitangent from the mesh tangent frame,
optionally rotated by `_AnisoDirectionMap`.

## Specular anti-aliasing

`ApplySpecularAA(r, N)` (Kaplanyan-Hill normal-variance filtering): `variance = 0.5 * (|ddx N|^2 + |ddy N|^2)`,
`kernel = min(2 * variance, 0.1)`, `r' = sqrt(clamp(r^2 + kernel))`. Applied at the start of every
`CalculateForwardLighting*` entry point.

## Translucency (`CalculateTranslucency`)

Two modes selected by `scatteringPower`:
- `== 0`: wrapped diffuse (wrap 0.5) on `-N` times a GGX-shaped back-scatter lobe (alpha 0.7) - foliage, paper.
- `> 0`: spherical-gaussian (Barre-Brisebois GDC 2011): `L + N * distortion`, `exp2(saturate(dot) * p - p)` - skin, wax.

Result is multiplied by `scale`, the light color * intensity * attenuation (no x8 here), albedo and shadow factor.
Used by Standard (translucency map) and Grass (`_Translucency`).

## Local light evaluation (`EvaluateLocalLight*`)

1. Spot early-out: `dot(spotDir, -L) <= cos(outer)` returns 0 before any BRDF work.
2. Back-face early-out: `NdotL <= 0` returns 0 (skips shadow sampling too).
3. Attenuation: `1 / max(d^2, 0.01) * saturate(1 - (d^2/r^2)^2)^2` (inverse square with a smooth window reaching 0 at
   `Range`; `Range` is the cutoff, `Intensity` is brightness). `1/r^2` is guarded with `max(r^2, 1e-6)`.
4. Spot cone: `smoothstep(cos(outer), cos(inner), axisCos)`.
5. Skip if attenuation <= 1e-4.
6. Radiance `color * intensity * 8 * attenuation`.
7. Shadow if the leaf is shadowed and has a slot: point -> `SamplePointShadow`, spot -> `SampleSpotShadow`.

The translucent variant samples shadows when either `NdotL > 0` or translucency > 0, and adds translucency regardless
of `NdotL`. The range check is not repeated because the BVH leaf sphere test already rejected fragments out of range.

## Directional evaluation

`EvaluateDirectional`: `L = normalize(_DirectionalLightDirection)`, same BRDF, radiance `color * intensity * 8`,
shadow from cascades if enabled. Not range limited.

## Entry points

- `CalculateForwardLighting(pos, N, V, albedo, metallic, roughness, ao)` - directional + static tree + dynamic tree.
- `CalculateForwardLighting(..., translucency, scatterPower, scatterDist, scatterScale)` - single pass PBR +
  translucency, shared attenuation and shadows.
- `CalculateForwardLightingAniso(..., T, B, ..., anisotropy, ao)`.

## Parallax occlusion mapping

`ParallaxOcclusionMapping(heightTex, uv, viewDirTS, scale, steps)`: linear march from height 1 down with slope damping
at grazing angles, `textureGrad` with precomputed derivatives, then 3 secant refinements (with a 0/0 guard). Height from
the G channel.

## BRDF LUT

`BRDFLutGenerator.GetLut()` loads `Assets/brdf_lut.brdf` (raw RGBA8 256x256: R = scale, G = bias of the split-sum
Fresnel integral, indexed by NdotV x roughness), linear filtered, clamp to edge, and `UploadGlobal()` binds it as
`_BRDFLut` every render (`ValidateDefaults`). The generator is the standalone `BRDFGen` console tool (Hammersley +
GGX importance sampling, 1024 samples); the old in-engine generator is kept commented out. **No default shader samples
`_BRDFLut`.** Ambient specular uses `FresnelSchlickRoughness` instead.

## Rebuild notes

- The x8 intensity multiplier is a compatibility constant; changing it rescales every scene.
- The GGX alpha convention (alpha = perceptual roughness) differs from the common alpha = r^2 convention and from its
  own comment; decide deliberately.
- Energy conservation is approximate (no multi-scatter compensation).
