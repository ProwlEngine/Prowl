// Forward lighting utilities for Prowl Engine
// Include this in any lit forward shader (Standard, Terrain, Grass, etc.)
// Provides: CalculateForwardLighting(), CalculateAmbient(), ApplyFog()
//
// Point and spot lights live in two four wide trees (static + dynamic), walked together per
// fragment, adding every light whose sphere holds worldPos. Directional lights are uploaded
// separately and evaluated unconditionally.

#ifndef PROWL_LIGHTING
#define PROWL_LIGHTING

#include "PBR"
#include "Shadow"
#include "LightTree"

// ============================================================
//  Directional lights: the brightest one is the main light and owns the shadow cascades,
//  the rest light unshadowed.
// ============================================================

uniform int   _DirectionalLightEnabled;     // 0 / 1
uniform vec3  _DirectionalLightDirection;
uniform vec3  _DirectionalLightColor;
uniform float _DirectionalLightIntensity;
uniform int   _DirectionalLightShadowEnabled;
uniform float _DirectionalLightShadowDepthBias;    // shadow texels
uniform float _DirectionalLightShadowNormalBias;   // shadow texels
uniform float _DirectionalLightShadowDistance;     // view depth where shadows end
uniform float _DirectionalLightShadowStrength;
uniform float _DirectionalLightShadowQuality;

#ifndef MAX_EXTRA_DIRECTIONAL_LIGHTS
#define MAX_EXTRA_DIRECTIONAL_LIGHTS 4
#endif

uniform int  _ExtraDirectionalLightCount;
uniform vec3 _ExtraDirectionalLightDirection[MAX_EXTRA_DIRECTIONAL_LIGHTS];
uniform vec3 _ExtraDirectionalLightColor[MAX_EXTRA_DIRECTIONAL_LIGHTS]; // color * intensity

// ============================================================
//  Shadow atlas and per light shadow data
// ============================================================

// Shadow atlas (hardware depth-compare sampler; the atlas depth texture has
// GL_TEXTURE_COMPARE_MODE enabled so texture() does the PCF comparison)
uniform sampler2DShadow _ShadowAtlas;
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
uniform vec4 _CascadeAtlasParams3;   // xy: atlasPos, z: tileSize, w: cascade radius
uniform vec4 _CascadeSphere0;        // xyz: center, w: radius
uniform vec4 _CascadeSphere1;
uniform vec4 _CascadeSphere2;
uniform vec4 _CascadeSphere3;

// The view the cascades were fitted to, shadows fade out by depth along it
uniform vec3 _ShadowViewOrigin;
uniform vec3 _ShadowViewForward;

// One block of SHADOW_BLOCK_TEXELS texels per shadowed point or spot light, at L.ShadowSlot. The texture is
// 1 << _ShadowDataShift texels wide.
//   +0     header: x fade (1 full shadow, 0 none)
//   +1..   one rect per face (6 for point, 1 for spot): xy tile position, z tile size, w texel size one unit from the light
//   then   one matrix per face, four columns each
uniform sampler2D _ShadowData;
uniform int _ShadowDataShift;
#define SHADOW_BLOCK_TEXELS 32

vec4 ShadowData(int texel)
{
    return texelFetch(_ShadowData, ivec2(texel & ((1 << _ShadowDataShift) - 1), texel >> _ShadowDataShift), 0);
}

mat4 ShadowDataMatrix(int texel)
{
    return mat4(ShadowData(texel), ShadowData(texel + 1), ShadowData(texel + 2), ShadowData(texel + 3));
}

// ============================================================
//  Fog uniforms
// ============================================================

uniform vec4 _FogColor;
uniform vec4 _FogParams;
uniform vec3 _FogStates;
uniform vec2 _FogSky; // x: color the fog with the sky, y: keep the sun glow

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

// Each returns the fraction of light blocked, 0 to 1. geomNormal is the interpolated surface normal
// before normal mapping (zero for points in the air), quality 0 is hard and 1 soft.

