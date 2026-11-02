// One layer of a view as shaders read it, matching ViewBlock.
#pragma once

struct Graphite_ViewData
{
    mat4 view;                      // rotation only, the camera translation is applied by the camera relative positions
    mat4 projection;
    mat4 viewProjection;
    mat4 inverseViewProjection;
    mat4 previousViewProjection;    // relative to the previous camera position
    vec4 cameraHigh;
    vec4 cameraLow;
    vec4 previousCameraHigh;
    vec4 previousCameraLow;
    vec4 screen;                    // width, height, 1 / width, 1 / height
    vec4 jitter;                    // this frame's jitter in xy, the previous frame's in zw
    vec4 time;                      // time, delta time, frame index, the part of time the first lost
    vec4 depth;                     // near plane, logical far distance
    vec4 treeOffset;                // this layer's camera minus the first layer's, which the view's trees are built around
    vec4 exposure;                  // the exposure light is pre-exposed by, last frame's, 1 in a capture, unused
};

// The relative position of a screen uv, running down the image as texture rows do, and a reversed Z depth in a view
vec3 Graphite_RelativeFromDepth(Graphite_ViewData view, vec2 uv, float depth)
{
    vec4 p = view.inverseViewProjection * vec4(uv.x * 2.0 - 1.0, 1.0 - uv.y * 2.0, depth, 1.0);
    return p.xyz / p.w;
}
