module Lighting;

// Forward lighting utilities for Prowl Engine.
// Provides: CalculateForwardLighting(), CalculateAmbient(), ApplyFog(), ShadeSH9().
//
// Light data lives in two BVHs (static + dynamic). The single directional light is uploaded
// separately and evaluated unconditionally (no BVH). fragCoord (SV_Position.xy) is threaded into
// the shadow path for PCF rotation, since Slang has no global gl_FragCoord.

__exported import PBR;
__exported import Shadow;
__exported import LightBVH;

#ifndef MAX_SHADOW_CASTERS
#define MAX_SHADOW_CASTERS 4
#endif

public struct LightingData
{
    // Directional light (a single one per scene)
    public int   _DirectionalLightEnabled;     // 0 / 1
    public float3 _DirectionalLightDirection;
    public float3 _DirectionalLightColor;
    public float _DirectionalLightIntensity;
    public int   _DirectionalLightShadowEnabled;
    public float _DirectionalLightShadowBias;
    public float _DirectionalLightShadowNormalBias;
    public float _DirectionalLightShadowStrength;
    public float _DirectionalLightShadowQuality;

    public float2 _ShadowAtlasSize;

    // Directional cascade shadows
    public int  _CascadeCount;
    public float4x4 _CascadeShadowMatrix0;
    public float4x4 _CascadeShadowMatrix1;
    public float4x4 _CascadeShadowMatrix2;
    public float4x4 _CascadeShadowMatrix3;
    public float4 _CascadeAtlasParams0;
    public float4 _CascadeAtlasParams1;
    public float4 _CascadeAtlasParams2;
    public float4 _CascadeAtlasParams3;

    // Point shadows (6 faces per light). slot s uses indices [s*6 .. s*6+5].
    public float4x4 _PointShadowMatrices[MAX_SHADOW_CASTERS * 6];
    public float4 _PointShadowFaceParams[MAX_SHADOW_CASTERS * 6]; // xy: atlasPos, z: faceSize, w: farPlane

    // Spot shadows (1 matrix per light, indexed by slot directly).
    public float4x4 _SpotShadowMatrices[MAX_SHADOW_CASTERS];
    public float4 _SpotShadowAtlasParams[MAX_SHADOW_CASTERS]; // xy: atlasPos, z: atlasSize, w: unused

    // Fog
    public float4 _FogColor;
    public float4 _FogParams;
    public float3 _FogStates;

    // Ambient
    public float2 _AmbientMode;
    public float4 _AmbientColor;
    public float4 _AmbientSkyColor;
    public float4 _AmbientGroundColor;
    public float _AmbientStrength;

    // Light-probe spherical harmonics (per-object, set by the pipeline for dynamic objects)
    public float4 prowl_SHAr;
    public float4 prowl_SHAg;
    public float4 prowl_SHAb;
    public float4 prowl_SHBr;
    public float4 prowl_SHBg;
    public float4 prowl_SHBb;
    public float4 prowl_SHC;

    // Shadow atlas (hardware depth-compare sampler)
    public Sampler2DShadow _ShadowAtlas;
}
public ParameterBlock<LightingData> Light;

// ============================================================
//  Helpers preserved for shader-graph access
// ============================================================

// Unit direction FROM the surface TO the camera, world-space.
public float3 GetWorldViewDir(float3 worldPos)
{
    return normalize(Frame._WorldSpaceCameraPos.xyz - worldPos);
}

// Unit direction FROM the surface TO the camera in tangent space.
// GLSL transpose(mat3(t,b,n)) (cols t,b,n) world->tangent == Slang float3x3(t,b,n) (rows t,b,n).
public float3 GetTangentViewDir(float3 worldPos, float3 worldNormal, float3 worldTangent, float3 worldBitangent)
{
    float3 vWorld = GetWorldViewDir(worldPos);
    float3x3 tbn = float3x3(normalize(worldTangent), normalize(worldBitangent), normalize(worldNormal));
    return normalize(mul(tbn, vWorld));
}

// ============================================================
//  Shadow sampling
// ============================================================

