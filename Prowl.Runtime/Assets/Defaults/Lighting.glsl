// Forward lighting utilities for Prowl Engine
// Include this in any lit forward shader (Standard, Terrain, Grass, etc.)
// Provides: CalculateForwardLighting(), CalculateAmbient(), ApplyFog()
//
// Light data lives in two BVHs (static + dynamic). Per fragment we walk both trees and
// accumulate contributions from every light whose tight AABB contains worldPos. The single
// directional light is uploaded separately and evaluated unconditionally (no BVH).

#ifndef PROWL_LIGHTING
#define PROWL_LIGHTING

#include "PBR"
#include "Shadow"
#include "LightBVH"

// ============================================================
//  Directional light (a single one per scene)
// ============================================================

uniform int   _DirectionalLightEnabled;     // 0 / 1
uniform vec3  _DirectionalLightDirection;
uniform vec3  _DirectionalLightColor;
uniform float _DirectionalLightIntensity;
uniform int   _DirectionalLightShadowEnabled;
uniform float _DirectionalLightShadowBias;
uniform float _DirectionalLightShadowNormalBias;
uniform float _DirectionalLightShadowStrength;
uniform float _DirectionalLightShadowQuality;

// ============================================================
//  Local-light shadow atlas (closest N point + spot lights share these slots)
// ============================================================

#ifndef MAX_SHADOW_CASTERS
#define MAX_SHADOW_CASTERS 4
#endif

// Shadow atlas
uniform sampler2D _ShadowAtlas;
uniform vec2 _ShadowAtlasSize;

// Directional cascade shadows (one directional light)
uniform int  _CascadeCount;
uniform mat4 _CascadeShadowMatrix0;
uniform mat4 _CascadeShadowMatrix1;
uniform mat4 _CascadeShadowMatrix2;
uniform mat4 _CascadeShadowMatrix3;
uniform vec4 _CascadeAtlasParams0;
uniform vec4 _CascadeAtlasParams1;
uniform vec4 _CascadeAtlasParams2;
uniform vec4 _CascadeAtlasParams3;

// Point shadows (6 faces per light). A point light occupying slot s uses indices [s*6 .. s*6+5].
uniform mat4 _PointShadowMatrices[MAX_SHADOW_CASTERS * 6];
uniform vec4 _PointShadowFaceParams[MAX_SHADOW_CASTERS * 6]; // xy: atlasPos, z: faceSize, w: farPlane

// Spot shadows (1 matrix per light, indexed by slot directly).
uniform mat4 _SpotShadowMatrices[MAX_SHADOW_CASTERS];
uniform vec4 _SpotShadowAtlasParams[MAX_SHADOW_CASTERS]; // xy: atlasPos, z: atlasSize, w: unused

// ============================================================
//  Fog uniforms
// ============================================================

uniform vec4 _FogColor;
uniform vec4 _FogParams;
uniform vec3 _FogStates;

// ============================================================
//  Ambient lighting uniforms
// ============================================================

uniform vec2 _AmbientMode;
uniform vec4 _AmbientColor;
uniform vec4 _AmbientSkyColor;
uniform vec4 _AmbientGroundColor;
uniform float _AmbientStrength;

// ============================================================
//  Helpers preserved for shader-graph access
// ============================================================

// Unit direction FROM the surface TO the camera, world-space.
vec3 GetWorldViewDir(vec3 worldPos)
{
    return normalize(_WorldSpaceCameraPos.xyz - worldPos);
}

// Unit direction FROM the surface TO the camera in tangent space.
vec3 GetTangentViewDir(vec3 worldPos, vec3 worldNormal, vec3 worldTangent, vec3 worldBitangent)
{
    vec3 vWorld = GetWorldViewDir(worldPos);
    mat3 tbnT = transpose(mat3(normalize(worldTangent), normalize(worldBitangent), normalize(worldNormal)));
    return normalize(tbnT * vWorld);
}

// ============================================================
//  Shadow sampling
// ============================================================

