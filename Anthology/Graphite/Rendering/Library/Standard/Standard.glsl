// The standard surface shared by Graphite/Lit, Graphite/Unlit and every shader with a Surface block, whose passes are
// the surface model's: each defines its pass and model in its Shared section and includes this in both stages. The
// vertex stage includes Graphite/Standard/StandardVertex.glsl after the model's "#pragma surface_vertex" line and calls
// Graphite_StandardVertex from main, the fragment stage includes Graphite/Standard/StandardFragment.glsl after the
// model's "#pragma surface" line and calls Graphite_StandardFragment from main.
//
// Pass defines, exactly one: GRAPHITE_STANDARD_FORWARD, GRAPHITE_STANDARD_DEPTH (prepass and shadow caster).
// GRAPHITE_STANDARD_DEBUG: with the forward define, the Debug pass, showing the view's GRAPHITE_DEBUG value. See Graphite/Debug.glsl.
// Model define: GRAPHITE_STANDARD_UNLIT for no lighting, ambient or PBR inputs.
// GRAPHITE_SURFACE_FUNCTION: the shader defines void Surface(inout Graphite_SurfaceData s), run before lighting.
// GRAPHITE_SURFACE_VERTEX_FUNCTION: the shader defines void SurfaceVertex(inout Graphite_SurfaceVertex v), run in every
// pass before the vertex is transformed, so shadows and depth match. It animates with v.Time for motion vectors to follow.
//
// Material axes, set by SurfaceSettings and by the properties that drive them:
//   _SURFACE        OPAQUE, CUTOUT (alpha test against _AlphaCutoff) or TRANSPARENT (alpha reaches the target)
//   _INSTANCE_TINT  the object's custom0, an instance batch's per instance colour, multiplies the vertex colour
// Back faces, drawn when the material culls none, shade with a flipped frame.
//   _NORMALMAP _SURFACEMAP _OCCLUSIONMAP _EMISSION _PARALLAXMAP _TRANSLUCENCY _ANISOTROPIC   optional lit inputs, declared by Lit
//
// Every texture samples its UV set through its own scale and offset, <name>_ST.
// Colour textures are sampled as sRGB textures decode them, every factor is linear and applied after.
#pragma once

#pragma variant _SURFACE OPAQUE CUTOUT TRANSPARENT
#pragma variant _INSTANCE_TINT

#if defined(_SURFACE_CUTOUT) || defined(_SURFACE_TRANSPARENT)
    #define GRAPHITE_STANDARD_ALPHA
#endif

#if defined(GRAPHITE_STANDARD_FORWARD) && !defined(GRAPHITE_STANDARD_UNLIT)
    #define GRAPHITE_STANDARD_LIT
#endif

#ifdef GRAPHITE_VERTEX
    #define GRAPHITE_STANDARD_VARYING out
    #include "Graphite/Vertex.glsl"
#else
    #define GRAPHITE_STANDARD_VARYING in
    #ifdef GRAPHITE_STANDARD_FORWARD
        #include "Graphite/Lighting.glsl"
        #include "Graphite/Decals.glsl"
        #include "Graphite/Passes.glsl"
    #endif
    #ifdef GRAPHITE_STANDARD_DEBUG
        #include "Graphite/Debug.glsl"
    #endif
#endif

#if defined(GRAPHITE_STANDARD_FORWARD) || defined(GRAPHITE_STANDARD_ALPHA)
uniform sampler2D _MainTex;
uniform vec4 _MainTex_ST;
uniform vec4 _MainColor;
uniform int _MainTexUV;
#endif

#ifdef _SURFACE_CUTOUT
uniform float _AlphaCutoff;
#endif

