// The standard surface's fragment stage, Graphite_StandardFragment, included after Graphite/Standard/Standard.glsl and
// the shader's Surface function. See Standard.glsl.
#pragma once

#include "Graphite/Standard/Standard.glsl"

#ifdef GRAPHITE_FRAGMENT

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
    vec2 uv1 = vUV1;
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

    // Parallax occlusion mapping moves UV0 only, a height map means nothing in a second set's parameterisation. The
    // shift found in the height map's scale and offset moves UV0 by the same amount in that texture's units.
#ifdef _PARALLAXMAP
    if (hasTangents && _Parallax > 0.0 && _ParallaxSteps > 0)
    {
        mat3 TBN = mat3(T, B, N);
        vec3 viewDirTS = normalize(transpose(TBN) * viewDir);
        vec2 heightUV = Graphite_StandardUV(0, _ParallaxMap_ST, uv, uv1);
        vec2 shifted = ParallaxOcclusionMapping(_ParallaxMap_Texture, _ParallaxMap_Sampler, heightUV, viewDirTS, _Parallax, _ParallaxSteps);
        uv += (shifted - heightUV) / _ParallaxMap_ST.xy;
    }
#endif
#else
    vec3 N = vec3(0.0, 1.0, 0.0);
#endif

    vec4 albedoTexel = texture(_MainTex, Graphite_StandardUV(_MainTexUV, _MainTex_ST, uv, uv1));
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
        worldNormal = Graphite_StandardNormalMap(texture(_NormalTex, Graphite_StandardUV(_NormalTexUV, _NormalTex_ST, uv, uv1)).rgb, N, T, B, _NormalScale);
#endif

    // Surface map: G roughness, B metallic, each scaling its factor
    roughness = _Roughness;
    metallic = _Metallic;
#ifdef _SURFACEMAP
    vec4 surface = texture(_SurfaceTex, Graphite_StandardUV(_SurfaceTexUV, _SurfaceTex_ST, uv, uv1));
    roughness *= surface.g;
    metallic *= surface.b;
#endif

#ifdef _OCCLUSIONMAP
    ao = mix(1.0, texture(_OcclusionTex, Graphite_StandardUV(_OcclusionTexUV, _OcclusionTex_ST, uv, uv1)).r, _OcclusionStrength);
#endif

    // Translucency map: G extra occlusion, B thickness
    float translucency = 0.0;
    float scatteringPower = 0.0, scatteringDistortion = 0.5, scatteringScale = 1.0;
#ifdef _TRANSLUCENCY
    vec4 transOcc = texture(_TranslucencyMap, Graphite_StandardUV(0, _TranslucencyMap_ST, uv, uv1));
    ao *= transOcc.g;
    translucency = transOcc.b * _TranslucencyStrength;
    scatteringPower = _ScatteringPower;
    scatteringDistortion = _ScatteringDistortion;
    scatteringScale = _ScatteringScale;
#endif

#ifdef _EMISSION
    emission = texture(_EmissionTex, Graphite_StandardUV(_EmissionTexUV, _EmissionTex_ST, uv, uv1)).rgb * _EmissiveColor.rgb * _EmissionIntensity;
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
#ifdef GRAPHITE_STANDARD_DEBUG
    color = Graphite_DebugView(color, baseColor, vec3(0.0), 1.0, 0.0, 1.0, vec3(0.0), vWorldPos, vec2(0.0));
#endif
#ifndef _SURFACE_TRANSPARENT
    Graphite_WriteGBuffer(vec3(0.0, 1.0, 0.0), 1.0, 0.0);
#endif
#else
    roughness = clamp(roughness, GRAPHITE_MIN_ROUGHNESS, 1.0);
    metallic = clamp(metallic, 0.0, 1.0);
#ifdef GRAPHITE_STANDARD_DEBUG
    Graphite_DebugInputs(baseColor, metallic);
#endif

#ifdef _ANISOTROPIC
    // RG holds a direction in the tangent plane, (0.5, 0.5) keeps the mesh tangent
    vec2 anisoDir = texture(_AnisoDirectionMap, Graphite_StandardUV(0, _AnisoDirectionMap_ST, uv, uv1)).rg * 2.0 - 1.0;
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
#ifdef GRAPHITE_STANDARD_DEBUG
    color = Graphite_DebugView(color, baseColor, worldNormal, roughness, metallic, ao, emission, vWorldPos, vLightmapUV);
#endif
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
    vec4 albedo = texture(_MainTex, Graphite_StandardUV(_MainTexUV, _MainTex_ST, vUV, vUV1)) * _MainColor;
#ifdef GRAPHITE_SURFACE_FUNCTION
    Graphite_SurfaceData s = Graphite_NewSurfaceData(vUV, vUV1, albedo.rgb, albedo.a);
    Surface(s);
    albedo.a = s.Alpha;
#endif
    if (albedo.a < _AlphaCutoff)
        discard;
#endif
}

#endif

#endif
