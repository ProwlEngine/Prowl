// Outputs of the built in pass types. The forward opaque pass writes the thin G-buffer next to its colour: screen
// motion, the world normal, octahedral in a signed two channel target, and the top layer's specular reflectance and
// perceptual roughness. Transmissive passes write motion and the reactive mask, how much of the pixel is what they
// refract from behind, which temporal effects trust history less for. A view has only the targets its features read,
// so writes to the others go nowhere.
#pragma once

#include "Graphite/Core.glsl"
#include "Graphite/Octahedral.glsl"

#ifdef GRAPHITE_FRAGMENT
layout(location = 1) out vec2 graphite_GBufferMotion;
layout(location = 2) out float graphite_Reactive;
layout(location = 3) out vec2 graphite_GBufferNormal;
layout(location = 4) out vec4 graphite_GBufferMaterial;
layout(location = 24) in vec4 graphite_MotionCurrent;
layout(location = 25) in vec4 graphite_MotionPrevious;
#endif

#ifdef GRAPHITE_FRAGMENT
// How far the surface moved across the screen since last frame, in uv, without the camera's jitter: last frame's view
// projection is kept unjittered, so only this frame's comes off. A point that was behind the camera last frame reports none.
void Graphite_WriteMotion(vec4 currentClip, vec4 previousClip)
{
    if (currentClip.w <= 1e-6 || previousClip.w <= 1e-6)
    {
        graphite_GBufferMotion = vec2(0.0);
        return;
    }
    Graphite_ViewData view = Graphite_View();
    vec2 current = currentClip.xy / currentClip.w - view.jitter.xy;
    vec2 previous = previousClip.xy / previousClip.w;
    graphite_GBufferMotion = (current - previous) * vec2(0.5, -0.5);
}

// reactive is how much of the pixel shows what the surface refracts from behind it, 0 to 1
void Graphite_WriteReactive(float reactive) { graphite_Reactive = clamp(reactive, 0.0, 1.0); }

// reflectance is the directional albedo of the top specular lobe, multiple scattering compensated, which screen space
// reflections weight their trace by in place of the probes. roughness is that lobe's perceptual roughness.
void Graphite_WriteGBuffer(vec3 worldNormal, vec3 reflectance, float roughness)
{
    graphite_GBufferNormal = Graphite_OctEncode(normalize(worldNormal));
    graphite_GBufferMaterial = vec4(reflectance, roughness);
    Graphite_WriteMotion(graphite_MotionCurrent, graphite_MotionPrevious);
}
#endif