// Whether p sits inside a cascade, at least inset texels in from its edge
bool InCascade(vec3 p, vec4 sphere, vec4 params, float inset)
{
    vec3 d = p - sphere.xyz;
    float r = sphere.w * (1.0 - 2.0 * inset / max(params.z, 1.0));
    return params.z > 0.0 && dot(d, d) <= r * r;
}

// The sharpest cascade holding p, -1 for none
int SelectCascade(vec3 p, float inset)
{
    if (InCascade(p, _CascadeSphere0, _CascadeAtlasParams0, inset)) return 0;
    if (_CascadeCount > 1 && InCascade(p, _CascadeSphere1, _CascadeAtlasParams1, inset)) return 1;
    if (_CascadeCount > 2 && InCascade(p, _CascadeSphere2, _CascadeAtlasParams2, inset)) return 2;
    if (_CascadeCount > 3 && InCascade(p, _CascadeSphere3, _CascadeAtlasParams3, inset)) return 3;
    return -1;
}

float DirectionalShadow(vec3 worldPos, vec3 geomNormal, float normalBias, float quality)
{
    if (_CascadeCount == 0) return 0.0;

    float viewDepth = dot(worldPos - _ShadowViewOrigin, _ShadowViewForward);
    if (viewDepth > _DirectionalLightShadowDistance) return 0.0;

    // Three texels of room keeps the bias offset and the filter inside the map, points right at a
    // cascade's outer corners take whichever cascade holds them at all
    int cascade = SelectCascade(worldPos, 3.0);
    if (cascade < 0) cascade = SelectCascade(worldPos, 0.0);
    if (cascade < 0) return 0.0;

    mat4 cascadeMatrix;
    vec4 cascadeParams;
    if (cascade == 0)      { cascadeMatrix = _CascadeShadowMatrix0; cascadeParams = _CascadeAtlasParams0; }
    else if (cascade == 1) { cascadeMatrix = _CascadeShadowMatrix1; cascadeParams = _CascadeAtlasParams1; }
    else if (cascade == 2) { cascadeMatrix = _CascadeShadowMatrix2; cascadeParams = _CascadeAtlasParams2; }
    else                   { cascadeMatrix = _CascadeShadowMatrix3; cascadeParams = _CascadeAtlasParams3; }

    float texelWorld = 2.0 * cascadeParams.w / cascadeParams.z;
    vec3 biasedPos = ApplyShadowBias(worldPos, geomNormal, normalize(_DirectionalLightDirection), texelWorld,
                                     _DirectionalLightShadowDepthBias, normalBias);
    vec3 projCoords = ProjectToShadowMap(cascadeMatrix, biasedPos);
    if (projCoords.z > 1.0) return 0.0;

    float shadow = SampleShadowPCF(_ShadowAtlas, _ShadowAtlasSize.x, projCoords, cascadeParams, quality);

    // Fades out over the last tenth of the shadow distance
    float fade = smoothstep(_DirectionalLightShadowDistance * 0.9, _DirectionalLightShadowDistance, viewDepth);
    return shadow * _DirectionalLightShadowStrength * (1.0 - fade);
}

float PointShadow(LightSample L, vec3 worldPos, vec3 geomNormal, float normalBias, float quality)
{
    int block = L.ShadowSlot * SHADOW_BLOCK_TEXELS;
    float fade = ShadowData(block).x;
    if (fade <= 0.0) return 0.0;

    vec3 lightToFrag = worldPos - L.Position;
    float dist = length(lightToFrag);
    vec3 absDir = abs(lightToFrag);
    float axisDist = max(absDir.x, max(absDir.y, absDir.z));

    // Every face holds the same tile size, so the first face's texel size serves before a face is picked
    float texelWorld = ShadowData(block + 1).w * axisDist;
    vec3 biasedPos = ApplyShadowBias(worldPos, geomNormal, -lightToFrag / max(dist, 1e-6), texelWorld,
                                     L.ShadowDepthBias, normalBias);

    // The face is picked from the biased position, so the offset can never push it off its face
    vec3 dir = biasedPos - L.Position;
    vec3 absBiased = abs(dir);
    int faceIndex;
    if (absBiased.x >= absBiased.y && absBiased.x >= absBiased.z)
        faceIndex = dir.x > 0.0 ? 0 : 1;
    else if (absBiased.y >= absBiased.z)
        faceIndex = dir.y > 0.0 ? 2 : 3;
    else
        faceIndex = dir.z > 0.0 ? 4 : 5;

    vec4 faceRect = ShadowData(block + 1 + faceIndex);
    if (faceRect.z <= 0.0) return 0.0;

    vec3 projCoords = ProjectToShadowMap(ShadowDataMatrix(block + 7 + faceIndex * 4), biasedPos);
    if (projCoords.z > 1.0) return 0.0;

    return SampleShadowPCF(_ShadowAtlas, _ShadowAtlasSize.x, projCoords, faceRect, quality) * L.ShadowStrength * fade;
}