float SampleDirectionalShadow(float3 worldPos, float3 worldNormal, float2 fragCoord)
{
    if (Light._CascadeCount == 0) return 0.0;

    // Compare squared distance against squared cascade splits to avoid the per-fragment sqrt.
    float3 toCamera = worldPos - Frame._WorldSpaceCameraPos.xyz;
    float worldDistSq = dot(toCamera, toCamera) * 4.0;

    float4x4 cascadeMatrix;
    float4 cascadeParams;

    if (Light._CascadeCount >= 1 && worldDistSq <= Light._CascadeAtlasParams0.w * Light._CascadeAtlasParams0.w) {
        cascadeMatrix = Light._CascadeShadowMatrix0;
        cascadeParams = Light._CascadeAtlasParams0;
    } else if (Light._CascadeCount >= 2 && worldDistSq <= Light._CascadeAtlasParams1.w * Light._CascadeAtlasParams1.w) {
        cascadeMatrix = Light._CascadeShadowMatrix1;
        cascadeParams = Light._CascadeAtlasParams1;
    } else if (Light._CascadeCount >= 3 && worldDistSq <= Light._CascadeAtlasParams2.w * Light._CascadeAtlasParams2.w) {
        cascadeMatrix = Light._CascadeShadowMatrix2;
        cascadeParams = Light._CascadeAtlasParams2;
    } else if (Light._CascadeCount >= 4 && worldDistSq <= Light._CascadeAtlasParams3.w * Light._CascadeAtlasParams3.w) {
        cascadeMatrix = Light._CascadeShadowMatrix3;
        cascadeParams = Light._CascadeAtlasParams3;
    } else {
        // Beyond all cascades: clamp to last available.
        if      (Light._CascadeCount == 1) { cascadeMatrix = Light._CascadeShadowMatrix0; cascadeParams = Light._CascadeAtlasParams0; }
        else if (Light._CascadeCount == 2) { cascadeMatrix = Light._CascadeShadowMatrix1; cascadeParams = Light._CascadeAtlasParams1; }
        else if (Light._CascadeCount == 3) { cascadeMatrix = Light._CascadeShadowMatrix2; cascadeParams = Light._CascadeAtlasParams2; }
        else                               { cascadeMatrix = Light._CascadeShadowMatrix3; cascadeParams = Light._CascadeAtlasParams3; }
    }

    if (cascadeParams.z <= 0.0) return 0.0;

    float3 worldPosBiased = worldPos + normalize(worldNormal) * Light._DirectionalLightShadowNormalBias;
    float4 lightSpacePos = mul(cascadeMatrix, float4(worldPosBiased, 1.0));
    float3 projCoords = lightSpacePos.xyz / lightSpacePos.w;
    projCoords = projCoords * 0.5 + 0.5;

    if (projCoords.z > 1.0 || projCoords.x < 0.0 || projCoords.x > 1.0 || projCoords.y < 0.0 || projCoords.y > 1.0)
        return 0.0;

    float atlasSize = Light._ShadowAtlasSize.x;
    float2 atlasCoords, shadowMin, shadowMax;
    GetAtlasCoordinates(projCoords, cascadeParams, atlasSize, atlasCoords, shadowMin, shadowMax);

    float slopeBias = CalculateSlopeBias(worldNormal, Light._DirectionalLightDirection, Light._DirectionalLightShadowBias);
    float texelWorldSize = ((cascadeParams.w * 4.0) / (cascadeParams.z * atlasSize)) * 8.0;
    float currentDepth = projCoords.z - (slopeBias + texelWorldSize);

    return SampleShadowPCF(Light._ShadowAtlas, atlasCoords, shadowMin, shadowMax,
                           currentDepth, Light._DirectionalLightShadowQuality, Light._DirectionalLightShadowStrength, fragCoord);
}