float SampleDirectionalShadow(vec3 worldPos, vec3 worldNormal)
{
    if (_CascadeCount == 0) return 0.0;

    float worldDistance = distance(worldPos, _WorldSpaceCameraPos.xyz) * 2.0;

    mat4 cascadeMatrix;
    vec4 cascadeParams;

    if (_CascadeCount >= 1 && worldDistance <= _CascadeAtlasParams0.w) {
        cascadeMatrix = _CascadeShadowMatrix0;
        cascadeParams = _CascadeAtlasParams0;
    } else if (_CascadeCount >= 2 && worldDistance <= _CascadeAtlasParams1.w) {
        cascadeMatrix = _CascadeShadowMatrix1;
        cascadeParams = _CascadeAtlasParams1;
    } else if (_CascadeCount >= 3 && worldDistance <= _CascadeAtlasParams2.w) {
        cascadeMatrix = _CascadeShadowMatrix2;
        cascadeParams = _CascadeAtlasParams2;
    } else if (_CascadeCount >= 4 && worldDistance <= _CascadeAtlasParams3.w) {
        cascadeMatrix = _CascadeShadowMatrix3;
        cascadeParams = _CascadeAtlasParams3;
    } else {
        // Beyond all cascades: clamp to last available.
        if      (_CascadeCount == 1) { cascadeMatrix = _CascadeShadowMatrix0; cascadeParams = _CascadeAtlasParams0; }
        else if (_CascadeCount == 2) { cascadeMatrix = _CascadeShadowMatrix1; cascadeParams = _CascadeAtlasParams1; }
        else if (_CascadeCount == 3) { cascadeMatrix = _CascadeShadowMatrix2; cascadeParams = _CascadeAtlasParams2; }
        else                         { cascadeMatrix = _CascadeShadowMatrix3; cascadeParams = _CascadeAtlasParams3; }
    }

    if (cascadeParams.z <= 0.0) return 0.0;

    vec3 worldPosBiased = worldPos + normalize(worldNormal) * _DirectionalLightShadowNormalBias;
    vec4 lightSpacePos = cascadeMatrix * vec4(worldPosBiased, 1.0);
    vec3 projCoords = lightSpacePos.xyz / lightSpacePos.w;
    projCoords = projCoords * 0.5 + 0.5;

    if (projCoords.z > 1.0 || projCoords.x < 0.0 || projCoords.x > 1.0 || projCoords.y < 0.0 || projCoords.y > 1.0)
        return 0.0;

    float atlasSize = _ShadowAtlasSize.x;
    vec2 atlasCoords, shadowMin, shadowMax;
    GetAtlasCoordinates(projCoords, cascadeParams, atlasSize, atlasCoords, shadowMin, shadowMax);

    float slopeBias = CalculateSlopeBias(worldNormal, _DirectionalLightDirection, _DirectionalLightShadowBias);
    float texelWorldSize = ((cascadeParams.w * 4.0) / (cascadeParams.z * atlasSize)) * 8.0;
    float currentDepth = projCoords.z - (slopeBias + texelWorldSize);

    return SampleShadowPCF(_ShadowAtlas, atlasCoords, shadowMin, shadowMax,
                           currentDepth, _DirectionalLightShadowQuality, _DirectionalLightShadowStrength);
}