float SpotShadow(LightSample L, vec3 worldPos, vec3 geomNormal, float normalBias, float quality)
{
    int block = L.ShadowSlot * SHADOW_BLOCK_TEXELS;
    float fade = ShadowData(block).x;
    vec4 rect = ShadowData(block + 1);
    if (fade <= 0.0 || rect.z <= 0.0) return 0.0;

    vec3 lightToFrag = worldPos - L.Position;
    float dist = length(lightToFrag);
    float axisDist = max(dot(lightToFrag, normalize(L.Direction)), 0.0);

    vec3 biasedPos = ApplyShadowBias(worldPos, geomNormal, -lightToFrag / max(dist, 1e-6), rect.w * axisDist,
                                     L.ShadowDepthBias, normalBias);
    vec3 projCoords = ProjectToShadowMap(ShadowDataMatrix(block + 2), biasedPos);
    if (projCoords.z > 1.0 || projCoords.x < 0.0 || projCoords.x > 1.0 || projCoords.y < 0.0 || projCoords.y > 1.0)
        return 0.0;

    return SampleShadowPCF(_ShadowAtlas, _ShadowAtlasSize.x, projCoords, rect, quality) * L.ShadowStrength * fade;
}

float LocalLightShadow(LightSample L, vec3 worldPos, vec3 geomNormal)
{
    if (L.ShadowEnabled == 0 || L.ShadowSlot < 0) return 0.0;
    return L.Type == 1
        ? PointShadow(L, worldPos, geomNormal, L.ShadowNormalBias, L.ShadowQuality)
        : SpotShadow(L, worldPos, geomNormal, L.ShadowNormalBias, L.ShadowQuality);
}

// ============================================================
//  Per-light evaluation (BVH leaf -> radiance)
// ============================================================

vec3 EvaluateLocalLight(LightSample L, vec3 worldPos, vec3 worldNormal, vec3 geomNormal, vec3 viewDir,
                        vec3 albedo, float metallic, float roughness, float ao, vec3 F0)
{
    // BVH only emits point + spot leaves; directional has its own path. The leaf-level sphere
    // test in LightTree_FetchLight has already rejected fragments past Range, so we don't repeat it here.
    //
    // Two remaining rejections before PBR:
    //   1) Spot cone reject (outer-cone-cosine test before any GGX work).
    //   2) Backface reject (NdotL <= 0 contributes 0; skip GGX/Smith/Fresnel/shadow sampling).
    vec3 lightToPixel = worldPos - L.Position;
    float dist2 = dot(lightToPixel, lightToPixel);
    float dist = sqrt(dist2);
    vec3 lightDir = -lightToPixel * (1.0 / max(dist, 1e-6));

    // Spot pre-check: if outside the outer cone the smoothstep returns 0 anyway. The axis cosine
    // is reused below for the cone falloff, so normalize the direction only once.
    float spotAxisCos = 0.0;
    if (L.Type == 2) {
        spotAxisCos = dot(normalize(L.Direction), -lightDir);
        if (spotAxisCos <= L.SpotCos) return vec3(0.0);
    }

    float NdotL = dot(worldNormal, lightDir);
    if (NdotL <= 0.0) return vec3(0.0);

    // Physical inverse-square with smooth window cutoff.
    //   1 / d^2   pure inverse-square in absolute world units
    //   (1 - (d/r)^4)^2   smooth cutoff to 0 at d == Range
    // Range here is purely the cutoff distance, not a scale Intensity is what you tune for
    // visual brightness, in roughly physical units.
    // A Range of 0 is an authoring mistake rather than an impossibility, and it makes invR2 infinite;
    // multiplied by a dist2 of 0 (fragment sitting on the light) that is Inf * 0 = NaN.
    float invR2 = 1.0 / max(L.Range * L.Range, 1e-6);
    float factor = dist2 * invR2;            // (d / r)^2
    float window = clamp(1.0 - factor * factor, 0.0, 1.0);
    window *= window;                          // (1 - (d/r)^4)^2
    float invSqr = 1.0 / max(dist2, 0.01);     // 1/d^2 with origin guard
    float attenuation = invSqr * window;

    if (L.Type == 2)
        attenuation *= smoothstep(L.SpotCos, L.InnerSpotCos, spotAxisCos);

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
    shadowFactor = 1.0 - LocalLightShadow(L, worldPos, geomNormal);
#endif

    return (diffuse + specular) * radiance * NdotL * shadowFactor * ao;
}

