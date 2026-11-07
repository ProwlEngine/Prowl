// Caustics on what lies under the water: two taps of the caustics texture, scrolling at their own speeds and
// scales, at the scene point moved along the sun by the water above it over four times the sun's height, so the
// pattern leans with the light. They are blurred by mip with distance and away from the focal depth, and scale the
// light around the texture's grey point, so they darken as well as brighten. Both taps are pushed about by one tap of
// the wave normal map, still in the world, so the pattern wobbles as it slides through it.
#pragma once

#include "Graphite/ViewData.glsl"

uniform sampler2D _OceanCaustics;
uniform float _CausticsTextureScale;    // metres the texture spans
uniform float _CausticsTextureAverage;  // the texture's mean, the value that leaves the light as it is
uniform float _CausticsStrength;
uniform float _CausticsFocalDepth;      // the depth in metres the pattern is sharpest at
uniform float _CausticsDepthOfField;    // metres of depth over which it blurs a mip away from the focal depth
uniform sampler2D _OceanNormals;
uniform float _CausticsDistortionScale;     // metres the normal map spans
uniform float _CausticsDistortionStrength;  // how far its normals push the pattern, in texture widths

// What the light at a scene point relative to a view's camera is scaled by, under a depth of water, toward the sun
vec3 Ocean_Caustics(Graphite_ViewData view, vec3 scene, float depth, vec3 toSun)
{
    if (depth <= 0.0 || toSun.y <= 0.0)
        return vec3(1.0);
    float size = max(_CausticsTextureScale, 1e-3);
    vec2 at = view.cameraLow.xz + scene.xz + toSun.xz * depth / (4.0 * toSun.y);
    float time = view.time.x;
    float bend = max(_CausticsDistortionScale, 1e-3);
    vec2 distortion = (texture(_OceanNormals, fract(view.cameraHigh.xz / bend) + at / bend).rg * 2.0 - 1.0) * _CausticsDistortionStrength;
    float lod = log2(max(length(scene), 1.0)) + abs(depth - _CausticsFocalDepth) / max(_CausticsDepthOfField, 0.01);
    vec2 first = fract(view.cameraHigh.xz / size) + at / size + vec2(0.044 * time + 17.16, -0.169 * time) + 1.30 * distortion;
    vec2 second = fract(view.cameraHigh.xz * 1.37 / size) + at * 1.37 / size + vec2(0.248 * time, 0.117 * time) + 1.77 * distortion;
    vec3 caustics = 0.5 * textureLod(_OceanCaustics, first, lod).rgb + 0.5 * textureLod(_OceanCaustics, second, lod).rgb;
    return max(vec3(1.0) + _CausticsStrength * (caustics - _CausticsTextureAverage), vec3(0.0));
}
