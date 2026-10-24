// The view's opaque depth, copied after the opaque surfaces, for transparent passes that fade or intersect with what is
// behind them. A pass reading it declares Tags { "SceneDepth" = "Read" }, which makes the views drawing it run the copy.
// Views without one read 0, the far plane. Depth is reversed Z, 1 at the near plane.
#pragma once

#include "Graphite/Core.glsl"
#include "Graphite/SceneColor.glsl"

layout(set = 1, binding = 36) uniform texture2DArray Graphite_SceneDepth;

// The opaque depth at a screen uv, not filtered
float Graphite_FetchSceneDepth(vec2 screenUV)
{
    ivec2 size = textureSize(Graphite_SceneDepth, 0).xy;
    ivec2 pixel = clamp(ivec2(screenUV * vec2(size)), ivec2(0), size - 1);
    return texelFetch(Graphite_SceneDepth, ivec3(pixel, gl_ViewIndex), 0).r;
}

// The distance along the view of the opaque surface at a screen uv, or very large where there is none
float Graphite_SceneLinearDepth(vec2 screenUV) { return Graphite_LinearDepth(Graphite_FetchSceneDepth(screenUV)); }

// The camera relative position of the opaque surface at a screen uv
vec3 Graphite_SceneRelativePosition(vec2 screenUV)
{
    return Graphite_RelativeFromDepth(vec2(screenUV.x, 1.0 - screenUV.y), Graphite_FetchSceneDepth(screenUV));
}