vec3 EvaluateLocalLightAniso(LightSample L, vec3 worldPos, vec3 worldNormal, vec3 geomNormal, vec3 viewDir,
                              vec3 worldTangent, vec3 worldBitangent,
                              vec3 albedo, float metallic, float mt, float mb,
                              float perceptualRoughness, float ao, vec3 F0)
{
    // BVH leaf already culled fragments past Range; skip the redundant dist check.
    vec3 lightToPixel = worldPos - L.Position;
    float dist2 = dot(lightToPixel, lightToPixel);
    float dist = sqrt(dist2);
    vec3 lightDir = -lightToPixel * (1.0 / max(dist, 1e-6));

    float spotAxisCos = 0.0;
    if (L.Type == 2) {
        spotAxisCos = dot(normalize(L.Direction), -lightDir);
        if (spotAxisCos <= L.SpotCos) return vec3(0.0);
    }

    float NdotL = dot(worldNormal, lightDir);
    if (NdotL <= 0.0) return vec3(0.0);

    // Physical inverse-square + smooth window cutoff. See EvaluateLocalLight.
    // A Range of 0 is an authoring mistake rather than an impossibility, and it makes invR2 infinite;
    // multiplied by a dist2 of 0 (fragment sitting on the light) that is Inf * 0 = NaN.
    float invR2 = 1.0 / max(L.Range * L.Range, 1e-6);
    float factor = dist2 * invR2;
    float window = clamp(1.0 - factor * factor, 0.0, 1.0);
    window *= window;
    float invSqr = 1.0 / max(dist2, 0.01);
    float attenuation = invSqr * window;

    if (L.Type == 2)
        attenuation *= smoothstep(L.SpotCos, L.InnerSpotCos, spotAxisCos);

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
    shadowFactor = 1.0 - LocalLightShadow(L, worldPos, geomNormal);
#endif

    vec3 lightColor = L.Color * (L.Intensity * 8.0) * attenuation * shadowFactor;
    return (kD * albedo * diffuseTerm + specularTerm * F) * lightColor * ao;
}

// ============================================================
//  Directional evaluator
// ============================================================

// Directional lights shine along their Transform.Forward, and the direction uploaded to the
// shaders is -Forward: it points FROM the surface TO the light, so it already IS the
// surface-to-light "L" vector. Don't negate it.

float MainDirectionalShadowFactor(vec3 worldPos, vec3 geomNormal)
{
#ifdef SG_NO_SHADOWS
    return 1.0;
#else
    float shadow = (_DirectionalLightShadowEnabled != 0)
        ? DirectionalShadow(worldPos, geomNormal, _DirectionalLightShadowNormalBias, _DirectionalLightShadowQuality) : 0.0;
    return 1.0 - shadow;
#endif
}

// One directional light. lightColor is color * intensity.
vec3 ShadeDirectional(vec3 lightDir, vec3 lightColor, float shadowFactor, vec3 worldNormal, vec3 viewDir,
                      vec3 albedo, float metallic, float roughness, float ao, vec3 F0)
{
    vec3 halfDir = normalize(lightDir + viewDir);
    vec3 radiance = lightColor * 8.0;

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

    return (diffuse + specular) * radiance * NdotL * shadowFactor * ao;
}

