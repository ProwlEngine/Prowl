// The standard surface's vertex stage, Graphite_StandardVertex, included after Graphite/Standard/Standard.glsl and
// the shader's SurfaceVertex function. See Standard.glsl.
#pragma once

#include "Graphite/Standard/Standard.glsl"

#ifdef GRAPHITE_VERTEX

Graphite_SurfaceVertex Graphite_LoadSurfaceVertex(vec3 position, float time)
{
    Graphite_SurfaceVertex v;
    v.position = position;
    v.normal = vertexNormal;
    v.tangent = vertexTangent;
    v.color = vertexColor;
    v.uv = vertexUV0;
    v.uv1 = vertexUV1;
    v.time = time;
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
    Graphite_SetPreviousPosition(previous.position);
#endif
#endif

    vec3 relative = Graphite_ObjectToRelative(v.position);
    gl_Position = Graphite_RelativeToClip(relative);

#if defined(GRAPHITE_STANDARD_FORWARD) || defined(GRAPHITE_STANDARD_ALPHA) || defined(GRAPHITE_STANDARD_SHADOW_DITHER)
    vUV = v.uv;
    vUV1 = v.uv1;
#endif

#ifdef GRAPHITE_STANDARD_VERTEX_DATA
    vRelativePos = relative;
    vColor = v.color;
#endif

#ifdef GRAPHITE_STANDARD_LIT
    vNormal = Graphite_ObjectToWorldNormal(v.normal);
    vec4 tangent = v.tangent;
    bool hasTangents = Graphite_HasStream(2u);
    vec3 worldTangent = hasTangents ? normalize(Graphite_ObjectToWorldDirection(tangent.xyz)) : vec3(1.0, 0.0, 0.0);
    Graphite_StandardTangentFrame(vNormal, worldTangent, tangent.w, hasTangents, vTangent, vBitangent);
    vHasTangents = hasTangents ? 1.0 : 0.0;
    vLightmapUV = Graphite_LightmapUV();
#endif
}

#endif