float SamplePointShadow(LightSample L, int shadowSlot, float3 worldPos, float3 worldNormal, float2 fragCoord)
{
    float3 lightToFrag = worldPos - L.Position;
    float3 absDir = abs(lightToFrag);

    int faceIndex = 0;
    if (absDir.x >= absDir.y && absDir.x >= absDir.z)
        faceIndex = lightToFrag.x > 0.0 ? 0 : 1;
    else if (absDir.y >= absDir.x && absDir.y >= absDir.z)
        faceIndex = lightToFrag.y > 0.0 ? 2 : 3;
    else
        faceIndex = lightToFrag.z > 0.0 ? 4 : 5;

    int idx = shadowSlot * 6 + faceIndex;
    float4x4 shadowMatrix = Light._PointShadowMatrices[idx];
    float4 faceParams = Light._PointShadowFaceParams[idx];

    float3 worldPosBiased = worldPos + normalize(worldNormal) * L.ShadowNormalBias;
    float4 lightSpacePos = mul(shadowMatrix, float4(worldPosBiased, 1.0));
    float3 projCoords = lightSpacePos.xyz / lightSpacePos.w;
    projCoords = projCoords * 0.5 + 0.5;

    if (projCoords.z > 1.0 || projCoords.x < 0.0 || projCoords.x > 1.0 || projCoords.y < 0.0 || projCoords.y > 1.0)
        return 0.0;

    float atlasSize = Light._ShadowAtlasSize.x;
    float2 atlasCoords, shadowMin, shadowMax;
    GetAtlasCoordinates(projCoords, faceParams, atlasSize, atlasCoords, shadowMin, shadowMax);

    float finalBias = CalculateSlopeBias(worldNormal, normalize(lightToFrag), L.ShadowBias);
    float currentDepth = projCoords.z - finalBias;

    return SampleShadowPCF(Light._ShadowAtlas, atlasCoords, shadowMin, shadowMax,
                           currentDepth, L.ShadowQuality, L.ShadowStrength, fragCoord);
}

float SampleSpotShadow(LightSample L, int shadowSlot, float3 worldPos, float3 worldNormal, float2 fragCoord)
{
    float4 atlasParams = Light._SpotShadowAtlasParams[shadowSlot];
    if (atlasParams.z <= 0.0) return 0.0;

    float3 worldPosBiased = worldPos + normalize(worldNormal) * L.ShadowNormalBias;
    float4 lightSpacePos = mul(Light._SpotShadowMatrices[shadowSlot], float4(worldPosBiased, 1.0));
    float3 projCoords = lightSpacePos.xyz / lightSpacePos.w;
    projCoords = projCoords * 0.5 + 0.5;

    if (projCoords.z > 1.0 || projCoords.x < 0.0 || projCoords.x > 1.0 || projCoords.y < 0.0 || projCoords.y > 1.0)
        return 0.0;

    float atlasSize = Light._ShadowAtlasSize.x;
    float2 atlasCoords, shadowMin, shadowMax;
    GetAtlasCoordinates(projCoords, atlasParams, atlasSize, atlasCoords, shadowMin, shadowMax);

    float finalBias = CalculateSlopeBias(worldNormal, L.Direction, L.ShadowBias);
    float currentDepth = projCoords.z - finalBias;

    return SampleShadowPCF(Light._ShadowAtlas, atlasCoords, shadowMin, shadowMax,
                           currentDepth, L.ShadowQuality, L.ShadowStrength, fragCoord);
}

// ============================================================
//  Per-light evaluation (BVH leaf -> radiance)
// ============================================================

float3 EvaluateLocalLight(LightSample L, float3 worldPos, float3 worldNormal, float3 viewDir,
                        float3 albedo, float metallic, float roughness, float ao, float3 F0, float2 fragCoord)
{
    float3 lightToPixel = worldPos - L.Position;
    float dist2 = dot(lightToPixel, lightToPixel);
    float dist = sqrt(dist2);
    float3 lightDir = -lightToPixel * (1.0 / max(dist, 1e-6));

    float spotAxisCos = 0.0;
    if (L.Type == 2) {
        spotAxisCos = dot(normalize(L.Direction), -lightDir);
        if (spotAxisCos <= L.SpotCos) return float3(0.0);
    }

    float NdotL = dot(worldNormal, lightDir);
    if (NdotL <= 0.0) return float3(0.0);

    float invR2 = 1.0 / (L.Range * L.Range);
    float factor = dist2 * invR2;            // (d / r)^2
    float window = clamp(1.0 - factor * factor, 0.0, 1.0);
    window *= window;                          // (1 - (d/r)^4)^2
    float invSqr = 1.0 / max(dist2, 0.01);     // 1/d^2 with origin guard
    float attenuation = invSqr * window;

    if (L.Type == 2)
        attenuation *= smoothstep(L.SpotCos, L.InnerSpotCos, spotAxisCos);

    if (attenuation <= 0.0001) return float3(0.0);

    float3 halfDir = normalize(lightDir + viewDir);
    float3 radiance = L.Color * (L.Intensity * 8.0) * attenuation;

    float NdotV = abs(dot(worldNormal, viewDir));
    float LdotH = max(dot(lightDir, halfDir), 0.0);

    float NDF = DistributionGGX(worldNormal, halfDir, roughness);
    float G = GeometrySmith(worldNormal, viewDir, lightDir, roughness);
    float3 F = FresnelSchlick(LdotH, F0);

    float3 kS = F;
    float3 kD = (float3(1.0) - kS) * (1.0 - metallic);

    float3 numerator = NDF * G * F;
    float denominator = 4.0 * NdotV * NdotL + 0.0001;
    float3 specular = numerator / denominator;

    float diffuseTerm = DisneyDiffuse(NdotV, NdotL, LdotH, roughness);
    float3 diffuse = kD * albedo * diffuseTerm;

    float shadowFactor;
#ifdef SG_NO_SHADOWS
    shadowFactor = 1.0;
#else
    float shadow = 0.0;
    if (L.ShadowEnabled != 0 && L.ShadowSlot >= 0) {
        if (L.Type == 1)
            shadow = SamplePointShadow(L, L.ShadowSlot, worldPos, worldNormal, fragCoord);
        else
            shadow = SampleSpotShadow(L, L.ShadowSlot, worldPos, worldNormal, fragCoord);
    }
    shadowFactor = 1.0 - shadow;
#endif

    return (diffuse + specular) * radiance * NdotL * shadowFactor * ao;
}