vec3 EvaluateDirectional(vec3 worldPos, vec3 worldNormal, vec3 geomNormal, vec3 viewDir,
                         vec3 albedo, float metallic, float roughness, float ao, vec3 F0)
{
    vec3 total = vec3(0.0);
    if (_DirectionalLightEnabled != 0)
        total += ShadeDirectional(normalize(_DirectionalLightDirection), _DirectionalLightColor * _DirectionalLightIntensity,
                                  MainDirectionalShadowFactor(worldPos, geomNormal),
                                  worldNormal, viewDir, albedo, metallic, roughness, ao, F0);

    int extraCount = min(_ExtraDirectionalLightCount, MAX_EXTRA_DIRECTIONAL_LIGHTS);
    for (int i = 0; i < extraCount; i++)
        total += ShadeDirectional(normalize(_ExtraDirectionalLightDirection[i]), _ExtraDirectionalLightColor[i], 1.0,
                                  worldNormal, viewDir, albedo, metallic, roughness, ao, F0);
    return total;
}

vec3 ShadeDirectionalAniso(vec3 lightDir, vec3 lightColor, float shadowFactor, vec3 worldNormal, vec3 viewDir,
                           vec3 worldTangent, vec3 worldBitangent,
                           vec3 albedo, float metallic, float mt, float mb,
                           float perceptualRoughness, float ao, vec3 F0)
{
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

    vec3 radiance = lightColor * 8.0 * shadowFactor;
    return (kD * albedo * diffuseTerm + specularTerm * F) * radiance * ao;
}

vec3 EvaluateDirectionalAniso(vec3 worldPos, vec3 worldNormal, vec3 geomNormal, vec3 viewDir,
                              vec3 worldTangent, vec3 worldBitangent,
                              vec3 albedo, float metallic, float mt, float mb,
                              float perceptualRoughness, float ao, vec3 F0)
{
    vec3 total = vec3(0.0);
    if (_DirectionalLightEnabled != 0)
        total += ShadeDirectionalAniso(normalize(_DirectionalLightDirection), _DirectionalLightColor * _DirectionalLightIntensity,
                                       MainDirectionalShadowFactor(worldPos, geomNormal), worldNormal, viewDir,
                                       worldTangent, worldBitangent, albedo, metallic, mt, mb, perceptualRoughness, ao, F0);

    int extraCount = min(_ExtraDirectionalLightCount, MAX_EXTRA_DIRECTIONAL_LIGHTS);
    for (int i = 0; i < extraCount; i++)
        total += ShadeDirectionalAniso(normalize(_ExtraDirectionalLightDirection[i]), _ExtraDirectionalLightColor[i], 1.0,
                                       worldNormal, viewDir, worldTangent, worldBitangent,
                                       albedo, metallic, mt, mb, perceptualRoughness, ao, F0);
    return total;
}

// ============================================================
//  Forward lighting entry points (BVH-driven)
// ============================================================

// geomNormal is the interpolated surface normal before normal mapping, used to offset shadow lookups
vec3 CalculateForwardLighting(vec3 worldPos, vec3 worldNormal, vec3 geomNormal, vec3 viewDir,
                              vec3 albedo, float metallic, float roughness, float ao)
{
    roughness = ApplySpecularAA(roughness, worldNormal);
    vec3 F0 = mix(vec3(0.04), albedo, metallic);

    vec3 totalLight = EvaluateDirectional(worldPos, worldNormal, geomNormal, viewDir, albedo, metallic, roughness, ao, F0);

    LightTreeWalk walk;
    LightTree_Begin(walk);
    int first, last;
    while (LightTree_NextLeaf(walk, worldPos, first, last)) {
        for (int k = first; k < last; k++) {
            LightSample L;
            if (!LightTree_FetchLight(k, worldPos, L)) continue;
            totalLight += EvaluateLocalLight(L, worldPos, worldNormal, geomNormal, viewDir, albedo, metallic, roughness, ao, F0);
        }
    }

    return totalLight;
}