float SamplePointShadow(LightSample L, int shadowSlot, vec3 worldPos, vec3 worldNormal)
{
    vec3 lightToFrag = worldPos - L.Position;
    vec3 absDir = abs(lightToFrag);

    int faceIndex = 0;
    if (absDir.x >= absDir.y && absDir.x >= absDir.z)
        faceIndex = lightToFrag.x > 0.0 ? 0 : 1;
    else if (absDir.y >= absDir.x && absDir.y >= absDir.z)
        faceIndex = lightToFrag.y > 0.0 ? 2 : 3;
    else
        faceIndex = lightToFrag.z > 0.0 ? 4 : 5;

    int idx = shadowSlot * 6 + faceIndex;
    mat4 shadowMatrix = _PointShadowMatrices[idx];
    vec4 faceParams = _PointShadowFaceParams[idx];

    vec3 worldPosBiased = worldPos + normalize(worldNormal) * L.ShadowNormalBias;
    vec4 lightSpacePos = shadowMatrix * vec4(worldPosBiased, 1.0);
    vec3 projCoords = lightSpacePos.xyz / lightSpacePos.w;
    projCoords = projCoords * 0.5 + 0.5;

    if (projCoords.z > 1.0 || projCoords.x < 0.0 || projCoords.x > 1.0 || projCoords.y < 0.0 || projCoords.y > 1.0)
        return 0.0;

    float atlasSize = _ShadowAtlasSize.x;
    vec2 atlasCoords, shadowMin, shadowMax;
    GetAtlasCoordinates(projCoords, faceParams, atlasSize, atlasCoords, shadowMin, shadowMax);

    float finalBias = CalculateSlopeBias(worldNormal, normalize(lightToFrag), L.ShadowBias);
    float currentDepth = projCoords.z - finalBias;

    return SampleShadowPCF(_ShadowAtlas, atlasCoords, shadowMin, shadowMax,
                           currentDepth, L.ShadowQuality, L.ShadowStrength);
}

float SampleSpotShadow(LightSample L, int shadowSlot, vec3 worldPos, vec3 worldNormal)
{
    vec4 atlasParams = _SpotShadowAtlasParams[shadowSlot];
    if (atlasParams.z <= 0.0) return 0.0;

    vec3 worldPosBiased = worldPos + normalize(worldNormal) * L.ShadowNormalBias;
    vec4 lightSpacePos = _SpotShadowMatrices[shadowSlot] * vec4(worldPosBiased, 1.0);
    vec3 projCoords = lightSpacePos.xyz / lightSpacePos.w;
    projCoords = projCoords * 0.5 + 0.5;

    if (projCoords.z > 1.0 || projCoords.x < 0.0 || projCoords.x > 1.0 || projCoords.y < 0.0 || projCoords.y > 1.0)
        return 0.0;

    float atlasSize = _ShadowAtlasSize.x;
    vec2 atlasCoords, shadowMin, shadowMax;
    GetAtlasCoordinates(projCoords, atlasParams, atlasSize, atlasCoords, shadowMin, shadowMax);

    float finalBias = CalculateSlopeBias(worldNormal, L.Direction, L.ShadowBias);
    float currentDepth = projCoords.z - finalBias;

    return SampleShadowPCF(_ShadowAtlas, atlasCoords, shadowMin, shadowMax,
                           currentDepth, L.ShadowQuality, L.ShadowStrength);
}

// ============================================================
//  Per-light evaluation (BVH leaf -> radiance)
// ============================================================

