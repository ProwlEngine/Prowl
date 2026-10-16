// The standard surface shared by Graphite/Lit, Graphite/Unlit and every shader with a Surface block, whose passes the
// shader parser writes: each defines its pass and model in its Shared section, includes this in both stages and calls
// Graphite_StandardVertex and Graphite_StandardFragment from main.
//
// Pass defines, exactly one: GRAPHITE_STANDARD_FORWARD, GRAPHITE_STANDARD_DEPTH (prepass and shadow caster).
// Model define: GRAPHITE_STANDARD_UNLIT for no lighting, ambient or PBR inputs.
// GRAPHITE_SURFACE_FUNCTION: the shader defines void Surface(inout Graphite_SurfaceData s), run before lighting.
//
// Material axes, set by SurfaceSettings and by the properties that drive them:
//   _SURFACE        OPAQUE, CUTOUT (alpha test against _AlphaCutoff) or TRANSPARENT (alpha reaches the target)
//   _INSTANCE_TINT  the object's custom0, an instance batch's per instance colour, multiplies the vertex colour
// Back faces, drawn when the material culls none, shade with a flipped frame.
//   _NORMALMAP _SURFACEMAP _OCCLUSIONMAP _EMISSION _PARALLAXMAP _TRANSLUCENCY _ANISOTROPIC   optional lit inputs, declared by Lit
//
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
        #include "Graphite/Debug.glsl"
    #endif
#endif

uniform vec2 _Tiling;
uniform vec2 _Offset;

#if defined(GRAPHITE_STANDARD_FORWARD) || defined(GRAPHITE_STANDARD_ALPHA)
uniform sampler2D _MainTex;
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
uniform float _NormalScale;
uniform int _NormalTexUV;
#endif
#ifdef _SURFACEMAP
uniform sampler2D _SurfaceTex;
uniform int _SurfaceTexUV;
#endif
#ifdef _OCCLUSIONMAP
uniform sampler2D _OcclusionTex;
uniform float _OcclusionStrength;
uniform int _OcclusionTexUV;
#endif
#ifdef _EMISSION
uniform sampler2D _EmissionTex;
uniform vec4 _EmissiveColor;
uniform float _EmissionIntensity;
uniform int _EmissionTexUV;
#endif
#ifdef _PARALLAXMAP
uniform sampler2D _ParallaxMap;
uniform float _Parallax;
uniform int _ParallaxSteps;
#endif
#ifdef _TRANSLUCENCY
uniform sampler2D _TranslucencyMap;
uniform float _TranslucencyStrength;
uniform float _ScatteringPower;
uniform float _ScatteringDistortion;
uniform float _ScatteringScale;
#endif
#ifdef _ANISOTROPIC
uniform float _Anisotropy;
uniform sampler2D _AnisoDirectionMap;
#endif
#endif

// vUV is UV0 with the material's tiling and offset, vUV1 the raw second set, the lightmap UV placed
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
        bitangent = cross(tangent, worldNormal) * sign;
        if (dot(bitangent, bitangent) > 0.000001)
            return;
    }
    tangent = abs(worldNormal.y) < 0.999 ? normalize(cross(worldNormal, vec3(0.0, 1.0, 0.0))) : normalize(cross(worldNormal, vec3(1.0, 0.0, 0.0)));
    bitangent = cross(tangent, worldNormal) * sign;
}

#ifdef GRAPHITE_VERTEX

void Graphite_StandardVertex()
{
    vec3 world = Graphite_ObjectToWorld(vertexPosition);
    gl_Position = Graphite_RelativeToClip(world);

#if defined(GRAPHITE_STANDARD_FORWARD) || defined(GRAPHITE_STANDARD_ALPHA)
    vUV = vertexUV0 * _Tiling + _Offset;
    vUV1 = vertexUV1;
#endif

#ifdef GRAPHITE_STANDARD_FORWARD
    vWorldPos = world;
    vColor = vertexColor;
#ifdef _INSTANCE_TINT
    vColor *= Graphite_Object().custom0;
#endif
#ifdef GRAPHITE_STANDARD_LIT
    vNormal = Graphite_ObjectToWorldNormal(vertexNormal);
    vec4 tangent = vertexTangent;
    bool hasTangents = Graphite_HasStream(2u);
    vec3 worldTangent = hasTangents ? normalize(Graphite_ObjectToWorldDirection(tangent.xyz)) : vec3(1.0, 0.0, 0.0);
    Graphite_StandardTangentFrame(vNormal, worldTangent, tangent.w, hasTangents, vTangent, vBitangent);
    vHasTangents = hasTangents ? 1.0 : 0.0;
    vLightmapUV = Graphite_LightmapUV();
#endif
#endif
}

#endif

#ifdef GRAPHITE_FRAGMENT