vec3 CalculateForwardLightingAniso(vec3 worldPos, vec3 worldNormal, vec3 geomNormal, vec3 viewDir,
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

    vec3 totalLight = EvaluateDirectionalAniso(worldPos, worldNormal, geomNormal, viewDir, worldTangent, worldBitangent,
                                                albedo, metallic, mt, mb, roughness, ao, F0);

    LightTreeWalk walk;
    LightTree_Begin(walk);
    int first, last;
    while (LightTree_NextLeaf(walk, worldPos, first, last)) {
        for (int k = first; k < last; k++) {
            LightSample L;
            if (!LightTree_FetchLight(k, worldPos, L)) continue;
            totalLight += EvaluateLocalLightAniso(L, worldPos, worldNormal, geomNormal, viewDir, worldTangent, worldBitangent,
                                                   albedo, metallic, mt, mb, roughness, ao, F0);
        }
    }

    return totalLight;
}

// ============================================================
//  Local light with translucency (shared attenuation + shadow)
// ============================================================

vec3 EvaluateLocalLightTranslucent(LightSample L, vec3 worldPos, vec3 worldNormal, vec3 geomNormal, vec3 viewDir,
                                    vec3 albedo, float metallic, float roughness, float ao, vec3 F0,
                                    float translucency, float scatterPower, float scatterDist, float scatterScale)
{
    vec3 lightToPixel = worldPos - L.Position;
    float dist2 = dot(lightToPixel, lightToPixel);
    float dist = sqrt(dist2);
    vec3 lightDir = -lightToPixel * (1.0 / max(dist, 1e-6));

    float spotAxisCos = 0.0;
    if (L.Type == 2) {
        spotAxisCos = dot(normalize(L.Direction), -lightDir);
        if (spotAxisCos <= L.SpotCos) return vec3(0.0);
    }

    // A Range of 0 is an authoring mistake rather than an impossibility, and it makes invR2 infinite;
    // multiplied by a dist2 of 0 (fragment sitting on the light) that is Inf * 0 = NaN.
    float invR2 = 1.0 / max(L.Range * L.Range, 1e-6);
    float factor = dist2 * invR2;
    float window = clamp(1.0 - factor * factor, 0.0, 1.0);
    window *= window;
    float invSqr = 1.0 / max(dist2, 0.01);
    float attenuation = invSqr * window;

    if (L.Type == 2)
        attenuation *= smoothstep(L.SpotCos, L.InnerSpotCos, spotAxisCos);

    if (attenuation <= 0.0001) return vec3(0.0);

    float NdotL = dot(worldNormal, lightDir);

    // Only sample the shadow atlas when something will actually use it: the front-lit PBR
    // term (NdotL > 0) or the translucency term. A back-facing opaque fragment skips the
    // full PCF tap set entirely.
    float shadowFactor = 1.0;
#ifndef SG_NO_SHADOWS
    if (NdotL > 0.0 || translucency > 0.0)
        shadowFactor = 1.0 - LocalLightShadow(L, worldPos, geomNormal);
#endif

    vec3 radiance = L.Color * (L.Intensity * 8.0) * attenuation;
    vec3 result = vec3(0.0);

    // PBR (front-lit only)
    if (NdotL > 0.0) {
        vec3 halfDir = normalize(lightDir + viewDir);
        float NdotV = abs(dot(worldNormal, viewDir));
        float LdotH = max(dot(lightDir, halfDir), 0.0);

        float NDF = DistributionGGX(worldNormal, halfDir, roughness);
        float G = GeometrySmith(worldNormal, viewDir, lightDir, roughness);
        vec3 F = FresnelSchlick(LdotH, F0);
        vec3 kD = (vec3(1.0) - F) * (1.0 - metallic);
        vec3 specular = (NDF * G * F) / (4.0 * NdotV * NdotL + 0.0001);
        float diffuseTerm = DisneyDiffuse(NdotV, NdotL, LdotH, roughness);
        result = (kD * albedo * diffuseTerm + specular) * radiance * NdotL * shadowFactor * ao;
    }

    // Translucency (uses same attenuation and shadow, works regardless of NdotL)
    if (translucency > 0.0) {
        vec3 scatter = CalculateTranslucency(lightDir, viewDir, worldNormal,
                           translucency, scatterPower, scatterDist, scatterScale,
                           L.Color * L.Intensity * attenuation);
        result += scatter * albedo * shadowFactor;
    }

    return result;
}