float3 EvaluateLocalLightAniso(LightSample L, float3 worldPos, float3 worldNormal, float3 viewDir,
                              float3 worldTangent, float3 worldBitangent,
                              float3 albedo, float metallic, float mt, float mb,
                              float perceptualRoughness, float ao, float3 F0, float2 fragCoord)
{
    float3 lightToPixel = worldPos - L.Position;
    float dist2 = dot(lightToPixel, lightToPixel);
    float dist = sqrt(dist2);
    float3 lightDir = -lightToPixel * (1.0 / max(dist, 1e-6));

    float spotAxisCos = 0.0;
    if (L.Type == 2) {
        spotAxisCos = dot(normalize(L.Direction), -lightDir);
        if (spotAxisCos <= L.SpotCos) return float3(0.0);
    }

    float NdotL = dot(worldNormal, lightDir);
    if (NdotL <= 0.0) return float3(0.0);

    float invR2 = 1.0 / (L.Range * L.Range);
    float factor = dist2 * invR2;
    float window = clamp(1.0 - factor * factor, 0.0, 1.0);
    window *= window;
    float invSqr = 1.0 / max(dist2, 0.01);
    float attenuation = invSqr * window;

    if (L.Type == 2)
        attenuation *= smoothstep(L.SpotCos, L.InnerSpotCos, spotAxisCos);

    if (attenuation <= 0.0001) return float3(0.0);

    float3 halfDir = normalize(lightDir + viewDir);
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
    float3 F = FresnelSchlick(LdotH, F0);

    float specularTerm = max(0.0, (V * D) * PI * NdotL);
    float3 kD = (float3(1.0) - F) * (1.0 - metallic);

    float diffuseTerm = DisneyDiffuse(NdotV, NdotL, LdotH, perceptualRoughness) * NdotL;

    float shadowFactor;
#ifdef SG_NO_SHADOWS
    shadowFactor = 1.0;
#else
    float shadow = 0.0;
    if (L.ShadowEnabled != 0 && L.ShadowSlot >= 0) {
        if (L.Type == 1)
            shadow = SamplePointShadow(L, L.ShadowSlot, worldPos, worldNormal, fragCoord);
        else
            shadow = SampleSpotShadow(L, L.ShadowSlot, worldPos, worldNormal, fragCoord);
    }
    shadowFactor = 1.0 - shadow;
#endif

    float3 lightColor = L.Color * (L.Intensity * 8.0) * attenuation * shadowFactor;
    return (kD * albedo * diffuseTerm + specularTerm * F) * lightColor * ao;
}

// ============================================================
//  Directional evaluator
// ============================================================

