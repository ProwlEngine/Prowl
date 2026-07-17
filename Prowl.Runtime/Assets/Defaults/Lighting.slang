module Lighting;

__exported import ShaderVariables;
__exported import PBR;
__exported import Shadow;
__exported import LightBVH;
import VariantAttributes;

// Shadow toggle for shader-graph / lit shaders. Declared here (module-level) so every importer
// compiles a shadowed and an unshadowed permutation.
[VariantAxis]
extern static const bool SG_NO_SHADOWS;

public static const int MAX_SHADOW_CASTERS = 4;

// ============================================================
//  Per-frame / per-scene lighting data
// ============================================================

public struct LightingData
{
    // Directional light (a single one per scene)
    public int _DirectionalLightEnabled;
    public float3 _DirectionalLightDirection;
    public float3 _DirectionalLightColor;
    public float _DirectionalLightIntensity;
    public int _DirectionalLightShadowEnabled;
    public float _DirectionalLightShadowBias;
    public float _DirectionalLightShadowNormalBias;
    public float _DirectionalLightShadowStrength;
    public float _DirectionalLightShadowQuality;

    // Shadow atlas (hardware depth-compare sampler)
    public Sampler2DShadow _ShadowAtlas;
    public float2 _ShadowAtlasSize;

    // Directional cascade shadows
    public int _CascadeCount;
    public float4x4 _CascadeShadowMatrix0;
    public float4x4 _CascadeShadowMatrix1;
    public float4x4 _CascadeShadowMatrix2;
    public float4x4 _CascadeShadowMatrix3;
    public float4 _CascadeAtlasParams0;
    public float4 _CascadeAtlasParams1;
    public float4 _CascadeAtlasParams2;
    public float4 _CascadeAtlasParams3;

    // Point shadows (6 faces per light)
    public float4x4 _PointShadowMatrices[MAX_SHADOW_CASTERS * 6];
    public float4 _PointShadowFaceParams[MAX_SHADOW_CASTERS * 6];

    // Spot shadows (1 matrix per light)
    public float4x4 _SpotShadowMatrices[MAX_SHADOW_CASTERS];
    public float4 _SpotShadowAtlasParams[MAX_SHADOW_CASTERS];

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

    // Light BVH textures
    public Sampler2D<float4> _StaticLightData;
    public Sampler2D<float4> _StaticLightNodes;
    public Sampler2D<float4> _DynamicLightData;
    public Sampler2D<float4> _DynamicLightNodes;

    public int _StaticLightTexSize;
    public int _StaticLightTexShift;
    public int _StaticNodeTexSize;
    public int _StaticNodeTexShift;
    public int _DynamicLightTexSize;
    public int _DynamicLightTexShift;
    public int _DynamicNodeTexSize;
    public int _DynamicNodeTexShift;

    public int _StaticLightRoot;
    public int _DynamicLightRoot;
}
public ParameterBlock<LightingData> Lights;

// ============================================================
//  Helpers preserved for shader-graph access
// ============================================================

public float3 GetWorldViewDir(float3 worldPos)
{
    return normalize(Frame._WorldSpaceCameraPos.xyz - worldPos);
}

public float3 GetTangentViewDir(float3 worldPos, float3 worldNormal, float3 worldTangent, float3 worldBitangent)
{
    float3 vWorld = GetWorldViewDir(worldPos);
    return normalize(mul(float3x3(normalize(worldTangent), normalize(worldBitangent), normalize(worldNormal)), vWorld));
}

// ============================================================
//  Shadow sampling
// ============================================================

