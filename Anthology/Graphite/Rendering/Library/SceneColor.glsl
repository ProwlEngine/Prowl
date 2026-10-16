// The view's opaque colour, copied after the opaque surfaces and the sky with its mips, for transparent passes that
// refract or blur what is behind them. A pass reading it declares Tags { "SceneColor" = "Read" }, which makes the views
// drawing it run the copy. Views without one read black.
#pragma once

#include "Graphite/Core.glsl"

layout(set = 1, binding = 34) uniform texture2DArray Graphite_SceneColor;
layout(set = 1, binding = 35) uniform sampler Graphite_SceneColorSampler;

// Where a camera relative position lands on the view's screen textures, uv 0 to 1 from the first row
vec2 Graphite_RelativeToScreenUV(vec3 relative)
{
    vec4 clip = Graphite_RelativeToClip(relative);
    vec2 ndc = clip.xy / clip.w;
    return vec2(ndc.x * 0.5 + 0.5, 0.5 - ndc.y * 0.5);
}

// The scene colour's mip count, the last mip a single texel
float Graphite_SceneColorMips() { return float(textureQueryLevels(sampler2DArray(Graphite_SceneColor, Graphite_SceneColorSampler))); }

// The opaque colour at a screen uv, lod 0 sharp and each lod up a box blur twice as wide
vec3 Graphite_SampleSceneColor(vec2 screenUV, float lod)
{
    return textureLod(sampler2DArray(Graphite_SceneColor, Graphite_SceneColorSampler), vec3(screenUV, float(gl_ViewIndex)), lod).rgb;
}

vec3 Graphite_SampleSceneColor(vec2 screenUV) { return Graphite_SampleSceneColor(screenUV, 0.0); }

#ifdef GRAPHITE_FRAGMENT
// This fragment's uv on the view's screen textures
vec2 Graphite_ScreenUV() { return gl_FragCoord.xy * Graphite_View().screen.zw; }
#endif
