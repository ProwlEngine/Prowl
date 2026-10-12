// Vertex pulling for material passes. Streams load from the page by gl_VertexIndex, or give their default when the mesh
// lacks them. The library's own main runs first, so the material slot is known before the shader's main reads material
// properties, then calls the shader's main.
#pragma once

#include "Graphite/Core.glsl"

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

// The instance a provider's instanced or indirect draw is drawing, 0 to its count
uint Graphite_InstanceIndex() { return uint(gl_InstanceIndex); }

bool Graphite_HasStream(uint stream) { return (graphite_Chunk.streamsPresent & (1u << stream)) != 0u; }

uint graphite_Word(uint stream, uint words, uint component) { return graphite_Words[graphite_Chunk.streamOffsets[stream] + uint(gl_VertexIndex) * words + component]; }
float graphite_Float(uint stream, uint words, uint component) { return uintBitsToFloat(graphite_Word(stream, words, component)); }

vec3 Graphite_LoadPosition() { return vec3(graphite_Float(0u, 3u, 0u), graphite_Float(0u, 3u, 1u), graphite_Float(0u, 3u, 2u)); }
vec3 Graphite_LoadNormal() { return Graphite_HasStream(1u) ? vec3(graphite_Float(1u, 3u, 0u), graphite_Float(1u, 3u, 1u), graphite_Float(1u, 3u, 2u)) : vec3(0.0, 0.0, 1.0); }
vec4 Graphite_LoadTangent() { return Graphite_HasStream(2u) ? vec4(graphite_Float(2u, 4u, 0u), graphite_Float(2u, 4u, 1u), graphite_Float(2u, 4u, 2u), graphite_Float(2u, 4u, 3u)) : vec4(1.0, 0.0, 0.0, 1.0); }
vec4 Graphite_LoadColor() { return Graphite_HasStream(3u) ? unpackUnorm4x8(graphite_Word(3u, 1u, 0u)) : vec4(1.0); }
vec2 Graphite_LoadUV(uint set) { uint stream = 4u + set; return Graphite_HasStream(stream) ? vec2(graphite_Float(stream, 2u, 0u), graphite_Float(stream, 2u, 1u)) : vec2(0.0); }
uvec4 Graphite_LoadBoneIndices()
{
    if (!Graphite_HasStream(8u))
        return uvec4(0u);
    uint a = graphite_Word(8u, 2u, 0u), b = graphite_Word(8u, 2u, 1u);
    return uvec4(a & 0xFFFFu, a >> 16, b & 0xFFFFu, b >> 16);
}
vec4 Graphite_LoadBoneWeights() { return Graphite_HasStream(9u) ? vec4(unpackUnorm2x16(graphite_Word(9u, 2u, 0u)), unpackUnorm2x16(graphite_Word(9u, 2u, 1u))) : vec4(0.0); }
vec4 Graphite_LoadCustom(uint index) { uint stream = 10u + index; return Graphite_HasStream(stream) ? vec4(graphite_Float(stream, 4u, 0u), graphite_Float(stream, 4u, 1u), graphite_Float(stream, 4u, 2u), graphite_Float(stream, 4u, 3u)) : vec4(0.0); }

#define vertexPosition Graphite_LoadPosition()
#define vertexNormal Graphite_LoadNormal()
#define vertexTangent Graphite_LoadTangent()
#define vertexColor Graphite_LoadColor()
#define vertexUV0 Graphite_LoadUV(0u)
#define vertexUV1 Graphite_LoadUV(1u)
#define vertexUV2 Graphite_LoadUV(2u)
#define vertexUV3 Graphite_LoadUV(3u)
#define vertexTexCoord0 vertexUV0
#define vertexTexCoord1 vertexUV1
#define vertexTexCoord2 vertexUV2
#define vertexTexCoord3 vertexUV3
#define vertexBoneIndices Graphite_LoadBoneIndices()
#define vertexBoneWeights Graphite_LoadBoneWeights()
#define vertexCustom0 Graphite_LoadCustom(0u)
#define vertexCustom1 Graphite_LoadCustom(1u)

// The camera relative world position of an object space point.
vec3 Graphite_ObjectToWorld(vec3 p)
{
    Graphite_ViewData view = Graphite_View();
    return Graphite_TransformPoint(Graphite_ObjectTransform(), p, view.cameraHigh.xyz, view.cameraLow.xyz);
}

vec3 Graphite_ObjectToWorldDirection(vec3 d) { return Graphite_Linear(Graphite_ObjectTransform()) * d; }

vec3 Graphite_ObjectToWorldNormal(vec3 n) { return normalize(Graphite_NormalMatrix(Graphite_ObjectTransform()) * n); }

// Where the vertex samples the object's lightmap: its second UV set when the mesh has one, else its first, placed on the page
vec2 Graphite_LightmapUV()
{
    vec2 uv = Graphite_HasStream(5u) ? Graphite_LoadUV(1u) : Graphite_LoadUV(0u);
    vec4 scaleOffset = Graphite_Object().lightmapScaleOffset;
    return uv * scaleOffset.xy + scaleOffset.zw;
}

vec4 Graphite_ObjectToClip(vec3 p) { return Graphite_RelativeToClip(Graphite_ObjectToWorld(p)); }

// Where the point was last frame in last frame's clip space, for motion vectors. Objects without a previous transform did not move.
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

// For shaders that move their vertices: the object space position the vertex had last frame, so motion vectors follow the move
void Graphite_SetPreviousPosition(vec3 p)
{
    graphite_PreviousSet = true;
    graphite_PreviousPosition = p;
}

// The vertex's clip position now and last frame, for the forward pass's motion vectors, from last frame's pose when skinned
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
        position = vertexPosition;
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

#define main graphite_ShaderMain
