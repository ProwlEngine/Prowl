// Forward lighting: point and spot lights from the H-Forward trees, directional lights from the world record with the
// sun first, ambient light and fog. A port of Prowl's Lighting.glsl, the same math.
//
// worldPos is camera relative, as every world position in Graphite shaders is, so the camera sits at the origin.
// Shadows come with the shadow system; until then lights are unshadowed.
#pragma once

#include "Graphite/Trees.glsl"
#include "Graphite/PBR.glsl"
#include "Graphite/Textures.glsl"

struct Graphite_LightRecord
{
    vec4 positionRange;      // position in its tree's space, its region's or the view's, range
    vec4 colorIntensity;
    vec4 directionFlags;     // direction it shines, type in the low 2 bits and shadow enabled in bit 2 as int bits
    vec4 spotShadow;         // cos of the outer and inner spot angles, shadow depth and normal bias
    vec4 shadow;             // shadow strength, shadow quality, shadow slot as int bits, layer mask as uint bits
};

struct Graphite_Directional
{
    vec4 directionShadow;    // surface to light direction, shadow enabled
    vec4 color;              // colour times intensity, layer mask as uint bits
};

layout(set = 0, binding = 11, std430) readonly buffer Graphite_StaticLights { Graphite_LightRecord graphite_StaticLights[]; };
layout(set = 1, binding = 3, std430) readonly buffer Graphite_ViewLights { Graphite_LightRecord graphite_ViewLights[]; };
layout(set = 0, binding = 15, std430) readonly buffer Graphite_WorldLighting
{
    uvec4 counts;            // directional lights, lightmap pages, probes present, lightmap encoding (0 linear, 1 RGBM)
    uvec4 reflections;       // the sky probe's cube slot, the probes' sampler slot, the sky probe's mips, whether it is captured
    vec4 ambientColor;
    vec4 ambientSky;
    vec4 ambientGround;
    vec4 ambientMode;        // uniform, hemisphere, strength
    vec4 fogColor;
    vec4 fogParams;
    vec4 fogStates;          // linear, exponential, exponential squared, use sky
    Graphite_Directional directionals[8];
} graphite_World;

struct LightSample
{
    vec3  Position;          // camera relative
    float Range;
    vec3  Color;
    float Intensity;
    vec3  Direction;
    int   Type;              // 0 directional, 1 point, 2 spot
    float SpotCos;           // cos(outer)
    float InnerSpotCos;      // cos(inner)
    float ShadowDepthBias;
    float ShadowNormalBias;
    float ShadowStrength;
    float ShadowQuality;
    int   ShadowSlot;        // -1 if no atlas slot this frame
    int   ShadowEnabled;     // 0 / 1
};

float Graphite_LocalLightShadow(LightSample L, vec3 worldPos, vec3 geomNormal) { return 0.0; }

float Graphite_SunShadow(vec3 worldPos, vec3 geomNormal) { return 0.0; }

// Reads a light when the camera relative point is inside its sphere and the surface is on one of its layers. offset and
// offsetLow move camera relative positions into the light's tree.
bool Graphite_FetchLight(uint tree, uint index, vec3 worldPos, vec3 offset, vec3 offsetLow, uint layers, out LightSample L)
{
    vec4 t0 = tree == GRAPHITE_STATIC_LIGHTS ? graphite_StaticLights[index].positionRange : graphite_ViewLights[index].positionRange;
    precise vec3 position = (t0.xyz - offset) - offsetLow;
    vec3 d = worldPos - position;
    if (dot(d, d) > t0.w * t0.w)
        return false;

    Graphite_LightRecord r = tree == GRAPHITE_STATIC_LIGHTS ? graphite_StaticLights[index] : graphite_ViewLights[index];
    if ((floatBitsToUint(r.shadow.w) & layers) == 0u)
        return false;

    int typeAndFlags = floatBitsToInt(r.directionFlags.w);
    L.Position = position;
    L.Range = t0.w;
    L.Color = r.colorIntensity.rgb;
    L.Intensity = r.colorIntensity.w;
    L.Direction = r.directionFlags.xyz;
    L.Type = typeAndFlags & 3;
    L.ShadowEnabled = (typeAndFlags >> 2) & 1;
    L.SpotCos = r.spotShadow.x;
    L.InnerSpotCos = r.spotShadow.y;
    L.ShadowDepthBias = r.spotShadow.z;
    L.ShadowNormalBias = r.spotShadow.w;
    L.ShadowStrength = r.shadow.x;
    L.ShadowQuality = r.shadow.y;
    L.ShadowSlot = floatBitsToInt(r.shadow.z);
    return true;
}

