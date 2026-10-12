// Outputs of the built in pass types. The forward opaque pass writes the thin G-buffer next to its colour: the world
// normal, octahedral in a signed two channel target, roughness and metallic, and screen motion. Screen space effects
// and TAA read it.
#pragma once

#include "Graphite/Core.glsl"
#include "Graphite/Octahedral.glsl"

#ifdef GRAPHITE_FRAGMENT
layout(location = 1) out vec2 graphite_GBufferNormal;
layout(location = 2) out vec2 graphite_GBufferMaterial;
layout(location = 3) out vec2 graphite_GBufferMotion;
layout(location = 28) in vec4 graphite_MotionCurrent;
layout(location = 29) in vec4 graphite_MotionPrevious;
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

void Graphite_WriteGBuffer(vec3 worldNormal, float roughness, float metallic)
{
    graphite_GBufferNormal = Graphite_OctEncode(normalize(worldNormal));
    graphite_GBufferMaterial = vec2(roughness, metallic);
    Graphite_WriteMotion(graphite_MotionCurrent, graphite_MotionPrevious);
}
#endif
