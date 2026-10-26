// The view's layers for fullscreen effects, bound by FeatureContext on every fullscreen draw.
#pragma once

#include "Graphite/ViewData.glsl"
#include "Graphite/Octahedral.glsl"

layout(std430) readonly buffer Graphite_PostViews { Graphite_ViewData graphite_PostViews[]; };

// The layer this fragment draws, jittered when the view jitters
Graphite_ViewData Graphite_PostView() { return graphite_PostViews[GRAPHITE_LAYER]; }

// Whether the layer's projection is orthographic, from the row that makes w
bool Graphite_PostOrthographic(Graphite_ViewData view) { return abs(view.projection[2][3]) < 0.5; }

// The view space position of a uv and reversed Z depth. View space is the view matrix's, rotation only, from the camera.
vec3 Graphite_PostViewPosition(Graphite_ViewData view, vec2 uv, float depth)
{
    vec4 p = view.inverseViewProjection * vec4(uv.x * 2.0 - 1.0, 1.0 - uv.y * 2.0, depth, 1.0);
    return mat3(view.view) * (p.xyz / p.w);
}

// The relative position of a uv and depth
vec3 Graphite_PostRelativePosition(Graphite_ViewData view, vec2 uv, float depth)
{
    vec4 p = view.inverseViewProjection * vec4(uv.x * 2.0 - 1.0, 1.0 - uv.y * 2.0, depth, 1.0);
    return p.xyz / p.w;
}

// Where a camera relative position was on screen last frame, in uv
vec2 Graphite_PostPreviousUV(Graphite_ViewData view, vec3 relative)
{
    vec3 moved = relative + (view.cameraHigh.xyz - view.previousCameraHigh.xyz) + (view.cameraLow.xyz - view.previousCameraLow.xyz);
    vec4 clip = view.previousViewProjection * vec4(moved, 1.0);
    vec2 ndc = clip.xy / clip.w;
    return vec2(ndc.x * 0.5 + 0.5, 0.5 - ndc.y * 0.5);
}
