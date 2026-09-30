# Depth of Field (Circular Bokeh)

Source: [`BokehDepthOfFieldEffect.cs`](../../Prowl.Runtime/Rendering/Image%20Effects/BokehDepthOfFieldEffect.cs),
[`BokehDoF.shader`](../../Prowl.Runtime/Assets/Defaults/BokehDoF.shader). Stage: `PostProcess`.

Separable circular (complex-valued) bokeh convolution: two complex Gaussian kernel components approximate a disc, so a
horizontal pass followed by a vertical pass produces a round bokeh with only 2 x 17 taps per channel.

## Settings

| Field | Default |
|---|---|
| `UseAutoFocus` | true (`AUTOFOCUS` keyword: focus = linear depth at screen center) |
| `ManualFocusPoint` | 0.5 (linear view depth) |
| `FocusStrength` | 1 |
| `MaxBlurRadius` | 2 (percent of screen height) |
| `Resolution` | Half (Full / Half / Quarter / Eighth) |

## Passes

1. `CircularHorizMRT` (blur resolution): 17 taps along X with spacing `CoC / width / 8`; for each of R, G, B writes a
   vec4 of two complex responses (kernel 0 and kernel 1) into three `Short4` MRT targets (values can be negative).
2. `CircularVerticalComposite`: 17 taps along Y on the three MRT inputs, complex multiply with the kernel, recombine
   the two kernels with `FinalWeights_Kernel0 (0.411, -0.549)` and `Kernel1 (0.513, 4.561)`.
3. `DoFCombine` (full res): `mix(original, blurred, smoothstep(0.5, maxBlurPx * 0.5, CoC))`.

Circle of confusion: `abs(depth - focus) / focus * FocusStrength * 0.01 * height`, clamped to
`MaxBlurRadius * 0.01 * height` pixels. Depth is linearized with `linearizeDepthFromProjection` (settings were retuned
when the CoC moved from raw depth to linear depth).

## Rebuild notes

- Single CoC per pixel (no near/far layer separation), so foreground bokeh does not bleed over in-focus background.
- `_CameraDepthTexture` is read from the pipeline global, not bound by the effect.
