// The standard surface shared by Graphite/PBR, Graphite/Unlit and every shader with a Surface block, whose passes are
// the surface model's: each defines its pass and model in its Shared section and includes this in both stages. The
// vertex stage includes Graphite/Standard/StandardVertex.glsl after the model's "#pragma surface_vertex" line and calls
// Graphite_StandardVertex from main, the fragment stage includes Graphite/Standard/StandardFragment.glsl after the
// model's "#pragma surface" line and calls Graphite_StandardFragment from main.
//
// Pass defines, exactly one: GRAPHITE_STANDARD_FORWARD, GRAPHITE_STANDARD_DEPTH (prepass and shadow caster).
// GRAPHITE_STANDARD_BACK_DEPTH: with the depth define, the back depth pass, writing the distance along the view ray.
// GRAPHITE_STANDARD_SHADOW: with the depth define, the shadow caster. Transparent casters, and PBR ones letting light
// through by transmission or thin walled subsurface, dither their coverage by what they let through.
// GRAPHITE_STANDARD_DEBUG: with the forward define, the Debug pass, showing the view's GRAPHITE_DEBUG value. See Graphite/Debug.glsl.
// Model define: GRAPHITE_STANDARD_UNLIT for no lighting, else the PBR model, Graphite/PBR/Closure.glsl.
// GRAPHITE_SURFACE_FUNCTION: the shader defines the model's Surface function, run before lighting over the read only
// inputs i: void Surface(inout PBR_Params p, Graphite_SurfaceData i), or void Surface(inout Unlit_Params p, Graphite_SurfaceData i).
// GRAPHITE_SURFACE_VERTEX_FUNCTION: the shader defines void SurfaceVertex(inout Graphite_SurfaceVertex v), run in every
// pass before the vertex is transformed, so shadows and depth match. It animates with v.Time for motion vectors to follow.
//
// Material axis, set by SurfaceSettings:
//   _SURFACE  OPAQUE, CUTOUT (alpha test against the model's cutoff) or TRANSPARENT (coverage reaches the target)
// Back faces, drawn when the material culls none, shade with a flipped frame.
//
// Every texture samples its UV set through its own scale and offset, maps also through their rotation. Colour textures are sampled as sRGB textures
// decode them, every factor is linear and applied after. The vertex colour multiplies the base colour and coverage.
#pragma once

#pragma variant _SURFACE OPAQUE CUTOUT TRANSPARENT

#if defined(_SURFACE_CUTOUT) || defined(_SURFACE_TRANSPARENT)
    #define GRAPHITE_STANDARD_ALPHA
#endif

#if defined(GRAPHITE_STANDARD_FORWARD) && !defined(GRAPHITE_STANDARD_UNLIT)
    #define GRAPHITE_STANDARD_LIT
#endif

#if defined(GRAPHITE_STANDARD_SHADOW) && (defined(_SURFACE_TRANSPARENT) || (!defined(GRAPHITE_STANDARD_UNLIT) && (defined(PBR_TRANSMISSION) || (defined(PBR_SUBSURFACE) && defined(PBR_THIN_WALLED)))))
    #define GRAPHITE_STANDARD_SHADOW_DITHER
#endif

// Passes whose fragments read the vertex colour and relative position: forward, back depth, dithered shadow casters, and the depth passes of cutout surfaces
#if defined(GRAPHITE_STANDARD_FORWARD) || defined(GRAPHITE_STANDARD_BACK_DEPTH) || defined(GRAPHITE_STANDARD_SHADOW_DITHER) || defined(_SURFACE_CUTOUT)
    #define GRAPHITE_STANDARD_VERTEX_DATA
#endif

#ifdef GRAPHITE_VERTEX
    #define GRAPHITE_STANDARD_VARYING out
    #include "Graphite/Vertex.glsl"
#else
    #define GRAPHITE_STANDARD_VARYING in
    #include "Graphite/Core.glsl"
    #ifdef GRAPHITE_STANDARD_LIT
        #include "Graphite/PBR/Closure.glsl"
        #include "Graphite/Decals.glsl"
        #ifdef PBR_TRANSMISSION
            #include "Graphite/PBR/Transmission.glsl"
        #endif
    #endif
    #ifdef GRAPHITE_STANDARD_BACK_DEPTH
        #include "Graphite/SceneColor.glsl"
    #endif
    #if defined(GRAPHITE_STANDARD_SHADOW_DITHER) && !defined(GRAPHITE_STANDARD_UNLIT)
        #include "Graphite/PBR/Shadow.glsl"
    #endif
    #ifdef GRAPHITE_STANDARD_FORWARD
        #include "Graphite/Lighting.glsl"
        #include "Graphite/Passes.glsl"
    #endif
    #ifdef GRAPHITE_STANDARD_DEBUG
        #include "Graphite/Debug.glsl"
        #ifdef GRAPHITE_STANDARD_LIT
            #include "Graphite/PBR/Debug.glsl"
        #endif
    #endif
#endif

// vUV and vUV1 are the mesh's two UV sets as they are, vLightmapUV the lightmap UV placed
#if defined(GRAPHITE_STANDARD_FORWARD) || defined(GRAPHITE_STANDARD_ALPHA) || defined(GRAPHITE_STANDARD_SHADOW_DITHER)
GRAPHITE_STANDARD_VARYING vec2 vUV;
GRAPHITE_STANDARD_VARYING vec2 vUV1;
#endif