float3 EvaluateDirectional(float3 worldPos, float3 worldNormal, float3 viewDir,
                         float3 albedo, float metallic, float roughness, float ao, float3 F0, float2 fragCoord)
{
    if (Light._DirectionalLightEnabled == 0) return float3(0.0);

    float3 lightDir = normalize(Light._DirectionalLightDirection);
    float3 halfDir = normalize(lightDir + viewDir);
    float3 radiance = Light._DirectionalLightColor * (Light._DirectionalLightIntensity * 8.0);

    float NdotL = max(dot(worldNormal, lightDir), 0.0);
    float NdotV = abs(dot(worldNormal, viewDir));
    float LdotH = max(dot(lightDir, halfDir), 0.0);

    float NDF = DistributionGGX(worldNormal, halfDir, roughness);
    float G = GeometrySmith(worldNormal, viewDir, lightDir, roughness);
    float3 F = FresnelSchlick(LdotH, F0);

    float3 kS = F;
    float3 kD = (float3(1.0) - kS) * (1.0 - metallic);

    float3 numerator = NDF * G * F;
    float denominator = 4.0 * NdotV * NdotL + 0.0001;
    float3 specular = numerator / denominator;

    float diffuseTerm = DisneyDiffuse(NdotV, NdotL, LdotH, roughness);
    float3 diffuse = kD * albedo * diffuseTerm;

    float shadowFactor;
#ifdef SG_NO_SHADOWS
    shadowFactor = 1.0;
#else
    float shadow = (Light._DirectionalLightShadowEnabled != 0)
        ? SampleDirectionalShadow(worldPos, worldNormal, fragCoord) : 0.0;
    shadowFactor = 1.0 - shadow;
#endif

    return (diffuse + specular) * radiance * NdotL * shadowFactor * ao;
}

float3 EvaluateDirectionalAniso(float3 worldPos, float3 worldNormal, float3 viewDir,
                              float3 worldTangent, float3 worldBitangent,
                              float3 albedo, float metallic, float mt, float mb,
                              float perceptualRoughness, float ao, float3 F0, float2 fragCoord)
{
    if (Light._DirectionalLightEnabled == 0) return float3(0.0);

    float3 lightDir = normalize(Light._DirectionalLightDirection);
    float3 halfDir = normalize(lightDir + viewDir);

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
    float3 F = FresnelSchlick(LdotH, F0);

    float specularTerm = max(0.0, (V * D) * PI * NdotL);
    float3 kD = (float3(1.0) - F) * (1.0 - metallic);
    float diffuseTerm = DisneyDiffuse(NdotV, NdotL, LdotH, perceptualRoughness) * NdotL;

    float shadowFactor;
#ifdef SG_NO_SHADOWS
    shadowFactor = 1.0;
#else
    float shadow = (Light._DirectionalLightShadowEnabled != 0)
        ? SampleDirectionalShadow(worldPos, worldNormal, fragCoord) : 0.0;
    shadowFactor = 1.0 - shadow;
#endif

    float3 lightColor = Light._DirectionalLightColor * (Light._DirectionalLightIntensity * 8.0) * shadowFactor;
    return (kD * albedo * diffuseTerm + specularTerm * F) * lightColor * ao;
}

// ============================================================
//  Forward lighting entry points (BVH-driven)
// ============================================================

public float3 CalculateForwardLighting(float3 worldPos, float3 worldNormal, float3 viewDir,
                              float3 albedo, float metallic, float roughness, float ao, float2 fragCoord)
{
    roughness = ApplySpecularAA(roughness, worldNormal);
    float3 F0 = lerp(float3(0.04), albedo, metallic);

    float3 totalLight = EvaluateDirectional(worldPos, worldNormal, viewDir, albedo, metallic, roughness, ao, F0, fragCoord);

    // Static tree.
    if (Lights._StaticLightRoot >= 0) {
        LBVH_Iter it;
        LBVH_Begin(it, Lights._StaticLightRoot);
        int slot;
        while ((slot = LBVH_Next(it, Lights._StaticLightNodes, Lights._StaticNodeTexSize, Lights._StaticNodeTexShift, worldPos)) >= 0) {
            LightSample L = LBVH_FetchLight(Lights._StaticLightData, Lights._StaticLightTexSize, Lights._StaticLightTexShift, slot);
            totalLight += EvaluateLocalLight(L, worldPos, worldNormal, viewDir, albedo, metallic, roughness, ao, F0, fragCoord);
        }
    }

    // Dynamic tree.
    if (Lights._DynamicLightRoot >= 0) {
        LBVH_Iter it;
        LBVH_Begin(it, Lights._DynamicLightRoot);
        int slot;
        while ((slot = LBVH_Next(it, Lights._DynamicLightNodes, Lights._DynamicNodeTexSize, Lights._DynamicNodeTexShift, worldPos)) >= 0) {
            LightSample L = LBVH_FetchLight(Lights._DynamicLightData, Lights._DynamicLightTexSize, Lights._DynamicLightTexShift, slot);
            totalLight += EvaluateLocalLight(L, worldPos, worldNormal, viewDir, albedo, metallic, roughness, ao, F0, fragCoord);
        }
    }

    return totalLight;
}