public float SampleDirectionalShadow(float3 worldPos, float3 worldNormal, float2 fragCoord)
{
    if (Lights._CascadeCount == 0) return 0.0;

    float3 toCamera = worldPos - Frame._WorldSpaceCameraPos.xyz;
    float worldDistSq = dot(toCamera, toCamera) * 4.0;

    float4x4 cascadeMatrix;
    float4 cascadeParams;

    if (Lights._CascadeCount >= 1 && worldDistSq <= Lights._CascadeAtlasParams0.w * Lights._CascadeAtlasParams0.w) {
        cascadeMatrix = Lights._CascadeShadowMatrix0;
        cascadeParams = Lights._CascadeAtlasParams0;
    } else if (Lights._CascadeCount >= 2 && worldDistSq <= Lights._CascadeAtlasParams1.w * Lights._CascadeAtlasParams1.w) {
        cascadeMatrix = Lights._CascadeShadowMatrix1;
        cascadeParams = Lights._CascadeAtlasParams1;
    } else if (Lights._CascadeCount >= 3 && worldDistSq <= Lights._CascadeAtlasParams2.w * Lights._CascadeAtlasParams2.w) {
        cascadeMatrix = Lights._CascadeShadowMatrix2;
        cascadeParams = Lights._CascadeAtlasParams2;
    } else if (Lights._CascadeCount >= 4 && worldDistSq <= Lights._CascadeAtlasParams3.w * Lights._CascadeAtlasParams3.w) {
        cascadeMatrix = Lights._CascadeShadowMatrix3;
        cascadeParams = Lights._CascadeAtlasParams3;
    } else {
        if      (Lights._CascadeCount == 1) { cascadeMatrix = Lights._CascadeShadowMatrix0; cascadeParams = Lights._CascadeAtlasParams0; }
        else if (Lights._CascadeCount == 2) { cascadeMatrix = Lights._CascadeShadowMatrix1; cascadeParams = Lights._CascadeAtlasParams1; }
        else if (Lights._CascadeCount == 3) { cascadeMatrix = Lights._CascadeShadowMatrix2; cascadeParams = Lights._CascadeAtlasParams2; }
        else                                { cascadeMatrix = Lights._CascadeShadowMatrix3; cascadeParams = Lights._CascadeAtlasParams3; }
    }

    if (cascadeParams.z <= 0.0) return 0.0;

    float3 worldPosBiased = worldPos + normalize(worldNormal) * Lights._DirectionalLightShadowNormalBias;
    float4 lightSpacePos = mul(cascadeMatrix, float4(worldPosBiased, 1.0));
    float3 projCoords = lightSpacePos.xyz / lightSpacePos.w;
    projCoords = projCoords * 0.5 + 0.5;

    if (projCoords.z > 1.0 || projCoords.x < 0.0 || projCoords.x > 1.0 || projCoords.y < 0.0 || projCoords.y > 1.0)
        return 0.0;

    float atlasSize = Lights._ShadowAtlasSize.x;
    float2 atlasCoords, shadowMin, shadowMax;
    GetAtlasCoordinates(projCoords, cascadeParams, atlasSize, atlasCoords, shadowMin, shadowMax);

    float slopeBias = CalculateSlopeBias(worldNormal, Lights._DirectionalLightDirection, Lights._DirectionalLightShadowBias);
    float texelWorldSize = ((cascadeParams.w * 4.0) / (cascadeParams.z * atlasSize)) * 8.0;
    float currentDepth = projCoords.z - (slopeBias + texelWorldSize);

    return SampleShadowPCF(Lights._ShadowAtlas, atlasCoords, shadowMin, shadowMax,
                           currentDepth, Lights._DirectionalLightShadowQuality, Lights._DirectionalLightShadowStrength, fragCoord);
}