// Walks the static light regions the view sees, then its tree of moving lights, returning every light whose sphere holds the point
struct Graphite_LightIterator
{
    Graphite_TreeQuery query;
    uint tree;
    uint k;
    uint last;
    uint layers;
};

Graphite_LightIterator Graphite_BeginLights(vec3 worldPos)
{
    Graphite_LightIterator it;
    Graphite_BeginQuery(it.query, worldPos, GRAPHITE_QUERY_LIGHTS);
    it.tree = GRAPHITE_STATIC_LIGHTS;
    it.k = 0u;
    it.last = 0u;
    it.layers = Graphite_SurfaceLayers();
    return it;
}

bool Graphite_NextLight(inout Graphite_LightIterator it, out LightSample L)
{
    for (;;)
    {
        while (it.k < it.last)
        {
            uint index = it.k++;
            if (Graphite_FetchLight(it.tree, index, it.query.position, it.query.offset, it.query.offsetLow, it.layers, L))
                return true;
        }

        if (!Graphite_NextRun(it.query, it.tree, it.k, it.last))
            return false;
    }
    return false;
}

// Unit direction from the surface to the camera, world space
vec3 GetWorldViewDir(vec3 worldPos)
{
    return normalize(-worldPos);
}

// Unit direction from the surface to the camera, in tangent space
vec3 GetTangentViewDir(vec3 worldPos, vec3 worldNormal, vec3 worldTangent, vec3 worldBitangent)
{
    vec3 viewDir = GetWorldViewDir(worldPos);
    return normalize(vec3(dot(viewDir, normalize(worldTangent)), dot(viewDir, normalize(worldBitangent)), dot(viewDir, normalize(worldNormal))));
}

// ============================================================
//  Per-light evaluation (BVH leaf -> radiance)
// ============================================================

vec3 EvaluateLocalLight(LightSample L, vec3 worldPos, vec3 worldNormal, vec3 geomNormal, vec3 viewDir,
                        vec3 albedo, float metallic, float roughness, float ao, vec3 F0)
{
    // BVH only emits point + spot leaves; directional has its own path. The leaf-level sphere
    // test in Graphite_FetchLight has already rejected fragments past Range, so we don't repeat it here.
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
    shadowFactor = 1.0 - Graphite_LocalLightShadow(L, worldPos, geomNormal);
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
    shadowFactor = 1.0 - Graphite_LocalLightShadow(L, worldPos, geomNormal);
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
    return 1.0 - Graphite_SunShadow(worldPos, geomNormal);
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
    uint layers = Graphite_SurfaceLayers();
    for (uint i = 0u; i < graphite_World.counts.x; i++)
    {
        Graphite_Directional d = graphite_World.directionals[i];
        if ((floatBitsToUint(d.color.w) & layers) == 0u)
            continue;
        total += ShadeDirectional(normalize(d.directionShadow.xyz), d.color.rgb,
            i == 0u ? MainDirectionalShadowFactor(worldPos, geomNormal) : 1.0, worldNormal, viewDir, albedo, metallic, roughness, ao, F0);
    }
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
    uint layers = Graphite_SurfaceLayers();
    for (uint i = 0u; i < graphite_World.counts.x; i++)
    {
        Graphite_Directional d = graphite_World.directionals[i];
        if ((floatBitsToUint(d.color.w) & layers) == 0u)
            continue;
        total += ShadeDirectionalAniso(normalize(d.directionShadow.xyz), d.color.rgb,
            i == 0u ? MainDirectionalShadowFactor(worldPos, geomNormal) : 1.0, worldNormal, viewDir, worldTangent, worldBitangent, albedo, metallic, mt, mb, perceptualRoughness, ao, F0);
    }
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

    Graphite_LightIterator it = Graphite_BeginLights(worldPos);
    LightSample L;
    while (Graphite_NextLight(it, L))
        totalLight += EvaluateLocalLight(L, worldPos, worldNormal, geomNormal, viewDir, albedo, metallic, roughness, ao, F0);

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

    Graphite_LightIterator it = Graphite_BeginLights(worldPos);
    LightSample L;
    while (Graphite_NextLight(it, L))
        totalLight += EvaluateLocalLightAniso(L, worldPos, worldNormal, geomNormal, viewDir, worldTangent, worldBitangent,
        albedo, metallic, mt, mb, roughness, ao, F0);

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
        shadowFactor = 1.0 - Graphite_LocalLightShadow(L, worldPos, geomNormal);
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
    uint layers = Graphite_SurfaceLayers();
    for (uint i = 0u; i < graphite_World.counts.x; i++)
    {
        Graphite_Directional d = graphite_World.directionals[i];
        if ((floatBitsToUint(d.color.w) & layers) == 0u)
            continue;
        totalLight += ShadeDirectionalTranslucent(normalize(d.directionShadow.xyz), d.color.rgb,
            i == 0u ? MainDirectionalShadowFactor(worldPos, geomNormal) : 1.0, worldNormal, viewDir, albedo, metallic, roughness, ao, F0, translucency, scatterPower, scatterDist, scatterScale);
    }

    Graphite_LightIterator it = Graphite_BeginLights(worldPos);
    LightSample L;
    while (Graphite_NextLight(it, L))
        totalLight += EvaluateLocalLightTranslucent(L, worldPos, worldNormal, geomNormal, viewDir,
        albedo, metallic, roughness, ao, F0,
        translucency, scatterPower, scatterDist, scatterScale);

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
    ambient += graphite_World.ambientColor.rgb * graphite_World.ambientMode.x;

    float upDot = dot(worldNormal, vec3(0.0, 1.0, 0.0));
    ambient += mix(graphite_World.ambientGround.rgb, graphite_World.ambientSky.rgb, upDot * 0.5 + 0.5) * graphite_World.ambientMode.y;

    return ambient;
}

