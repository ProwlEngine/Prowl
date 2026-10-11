// The renderer's data for every material pass: the view block, the draw and object records, and camera relative transforms.
// World positions in shaders are relative to the camera, so objects far from the origin keep their precision.
#pragma once

#extension GL_EXT_multiview : require
#extension GL_EXT_nonuniform_qualifier : require

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
};

// flags: 1 every instance draws object, 2 subMesh holds the material slot itself
struct Graphite_DrawRecord { uint chunk; uint object; uint flags; uint subMesh; };

struct Graphite_Transform { vec4 row0; vec4 row1; vec4 row2; vec4 translationLow; };

struct Graphite_ObjectData
{
    uint materialSliceBase;
    uint flags;
    uint objectId;
    uint previousTransform;
    uint lightmapTexture;           // the lightmap's 2D slot, all ones for none, all ones but one while it loads
    uint probeIndex;
    float lodFade;
    uint layerMask;
    uint lightmapSampler;           // the lightmap's sampler slot, its encoding in the high 16 bits (0 linear, 1 RGBM)
    uint pad0;
    uint pad1;
    uint pad2;
    vec4 lightmapScaleOffset;
    vec4 custom0;
    vec4 custom1;
};

struct Graphite_Chunk
{
    uint streamOffsets[12];
    vec3 boundsMin;
    uint streamsPresent;
    vec3 boundsMax;
    uint page;
    uint firstIndex;
    uint indexCount;
    uint mesh;
    uint subMesh;
};

layout(set = 1, binding = 13, std430) readonly buffer Graphite_Chunks { Graphite_Chunk graphite_Chunks[]; };
layout(set = 1, binding = 14, std430) readonly buffer Graphite_Transforms { Graphite_Transform graphite_Transforms[]; };
layout(set = 1, binding = 15, std430) readonly buffer Graphite_PreviousTransforms { Graphite_Transform graphite_PreviousTransforms[]; };
layout(set = 1, binding = 16, std430) readonly buffer Graphite_Objects { Graphite_ObjectData graphite_Objects[]; };
layout(set = 1, binding = 17, std430) readonly buffer Graphite_MaterialSlots { uint graphite_MaterialSlots[]; };

layout(set = 1, binding = 2, std430) readonly buffer Graphite_Views { Graphite_ViewData graphite_Views[]; };

layout(set = 2, binding = 1, std430) readonly buffer Graphite_Instances { uint graphite_Instances[]; };
layout(set = 2, binding = 2, std430) readonly buffer Graphite_DrawRecords { Graphite_DrawRecord graphite_DrawRecords[]; };

layout(push_constant) uniform Graphite_Push { uint drawRecordBase; uint instanceBase; uint viewBase; uint flags; } graphite_Push;

Graphite_ViewData Graphite_View() { return graphite_Views[graphite_Push.viewBase + gl_ViewIndex]; }

vec3 Graphite_CameraOffset(vec3 high, vec3 low)
{
    Graphite_ViewData view = Graphite_View();
    precise vec3 offset = (high - view.cameraHigh.xyz) + (low - view.cameraLow.xyz);
    return offset;
}

// An absolute world position, as a float. Fine near the origin, for noise and other effects that need world space.
vec3 Graphite_AbsoluteFromRelative(vec3 relative)
{
    Graphite_ViewData view = Graphite_View();
    return relative + view.cameraHigh.xyz + view.cameraLow.xyz;
}

mat3 Graphite_Linear(Graphite_Transform t) { return transpose(mat3(t.row0.xyz, t.row1.xyz, t.row2.xyz)); }

vec3 Graphite_TransformPoint(Graphite_Transform t, vec3 p, vec3 cameraHigh, vec3 cameraLow)
{
    vec3 linear = vec3(dot(t.row0.xyz, p), dot(t.row1.xyz, p), dot(t.row2.xyz, p));
    vec3 high = vec3(t.row0.w, t.row1.w, t.row2.w);
    precise vec3 relative = linear + ((high - cameraHigh) + (t.translationLow.xyz - cameraLow));
    return relative;
}

// The inverse transpose of the linear part, up to scale: its cofactor matrix, exact for non uniform scale and mirroring.
mat3 Graphite_NormalMatrix(Graphite_Transform t)
{
    mat3 m = Graphite_Linear(t);
    mat3 cofactor = mat3(cross(m[1], m[2]), cross(m[2], m[0]), cross(m[0], m[1]));
    return dot(m[0], cross(m[1], m[2])) < 0.0 ? -cofactor : cofactor;
}

vec4 Graphite_RelativeToClip(vec3 relative) { return Graphite_View().viewProjection * vec4(relative, 1.0); }

// The unit camera relative direction a clip space position looks along, for any projection, infinite or not
vec3 Graphite_ViewRay(vec2 clip)
{
    mat4 inverse = Graphite_View().inverseViewProjection;
    vec4 near = inverse * vec4(clip, 1.0, 1.0);
    vec4 far = inverse * vec4(clip, 0.0, 1.0);
    return normalize(far.xyz * near.w - near.xyz * far.w);
}

float Graphite_LinearDepth(float deviceDepth)
{
    // Reversed Z with an infinite far plane: depth is near / view distance.
    return Graphite_View().depth.x / max(deviceDepth, 1e-12);
}

vec3 Graphite_RelativeFromDepth(vec2 uv, float deviceDepth)
{
    vec4 clip = vec4(uv * 2.0 - 1.0, deviceDepth, 1.0);
    vec4 relative = Graphite_View().inverseViewProjection * clip;
    return relative.xyz / relative.w;
}

#if defined(GRAPHITE_FRAGMENT) && !defined(GRAPHITE_NO_OBJECT)
layout(location = 30) flat in uint graphite_ObjectIndex;

uint Graphite_ObjectIndex() { return graphite_ObjectIndex; }
Graphite_ObjectData Graphite_Object() { return graphite_Objects[graphite_ObjectIndex]; }
#endif