// One directional light with translucency. lightColor is color * intensity.
vec3 ShadeDirectionalTranslucent(vec3 lightDir, vec3 lightColor, float shadowFactor, vec3 worldNormal, vec3 viewDir,
                                 vec3 albedo, float metallic, float roughness, float ao, vec3 F0,
                                 float translucency, float scatterPower, float scatterDist, float scatterScale)
{
    vec3 result = vec3(0.0);

    // PBR
    float NdotL = max(dot(worldNormal, lightDir), 0.0);
    if (NdotL > 0.0) {
        vec3 halfDir = normalize(lightDir + viewDir);
        float NdotV = abs(dot(worldNormal, viewDir));
        float LdotH = max(dot(lightDir, halfDir), 0.0);

        float NDF = DistributionGGX(worldNormal, halfDir, roughness);
        float G = GeometrySmith(worldNormal, viewDir, lightDir, roughness);
        vec3 F = FresnelSchlick(LdotH, F0);
        vec3 kD = (vec3(1.0) - F) * (1.0 - metallic);
        vec3 specular = (NDF * G * F) / (4.0 * NdotV * NdotL + 0.0001);
        float diffuseTerm = DisneyDiffuse(NdotV, NdotL, LdotH, roughness);
        result += (kD * albedo * diffuseTerm + specular) * (lightColor * 8.0) * NdotL * shadowFactor * ao;
    }

    // Translucency (same shadow, no distance attenuation for directional)
    if (translucency > 0.0) {
        vec3 scatter = CalculateTranslucency(lightDir, viewDir, worldNormal,
                           translucency, scatterPower, scatterDist, scatterScale, lightColor);
        result += scatter * albedo * shadowFactor;
    }

    return result;
}

// ============================================================
//  Forward lighting with translucency (single-pass, unified)
//  PBR + translucency share the same attenuation and shadow.
// ============================================================

vec3 CalculateForwardLighting(vec3 worldPos, vec3 worldNormal, vec3 geomNormal, vec3 viewDir,
                              vec3 albedo, float metallic, float roughness, float ao,
                              float translucency, float scatterPower,
                              float scatterDist, float scatterScale)
{
    roughness = ApplySpecularAA(roughness, worldNormal);
    vec3 F0 = mix(vec3(0.04), albedo, metallic);

    vec3 totalLight = vec3(0.0);

    // ---- Directional lights ----
    if (_DirectionalLightEnabled != 0)
        totalLight += ShadeDirectionalTranslucent(normalize(_DirectionalLightDirection),
                          _DirectionalLightColor * _DirectionalLightIntensity, MainDirectionalShadowFactor(worldPos, geomNormal),
                          worldNormal, viewDir, albedo, metallic, roughness, ao, F0,
                          translucency, scatterPower, scatterDist, scatterScale);

    int extraCount = min(_ExtraDirectionalLightCount, MAX_EXTRA_DIRECTIONAL_LIGHTS);
    for (int i = 0; i < extraCount; i++)
        totalLight += ShadeDirectionalTranslucent(normalize(_ExtraDirectionalLightDirection[i]), _ExtraDirectionalLightColor[i], 1.0,
                          worldNormal, viewDir, albedo, metallic, roughness, ao, F0,
                          translucency, scatterPower, scatterDist, scatterScale);

    LightTreeWalk walk;
    LightTree_Begin(walk);
    int first, last;
    while (LightTree_NextLeaf(walk, worldPos, first, last)) {
        for (int k = first; k < last; k++) {
            LightSample L;
            if (!LightTree_FetchLight(k, worldPos, L)) continue;
            totalLight += EvaluateLocalLightTranslucent(L, worldPos, worldNormal, geomNormal, viewDir,
                              albedo, metallic, roughness, ao, F0,
                              translucency, scatterPower, scatterDist, scatterScale);
        }
    }

    return totalLight;
}