#ifdef GRAPHITE_STANDARD_LIT
uniform float _Metallic;
uniform float _Roughness;
#ifdef _NORMALMAP
uniform sampler2D _NormalTex;
uniform vec4 _NormalTex_ST;
uniform float _NormalScale;
uniform int _NormalTexUV;
#endif
#ifdef _SURFACEMAP
uniform sampler2D _SurfaceTex;
uniform vec4 _SurfaceTex_ST;
uniform int _SurfaceTexUV;
#endif
#ifdef _OCCLUSIONMAP
uniform sampler2D _OcclusionTex;
uniform vec4 _OcclusionTex_ST;
uniform float _OcclusionStrength;
uniform int _OcclusionTexUV;
#endif
#ifdef _EMISSION
uniform sampler2D _EmissionTex;
uniform vec4 _EmissionTex_ST;
uniform vec4 _EmissiveColor;
uniform float _EmissionIntensity;
uniform int _EmissionTexUV;
#endif
#ifdef _PARALLAXMAP
uniform sampler2D _ParallaxMap;
uniform vec4 _ParallaxMap_ST;
uniform float _Parallax;
uniform int _ParallaxSteps;
#endif
#ifdef _TRANSLUCENCY
uniform sampler2D _TranslucencyMap;
uniform vec4 _TranslucencyMap_ST;
uniform float _TranslucencyStrength;
uniform float _ScatteringPower;
uniform float _ScatteringDistortion;
uniform float _ScatteringScale;
#endif
#ifdef _ANISOTROPIC
uniform float _Anisotropy;
uniform sampler2D _AnisoDirectionMap;
uniform vec4 _AnisoDirectionMap_ST;
#endif
#endif

// vUV and vUV1 are the mesh's two UV sets as they are, vLightmapUV the lightmap UV placed
#if defined(GRAPHITE_STANDARD_FORWARD) || defined(GRAPHITE_STANDARD_ALPHA)
GRAPHITE_STANDARD_VARYING vec2 vUV;
GRAPHITE_STANDARD_VARYING vec2 vUV1;
#endif

#ifdef GRAPHITE_STANDARD_FORWARD
GRAPHITE_STANDARD_VARYING vec3 vWorldPos;
GRAPHITE_STANDARD_VARYING vec4 vColor;
#ifdef GRAPHITE_STANDARD_LIT
GRAPHITE_STANDARD_VARYING vec3 vNormal;
GRAPHITE_STANDARD_VARYING vec3 vTangent;
GRAPHITE_STANDARD_VARYING vec3 vBitangent;
GRAPHITE_STANDARD_VARYING vec2 vLightmapUV;
flat GRAPHITE_STANDARD_VARYING float vHasTangents;
#endif
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

// A texture's UV: set 0 picks UV0, anything else UV1, then the texture's scale and offset
vec2 Graphite_StandardUV(int set, vec4 scaleOffset, vec2 uv0, vec2 uv1)
{
    return (set == 0 ? uv0 : uv1) * scaleOffset.xy + scaleOffset.zw;
}

// The standard surface a Surface function may change before it is lit. Depth passes call it only for cutout surfaces,
// with the UVs, base colour and alpha filled in and the rest at defaults.
struct Graphite_SurfaceData
{
    vec2 UV;                // UV0, moved by parallax, before any texture's scale and offset
    vec2 UV1;               // UV1, before any texture's scale and offset
    vec4 VertexColor;
    vec3 WorldPos;          // camera relative
    vec3 ViewDir;           // from the surface toward the camera
    vec3 GeometryNormal;    // the mesh's, facing the camera on back faces
    vec3 BaseColor;
    float Alpha;
    vec3 Normal;            // world space, after the normal map
    float Metallic;
    float Roughness;
    float Occlusion;
    vec3 Emission;
};

Graphite_SurfaceData Graphite_NewSurfaceData(vec2 uv, vec2 uv1, vec3 baseColor, float alpha)
{
    Graphite_SurfaceData s;
    s.UV = uv;
    s.UV1 = uv1;
    s.VertexColor = vec4(1.0);
    s.WorldPos = vec3(0.0);
    s.ViewDir = vec3(0.0, 0.0, 1.0);
    s.GeometryNormal = vec3(0.0, 1.0, 0.0);
    s.BaseColor = baseColor;
    s.Alpha = alpha;
    s.Normal = vec3(0.0, 1.0, 0.0);
    s.Metallic = 0.0;
    s.Roughness = 1.0;
    s.Occlusion = 1.0;
    s.Emission = vec3(0.0);
    return s;
}

#endif