// ============================================================
//  Baked lighting
// ============================================================

struct Graphite_ProbeSH { vec4 shAr; vec4 shAg; vec4 shAb; vec4 shBr; vec4 shBg; vec4 shBb; vec4 shC; };

layout(set = 0, binding = 16, std430) readonly buffer Graphite_LightmapPages { uvec2 graphite_LightmapPages[]; };
layout(set = 0, binding = 17, std430) readonly buffer Graphite_ObjectProbes { Graphite_ProbeSH graphite_ObjectProbes[]; };

vec3 DecodeLightmap(vec4 texel)
{
    return graphite_World.counts.w == 1u ? texel.rgb * (texel.a * 8.0) : texel.rgb;
}

// Diffuse irradiance over pi for a normal from packed order 2 harmonics
vec3 ShadeSH9(Graphite_ProbeSH sh, vec3 n)
{
    vec4 nrm = vec4(n, 1.0);
    vec3 x;
    x.r = dot(sh.shAr, nrm);
    x.g = dot(sh.shAg, nrm);
    x.b = dot(sh.shAb, nrm);

    vec4 vB = n.xyzz * n.yzzx;
    vec3 x1;
    x1.r = dot(sh.shBr, vB);
    x1.g = dot(sh.shBg, vB);
    x1.b = dot(sh.shBb, vB);

    float vC = n.x * n.x - n.y * n.y;
    vec3 x2 = sh.shC.rgb * vC;

    return max(x + x1 + x2, vec3(0.0));
}

#if defined(GRAPHITE_FRAGMENT) && !defined(GRAPHITE_NO_OBJECT)
// The diffuse light that is not realtime: the object's lightmap, ambient while its page loads, else its light probes,
// else the ambient light.
// lightmapUV comes from Graphite_LightmapUV in the vertex stage.
vec3 Graphite_IndirectDiffuse(vec3 worldNormal, vec2 lightmapUV)
{
    Graphite_ObjectData object = Graphite_Object();
    if (object.lightmapPage >= 0 && uint(object.lightmapPage) < graphite_World.counts.y)
    {
        uvec2 page = graphite_LightmapPages[object.lightmapPage];
        if (page.x == 0xFFFFFFFFu)
            return CalculateAmbient(worldNormal) * graphite_World.ambientMode.z;
        return DecodeLightmap(GRAPHITE_SAMPLE_2D(page.x, page.y, lightmapUV));
    }
    if (object.probeIndex != 0xFFFFFFFFu && graphite_World.counts.z != 0u)
        return ShadeSH9(graphite_ObjectProbes[object.probeIndex], worldNormal);
    return CalculateAmbient(worldNormal) * graphite_World.ambientMode.z;
}
#endif