public float3 CalculateForwardLightingAniso(float3 worldPos, float3 worldNormal, float3 viewDir,
                                    float3 worldTangent, float3 worldBitangent,
                                    float3 albedo, float metallic,
                                    float roughness, float anisotropy, float ao, float2 fragCoord)
{
    roughness = ApplySpecularAA(roughness, worldNormal);
    float roughnessT = roughness * (1.0 + anisotropy);
    float roughnessB = roughness * (1.0 - anisotropy);
    float mt = roughnessT * roughnessT;
    float mb = roughnessB * roughnessB;

    float3 F0 = lerp(float3(0.04), albedo, metallic);

    float3 totalLight = EvaluateDirectionalAniso(worldPos, worldNormal, viewDir, worldTangent, worldBitangent,
                                                albedo, metallic, mt, mb, roughness, ao, F0, fragCoord);

    if (Lights._StaticLightRoot >= 0) {
        LBVH_Iter it;
        LBVH_Begin(it, Lights._StaticLightRoot);
        int slot;
        while ((slot = LBVH_Next(it, Lights._StaticLightNodes, Lights._StaticNodeTexSize, Lights._StaticNodeTexShift, worldPos)) >= 0) {
            LightSample L = LBVH_FetchLight(Lights._StaticLightData, Lights._StaticLightTexSize, Lights._StaticLightTexShift, slot);
            totalLight += EvaluateLocalLightAniso(L, worldPos, worldNormal, viewDir, worldTangent, worldBitangent,
                                                   albedo, metallic, mt, mb, roughness, ao, F0, fragCoord);
        }
    }

    if (Lights._DynamicLightRoot >= 0) {
        LBVH_Iter it;
        LBVH_Begin(it, Lights._DynamicLightRoot);
        int slot;
        while ((slot = LBVH_Next(it, Lights._DynamicLightNodes, Lights._DynamicNodeTexSize, Lights._DynamicNodeTexShift, worldPos)) >= 0) {
            LightSample L = LBVH_FetchLight(Lights._DynamicLightData, Lights._DynamicLightTexSize, Lights._DynamicLightTexShift, slot);
            totalLight += EvaluateLocalLightAniso(L, worldPos, worldNormal, viewDir, worldTangent, worldBitangent,
                                                   albedo, metallic, mt, mb, roughness, ao, F0, fragCoord);
        }
    }

    return totalLight;
}

// ============================================================
//  Local light with translucency (shared attenuation + shadow)
// ============================================================

