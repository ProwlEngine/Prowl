// The renderer's data for every material pass: the view block, the draw and object records, and camera relative transforms.
// Positions in shaders are relative: world space moved so the camera sits at the origin, which keeps the precision of
// objects far from it. World means true world space, which directions and normals are in as moving changes none of them.
#pragma once

#extension GL_EXT_multiview : require
#extension GL_EXT_nonuniform_qualifier : require

#include "Graphite/Math.glsl"
#include "Graphite/ViewData.glsl"

// flags: 1 every instance draws object, 2 subMesh holds the material slot itself
struct Graphite_DrawRecord { uint chunk; uint object; uint flags; uint subMesh; uint firstInstance; };

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
    uint currentPositions;          // a skinned copy's current and last frame's positions, first word plus one, 0 for none
    uint previousPositions;
    float lightmapScale;            // what decoded lightmap texels are multiplied by
    vec4 lightmapScaleOffset;
    vec4 color;                     // multiplies the standard surfaces' vertex colour
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

// The scale every light the view draws is pre-exposed by, 1 / (1.2 * 2^EV100)
float Graphite_Exposure() { return Graphite_View().exposure.x; }

// Whether the view is capturing the sky or a reflection probe
bool Graphite_IsCapture() { return Graphite_View().exposure.z > 0.5; }

vec3 Graphite_CameraOffset(vec3 high, vec3 low)
{
    Graphite_ViewData view = Graphite_View();
    precise vec3 offset = (high - view.cameraHigh.xyz) + (low - view.cameraLow.xyz);
    return offset;
}

// The world position of a relative one, as a float. Fine near the origin, for noise and other effects that need world space.
vec3 Graphite_RelativeToWorld(vec3 relative)
{
    Graphite_ViewData view = Graphite_View();
    return relative + view.cameraHigh.xyz + view.cameraLow.xyz;
}

// Unit direction from a relative position to the camera
vec3 Graphite_ViewDir(vec3 relativePos)
{
    return normalize(-relativePos);
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

#if defined(GRAPHITE_FRAGMENT) && !defined(GRAPHITE_NO_OBJECT)
layout(location = 26) flat in uint graphite_ObjectIndex;

uint Graphite_ObjectIndex() { return graphite_ObjectIndex; }
Graphite_ObjectData Graphite_Object() { return graphite_Objects[graphite_ObjectIndex]; }
#endif