float SpecularOcclusion(float NdotV, float ao, float roughness)
{
    return clamp(pow(NdotV + ao, exp2(-16.0 * roughness - 1.0)) - 1.0 + ao, 0.0, 1.0);
}


// ============================================================
//  Reflection probes
// ============================================================

// The sky and every probe have their own cube prefiltered by roughness across its mips, found by its bindless slot.
// Captured probes live in regions like static lights, walked with the same trees.
struct Graphite_ReflectionProbe
{
    vec4 row0;               // the box's axes, its centre in its region's space in w
    vec4 row1;
    vec4 row2;
    vec4 box;                // box half size, blend distance
    vec4 params;             // intensity, box projection, priority, the cube's slot and mips (slot | mips << 16) as uint bits
};

layout(set = 0, binding = 18, std430) readonly buffer Graphite_ReflectionProbes { Graphite_ReflectionProbe graphite_ReflectionProbes[]; };

#define MAX_BLENDED_PROBES 4

bool ReflectionProbesReady() { return graphite_World.reflections.w != 0u; }

// A prefiltered cube's light along dir, its mip picked by roughness
vec3 SampleProbeCube(uint slot, uint mips, vec3 dir, float roughness)
{
    return GRAPHITE_SAMPLE_CUBE_LOD(slot, graphite_World.reflections.y, dir, roughness * (float(mips) - 1.0)).rgb;
}

vec3 SampleSkyProbe(vec3 dir, float roughness)
{
    return SampleProbeCube(graphite_World.reflections.x, graphite_World.reflections.z, dir, roughness);
}

// A camera relative point in the probe's box space. offset and offsetLow move camera relative positions into its region.
vec3 Graphite_ProbeLocal(Graphite_ReflectionProbe probe, vec3 worldPos, vec3 offset, vec3 offsetLow)
{
    precise vec3 center = (vec3(probe.row0.w, probe.row1.w, probe.row2.w) - offset) - offsetLow;
    precise vec3 q = worldPos - center;
    return vec3(dot(probe.row0.xyz, q), dot(probe.row1.xyz, q), dot(probe.row2.xyz, q));
}

// Projects the reflection onto the probe's box, so it lands where the reflected surface really is. p is the point in box space.
vec3 SampleReflectionProbe(Graphite_ReflectionProbe probe, vec3 p, vec3 dir, float roughness)
{
    uint cube = floatBitsToUint(probe.params.w);
    if (probe.params.y > 0.5)
    {
        vec3 halfSize = probe.box.xyz;
        vec3 d = vec3(dot(probe.row0.xyz, dir), dot(probe.row1.xyz, dir), dot(probe.row2.xyz, dir));
        d = mix(vec3(1e-6), d, greaterThanEqual(abs(d), vec3(1e-6)));
        vec3 far = max((halfSize - p) / d, (-halfSize - p) / d);
        float t = max(min(min(far.x, far.y), far.z), 0.0);
        vec3 hit = p + d * t;

        // The lobe's footprint shrinks with the distance to what it reflects, so close surfaces reflect sharper
        roughness *= sqrt(clamp(t / max(length(hit), 1e-4), 0.0, 1.0));
        dir = probe.row0.xyz * hit.x + probe.row1.xyz * hit.y + probe.row2.xyz * hit.z;
    }

    return SampleProbeCube(cube & 0xFFFFu, cube >> 16, dir, roughness) * probe.params.x;
}

