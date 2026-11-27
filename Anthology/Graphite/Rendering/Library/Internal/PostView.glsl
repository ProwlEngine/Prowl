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

// How far the camera moved since last frame, added to a camera relative position to make it relative to last frame's
vec3 Graphite_PostCameraMoved(Graphite_ViewData view)
{
    return (view.cameraHigh.xyz - view.previousCameraHigh.xyz) + (view.cameraLow.xyz - view.previousCameraLow.xyz);
}

// Where a camera relative position was on screen last frame, in uv, off screen where it was behind the camera
vec2 Graphite_PostPreviousUV(Graphite_ViewData view, vec3 relative)
{
    vec4 clip = view.previousViewProjection * vec4(relative + Graphite_PostCameraMoved(view), 1.0);
    if (clip.w <= 0.0)
        return vec2(-1.0);
    vec2 ndc = clip.xy / clip.w;
    return vec2(ndc.x * 0.5 + 0.5, 0.5 - ndc.y * 0.5);
}

// The direction from a camera relative position at a uv toward the camera, the same everywhere when orthographic
vec3 Graphite_PostTowardCamera(Graphite_ViewData view, vec2 uv, vec3 relative)
{
    if (Graphite_PostOrthographic(view))
        return normalize(Graphite_RelativeFromDepth(view, uv, 1.0) - Graphite_RelativeFromDepth(view, uv, 0.0));
    return -normalize(relative);
}

// How many pixels of a screen height a metre spans at a camera relative position
float Graphite_PostPixelsPerMetre(Graphite_ViewData view, float screenHeight, vec3 relative)
{
    return abs(view.projection[1][1]) * 0.5 * screenHeight / (Graphite_PostOrthographic(view) ? 1.0 : max(length(relative), 1e-3));
}

// GTAO's cosine weighted visibility of a slice through the view direction from that direction out to angle h, either
// side, the normal projected into the slice at angle n
float Graphite_SliceArc(float h, float n) { return 0.25 * (cos(n) + 2.0 * h * sin(n) - cos(2.0 * h - n)); }