vec3 EvaluateLocalLight(LightSample L, vec3 worldPos, vec3 worldNormal, vec3 viewDir,
                        vec3 albedo, float metallic, float roughness, float ao, vec3 F0)
{
    // BVH only emits point + spot leaves; directional has its own path. The leaf-level sphere
    // test in LBVH_Next has already rejected fragments past Range, so we don't repeat it here.
    //
    // Two remaining rejections before PBR:
    //   1) Spot cone reject (outer-cone-cosine test before any GGX work).
    //   2) Backface reject (NdotL <= 0 contributes 0; skip GGX/Smith/Fresnel/shadow sampling).
    vec3 lightToPixel = worldPos - L.Position;
    float dist2 = dot(lightToPixel, lightToPixel);
    float dist = sqrt(dist2);
    vec3 lightDir = -lightToPixel * (1.0 / max(dist, 1e-6));

    // Spot pre-check: if outside the outer cone the smoothstep returns 0 anyway.
    if (L.Type == 2) {
        float spotAxisCos = dot(normalize(L.Direction), -lightDir);
        if (spotAxisCos <= L.SpotCos) return vec3(0.0);
    }

    float NdotL = dot(worldNormal, lightDir);
    if (NdotL <= 0.0) return vec3(0.0);

    // Physical inverse-square with smooth window cutoff.
    //   1 / d^2   pure inverse-square in absolute world units
    //   (1 - (d/r)^4)^2   smooth cutoff to 0 at d == Range
    // Range here is purely the cutoff distance, not a scale Intensity is what you tune for
    // visual brightness, in roughly physical units.
    float invR2 = 1.0 / (L.Range * L.Range);
    float factor = dist2 * invR2;            // (d / r)^2
    float window = clamp(1.0 - factor * factor, 0.0, 1.0);
    window *= window;                          // (1 - (d/r)^4)^2
    float invSqr = 1.0 / max(dist2, 0.01);     // 1/d^2 with origin guard
    float attenuation = invSqr * window;

    if (L.Type == 2) {
        float lightAngleCos = dot(normalize(L.Direction), -lightDir);
        attenuation *= smoothstep(L.SpotCos, L.InnerSpotCos, lightAngleCos);
    }

    if (attenuation <= 0.0001) return vec3(0.0);

    vec3 halfDir = normalize(lightDir + viewDir);
    vec3 radiance = L.Color * (L.Intensity * 8.0) * attenuation;

    float NdotV = abs(dot(worldNormal, viewDir));
    float LdotH = max(dot(lightDir, halfDir), 0.0);

    float NDF = DistributionGGX(worldNormal, halfDir, roughness);
    float G = GeometrySmith(worldNormal, viewDir, lightDir, roughness);
    vec3 F = FresnelSchlick(LdotH, F0);

    vec3 kS = F;
    vec3 kD = (vec3(1.0) - kS) * (1.0 - metallic);

    vec3 numerator = NDF * G * F;
    float denominator = 4.0 * NdotV * NdotL + 0.0001;
    vec3 specular = numerator / denominator;

    float diffuseTerm = DisneyDiffuse(NdotV, NdotL, LdotH, roughness);
    vec3 diffuse = kD * albedo * diffuseTerm;

    float shadowFactor;
#ifdef SG_NO_SHADOWS
    shadowFactor = 1.0;
#else
    float shadow = 0.0;
    if (L.ShadowEnabled != 0 && L.ShadowSlot >= 0) {
        if (L.Type == 1)
            shadow = SamplePointShadow(L, L.ShadowSlot, worldPos, worldNormal);
        else
            shadow = SampleSpotShadow(L, L.ShadowSlot, worldPos, worldNormal);
    }
    shadowFactor = 1.0 - shadow;
#endif

    return (diffuse + specular) * radiance * NdotL * shadowFactor * ao;
}