public float SamplePointShadow(LightSample L, int shadowSlot, float3 worldPos, float3 worldNormal, float2 fragCoord)
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
    float4x4 shadowMatrix = Lights._PointShadowMatrices[idx];
    float4 faceParams = Lights._PointShadowFaceParams[idx];

    float3 worldPosBiased = worldPos + normalize(worldNormal) * L.ShadowNormalBias;
    float4 lightSpacePos = mul(shadowMatrix, float4(worldPosBiased, 1.0));
    float3 projCoords = lightSpacePos.xyz / lightSpacePos.w;
    projCoords = projCoords * 0.5 + 0.5;

    if (projCoords.z > 1.0 || projCoords.x < 0.0 || projCoords.x > 1.0 || projCoords.y < 0.0 || projCoords.y > 1.0)
        return 0.0;

    float atlasSize = Lights._ShadowAtlasSize.x;
    float2 atlasCoords, shadowMin, shadowMax;
    GetAtlasCoordinates(projCoords, faceParams, atlasSize, atlasCoords, shadowMin, shadowMax);

    float finalBias = CalculateSlopeBias(worldNormal, normalize(lightToFrag), L.ShadowBias);
    float currentDepth = projCoords.z - finalBias;

    return SampleShadowPCF(Lights._ShadowAtlas, atlasCoords, shadowMin, shadowMax,
                           currentDepth, L.ShadowQuality, L.ShadowStrength, fragCoord);
}

public float SampleSpotShadow(LightSample L, int shadowSlot, float3 worldPos, float3 worldNormal, float2 fragCoord)
{
    float4 atlasParams = Lights._SpotShadowAtlasParams[shadowSlot];
    if (atlasParams.z <= 0.0) return 0.0;

    float3 worldPosBiased = worldPos + normalize(worldNormal) * L.ShadowNormalBias;
    float4 lightSpacePos = mul(Lights._SpotShadowMatrices[shadowSlot], float4(worldPosBiased, 1.0));
    float3 projCoords = lightSpacePos.xyz / lightSpacePos.w;
    projCoords = projCoords * 0.5 + 0.5;

    if (projCoords.z > 1.0 || projCoords.x < 0.0 || projCoords.x > 1.0 || projCoords.y < 0.0 || projCoords.y > 1.0)
        return 0.0;

    float atlasSize = Lights._ShadowAtlasSize.x;
    float2 atlasCoords, shadowMin, shadowMax;
    GetAtlasCoordinates(projCoords, atlasParams, atlasSize, atlasCoords, shadowMin, shadowMax);

    float finalBias = CalculateSlopeBias(worldNormal, L.Direction, L.ShadowBias);
    float currentDepth = projCoords.z - finalBias;

    return SampleShadowPCF(Lights._ShadowAtlas, atlasCoords, shadowMin, shadowMax,
                           currentDepth, L.ShadowQuality, L.ShadowStrength, fragCoord);
}

// ============================================================
//  Per-light evaluation (BVH leaf -> radiance)
// ============================================================

public float3 EvaluateLocalLight(LightSample L, float3 worldPos, float3 worldNormal, float3 viewDir,
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
    float factor = dist2 * invR2;
    float window = clamp(1.0 - factor * factor, 0.0, 1.0);
    window *= window;
    float invSqr = 1.0 / max(dist2, 0.01);
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
    static if (SG_NO_SHADOWS) {
        shadowFactor = 1.0;
    } else {
        float shadow = 0.0;
        if (L.ShadowEnabled != 0 && L.ShadowSlot >= 0) {
            if (L.Type == 1)
                shadow = SamplePointShadow(L, L.ShadowSlot, worldPos, worldNormal, fragCoord);
            else
                shadow = SampleSpotShadow(L, L.ShadowSlot, worldPos, worldNormal, fragCoord);
        }
        shadowFactor = 1.0 - shadow;
    }

    return (diffuse + specular) * radiance * NdotL * shadowFactor * ao;
}

