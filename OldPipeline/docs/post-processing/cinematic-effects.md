# Cinematic Effects (Uber Shader)

Source: [`CinematicEffects.cs`](../../Prowl.Runtime/Rendering/Image%20Effects/CinematicEffects.cs),
[`CinematicEffects.shader`](../../Prowl.Runtime/Assets/Defaults/CinematicEffects.shader),
editor [`CinematicEffectsEditor.cs`](../../Prowl.Editor/GUI/CustomEditors/ImageEffectEditors/CinematicEffectsEditor.cs).
Stage: `PostProcess`. One full-screen pass; each sub-effect is a keyword so disabled ones cost nothing.

| Sub-effect | Keyword | Settings (defaults) | Implementation |
|---|---|---|---|
| Pixelation | `PIXELATION` | `PixelSize` 4 | snap UV to a pixel grid first |
| Chromatic aberration | `CHROMATIC_ABERRATION` | `ChromaticIntensity` 3 px, `ChromaticDistortion` 0.5 | barrel distortion per channel, red out, blue in |
| Sharpen | `SHARPEN` | `SharpenAmount` 0.5, `SharpenRadius` 1 | AMD FidelityFX CAS (contrast adaptive sharpening) |
| Edge detection | `EDGE_DETECTION` | `EdgeIntensity` 1, `EdgeColor` black, `EdgeBackgroundFade` 1 | Sobel on luminance |
| Color grading | `COLOR_GRADING` | `PostExposure` 0 EV, `Contrast` 0, `Saturation` 0, `Temperature` 0, `Lift`/`Gamma`/`Gain` black | exposure, contrast around 0.18, approximate Planckian temperature shift, saturation, ASC-CDL-style lift/gamma/gain |
| LUT | `LUT` | `LUTTexture` (strip, e.g. 256x16), `LUTContribution` 1 | 2D strip LUT, blue selects slice pair, half-texel offsets; size = texture height |
| God rays | `GOD_RAYS` | `GodRayIntensity` 0.5, `Decay` 0.96, `Density` 0.5, `Weight` 0.6, `Samples` 64 (8-128), `Threshold` 0.8 | screen-space radial march toward the projected sun; only sky pixels (depth ~1) brighter than threshold contribute; warm tint |
| Film grain | `FILM_GRAIN` | `GrainIntensity` 0.15, `GrainResponse` 0.5 | hash noise, luminance-weighted |
| Vignette | `VIGNETTE` | `VignetteIntensity` 0.4, `Smoothness` 0.2, `Roundness` 1 | radial falloff |

Order inside the shader: pixelation (UV) -> sample (with chromatic aberration) -> sharpen -> edge detection -> color
grading -> LUT -> god rays -> film grain -> vignette.

God rays: sun direction comes from the first enabled `DirectionalLight` found by scanning `Scene.Current.ActiveObjects`
(fallback `normalize(0.5, -0.7, 0.5)`), placed at `camPos - sunDir * 10000` and projected with
`camera.ViewMatrix * camera.ProjectionMatrix` (note the multiplication order is reversed relative to the rest of the
engine's `P * V`).

## Rebuild notes

- Color grading here runs wherever the effect sits in the list; place it after the tonemapper for LDR grading.
- God rays duplicate the sun lookup; volumetric fog already provides physically based shafts.
