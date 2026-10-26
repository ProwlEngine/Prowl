// The standard surface's vertex stage, Graphite_StandardVertex, included after Graphite/Standard/Standard.glsl and
// the shader's SurfaceVertex function. See Standard.glsl.
#pragma once

#include "Graphite/Standard/Standard.glsl"

#ifdef GRAPHITE_VERTEX

Graphite_SurfaceVertex Graphite_LoadSurfaceVertex(vec3 position, float time)
{
    Graphite_SurfaceVertex v;
    v.Position = position;
    v.Normal = vertexNormal;
    v.Tangent = vertexTangent;
    v.Color = vertexColor;
    v.UV = vertexUV0;
    v.UV1 = vertexUV1;
    v.Time = time;
    return v;
}

void Graphite_StandardVertex()
{
    vec4 time = Graphite_View().time;
    Graphite_SurfaceVertex v = Graphite_LoadSurfaceVertex(vertexPosition, time.x);
#ifdef GRAPHITE_SURFACE_VERTEX_FUNCTION
    SurfaceVertex(v);
#ifdef GRAPHITE_STANDARD_FORWARD
    Graphite_SurfaceVertex previous = Graphite_LoadSurfaceVertex(Graphite_PreviousVertexPosition(), time.x - time.y);
    SurfaceVertex(previous);
    Graphite_SetPreviousPosition(previous.Position);
#endif
#endif

    vec3 world = Graphite_ObjectToWorld(v.Position);
    gl_Position = Graphite_RelativeToClip(world);

#if defined(GRAPHITE_STANDARD_FORWARD) || defined(GRAPHITE_STANDARD_ALPHA)
    vUV = v.UV;
    vUV1 = v.UV1;
#endif

#ifdef GRAPHITE_STANDARD_VERTEX_DATA
    vWorldPos = world;
    vColor = v.Color;
#ifdef _INSTANCE_TINT
    vColor *= Graphite_Object().custom0;
#endif
#endif

#ifdef GRAPHITE_STANDARD_FORWARD
#ifdef GRAPHITE_STANDARD_LIT
    vNormal = Graphite_ObjectToWorldNormal(v.Normal);
    vec4 tangent = v.Tangent;
    bool hasTangents = Graphite_HasStream(2u);
    vec3 worldTangent = hasTangents ? normalize(Graphite_ObjectToWorldDirection(tangent.xyz)) : vec3(1.0, 0.0, 0.0);
    Graphite_StandardTangentFrame(vNormal, worldTangent, tangent.w, hasTangents, vTangent, vBitangent);
    vHasTangents = hasTangents ? 1.0 : 0.0;
    vLightmapUV = Graphite_LightmapUV();
#endif
#endif
}

#endif