vec3 EvaluateLocalLightAniso(LightSample L, vec3 worldPos, vec3 worldNormal, vec3 viewDir,
                              vec3 worldTangent, vec3 worldBitangent,
                              vec3 albedo, float metallic, float mt, float mb,
                              float perceptualRoughness, float ao, vec3 F0)
{
    // BVH leaf already culled fragments past Range; skip the redundant dist check.
    vec3 lightToPixel = worldPos - L.Position;
    float dist2 = dot(lightToPixel, lightToPixel);
    float dist = sqrt(dist2);
    vec3 lightDir = -lightToPixel * (1.0 / max(dist, 1e-6));

    if (L.Type == 2) {
        float spotAxisCos = dot(normalize(L.Direction), -lightDir);
        if (spotAxisCos <= L.SpotCos) return vec3(0.0);
    }

    float NdotL = dot(worldNormal, lightDir);
    if (NdotL <= 0.0) return vec3(0.0);

    // Physical inverse-square + smooth window cutoff. See EvaluateLocalLight.
    float invR2 = 1.0 / (L.Range * L.Range);
    float factor = dist2 * invR2;
    float window = clamp(1.0 - factor * factor, 0.0, 1.0);
    window *= window;
    float invSqr = 1.0 / max(dist2, 0.01);
    float attenuation = invSqr * window;

    if (L.Type == 2) {
        float lightAngleCos = dot(normalize(L.Direction), -lightDir);
        attenuation *= smoothstep(L.SpotCos, L.InnerSpotCos, lightAngleCos);
    }

    if (attenuation <= 0.0001) return vec3(0.0);

    vec3 halfDir = normalize(lightDir + viewDir);
    float NdotV = abs(dot(worldNormal, viewDir));
    float LdotH = max(dot(lightDir, halfDir), 0.0);
    float NdotH = max(dot(worldNormal, halfDir), 0.0);

    float TdotH = dot(worldTangent, halfDir);
    float BdotH = dot(worldBitangent, halfDir);
    float TdotV = dot(worldTangent, viewDir);
    float BdotV = dot(worldBitangent, viewDir);
    float TdotL = dot(worldTangent, lightDir);
    float BdotL = dot(worldBitangent, lightDir);

    float D = DistributionGGXAniso(TdotH, BdotH, NdotH, mt, mb);
    float V = GeometrySmithAniso(TdotV, BdotV, NdotV, TdotL, BdotL, NdotL, mt, mb);
    vec3 F = FresnelSchlick(LdotH, F0);

    float specularTerm = max(0.0, (V * D) * PI * NdotL);
    vec3 kD = (vec3(1.0) - F) * (1.0 - metallic);

    float diffuseTerm = DisneyDiffuse(NdotV, NdotL, LdotH, perceptualRoughness) * NdotL;

    float shadowFactor;
#ifdef SG_NO_SHADOWS
    shadowFactor = 1.0;
#else
    float shadow = 0.0;
    if (L.ShadowEnabled != 0 && L.ShadowSlot >= 0) {
        if (L.Type == 1)
            shadow = SamplePointShadow(L, L.ShadowSlot, worldPos, worldNormal);
        else
            shadow = SampleSpotShadow(L, L.ShadowSlot, worldPos, worldNormal);
    }
    shadowFactor = 1.0 - shadow;
#endif

    vec3 lightColor = L.Color * (L.Intensity * 8.0) * attenuation * shadowFactor;
    return (kD * albedo * diffuseTerm + specularTerm * F) * lightColor * ao;
}

// ============================================================
//  Directional evaluator
// ============================================================

vec3 EvaluateDirectional(vec3 worldPos, vec3 worldNormal, vec3 viewDir,
                         vec3 albedo, float metallic, float roughness, float ao, vec3 F0)
{
    if (_DirectionalLightEnabled == 0) return vec3(0.0);

    // Prowl convention: a directional light's Transform.Forward points FROM the surface TO
    // the sun, so it already IS the surface-to-light "L" vector. The shadow camera flips it
    // separately (it wants the shining direction). Don't negate here.
    vec3 lightDir = normalize(_DirectionalLightDirection);
    vec3 halfDir = normalize(lightDir + viewDir);
    vec3 radiance = _DirectionalLightColor * (_DirectionalLightIntensity * 8.0);

    float NdotL = max(dot(worldNormal, lightDir), 0.0);
    float NdotV = abs(dot(worldNormal, viewDir));
    float LdotH = max(dot(lightDir, halfDir), 0.0);

    float NDF = DistributionGGX(worldNormal, halfDir, roughness);
    float G = GeometrySmith(worldNormal, viewDir, lightDir, roughness);
    vec3 F = FresnelSchlick(LdotH, F0);

    vec3 kS = F;
    vec3 kD = (vec3(1.0) - kS) * (1.0 - metallic);

    vec3 numerator = NDF * G * F;
    float denominator = 4.0 * NdotV * NdotL + 0.0001;
    vec3 specular = numerator / denominator;

    float diffuseTerm = DisneyDiffuse(NdotV, NdotL, LdotH, roughness);
    vec3 diffuse = kD * albedo * diffuseTerm;

    float shadowFactor;
#ifdef SG_NO_SHADOWS
    shadowFactor = 1.0;
#else
    float shadow = (_DirectionalLightShadowEnabled != 0)
        ? SampleDirectionalShadow(worldPos, worldNormal) : 0.0;
    shadowFactor = 1.0 - shadow;
#endif

    return (diffuse + specular) * radiance * NdotL * shadowFactor * ao;
}

