#version 460
#extension GL_GOOGLE_include_directive : enable
#line 1 "Graphite/Fallback/Forward"

#extension GL_GOOGLE_cpp_style_line_directive : require
#line 15 "Graphite/Fallback/Forward"

#line 1 "Graphite/Passes.glsl"





#line 1 "Graphite/Core.glsl"




#extension GL_EXT_multiview : require
#extension GL_EXT_nonuniform_qualifier : require

#line 1 "Graphite/ViewData.glsl"



struct Graphite_ViewData
{
    mat4 view;
    mat4 projection;
    mat4 viewProjection;
    mat4 inverseViewProjection;
    mat4 previousViewProjection;
    vec4 cameraHigh;
    vec4 cameraLow;
    vec4 previousCameraHigh;
    vec4 previousCameraLow;
    vec4 screen;
    vec4 jitter;
    vec4 time;
    vec4 depth;
    vec4 treeOffset;
};
#line 9 "Graphite/Core.glsl"


struct Graphite_DrawRecord { uint chunk; uint object; uint flags; uint subMesh; };

struct Graphite_Transform { vec4 row0; vec4 row1; vec4 row2; vec4 translationLow; };

struct Graphite_ObjectData
{
    uint materialSliceBase;
    uint flags;
    uint objectId;
    uint previousTransform;
    uint lightmapTexture;
    uint probeIndex;
    float lodFade;
    uint layerMask;
    uint lightmapSampler;
    uint currentPositions;
    uint previousPositions;
    uint pad0;
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
    precise vec3 relative = linear + ( (high - cameraHigh) + (t.translationLow.xyz - cameraLow));
    return relative;
}


mat3 Graphite_NormalMatrix(Graphite_Transform t)
{
    mat3 m = Graphite_Linear(t);
    mat3 cofactor = mat3(cross(m[1], m[2]), cross(m[2], m[0]), cross(m[0], m[1]));
    return dot(m[0], cross(m[1], m[2])) < 0.0 ? - cofactor : cofactor;
}

vec4 Graphite_RelativeToClip(vec3 relative) { return Graphite_View().viewProjection * vec4(relative, 1.0); }


vec3 Graphite_ViewRay(vec2 clip)
{
    mat4 inverse = Graphite_View().inverseViewProjection;
    vec4 near = inverse * vec4(clip, 1.0, 1.0);
    vec4 far = inverse * vec4(clip, 0.0, 1.0);
    return normalize(far.xyz * near.w - near.xyz * far.w);
}

float Graphite_LinearDepth(float deviceDepth)
{

    return Graphite_View().depth.x / max(deviceDepth, 1e-12);
}

vec3 Graphite_RelativeFromDepth(vec2 uv, float deviceDepth)
{
    vec4 clip = vec4(uv * 2.0 - 1.0, deviceDepth, 1.0);
    vec4 relative = Graphite_View().inverseViewProjection * clip;
    return relative.xyz / relative.w;
}


layout(location = 30) flat in uint graphite_ObjectIndex;

uint Graphite_ObjectIndex() { return graphite_ObjectIndex; }
Graphite_ObjectData Graphite_Object() { return graphite_Objects[graphite_ObjectIndex]; }

#line 7 "Graphite/Passes.glsl"
#line 1 "Graphite/Octahedral.glsl"



vec2 Graphite_OctWrap(vec2 v)
{
    return(1.0 - abs(v.yx)) * vec2(v.x >= 0.0 ? 1.0 : - 1.0, v.y >= 0.0 ? 1.0 : - 1.0);
}


vec2 Graphite_OctEncode(vec3 n)
{
    n /= abs(n.x) + abs(n.y) + abs(n.z);
    return n.z >= 0.0 ? n.xy : Graphite_OctWrap(n.xy);
}

vec3 Graphite_OctDecode(vec2 e)
{
    vec3 n = vec3(e, 1.0 - abs(e.x) - abs(e.y));
    if (n.z < 0.0)
        n.xy = Graphite_OctWrap(n.xy);
    return normalize(n);
}
#line 8 "Graphite/Passes.glsl"


layout(location = 1) out vec2 graphite_GBufferNormal;
layout(location = 2) out vec2 graphite_GBufferMaterial;
layout(location = 3) out vec2 graphite_GBufferMotion;
layout(location = 28) in vec4 graphite_MotionCurrent;
layout(location = 29) in vec4 graphite_MotionPrevious;





void Graphite_WriteMotion(vec4 currentClip, vec4 previousClip)
{
    if (currentClip.w <= 1e-6 || previousClip.w <= 1e-6)
    {
        graphite_GBufferMotion = vec2(0.0);
        return;
    }
    Graphite_ViewData view = Graphite_View();
    vec2 current = currentClip.xy / currentClip.w - view.jitter.xy;
    vec2 previous = previousClip.xy / previousClip.w;
    graphite_GBufferMotion = (current - previous) * vec2(0.5, - 0.5);
}

void Graphite_WriteGBuffer(vec3 worldNormal, float roughness, float metallic)
{
    graphite_GBufferNormal = Graphite_OctEncode(normalize(worldNormal));
    graphite_GBufferMaterial = vec2(roughness, metallic);
    Graphite_WriteMotion(graphite_MotionCurrent, graphite_MotionPrevious);
}

#line 17 "Graphite/Fallback/Forward"
 layout(location = 0) out vec4 finalColor;
            void main()
            {
                Graphite_WriteGBuffer(vec3(0.0, 1.0, 0.0), 1.0, 0.0);
                finalColor = vec4(0.5, 0.5, 0.5, 1.0);
            }
