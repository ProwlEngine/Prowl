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

vec3 graphite_SceneColorTap(vec2 uv, float level)
{
    return textureLod(sampler2DArray(Graphite_SceneColor, Graphite_SceneColorSampler), vec3(uv, float(gl_ViewIndex)), level).rgb;
}

// A mip through a cubic B-spline from four bilinear taps, so a small mip magnified shows no texels
vec3 graphite_SceneColorCubic(vec2 uv, float level)
{
    vec2 size = vec2(textureSize(sampler2DArray(Graphite_SceneColor, Graphite_SceneColorSampler), int(level)).xy);
    vec2 p = uv * size - 0.5;
    vec2 i = floor(p);
    vec2 f = p - i;
    vec2 f2 = f * f;
    vec2 f3 = f2 * f;
    vec2 w0 = (1.0 - 3.0 * f + 3.0 * f2 - f3) / 6.0;
    vec2 w1 = (4.0 - 6.0 * f2 + 3.0 * f3) / 6.0;
    vec2 w2 = (1.0 + 3.0 * f + 3.0 * f2 - 3.0 * f3) / 6.0;
    vec2 w3 = f3 / 6.0;
    vec2 g0 = w0 + w1;
    vec2 g1 = w2 + w3;
    vec2 t0 = (i - 0.5 + w1 / g0) / size;
    vec2 t1 = (i + 1.5 + w3 / g1) / size;
    return g0.y * (g0.x * graphite_SceneColorTap(vec2(t0.x, t0.y), level) + g1.x * graphite_SceneColorTap(vec2(t1.x, t0.y), level))
        + g1.y * (g0.x * graphite_SceneColorTap(vec2(t0.x, t1.y), level) + g1.x * graphite_SceneColorTap(vec2(t1.x, t1.y), level));
}

// The opaque colour at a screen uv, lod 0 sharp and each lod up a blur twice as wide, smooth between texels
vec3 Graphite_SampleSceneColor(vec2 screenUV, float lod)
{
    if (lod <= 0.0)
        return graphite_SceneColorTap(screenUV, 0.0);
    float level = floor(lod);
    vec3 lower = level < 1.0 ? graphite_SceneColorTap(screenUV, 0.0) : graphite_SceneColorCubic(screenUV, level);
    float t = lod - level;
    return t > 0.0 ? mix(lower, graphite_SceneColorCubic(screenUV, level + 1.0), t) : lower;
}

vec3 Graphite_SampleSceneColor(vec2 screenUV) { return Graphite_SampleSceneColor(screenUV, 0.0); }

#ifdef GRAPHITE_FRAGMENT
// This fragment's uv on the view's screen textures
vec2 Graphite_ScreenUV() { return gl_FragCoord.xy * Graphite_View().screen.zw; }

// The unit camera relative direction this fragment's pixel looks along, for any projection
vec3 Graphite_PixelRay()
{
    vec2 uv = Graphite_ScreenUV();
    return Graphite_ViewRay(vec2(uv.x * 2.0 - 1.0, 1.0 - uv.y * 2.0));
}
#endif