#ifdef GRAPHITE_STANDARD_VERTEX_DATA
GRAPHITE_STANDARD_VARYING vec3 vRelativePos;
GRAPHITE_STANDARD_VARYING vec4 vColor;
#endif

#ifdef GRAPHITE_STANDARD_LIT
GRAPHITE_STANDARD_VARYING vec3 vNormal;
GRAPHITE_STANDARD_VARYING vec3 vTangent;
GRAPHITE_STANDARD_VARYING vec3 vBitangent;
GRAPHITE_STANDARD_VARYING vec2 vLightmapUV;
flat GRAPHITE_STANDARD_VARYING float vHasTangents;
#endif

// A tangent frame for a world normal, from the mesh's tangent when it has a usable one, else from an arbitrary axis
void Graphite_StandardTangentFrame(vec3 worldNormal, vec3 worldTangent, float handedness, bool hasTangent, out vec3 tangent, out vec3 bitangent)
{
    float sign = handedness < 0.0 ? -1.0 : 1.0;
    if (hasTangent)
    {
        tangent = worldTangent;
        bitangent = cross(worldNormal, tangent) * sign;
        if (dot(bitangent, bitangent) > 0.000001)
            return;
    }
    tangent = abs(worldNormal.y) < 0.999 ? normalize(cross(worldNormal, vec3(0.0, 1.0, 0.0))) : normalize(cross(worldNormal, vec3(1.0, 0.0, 0.0)));
    bitangent = cross(worldNormal, tangent) * sign;
}

#ifdef GRAPHITE_VERTEX

// The vertex a SurfaceVertex function may move before it is transformed, in object space
struct Graphite_SurfaceVertex
{
    vec3 Position;
    vec3 Normal;
    vec4 Tangent;           // w is the handedness
    vec4 Color;
    vec2 UV;
    vec2 UV1;
    float Time;             // seconds, last frame's while the forward pass finds where the vertex was for motion vectors
};

#endif

#ifdef GRAPHITE_FRAGMENT

// What a Surface function reads about the point it shades. Depth passes run it only for cutout surfaces, with the UVs,
// vertex colour and relative position filled in and the rest at defaults.
struct Graphite_SurfaceData
{
    vec2 UV;                // UV0, before any texture's scale and offset
    vec2 UV1;               // UV1, before any texture's scale and offset
    vec4 VertexColor;
    vec3 RelativePos;       // relative to the camera, Graphite_RelativeToWorld for world space
    vec3 ViewDir;           // from the surface toward the camera
    vec3 GeometryNormal;    // the mesh's, facing the camera on back faces
    vec3 Tangent;           // world space, with Bitangent and GeometryNormal the frame tangent space normal maps use,
    vec3 Bitangent;         // arbitrary when the mesh has no tangents
};

// A texture's UV: set 0 picks UV0, anything else UV1, scaled, turned by rotation's cosine and sine and offset, the
// order of KHR_texture_transform
vec2 Graphite_StandardUV(int set, vec4 scaleOffset, vec2 rotation, Graphite_SurfaceData i)
{
    vec2 uv = (set == 0 ? i.UV : i.UV1) * scaleOffset.xy;
    return scaleOffset.zw + vec2(rotation.x * uv.x - rotation.y * uv.y, rotation.y * uv.x + rotation.x * uv.y);
}

vec2 Graphite_StandardUV(int set, vec4 scaleOffset, Graphite_SurfaceData i)
{
    return (set == 0 ? i.UV : i.UV1) * scaleOffset.xy + scaleOffset.zw;
}

Graphite_SurfaceData Graphite_NewSurfaceData(vec2 uv, vec2 uv1)
{
    Graphite_SurfaceData i;
    i.UV = uv;
    i.UV1 = uv1;
    i.VertexColor = vec4(1.0);
    i.RelativePos = vec3(0.0);
    i.ViewDir = vec3(0.0, 0.0, 1.0);
    i.GeometryNormal = vec3(0.0, 1.0, 0.0);
    i.Tangent = vec3(1.0, 0.0, 0.0);
    i.Bitangent = vec3(0.0, 0.0, -1.0);
    return i;
}

// A tangent space normal map texel, scaled, into world space, the surface normal when the result is degenerate.
// Z is rebuilt from X and Y, so two channel maps such as BC5 read the same as three channel ones.
vec3 Graphite_StandardNormalMap(vec3 texel, vec3 normal, vec3 tangent, vec3 bitangent, float scale)
{
    vec3 normalTS;
    normalTS.xy = texel.xy * 2.0 - 1.0;
    normalTS.z = sqrt(max(0.0, 1.0 - dot(normalTS.xy, normalTS.xy)));
    normalTS.xy *= scale;
    vec3 n = mat3(normalize(tangent), normalize(bitangent), normalize(normal)) * normalTS;
    float len2 = dot(n, n);
    return len2 > 1e-8 ? n * inversesqrt(len2) : normalize(normal);
}

// A tangent space normal map texel on the mesh frame of the inputs, scaled, into a world space normal
vec3 Graphite_SurfaceNormalMap(Graphite_SurfaceData i, vec3 texel, float scale)
{
    return Graphite_StandardNormalMap(texel, i.GeometryNormal, i.Tangent, i.Bitangent, scale);
}

#endif

#ifdef GRAPHITE_STANDARD_UNLIT
#include "Graphite/Standard/UnlitSurface.glsl"
#else
#include "Graphite/Standard/PBRSurface.glsl"
#endif
