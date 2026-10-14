#version 460
#extension GL_GOOGLE_include_directive : enable
#line 1 "Graphite/Lit/Forward"

#extension GL_GOOGLE_cpp_style_line_directive : require
#line 44 "Graphite/Lit/Forward"


#pragma variant_NORMALMAP
#pragma variant_SURFACEMAP
#pragma variant_OCCLUSIONMAP
#pragma variant_EMISSION
#pragma variant_PARALLAXMAP
#pragma variant_TRANSLUCENCY
#pragma variant_ANISOTROPIC

#line 55 "Graphite/Lit/Forward"

#line 1 "Graphite/Standard/Standard.glsl"

















#pragma variant_SURFACEOPAQUECUTOUTTRANSPARENT
#pragma variant_INSTANCE_TINT











#line 1 "Graphite/Vertex.glsl"





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







#line 7 "Graphite/Vertex.glsl"

layout(set = 2, binding = 3, std430) readonly buffer Graphite_Vertices { uint graphite_Words[]; };

layout(location = 28) out vec4 graphite_MotionCurrent;
layout(location = 29) out vec4 graphite_MotionPrevious;
layout(location = 30) flat out uint graphite_ObjectIndex;
layout(location = 31) flat out uint graphite_MaterialSlot;

invariant gl_Position;

Graphite_DrawRecord graphite_Draw;
Graphite_Chunk graphite_Chunk;
uint graphite_Object;

uint Graphite_ObjectIndex() { return graphite_Object; }
Graphite_ObjectData Graphite_Object() { return graphite_Objects[graphite_Object]; }
Graphite_DrawRecord Graphite_Draw() { return graphite_Draw; }
Graphite_Transform Graphite_ObjectTransform() { return graphite_Transforms[graphite_Object]; }


uint Graphite_InstanceIndex() { return uint(gl_InstanceIndex); }

bool Graphite_HasStream(uint stream) { return(graphite_Chunk.streamsPresent & (1u << stream)) != 0u; }

uint graphite_Word(uint stream, uint words, uint component) { return graphite_Words[graphite_Chunk.streamOffsets[stream] + uint(gl_VertexIndex) * words + component]; }
float graphite_Float(uint stream, uint words, uint component) { return uintBitsToFloat(graphite_Word(stream, words, component)); }

vec3 Graphite_LoadPosition() { return vec3(graphite_Float(0u, 3u, 0u), graphite_Float(0u, 3u, 1u), graphite_Float(0u, 3u, 2u)); }
vec3 Graphite_LoadNormal() { return Graphite_HasStream(1u) ? vec3(graphite_Float(1u, 3u, 0u), graphite_Float(1u, 3u, 1u), graphite_Float(1u, 3u, 2u)) : vec3(0.0, 0.0, 1.0); }
vec4 Graphite_LoadTangent() { return Graphite_HasStream(2u) ? vec4(graphite_Float(2u, 4u, 0u), graphite_Float(2u, 4u, 1u), graphite_Float(2u, 4u, 2u), graphite_Float(2u, 4u, 3u)) : vec4(1.0, 0.0, 0.0, 1.0); }
vec4 Graphite_LoadColor() { return Graphite_HasStream(3u) ? unpackUnorm4x8(graphite_Word(3u, 1u, 0u)) : vec4(1.0); }
vec2 Graphite_LoadUV(uint set) { uint stream = 4u + set; return Graphite_HasStream(stream) ? vec2(graphite_Float(stream, 2u, 0u), graphite_Float(stream, 2u, 1u)) : vec2(0.0); }
uvec4 Graphite_LoadBoneIndices()
{
    if (! Graphite_HasStream(8u))
        return uvec4(0u);
    uint a = graphite_Word(8u, 2u, 0u), b = graphite_Word(8u, 2u, 1u);
    return uvec4(a & 0xFFFFu, a >> 16, b & 0xFFFFu, b >> 16);
}
vec4 Graphite_LoadBoneWeights() { return Graphite_HasStream(9u) ? vec4(unpackUnorm2x16(graphite_Word(9u, 2u, 0u)), unpackUnorm2x16(graphite_Word(9u, 2u, 1u))) : vec4(0.0); }
vec4 Graphite_LoadCustom(uint index) { uint stream = 10u + index; return Graphite_HasStream(stream) ? vec4(graphite_Float(stream, 4u, 0u), graphite_Float(stream, 4u, 1u), graphite_Float(stream, 4u, 2u), graphite_Float(stream, 4u, 3u)) : vec4(0.0); }



















vec3 Graphite_ObjectToWorld(vec3 p)
{
    Graphite_ViewData view = Graphite_View();
    return Graphite_TransformPoint(Graphite_ObjectTransform(), p, view.cameraHigh.xyz, view.cameraLow.xyz);
}

vec3 Graphite_ObjectToWorldDirection(vec3 d) { return Graphite_Linear(Graphite_ObjectTransform()) * d; }

vec3 Graphite_ObjectToWorldNormal(vec3 n) { return normalize(Graphite_NormalMatrix(Graphite_ObjectTransform()) * n); }


vec2 Graphite_LightmapUV()
{
    vec2 uv = Graphite_HasStream(5u) ? Graphite_LoadUV(1u) : Graphite_LoadUV(0u);
    vec4 scaleOffset = Graphite_Object().lightmapScaleOffset;
    return uv * scaleOffset.xy + scaleOffset.zw;
}

