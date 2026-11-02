// The view's layers for fullscreen effects, bound by FeatureContext on every fullscreen draw.
#pragma once

#include "Graphite/Math.glsl"
#include "Graphite/ViewData.glsl"
#include "Graphite/Octahedral.glsl"

layout(std430) readonly buffer Graphite_PostViews { Graphite_ViewData graphite_PostViews[]; };

// The layer this fragment draws, jittered when the view jitters
Graphite_ViewData Graphite_PostView() { return graphite_PostViews[GRAPHITE_LAYER]; }

// What colour from last frame is multiplied by to match this frame's exposure
float Graphite_PostExposureRatio(Graphite_ViewData view) { return view.exposure.x / max(view.exposure.y, 1e-30); }

// Whether the layer's projection is orthographic, from the row that makes w
bool Graphite_PostOrthographic(Graphite_ViewData view) { return abs(view.projection[2][3]) < 0.5; }

// The view space position of a uv and reversed Z depth. View space is the view matrix's, rotation only, from the camera.
vec3 Graphite_PostViewPosition(Graphite_ViewData view, vec2 uv, float depth)
{
    return mat3(view.view) * Graphite_RelativeFromDepth(view, uv, depth);
}

// Where a camera relative position was on screen last frame, in uv
vec2 Graphite_PostPreviousUV(Graphite_ViewData view, vec3 relative)
{
    vec3 moved = relative + (view.cameraHigh.xyz - view.previousCameraHigh.xyz) + (view.cameraLow.xyz - view.previousCameraLow.xyz);
    vec4 clip = view.previousViewProjection * vec4(moved, 1.0);
    vec2 ndc = clip.xy / clip.w;
    return vec2(ndc.x * 0.5 + 0.5, 0.5 - ndc.y * 0.5);
}