float3 EvaluateLocalLightTranslucent(LightSample L, float3 worldPos, float3 worldNormal, float3 viewDir,
                                    float3 albedo, float metallic, float roughness, float ao, float3 F0,
                                    float translucency, float scatterPower, float scatterDist, float scatterScale, float2 fragCoord)
{
    float3 lightToPixel = worldPos - L.Position;
    float dist2 = dot(lightToPixel, lightToPixel);
    float dist = sqrt(dist2);
    float3 lightDir = -lightToPixel * (1.0 / max(dist, 1e-6));

    float spotAxisCos = 0.0;
    if (L.Type == 2) {
        spotAxisCos = dot(normalize(L.Direction), -lightDir);
        if (spotAxisCos <= L.SpotCos) return float3(0.0);
    }

    float invR2 = 1.0 / (L.Range * L.Range);
    float factor = dist2 * invR2;
    float window = clamp(1.0 - factor * factor, 0.0, 1.0);
    window *= window;
    float invSqr = 1.0 / max(dist2, 0.01);
    float attenuation = invSqr * window;

    if (L.Type == 2)
        attenuation *= smoothstep(L.SpotCos, L.InnerSpotCos, spotAxisCos);

    if (attenuation <= 0.0001) return float3(0.0);

    float NdotL = dot(worldNormal, lightDir);

    float shadowFactor = 1.0;
#ifndef SG_NO_SHADOWS
    if ((NdotL > 0.0 || translucency > 0.0) && L.ShadowEnabled != 0 && L.ShadowSlot >= 0) {
        float shadow = (L.Type == 1)
            ? SamplePointShadow(L, L.ShadowSlot, worldPos, worldNormal, fragCoord)
            : SampleSpotShadow(L, L.ShadowSlot, worldPos, worldNormal, fragCoord);
        shadowFactor = 1.0 - shadow;
    }
#endif

    float3 radiance = L.Color * (L.Intensity * 8.0) * attenuation;
    float3 result = float3(0.0);

    // PBR (front-lit only)
    if (NdotL > 0.0) {
        float3 halfDir = normalize(lightDir + viewDir);
        float NdotV = abs(dot(worldNormal, viewDir));
        float LdotH = max(dot(lightDir, halfDir), 0.0);

        float NDF = DistributionGGX(worldNormal, halfDir, roughness);
        float G = GeometrySmith(worldNormal, viewDir, lightDir, roughness);
        float3 F = FresnelSchlick(LdotH, F0);
        float3 kD = (float3(1.0) - F) * (1.0 - metallic);
        float3 specular = (NDF * G * F) / (4.0 * NdotV * NdotL + 0.0001);
        float diffuseTerm = DisneyDiffuse(NdotV, NdotL, LdotH, roughness);
        result = (kD * albedo * diffuseTerm + specular) * radiance * NdotL * shadowFactor * ao;
    }

    // Translucency (uses same attenuation and shadow, works regardless of NdotL)
    if (translucency > 0.0) {
        float3 scatter = CalculateTranslucency(lightDir, viewDir, worldNormal,
                           translucency, scatterPower, scatterDist, scatterScale,
                           L.Color * L.Intensity * attenuation);
        result += scatter * albedo * shadowFactor;
    }

    return result;
}

// ============================================================
//  Forward lighting with translucency (single-pass, unified)
// ============================================================

public float3 CalculateForwardLighting(float3 worldPos, float3 worldNormal, float3 viewDir,
                              float3 albedo, float metallic, float roughness, float ao,
                              float translucency, float scatterPower,
                              float scatterDist, float scatterScale, float2 fragCoord)
{
    roughness = ApplySpecularAA(roughness, worldNormal);
    float3 F0 = lerp(float3(0.04), albedo, metallic);
    bool hasTrans = translucency > 0.0;

    float3 totalLight = float3(0.0);

    // ---- Directional light ----
    if (Light._DirectionalLightEnabled != 0) {
        float3 lightDir = normalize(Light._DirectionalLightDirection);
        float3 radiance = Light._DirectionalLightColor * (Light._DirectionalLightIntensity * 8.0);

        float shadowFactor;
#ifdef SG_NO_SHADOWS
        shadowFactor = 1.0;
#else
        float shadow = (Light._DirectionalLightShadowEnabled != 0)
            ? SampleDirectionalShadow(worldPos, worldNormal, fragCoord) : 0.0;
        shadowFactor = 1.0 - shadow;
#endif

        // PBR
        float NdotL = max(dot(worldNormal, lightDir), 0.0);
        if (NdotL > 0.0) {
            float3 halfDir = normalize(lightDir + viewDir);
            float NdotV = abs(dot(worldNormal, viewDir));
            float LdotH = max(dot(lightDir, halfDir), 0.0);

            float NDF = DistributionGGX(worldNormal, halfDir, roughness);
            float G = GeometrySmith(worldNormal, viewDir, lightDir, roughness);
            float3 F = FresnelSchlick(LdotH, F0);
            float3 kD = (float3(1.0) - F) * (1.0 - metallic);
            float3 specular = (NDF * G * F) / (4.0 * NdotV * NdotL + 0.0001);
            float diffuseTerm = DisneyDiffuse(NdotV, NdotL, LdotH, roughness);
            totalLight += (kD * albedo * diffuseTerm + specular) * radiance * NdotL * shadowFactor * ao;
        }

        // Translucency (same shadow, no distance attenuation for directional)
        if (hasTrans) {
            float3 scatter = CalculateTranslucency(lightDir, viewDir, worldNormal,
                               translucency, scatterPower, scatterDist, scatterScale,
                               Light._DirectionalLightColor * Light._DirectionalLightIntensity);
            totalLight += scatter * albedo * shadowFactor;
        }
    }

    // ---- Static BVH ----
    if (Lights._StaticLightRoot >= 0) {
        LBVH_Iter it;
        LBVH_Begin(it, Lights._StaticLightRoot);
        int slot;
        while ((slot = LBVH_Next(it, Lights._StaticLightNodes, Lights._StaticNodeTexSize, Lights._StaticNodeTexShift, worldPos)) >= 0) {
            LightSample L = LBVH_FetchLight(Lights._StaticLightData, Lights._StaticLightTexSize, Lights._StaticLightTexShift, slot);
            totalLight += EvaluateLocalLightTranslucent(L, worldPos, worldNormal, viewDir,
                              albedo, metallic, roughness, ao, F0,
                              translucency, scatterPower, scatterDist, scatterScale, fragCoord);
        }
    }

    // ---- Dynamic BVH ----
    if (Lights._DynamicLightRoot >= 0) {
        LBVH_Iter it;
        LBVH_Begin(it, Lights._DynamicLightRoot);
        int slot;
        while ((slot = LBVH_Next(it, Lights._DynamicLightNodes, Lights._DynamicNodeTexSize, Lights._DynamicNodeTexShift, worldPos)) >= 0) {
            LightSample L = LBVH_FetchLight(Lights._DynamicLightData, Lights._DynamicLightTexSize, Lights._DynamicLightTexShift, slot);
            totalLight += EvaluateLocalLightTranslucent(L, worldPos, worldNormal, viewDir,
                              albedo, metallic, roughness, ao, F0,
                              translucency, scatterPower, scatterDist, scatterScale, fragCoord);
        }
    }

    return totalLight;
}