public float3 EvaluateLocalLightAniso(LightSample L, float3 worldPos, float3 worldNormal, float3 viewDir,
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
    static if (SG_NO_SHADOWS) {
        shadowFactor = 1.0;
    } else {
        float shadow = 0.0;
        if (L.ShadowEnabled != 0 && L.ShadowSlot >= 0) {
            if (L.Type == 1)
                shadow = SamplePointShadow(L, L.ShadowSlot, worldPos, worldNormal, fragCoord);
            else
                shadow = SampleSpotShadow(L, L.ShadowSlot, worldPos, worldNormal, fragCoord);
        }
        shadowFactor = 1.0 - shadow;
    }

    float3 lightColor = L.Color * (L.Intensity * 8.0) * attenuation * shadowFactor;
    return (kD * albedo * diffuseTerm + specularTerm * F) * lightColor * ao;
}

// ============================================================
//  Directional evaluator
// ============================================================

public float3 EvaluateDirectional(float3 worldPos, float3 worldNormal, float3 viewDir,
                         float3 albedo, float metallic, float roughness, float ao, float3 F0, float2 fragCoord)
{
    if (Lights._DirectionalLightEnabled == 0) return float3(0.0);

    float3 lightDir = normalize(Lights._DirectionalLightDirection);
    float3 halfDir = normalize(lightDir + viewDir);
    float3 radiance = Lights._DirectionalLightColor * (Lights._DirectionalLightIntensity * 8.0);

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
    static if (SG_NO_SHADOWS) {
        shadowFactor = 1.0;
    } else {
        float shadow = (Lights._DirectionalLightShadowEnabled != 0)
            ? SampleDirectionalShadow(worldPos, worldNormal, fragCoord) : 0.0;
        shadowFactor = 1.0 - shadow;
    }

    return (diffuse + specular) * radiance * NdotL * shadowFactor * ao;
}