// Shading normal doubles as the shadow normal, for surfaces without a separate geometric normal
vec3 CalculateForwardLighting(vec3 worldPos, vec3 worldNormal, vec3 viewDir,
                              vec3 albedo, float metallic, float roughness, float ao)
{
    return CalculateForwardLighting(worldPos, worldNormal, worldNormal, viewDir, albedo, metallic, roughness, ao);
}

vec3 CalculateForwardLightingAniso(vec3 worldPos, vec3 worldNormal, vec3 viewDir,
                                    vec3 worldTangent, vec3 worldBitangent,
                                    vec3 albedo, float metallic,
                                    float roughness, float anisotropy, float ao)
{
    return CalculateForwardLightingAniso(worldPos, worldNormal, worldNormal, viewDir, worldTangent, worldBitangent,
                                         albedo, metallic, roughness, anisotropy, ao);
}

vec3 CalculateForwardLighting(vec3 worldPos, vec3 worldNormal, vec3 viewDir,
                              vec3 albedo, float metallic, float roughness, float ao,
                              float translucency, float scatterPower,
                              float scatterDist, float scatterScale)
{
    return CalculateForwardLighting(worldPos, worldNormal, worldNormal, viewDir, albedo, metallic, roughness, ao,
                                    translucency, scatterPower, scatterDist, scatterScale);
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
//  Light-probe spherical harmonics (per-object, set by the pipeline for dynamic objects)
// ============================================================

uniform vec4 prowl_SHAr;
uniform vec4 prowl_SHAg;
uniform vec4 prowl_SHAb;
uniform vec4 prowl_SHBr;
uniform vec4 prowl_SHBg;
uniform vec4 prowl_SHBb;
uniform vec4 prowl_SHC;

// Evaluate per-object SH-L2 (the standard ShadeSH9) -> diffuse irradiance E/pi for a normal.
vec3 ShadeSH9(vec3 n)
{
    vec4 nrm = vec4(n, 1.0);
    vec3 x;
    x.r = dot(prowl_SHAr, nrm);
    x.g = dot(prowl_SHAg, nrm);
    x.b = dot(prowl_SHAb, nrm);

    vec4 vB = n.xyzz * n.yzzx;   // (xy, yz, z^2, zx)
    vec3 x1;
    x1.r = dot(prowl_SHBr, vB);
    x1.g = dot(prowl_SHBg, vB);
    x1.b = dot(prowl_SHBb, vB);

    float vC = n.x * n.x - n.y * n.y;
    vec3 x2 = prowl_SHC.rgb * vC;

    return max(x + x1 + x2, vec3(0.0));
}

// ============================================================
//  Fog
// ============================================================

// The fog color seen toward worldPos, either the flat fog color or the sky behind it
vec3 FogColor(vec3 worldPos)
{
    if (_FogSky.x < 0.5)
        return _FogColor.rgb;

    vec3 toPoint = worldPos - _WorldSpaceCameraPos.xyz;
    vec3 sun = _DirectionalLightEnabled != 0 ? normalize(_DirectionalLightDirection) : normalize(vec3(-0.5, 0.7, -0.5));
    vec3 c = prowlSky(toPoint / max(length(toPoint), 1e-4), sun, _FogSky.y) * 40.0;

    // The same exposure and tonemap the skybox uses, so the fog meets the sky seamlessly
    float l = dot(c, vec3(0.2126, 0.7152, 0.0722));
    vec3 tc = c / (c + 1.0);
    return mix(c / (l + 1.0), tc, tc);
}

vec3 ApplyFog(vec3 color, vec3 worldPos)
{
    if (_FogStates.x + _FogStates.y + _FogStates.z < 0.5)
        return color;

    float fogCoord = length(worldPos - _WorldSpaceCameraPos.xyz);
    float prowlFog = 0.0;
    prowlFog += (fogCoord * _FogParams.z + _FogParams.w) * _FogStates.x;
    prowlFog += exp2(-fogCoord * _FogParams.y) * _FogStates.y;
    prowlFog += exp2(-fogCoord * fogCoord * _FogParams.x * _FogParams.x) * _FogStates.z;
    return mix(FogColor(worldPos), color, clamp(prowlFog, 0.0, 1.0));
}

#endif