vec4 Graphite_ObjectToClip(vec3 p) { return Graphite_RelativeToClip(Graphite_ObjectToWorld(p)); }


vec4 Graphite_PreviousObjectToClip(vec3 p)
{
    Graphite_ViewData view = Graphite_View();
    uint previous = Graphite_Object().previousTransform;
    Graphite_Transform t = previous == 0xFFFFFFFFu ? Graphite_ObjectTransform() : graphite_PreviousTransforms[previous];
    return view.previousViewProjection * vec4(Graphite_TransformPoint(t, p, view.previousCameraHigh.xyz, view.previousCameraLow.xyz), 1.0);
}

void Graphite_BeginVertex()
{
    graphite_Draw = graphite_DrawRecords[graphite_Push.drawRecordBase + uint(gl_DrawID)];
    graphite_Chunk = graphite_Chunks[graphite_Draw.chunk];
    graphite_Object = (graphite_Draw.flags & 1u) != 0u ? graphite_Draw.object : graphite_Instances[graphite_Push.instanceBase + uint(gl_InstanceIndex)];
    uint currentPositions = graphite_Objects[graphite_Object].currentPositions;
    if (currentPositions != 0u)
        graphite_Chunk.streamOffsets[0] = currentPositions - 1u;
    graphite_ObjectIndex = graphite_Object;
    graphite_MaterialSlot = (graphite_Draw.flags & 2u) != 0u ? graphite_Draw.subMesh : graphite_MaterialSlots[graphite_Objects[graphite_Object].materialSliceBase + graphite_Draw.subMesh];
}

void graphite_ShaderMain();

bool graphite_PreviousSet = false;
vec3 graphite_PreviousPosition;


void Graphite_SetPreviousPosition(vec3 p)
{
    graphite_PreviousSet = true;
    graphite_PreviousPosition = p;
}


void Graphite_EndVertex()
{
    graphite_MotionCurrent = gl_Position;
    uint previous = Graphite_Object().previousPositions;
    vec3 position;
    if (graphite_PreviousSet)
    {
        position = graphite_PreviousPosition;
    }
    else if (previous == 0u)
    {
        position = Graphite_LoadPosition();
    }
    else
    {
        uint at = previous - 1u + uint(gl_VertexIndex) * 3u;
        position = uintBitsToFloat(uvec3(graphite_Words[at], graphite_Words[at + 1u], graphite_Words[at + 2u]));
    }
    graphite_MotionPrevious = Graphite_PreviousObjectToClip(position);
}

void main()
{
    Graphite_BeginVertex();
    graphite_ShaderMain();
    Graphite_EndVertex();
}


#line 32 "Graphite/Standard/Standard.glsl"










layout(set = 0, binding = 1, std140) uniform _GraphiteGlobal { vec2 _Tiling; vec2 _Offset; vec4 _MainColor; int _MainTexUV; float _AlphaCutoff; float _Metallic; float _Roughness; };



layout(set = 0, binding = 2) uniform sampler2D _MainTex;



















































                        layout(location = 0) out vec2 vUV;
                        layout(location = 1) out vec2 vUV1;



                        layout(location = 2) out vec3 vWorldPos;
                        layout(location = 3) out vec4 vColor;

                        layout(location = 4) out vec3 vNormal;
                        layout(location = 5) out vec3 vTangent;
                        layout(location = 6) out vec3 vBitangent;
                        layout(location = 7) out vec2 vLightmapUV;
layout(location = 8) flat out float vHasTangents;




void Graphite_StandardTangentFrame(vec3 worldNormal, vec3 worldTangent, float handedness, bool hasTangent, out vec3 tangent, out vec3 bitangent)
{
    float sign = handedness < 0.0 ? - 1.0 : 1.0;
    if (hasTangent)
    {
        tangent = worldTangent;
        bitangent = cross(tangent, worldNormal) * sign;
        if (dot(bitangent, bitangent) > 0.000001)
            return;
    }
    tangent = abs(worldNormal.y) < 0.999 ? normalize(cross(worldNormal, vec3(0.0, 1.0, 0.0))) : normalize(cross(worldNormal, vec3(1.0, 0.0, 0.0)));
    bitangent = cross(tangent, worldNormal) * sign;
}



void Graphite_StandardVertex()
{
    vec3 world = Graphite_ObjectToWorld(Graphite_LoadPosition());
    gl_Position = Graphite_RelativeToClip(world);


    vUV = Graphite_LoadUV(0u) * _Tiling + _Offset;
    vUV1 = Graphite_LoadUV(1u);



    vWorldPos = world;
    vColor = Graphite_LoadColor();




    vNormal = Graphite_ObjectToWorldNormal(Graphite_LoadNormal());
    vec4 tangent = Graphite_LoadTangent();
    bool hasTangents = Graphite_HasStream(2u);
    vec3 worldTangent = hasTangents ? normalize(Graphite_ObjectToWorldDirection(tangent.xyz)) : vec3(1.0, 0.0, 0.0);
    Graphite_StandardTangentFrame(vNormal, worldTangent, tangent.w, hasTangents, vTangent, vBitangent);
    vHasTangents = hasTangents ? 1.0 : 0.0;
    vLightmapUV = Graphite_LightmapUV();


}




















































































































































































































































#line 57 "Graphite/Lit/Forward"
 void graphite_ShaderMain() { Graphite_StandardVertex(); }
