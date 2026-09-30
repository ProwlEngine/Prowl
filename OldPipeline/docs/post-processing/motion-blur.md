# Motion Blur

Source: [`MotionBlurEffect.cs`](../../Prowl.Runtime/Rendering/Image%20Effects/MotionBlurEffect.cs),
[`MotionBlur.shader`](../../Prowl.Runtime/Assets/Defaults/MotionBlur.shader). Stage: `PostProcess`.

Settings: `Intensity` 1 (1 = physically matched), `Samples` 8 (clamped 1-32), `MaxBlurRadius` 40 px.

Algorithm (single pass):

1. Read the prepass motion vector (UV space), scale by `Intensity`, clamp its length to `MaxBlurRadius` pixels.
2. Motion under half a pixel: output the center color.
3. Per-pixel IGN dither in `[-0.5, 0.5]`; `Samples` taps at `t = (i + dither) / N - 0.5` along the vector (centered),
   UV clamped to the screen.
4. Depth-aware weight `1 / (1 + |linearDepth(sample) - linearDepth(center)| * 10)` to reduce background bleeding into
   foreground.

Camera and object motion both come from the unified prepass; instanced geometry contributes camera motion only.

## Rebuild notes

- No tile-max / neighbour-max velocity dilation, so blur does not extend past object silhouettes.