// Per slot UV set: 0 selects UV0, anything else UV1 with the material transform applied
#define GRAPHITE_STANDARD_UV(set, uv0, uv1) ((set) == 0 ? (uv0) : (uv1))

// The standard surface a Surface function may change before it is lit. Depth passes call it only for cutout surfaces,
// with the UVs, base colour and alpha filled in and the rest at defaults.
struct Graphite_SurfaceData
{
    vec2 UV;                // UV0 with the material's tiling and offset
    vec2 UV1;               // UV1 with the same
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

#ifdef GRAPHITE_SURFACE_FUNCTION
void Surface(inout Graphite_SurfaceData s);
#endif

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

#ifdef GRAPHITE_STANDARD_FORWARD

layout(location = 0) out vec4 fragColor;

#ifdef GRAPHITE_STANDARD_LIT
// A tangent space normal map texel, scaled, into world space, the surface normal when the result is degenerate
vec3 Graphite_StandardNormalMap(vec3 texel, vec3 normal, vec3 tangent, vec3 bitangent, float scale)
{
    vec3 normalTS = texel * 2.0 - 1.0;
    normalTS.xy *= scale;
    vec3 n = mat3(normalize(tangent), normalize(bitangent), normalize(normal)) * normalTS;
    float len2 = dot(n, n);
    return len2 > 1e-8 ? n * inversesqrt(len2) : normalize(normal);
}
#endif

void Graphite_StandardFragment()
{
    vec2 uv = vUV;
    vec2 uv1 = vUV1 * _Tiling + _Offset;
    vec3 viewDir = GetWorldViewDir(vWorldPos);

#ifdef GRAPHITE_STANDARD_LIT
    vec3 N = normalize(vNormal);
    vec3 T = normalize(vTangent);
    vec3 B = normalize(vBitangent);
    if (!gl_FrontFacing)
    {
        N = -N;
        B = -B;
    }
    bool hasTangents = vHasTangents > 0.5;

    // Parallax occlusion mapping moves UV0 only, a height map means nothing in a second set's parameterisation
#ifdef _PARALLAXMAP
    if (hasTangents && _Parallax > 0.0 && _ParallaxSteps > 0)
    {
        mat3 TBN = mat3(T, B, N);
        vec3 viewDirTS = normalize(transpose(TBN) * viewDir);
        uv = ParallaxOcclusionMapping(_ParallaxMap_Texture, _ParallaxMap_Sampler, uv, viewDirTS, _Parallax, _ParallaxSteps);
    }
#endif
#else
    vec3 N = vec3(0.0, 1.0, 0.0);
#endif

    vec4 albedoTexel = texture(_MainTex, GRAPHITE_STANDARD_UV(_MainTexUV, uv, uv1));
    vec3 baseColor = albedoTexel.rgb * _MainColor.rgb * vColor.rgb;
    float alpha = albedoTexel.a * _MainColor.a * vColor.a;

#if defined(_SURFACE_CUTOUT) && !defined(GRAPHITE_SURFACE_FUNCTION)
    if (alpha < _AlphaCutoff)
        discard;
#endif

    vec3 worldNormal = N;
    float roughness = 1.0, metallic = 0.0, ao = 1.0;
    vec3 emission = vec3(0.0);

#ifdef GRAPHITE_STANDARD_LIT
#ifdef _NORMALMAP
    if (hasTangents)
        worldNormal = Graphite_StandardNormalMap(texture(_NormalTex, GRAPHITE_STANDARD_UV(_NormalTexUV, uv, uv1)).rgb, N, T, B, _NormalScale);
#endif

    // Surface map: G roughness, B metallic, each scaling its factor
    roughness = _Roughness;
    metallic = _Metallic;
#ifdef _SURFACEMAP
    vec4 surface = texture(_SurfaceTex, GRAPHITE_STANDARD_UV(_SurfaceTexUV, uv, uv1));
    roughness *= surface.g;
    metallic *= surface.b;
#endif

#ifdef _OCCLUSIONMAP
    ao = mix(1.0, texture(_OcclusionTex, GRAPHITE_STANDARD_UV(_OcclusionTexUV, uv, uv1)).r, _OcclusionStrength);
#endif

    // Translucency map: G extra occlusion, B thickness
    float translucency = 0.0;
    float scatteringPower = 0.0, scatteringDistortion = 0.5, scatteringScale = 1.0;
#ifdef _TRANSLUCENCY
    vec4 transOcc = texture(_TranslucencyMap, uv);
    ao *= transOcc.g;
    translucency = transOcc.b * _TranslucencyStrength;
    scatteringPower = _ScatteringPower;
    scatteringDistortion = _ScatteringDistortion;
    scatteringScale = _ScatteringScale;
#endif

#ifdef _EMISSION
    emission = texture(_EmissionTex, GRAPHITE_STANDARD_UV(_EmissionTexUV, uv, uv1)).rgb * _EmissiveColor.rgb * _EmissionIntensity;
#endif

    Graphite_ApplyDecals(vWorldPos, N, baseColor, worldNormal, roughness, metallic, ao);
#endif

#ifdef GRAPHITE_SURFACE_FUNCTION
    Graphite_SurfaceData s = Graphite_NewSurfaceData(uv, uv1, baseColor, alpha);
    s.VertexColor = vColor;
    s.WorldPos = vWorldPos;
    s.ViewDir = viewDir;
    s.GeometryNormal = N;
    s.Normal = worldNormal;
    s.Metallic = metallic;
    s.Roughness = roughness;
    s.Occlusion = ao;
    s.Emission = emission;
    Surface(s);
    baseColor = s.BaseColor;
    alpha = s.Alpha;
    worldNormal = normalize(s.Normal);
    metallic = s.Metallic;
    roughness = s.Roughness;
    ao = s.Occlusion;
    emission = s.Emission;
#ifdef _SURFACE_CUTOUT
    if (alpha < _AlphaCutoff)
        discard;
#endif
#endif

#ifdef GRAPHITE_STANDARD_UNLIT
    vec3 color = ApplyFog(baseColor + emission, vWorldPos);
    color = Graphite_DebugView(color, baseColor, vec3(0.0), 1.0, 0.0, 1.0, vec3(0.0), vWorldPos, vec2(0.0));
#ifndef _SURFACE_TRANSPARENT
    Graphite_WriteGBuffer(vec3(0.0, 1.0, 0.0), 1.0, 0.0);
#endif
#else
    roughness = clamp(roughness, GRAPHITE_MIN_ROUGHNESS, 1.0);
    metallic = clamp(metallic, 0.0, 1.0);
    Graphite_DebugInputs(baseColor, metallic);

#ifdef _ANISOTROPIC
    // RG holds a direction in the tangent plane, (0.5, 0.5) keeps the mesh tangent
    vec2 anisoDir = texture(_AnisoDirectionMap, uv).rg * 2.0 - 1.0;
    float anisoDirLen = length(anisoDir);
    vec3 anisoTangent = T;
    vec3 anisoBitangent = B;
    if (anisoDirLen > 0.01)
    {
        anisoDir /= anisoDirLen;
        anisoTangent = normalize(T * anisoDir.x + B * anisoDir.y);
        anisoBitangent = normalize(cross(worldNormal, anisoTangent));
    }
    vec3 lighting = CalculateForwardLightingAniso(vWorldPos, worldNormal, N, viewDir, anisoTangent, anisoBitangent,
                                                  baseColor, metallic, roughness, _Anisotropy, ao);
#elif defined(_TRANSLUCENCY)
    vec3 lighting = CalculateForwardLighting(vWorldPos, worldNormal, N, viewDir, baseColor, metallic, roughness, ao,
                                             translucency, scatteringPower, scatteringDistortion, scatteringScale);
#else
    vec3 lighting = CalculateForwardLighting(vWorldPos, worldNormal, N, viewDir, baseColor, metallic, roughness, ao);
#endif

    vec3 ambientLight = Graphite_IndirectDiffuse(worldNormal, vLightmapUV) * ao;
    vec3 ambientDiffuse = ambientLight * baseColor * (1.0 - metallic);
    vec3 F0 = mix(vec3(0.04), baseColor, metallic);
    vec3 ambientSpecular = Graphite_IndirectSpecular(vWorldPos, worldNormal, viewDir, F0, roughness, ao, ambientLight);

    vec3 color = ApplyFog(ambientDiffuse + ambientSpecular + lighting + emission, vWorldPos);
    color = Graphite_DebugView(color, baseColor, worldNormal, roughness, metallic, ao, emission, vWorldPos, vLightmapUV);
#ifndef _SURFACE_TRANSPARENT
    Graphite_WriteGBuffer(worldNormal, roughness, metallic);
#endif
#endif

#ifdef _SURFACE_TRANSPARENT
    fragColor = vec4(color, alpha);
#else
    fragColor = vec4(color, 1.0);
#endif
}

#else

// Prepass and shadow caster: depth only, cut out where the alpha test fails
void Graphite_StandardFragment()
{
#ifdef _SURFACE_CUTOUT
    vec2 uv1 = vUV1 * _Tiling + _Offset;
    vec4 albedo = texture(_MainTex, GRAPHITE_STANDARD_UV(_MainTexUV, vUV, uv1)) * _MainColor;
#ifdef GRAPHITE_SURFACE_FUNCTION
    Graphite_SurfaceData s = Graphite_NewSurfaceData(vUV, uv1, albedo.rgb, albedo.a);
    Surface(s);
    albedo.a = s.Alpha;
#endif
    if (albedo.a < _AlphaCutoff)
        discard;
#endif
}

#endif

#endif