public float3 EvaluateDirectionalAniso(float3 worldPos, float3 worldNormal, float3 viewDir,
                              float3 worldTangent, float3 worldBitangent,
                              float3 albedo, float metallic, float mt, float mb,
                              float perceptualRoughness, float ao, float3 F0, float2 fragCoord)
{
    if (Lights._DirectionalLightEnabled == 0) return float3(0.0);

    float3 lightDir = normalize(Lights._DirectionalLightDirection);
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
    static if (SG_NO_SHADOWS) {
        shadowFactor = 1.0;
    } else {
        float shadow = (Lights._DirectionalLightShadowEnabled != 0)
            ? SampleDirectionalShadow(worldPos, worldNormal, fragCoord) : 0.0;
        shadowFactor = 1.0 - shadow;
    }

    float3 lightColor = Lights._DirectionalLightColor * (Lights._DirectionalLightIntensity * 8.0) * shadowFactor;
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

    if (Lights._StaticLightRoot >= 0) {
        LBVH_Iter it;
        LBVH_Begin(it, Lights._StaticLightRoot);
        int slot;
        while ((slot = LBVH_Next(it, Lights._StaticLightNodes, Lights._StaticNodeTexSize, Lights._StaticNodeTexShift, worldPos)) >= 0) {
            LightSample L = LBVH_FetchLight(Lights._StaticLightData, Lights._StaticLightTexSize, Lights._StaticLightTexShift, slot);
            totalLight += EvaluateLocalLight(L, worldPos, worldNormal, viewDir, albedo, metallic, roughness, ao, F0, fragCoord);
        }
    }

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
//  Local light with translucency
// ============================================================

public float3 EvaluateLocalLightTranslucent(LightSample L, float3 worldPos, float3 worldNormal, float3 viewDir,
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
    static if (!SG_NO_SHADOWS) {
        if ((NdotL > 0.0 || translucency > 0.0) && L.ShadowEnabled != 0 && L.ShadowSlot >= 0) {
            float shadow = (L.Type == 1)
                ? SamplePointShadow(L, L.ShadowSlot, worldPos, worldNormal, fragCoord)
                : SampleSpotShadow(L, L.ShadowSlot, worldPos, worldNormal, fragCoord);
            shadowFactor = 1.0 - shadow;
        }
    }

    float3 radiance = L.Color * (L.Intensity * 8.0) * attenuation;
    float3 result = float3(0.0);

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

    if (translucency > 0.0) {
        float3 scatter = CalculateTranslucency(lightDir, viewDir, worldNormal,
                           translucency, scatterPower, scatterDist, scatterScale,
                           L.Color * L.Intensity * attenuation);
        result += scatter * albedo * shadowFactor;
    }

    return result;
}

public float3 CalculateForwardLighting(float3 worldPos, float3 worldNormal, float3 viewDir,
                              float3 albedo, float metallic, float roughness, float ao,
                              float translucency, float scatterPower,
                              float scatterDist, float scatterScale, float2 fragCoord)
{
    roughness = ApplySpecularAA(roughness, worldNormal);
    float3 F0 = lerp(float3(0.04), albedo, metallic);
    bool hasTrans = translucency > 0.0;

    float3 totalLight = float3(0.0);

    if (Lights._DirectionalLightEnabled != 0) {
        float3 lightDir = normalize(Lights._DirectionalLightDirection);
        float3 radiance = Lights._DirectionalLightColor * (Lights._DirectionalLightIntensity * 8.0);

        float shadowFactor;
        static if (SG_NO_SHADOWS) {
            shadowFactor = 1.0;
        } else {
            float shadow = (Lights._DirectionalLightShadowEnabled != 0)
                ? SampleDirectionalShadow(worldPos, worldNormal, fragCoord) : 0.0;
            shadowFactor = 1.0 - shadow;
        }

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

        if (hasTrans) {
            float3 scatter = CalculateTranslucency(lightDir, viewDir, worldNormal,
                               translucency, scatterPower, scatterDist, scatterScale,
                               Lights._DirectionalLightColor * Lights._DirectionalLightIntensity);
            totalLight += scatter * albedo * shadowFactor;
        }
    }

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
    ambient += Lights._AmbientColor.rgb * Lights._AmbientMode.x;

    float upDot = dot(worldNormal, float3(0.0, 1.0, 0.0));
    ambient += lerp(Lights._AmbientGroundColor.rgb, Lights._AmbientSkyColor.rgb, upDot * 0.5 + 0.5) * Lights._AmbientMode.y;

    return ambient;
}

// ============================================================
//  Light-probe spherical harmonics (per-object, in the Object block)
// ============================================================

public float3 ShadeSH9(float3 n)
{
    float4 nrm = float4(n, 1.0);
    float3 x;
    x.r = dot(Object.prowl_SHAr, nrm);
    x.g = dot(Object.prowl_SHAg, nrm);
    x.b = dot(Object.prowl_SHAb, nrm);

    float4 vB = n.xyzz * n.yzzx;
    float3 x1;
    x1.r = dot(Object.prowl_SHBr, vB);
    x1.g = dot(Object.prowl_SHBg, vB);
    x1.b = dot(Object.prowl_SHBb, vB);

    float vC = n.x * n.x - n.y * n.y;
    float3 x2 = Object.prowl_SHC.rgb * vC;

    return max(x + x1 + x2, float3(0.0));
}

// ============================================================
//  Fog
// ============================================================

public float3 ApplyFog(float3 color, float3 worldPos)
{
    if (Lights._FogStates.x + Lights._FogStates.y + Lights._FogStates.z < 0.5)
        return color;

    float fogCoord = length(worldPos - Frame._WorldSpaceCameraPos.xyz);
    float prowlFog = 0.0;
    prowlFog += (fogCoord * Lights._FogParams.z + Lights._FogParams.w) * Lights._FogStates.x;
    prowlFog += exp2(-fogCoord * Lights._FogParams.y) * Lights._FogStates.y;
    prowlFog += exp2(-fogCoord * fogCoord * Lights._FogParams.x * Lights._FogParams.x) * Lights._FogStates.z;
    return lerp(Lights._FogColor.rgb, color, clamp(prowlFog, 0.0, 1.0));
}