vec3 EvaluateDirectionalAniso(vec3 worldPos, vec3 worldNormal, vec3 viewDir,
                              vec3 worldTangent, vec3 worldBitangent,
                              vec3 albedo, float metallic, float mt, float mb,
                              float perceptualRoughness, float ao, vec3 F0)
{
    if (_DirectionalLightEnabled == 0) return vec3(0.0);

    vec3 lightDir = normalize(_DirectionalLightDirection);
    vec3 halfDir = normalize(lightDir + viewDir);

    float NdotL = max(dot(worldNormal, lightDir), 0.0);
    float NdotV = abs(dot(worldNormal, viewDir));
    float LdotH = max(dot(lightDir, halfDir), 0.0);
    float NdotH = max(dot(worldNormal, halfDir), 0.0);

    float TdotH = dot(worldTangent, halfDir);
    float BdotH = dot(worldBitangent, halfDir);
    float TdotV = dot(worldTangent, viewDir);
    float BdotV = dot(worldBitangent, viewDir);
    float TdotL = dot(worldTangent, lightDir);
    float BdotL = dot(worldBitangent, lightDir);

    float D = DistributionGGXAniso(TdotH, BdotH, NdotH, mt, mb);
    float V = GeometrySmithAniso(TdotV, BdotV, NdotV, TdotL, BdotL, NdotL, mt, mb);
    vec3 F = FresnelSchlick(LdotH, F0);

    float specularTerm = max(0.0, (V * D) * PI * NdotL);
    vec3 kD = (vec3(1.0) - F) * (1.0 - metallic);
    float diffuseTerm = DisneyDiffuse(NdotV, NdotL, LdotH, perceptualRoughness) * NdotL;

    float shadowFactor;
#ifdef SG_NO_SHADOWS
    shadowFactor = 1.0;
#else
    float shadow = (_DirectionalLightShadowEnabled != 0)
        ? SampleDirectionalShadow(worldPos, worldNormal) : 0.0;
    shadowFactor = 1.0 - shadow;
#endif

    vec3 lightColor = _DirectionalLightColor * (_DirectionalLightIntensity * 8.0) * shadowFactor;
    return (kD * albedo * diffuseTerm + specularTerm * F) * lightColor * ao;
}

// ============================================================
//  Forward lighting entry points (BVH-driven)
// ============================================================

vec3 CalculateForwardLighting(vec3 worldPos, vec3 worldNormal, vec3 viewDir,
                              vec3 albedo, float metallic, float roughness, float ao)
{
    roughness = ApplySpecularAA(roughness, worldNormal);
    vec3 F0 = mix(vec3(0.04), albedo, metallic);

    vec3 totalLight = EvaluateDirectional(worldPos, worldNormal, viewDir, albedo, metallic, roughness, ao, F0);

    // Static tree.
    if (_StaticLightRoot >= 0) {
        LBVH_Iter it;
        LBVH_Begin(it, _StaticLightRoot);
        int slot;
        while ((slot = LBVH_Next(it, _StaticLightNodes, _StaticNodeTexSize, _StaticNodeTexShift, worldPos)) >= 0) {
            LightSample L = LBVH_FetchLight(_StaticLightData, _StaticLightTexSize, _StaticLightTexShift, slot);
            totalLight += EvaluateLocalLight(L, worldPos, worldNormal, viewDir, albedo, metallic, roughness, ao, F0);
        }
    }

    // Dynamic tree.
    if (_DynamicLightRoot >= 0) {
        LBVH_Iter it;
        LBVH_Begin(it, _DynamicLightRoot);
        int slot;
        while ((slot = LBVH_Next(it, _DynamicLightNodes, _DynamicNodeTexSize, _DynamicNodeTexShift, worldPos)) >= 0) {
            LightSample L = LBVH_FetchLight(_DynamicLightData, _DynamicLightTexSize, _DynamicLightTexShift, slot);
            totalLight += EvaluateLocalLight(L, worldPos, worldNormal, viewDir, albedo, metallic, roughness, ao, F0);
        }
    }

    return totalLight;
}