// Blends the most important probes around the point by how far inside their boxes it is, the sky filling the rest
vec3 EvaluateReflectionProbes(vec3 worldPos, vec3 N, vec3 V, float roughness)
{
    // Rough lobes lean toward the normal
    float a = roughness * roughness;
    vec3 R = normalize(mix(N, reflect(-V, N), (1.0 - a) * (sqrt(1.0 - a) + a)));

    uint index[MAX_BLENDED_PROBES];
    vec3 local[MAX_BLENDED_PROBES];
    float weight[MAX_BLENDED_PROBES];
    float priority[MAX_BLENDED_PROBES];
    int count = 0;

    Graphite_TreeQuery query;
    Graphite_BeginQuery(query, worldPos, GRAPHITE_QUERY_PROBES);
    uint tree, first, last;
    while (Graphite_NextRun(query, tree, first, last))
    {
        for (uint k = first; k < last; k++)
        {
            Graphite_ReflectionProbe probe = graphite_ReflectionProbes[k];
            vec3 p = Graphite_ProbeLocal(probe, worldPos, query.offset, query.offsetLow);
            vec3 inside3 = probe.box.xyz - abs(p);
            float inside = min(inside3.x, min(inside3.y, inside3.z));
            if (inside <= 0.0)
                continue;

            float w = probe.box.w > 0.0 ? clamp(inside / probe.box.w, 0.0, 1.0) : 1.0;
            float pr = probe.params.z;
            int at;
            if (count < MAX_BLENDED_PROBES)
                at = count++;
            else if (pr > priority[MAX_BLENDED_PROBES - 1])
                at = MAX_BLENDED_PROBES - 1;
            else
                continue;

            index[at] = k;
            local[at] = p;
            weight[at] = w;
            priority[at] = pr;
            for (int j = at; j > 0 && priority[j] > priority[j - 1]; j--)
            {
                uint ti = index[j]; index[j] = index[j - 1]; index[j - 1] = ti;
                vec3 tl = local[j]; local[j] = local[j - 1]; local[j - 1] = tl;
                float tw = weight[j]; weight[j] = weight[j - 1]; weight[j - 1] = tw;
                float tp = priority[j]; priority[j] = priority[j - 1]; priority[j - 1] = tp;
            }
        }
    }

    vec3 color = vec3(0.0);
    float total = 0.0;
    for (int i = 0; i < count && total < 0.999; i++)
    {
        float w = weight[i] * (1.0 - total);
        color += SampleReflectionProbe(graphite_ReflectionProbes[index[i]], local[i], R, roughness) * w;
        total += w;
    }
    if (total < 0.999)
        color += SampleSkyProbe(R, roughness) * (1.0 - total);
    return color;
}

// Specular light from the environment: the reflection probes once the sky is captured, else an approximation from the
// ambient light, Fresnel at the view angle and a roughness falloff, as the Standard shader does. ambientLight is the
// indirect diffuse light times occlusion.
vec3 Graphite_IndirectSpecular(vec3 worldPos, vec3 N, vec3 V, vec3 F0, float roughness, float ao, vec3 ambientLight)
{
    float NdotV = max(dot(N, V), 0.0);
    vec3 F = EnvBRDF(F0, roughness, NdotV);
    if (ReflectionProbesReady())
        return EvaluateReflectionProbes(worldPos, N, V, roughness) * F * SpecularOcclusion(NdotV, ao, roughness);
    float specOcclusion = 1.0 - roughness * roughness;
    return ambientLight * F * mix(specOcclusion, 1.0, 0.25);
}

// ============================================================
//  Fog
// ============================================================

// How blurred the sky is that fog takes its color from, as a sky probe roughness
#define FOG_SKY_ROUGHNESS 0.35

// The fog color seen toward worldPos, either the flat fog color or the sky behind it from the sky probe, so it
// meets whatever sky is drawn seamlessly
vec3 FogColor(vec3 worldPos)
{
    if (graphite_World.fogStates.w < 0.5 || !ReflectionProbesReady())
        return graphite_World.fogColor.rgb;

    // Below the horizon the fog keeps the horizon's color rather than the ground under the sky
    vec3 dir = worldPos;
    dir.y = max(dir.y, 0.0);
    dir = length(dir) > 1e-4 ? normalize(dir) : vec3(0.0, 0.0, 1.0);
    return SampleSkyProbe(dir, FOG_SKY_ROUGHNESS);
}

vec3 ApplyFog(vec3 color, vec3 worldPos)
{
    vec4 states = graphite_World.fogStates;
    if (states.x + states.y + states.z < 0.5)
        return color;

    vec4 params = graphite_World.fogParams;
    float fogCoord = length(worldPos);
    float prowlFog = 0.0;
    prowlFog += (fogCoord * params.z + params.w) * states.x;
    prowlFog += exp2(-fogCoord * params.y) * states.y;
    prowlFog += exp2(-fogCoord * fogCoord * params.x * params.x) * states.z;
    return mix(FogColor(worldPos), color, clamp(prowlFog, 0.0, 1.0));
}