// ============================================================
//  Ambient lighting
// ============================================================

public float3 CalculateAmbient(float3 worldNormal)
{
    float3 ambient = float3(0.0);
    ambient += Light._AmbientColor.rgb * Light._AmbientMode.x;

    float upDot = dot(worldNormal, float3(0.0, 1.0, 0.0));
    ambient += lerp(Light._AmbientGroundColor.rgb, Light._AmbientSkyColor.rgb, upDot * 0.5 + 0.5) * Light._AmbientMode.y;

    return ambient;
}

// ============================================================
//  Light-probe spherical harmonics
// ============================================================

// Evaluate per-object SH-L2 (the standard ShadeSH9) -> diffuse irradiance E/pi for a normal.
public float3 ShadeSH9(float3 n)
{
    float4 nrm = float4(n, 1.0);
    float3 x;
    x.r = dot(Light.prowl_SHAr, nrm);
    x.g = dot(Light.prowl_SHAg, nrm);
    x.b = dot(Light.prowl_SHAb, nrm);

    float4 vB = n.xyzz * n.yzzx;   // (xy, yz, z^2, zx)
    float3 x1;
    x1.r = dot(Light.prowl_SHBr, vB);
    x1.g = dot(Light.prowl_SHBg, vB);
    x1.b = dot(Light.prowl_SHBb, vB);

    float vC = n.x * n.x - n.y * n.y;
    float3 x2 = Light.prowl_SHC.rgb * vC;

    return max(x + x1 + x2, float3(0.0));
}

// ============================================================
//  Fog
// ============================================================

public float3 ApplyFog(float3 color, float3 worldPos)
{
    if (Light._FogStates.x + Light._FogStates.y + Light._FogStates.z < 0.5)
        return color;

    float fogCoord = length(worldPos - Frame._WorldSpaceCameraPos.xyz);
    float prowlFog = 0.0;
    prowlFog += (fogCoord * Light._FogParams.z + Light._FogParams.w) * Light._FogStates.x;
    prowlFog += exp2(-fogCoord * Light._FogParams.y) * Light._FogStates.y;
    prowlFog += exp2(-fogCoord * fogCoord * Light._FogParams.x * Light._FogParams.x) * Light._FogStates.z;
    return lerp(Light._FogColor.rgb, color, clamp(prowlFog, 0.0, 1.0));
}