vec3 CalculateForwardLightingAniso(vec3 worldPos, vec3 worldNormal, vec3 viewDir,
                                    vec3 worldTangent, vec3 worldBitangent,
                                    vec3 albedo, float metallic,
                                    float roughness, float anisotropy, float ao)
{
    roughness = ApplySpecularAA(roughness, worldNormal);
    float roughnessT = roughness * (1.0 + anisotropy);
    float roughnessB = roughness * (1.0 - anisotropy);
    float mt = roughnessT * roughnessT;
    float mb = roughnessB * roughnessB;

    vec3 F0 = mix(vec3(0.04), albedo, metallic);

    vec3 totalLight = EvaluateDirectionalAniso(worldPos, worldNormal, viewDir, worldTangent, worldBitangent,
                                                albedo, metallic, mt, mb, roughness, ao, F0);

    if (_StaticLightRoot >= 0) {
        LBVH_Iter it;
        LBVH_Begin(it, _StaticLightRoot);
        int slot;
        while ((slot = LBVH_Next(it, _StaticLightNodes, _StaticNodeTexSize, _StaticNodeTexShift, worldPos)) >= 0) {
            LightSample L = LBVH_FetchLight(_StaticLightData, _StaticLightTexSize, _StaticLightTexShift, slot);
            totalLight += EvaluateLocalLightAniso(L, worldPos, worldNormal, viewDir, worldTangent, worldBitangent,
                                                   albedo, metallic, mt, mb, roughness, ao, F0);
        }
    }

    if (_DynamicLightRoot >= 0) {
        LBVH_Iter it;
        LBVH_Begin(it, _DynamicLightRoot);
        int slot;
        while ((slot = LBVH_Next(it, _DynamicLightNodes, _DynamicNodeTexSize, _DynamicNodeTexShift, worldPos)) >= 0) {
            LightSample L = LBVH_FetchLight(_DynamicLightData, _DynamicLightTexSize, _DynamicLightTexShift, slot);
            totalLight += EvaluateLocalLightAniso(L, worldPos, worldNormal, viewDir, worldTangent, worldBitangent,
                                                   albedo, metallic, mt, mb, roughness, ao, F0);
        }
    }

    return totalLight;
}

// ============================================================
//  Ambient lighting
// ============================================================

vec3 CalculateAmbient(vec3 worldNormal)
{
    vec3 ambient = vec3(0.0);
    ambient += _AmbientColor.rgb * _AmbientMode.x;

    float upDot = dot(worldNormal, vec3(0.0, 1.0, 0.0));
    ambient += mix(_AmbientGroundColor.rgb, _AmbientSkyColor.rgb, upDot * 0.5 + 0.5) * _AmbientMode.y;

    return ambient;
}

// ============================================================
//  Fog
// ============================================================

vec3 ApplyFog(vec3 color, vec3 worldPos)
{
    if (_FogStates.x + _FogStates.y + _FogStates.z < 0.5)
        return color;

    float fogCoord = length(worldPos - _WorldSpaceCameraPos.xyz);
    float prowlFog = 0.0;
    prowlFog += (fogCoord * _FogParams.z + _FogParams.w) * _FogStates.x;
    prowlFog += exp2(-fogCoord * _FogParams.y) * _FogStates.y;
    prowlFog += exp2(-fogCoord * fogCoord * _FogParams.x * _FogParams.x) * _FogStates.z;
    return mix(_FogColor.rgb, color, clamp(prowlFog, 0.0, 1.0));
}

#endif
