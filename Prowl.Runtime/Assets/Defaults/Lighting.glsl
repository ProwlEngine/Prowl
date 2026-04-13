// Forward lighting utilities for Prowl Engine
// Include this in any lit forward shader (Standard, Terrain, Grass, etc.)
// Provides: CalculateForwardLighting(), ApplyAmbient(), ApplyFog()

#ifndef PROWL_LIGHTING
#define PROWL_LIGHTING

#include "PBR"
#include "Shadow"

// ============================================================
//  Light uniform declarations (max 8 lights per frame)
// ============================================================

#define MAX_FORWARD_LIGHTS 8

// Light types: 0 = Directional, 1 = Point, 2 = Spot
uniform int _LightCount;
uniform int _LightType[MAX_FORWARD_LIGHTS];

// Shared light properties
uniform vec3 _LightPositions[MAX_FORWARD_LIGHTS];
uniform vec3 _LightDirections[MAX_FORWARD_LIGHTS];
uniform vec3 _LightColors[MAX_FORWARD_LIGHTS];
uniform float _LightIntensities[MAX_FORWARD_LIGHTS];
uniform float _LightRanges[MAX_FORWARD_LIGHTS];

// Spot light specific
uniform float _LightSpotAngles[MAX_FORWARD_LIGHTS];      // outer cone angle in degrees
uniform float _LightInnerSpotAngles[MAX_FORWARD_LIGHTS];  // inner cone angle in degrees

// Shadow data per light
uniform int _LightShadowEnabled[MAX_FORWARD_LIGHTS];   // 0 or 1
uniform float _LightShadowBias[MAX_FORWARD_LIGHTS];
uniform float _LightShadowNormalBias[MAX_FORWARD_LIGHTS];
uniform float _LightShadowStrength[MAX_FORWARD_LIGHTS];
uniform float _LightShadowQuality[MAX_FORWARD_LIGHTS];

// Shadow atlas
uniform sampler2D _ShadowAtlas;
uniform vec2 _ShadowAtlasSize;

// ============================================================
//  Directional light cascade shadow data (light index 0 only)
// ============================================================

uniform int _CascadeCount;
uniform mat4 _CascadeShadowMatrix0;
uniform mat4 _CascadeShadowMatrix1;
uniform mat4 _CascadeShadowMatrix2;
uniform mat4 _CascadeShadowMatrix3;
uniform vec4 _CascadeAtlasParams0;
uniform vec4 _CascadeAtlasParams1;
uniform vec4 _CascadeAtlasParams2;
uniform vec4 _CascadeAtlasParams3;

// ============================================================
//  Additional shadow-casting point/spot lights (max 2)
// ============================================================

// Point light shadows: 6 faces per light, max 2 lights = 12 entries
uniform mat4 _PointShadowMatrices[12];
uniform vec4 _PointShadowFaceParams[12]; // xy: atlasPos, z: faceSize, w: farPlane

// Spot light shadows: 1 matrix per light, max 2 lights = 2 entries
uniform mat4 _SpotShadowMatrices[2];
uniform vec4 _SpotShadowAtlasParams[2]; // xy: atlasPos, z: atlasSize, w: unused

// Per-light: index into the shadow arrays (-1 = no shadow, 0 or 1 = slot index)
uniform int _LightShadowSlot[MAX_FORWARD_LIGHTS];

// ============================================================
//  Fog uniforms
// ============================================================

uniform vec4 _FogColor;
uniform vec4 _FogParams;  // x: density/sqrt(ln(2)) for Exp2, y: density/ln(2) for Exp, z: -1/(end-start) for Linear, w: end/(end-start) for Linear
uniform vec3 _FogStates;  // x: linear enabled, y: exp enabled, z: exp2 enabled

// ============================================================
//  Ambient lighting uniforms
// ============================================================

uniform vec2 _AmbientMode;  // x: uniform enabled, y: hemisphere enabled
uniform vec4 _AmbientColor;
uniform vec4 _AmbientSkyColor;
uniform vec4 _AmbientGroundColor;
uniform float _AmbientStrength;

// ============================================================
//  Shadow sampling functions
// ============================================================

float SampleDirectionalShadow(vec3 worldPos, vec3 worldNormal)
{
    if (_CascadeCount == 0) return 0.0;

    float worldDistance = distance(worldPos, _WorldSpaceCameraPos.xyz) * 2.0;

    mat4 cascadeMatrix;
    vec4 cascadeParams;

    // Select cascade by distance
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
        // Beyond all cascades — use last
        if (_CascadeCount == 1)      { cascadeMatrix = _CascadeShadowMatrix0; cascadeParams = _CascadeAtlasParams0; }
        else if (_CascadeCount == 2) { cascadeMatrix = _CascadeShadowMatrix1; cascadeParams = _CascadeAtlasParams1; }
        else if (_CascadeCount == 3) { cascadeMatrix = _CascadeShadowMatrix2; cascadeParams = _CascadeAtlasParams2; }
        else                         { cascadeMatrix = _CascadeShadowMatrix3; cascadeParams = _CascadeAtlasParams3; }
    }

    if (cascadeParams.z <= 0.0) return 0.0;

    vec3 worldPosBiased = worldPos + normalize(worldNormal) * _LightShadowNormalBias[0];
    vec4 lightSpacePos = cascadeMatrix * vec4(worldPosBiased, 1.0);
    vec3 projCoords = lightSpacePos.xyz / lightSpacePos.w;
    projCoords = projCoords * 0.5 + 0.5;

    if (projCoords.z > 1.0 || projCoords.x < 0.0 || projCoords.x > 1.0 || projCoords.y < 0.0 || projCoords.y > 1.0)
        return 0.0;

    float atlasSize = _ShadowAtlasSize.x;
    vec2 atlasCoords, shadowMin, shadowMax;
    GetAtlasCoordinates(projCoords, cascadeParams, atlasSize, atlasCoords, shadowMin, shadowMax);

    float slopeBias = CalculateSlopeBias(worldNormal, _LightDirections[0], _LightShadowBias[0]);
    float texelWorldSize = ((cascadeParams.w * 4.0) / (cascadeParams.z * atlasSize)) * 8.0;
    float currentDepth = projCoords.z - (slopeBias + texelWorldSize);

    return SampleShadowPCF(_ShadowAtlas, atlasCoords, shadowMin, shadowMax,
                           currentDepth, _LightShadowQuality[0], _LightShadowStrength[0]);
}

float SamplePointShadow(int lightIndex, int shadowSlot, vec3 worldPos, vec3 worldNormal)
{
    vec3 lightToFrag = worldPos - _LightPositions[lightIndex];
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

    vec3 worldPosBiased = worldPos + normalize(worldNormal) * _LightShadowNormalBias[lightIndex];
    vec4 lightSpacePos = shadowMatrix * vec4(worldPosBiased, 1.0);
    vec3 projCoords = lightSpacePos.xyz / lightSpacePos.w;
    projCoords = projCoords * 0.5 + 0.5;

    if (projCoords.z > 1.0 || projCoords.x < 0.0 || projCoords.x > 1.0 || projCoords.y < 0.0 || projCoords.y > 1.0)
        return 0.0;

    float atlasSize = _ShadowAtlasSize.x;
    vec2 atlasCoords, shadowMin, shadowMax;
    GetAtlasCoordinates(projCoords, faceParams, atlasSize, atlasCoords, shadowMin, shadowMax);

    float finalBias = CalculateSlopeBias(worldNormal, normalize(lightToFrag), _LightShadowBias[lightIndex]);
    float currentDepth = projCoords.z - finalBias;

    return SampleShadowPCF(_ShadowAtlas, atlasCoords, shadowMin, shadowMax,
                           currentDepth, _LightShadowQuality[lightIndex], _LightShadowStrength[lightIndex]);
}

float SampleSpotShadow(int lightIndex, int shadowSlot, vec3 worldPos, vec3 worldNormal)
{
    vec4 atlasParams = _SpotShadowAtlasParams[shadowSlot];
    if (atlasParams.z <= 0.0) return 0.0;

    vec3 worldPosBiased = worldPos + normalize(worldNormal) * _LightShadowNormalBias[lightIndex];
    vec4 lightSpacePos = _SpotShadowMatrices[shadowSlot] * vec4(worldPosBiased, 1.0);
    vec3 projCoords = lightSpacePos.xyz / lightSpacePos.w;
    projCoords = projCoords * 0.5 + 0.5;

    if (projCoords.z > 1.0 || projCoords.x < 0.0 || projCoords.x > 1.0 || projCoords.y < 0.0 || projCoords.y > 1.0)
        return 0.0;

    float atlasSize = _ShadowAtlasSize.x;
    vec2 atlasCoords, shadowMin, shadowMax;
    GetAtlasCoordinates(projCoords, atlasParams, atlasSize, atlasCoords, shadowMin, shadowMax);

    float finalBias = CalculateSlopeBias(worldNormal, _LightDirections[lightIndex], _LightShadowBias[lightIndex]);
    float currentDepth = projCoords.z - finalBias;

    return SampleShadowPCF(_ShadowAtlas, atlasCoords, shadowMin, shadowMax,
                           currentDepth, _LightShadowQuality[lightIndex], _LightShadowStrength[lightIndex]);
}

// ============================================================
//  Per-light PBR calculation
// ============================================================

vec3 CalculateSingleLight(int i, vec3 worldPos, vec3 worldNormal, vec3 viewDir,
                          vec3 albedo, float metallic, float roughness, float ao, vec3 F0)
{
    int lightType = _LightType[i];
    vec3 lightDir;
    float attenuation = 1.0;

    if (lightType == 0) {
        // Directional
        lightDir = normalize(_LightDirections[i]);
    } else {
        // Point or Spot
        vec3 lightToPixel = worldPos - _LightPositions[i];
        float dist = length(lightToPixel);
        lightDir = normalize(-lightToPixel);

        // Distance attenuation
        float range = _LightRanges[i];
        float distAtten = 1.0 / (dist * dist + 1.0);
        float rangeAtten = 1.0 - smoothstep(range * 0.8, range, dist);
        attenuation = distAtten * rangeAtten;

        if (lightType == 2) {
            // Spot cone attenuation
            float spotAngleRad = radians(_LightSpotAngles[i]);
            float innerSpotAngleRad = radians(_LightInnerSpotAngles[i]);
            float lightAngleCos = dot(normalize(_LightDirections[i]), normalize(lightToPixel));
            float outerCos = cos(spotAngleRad);
            float innerCos = cos(innerSpotAngleRad);
            attenuation *= smoothstep(outerCos, innerCos, lightAngleCos);
        }

        if (attenuation <= 0.0001) return vec3(0.0);
    }

    vec3 halfDir = normalize(lightDir + viewDir);
    vec3 radiance = _LightColors[i] * _LightIntensities[i] * attenuation;

    float NdotL = max(dot(worldNormal, lightDir), 0.0);
    float NdotV = abs(dot(worldNormal, viewDir)); // abs handles backface normals
    float LdotH = max(dot(lightDir, halfDir), 0.0);

    // Cook-Torrance specular BRDF
    float NDF = DistributionGGX(worldNormal, halfDir, roughness);
    float G = GeometrySmith(worldNormal, viewDir, lightDir, roughness);
    vec3 F = FresnelSchlick(LdotH, F0);

    vec3 kS = F;
    vec3 kD = (vec3(1.0) - kS) * (1.0 - metallic);

    vec3 numerator = NDF * G * F;
    float denominator = 4.0 * NdotV * NdotL + 0.0001;
    vec3 specular = numerator / denominator;

    // Disney Diffuse (roughness-aware)
    float diffuseTerm = DisneyDiffuse(NdotV, NdotL, LdotH, roughness);
    vec3 diffuse = kD * albedo * diffuseTerm;

    // Shadow
    float shadow = 0.0;
    if (_LightShadowEnabled[i] != 0) {
        if (lightType == 0) {
            shadow = SampleDirectionalShadow(worldPos, worldNormal);
        } else {
            int slot = _LightShadowSlot[i];
            if (slot >= 0) {
                if (lightType == 1)
                    shadow = SamplePointShadow(i, slot, worldPos, worldNormal);
                else
                    shadow = SampleSpotShadow(i, slot, worldPos, worldNormal);
            }
        }
    }
    float shadowFactor = 1.0 - shadow;

    return (diffuse + specular) * radiance * NdotL * shadowFactor * ao;
}

// ============================================================
//  Per-light Anisotropic PBR calculation
// ============================================================

// Per-light anisotropic BRDF (matches Lux convention)
// mt and mb are SQUARED roughness values passed in from the entry point.
vec3 CalculateSingleLightAniso(int i, vec3 worldPos, vec3 worldNormal, vec3 viewDir,
                                vec3 worldTangent, vec3 worldBitangent,
                                vec3 albedo, float metallic, float mt, float mb,
                                float perceptualRoughness, float ao, vec3 F0)
{
    int lightType = _LightType[i];
    vec3 lightDir;
    float attenuation = 1.0;

    if (lightType == 0) {
        lightDir = normalize(_LightDirections[i]);
    } else {
        vec3 lightToPixel = worldPos - _LightPositions[i];
        float dist = length(lightToPixel);
        lightDir = normalize(-lightToPixel);

        float range = _LightRanges[i];
        float distAtten = 1.0 / (dist * dist + 1.0);
        float rangeAtten = 1.0 - smoothstep(range * 0.8, range, dist);
        attenuation = distAtten * rangeAtten;

        if (lightType == 2) {
            float spotAngleRad = radians(_LightSpotAngles[i]);
            float innerSpotAngleRad = radians(_LightInnerSpotAngles[i]);
            float lightAngleCos = dot(normalize(_LightDirections[i]), normalize(lightToPixel));
            attenuation *= smoothstep(cos(spotAngleRad), cos(innerSpotAngleRad), lightAngleCos);
        }

        if (attenuation <= 0.0001) return vec3(0.0);
    }

    vec3 halfDir = normalize(lightDir + viewDir);

    float NdotL = max(dot(worldNormal, lightDir), 0.0);
    float NdotV = abs(dot(worldNormal, viewDir));
    float LdotH = max(dot(lightDir, halfDir), 0.0);
    float NdotH = max(dot(worldNormal, halfDir), 0.0);

    // Anisotropic dot products
    float TdotH = dot(worldTangent, halfDir);
    float BdotH = dot(worldBitangent, halfDir);
    float TdotV = dot(worldTangent, viewDir);
    float BdotV = dot(worldBitangent, viewDir);
    float TdotL = dot(worldTangent, lightDir);
    float BdotL = dot(worldBitangent, lightDir);

    // Anisotropic specular: V * D * PI (Lux convention)
    // V already includes 1/(4*NdotV*NdotL), so no separate denominator needed
    float D = DistributionGGXAniso(TdotH, BdotH, NdotH, mt, mb);
    float V = GeometrySmithAniso(TdotV, BdotV, NdotV, TdotL, BdotL, NdotL, mt, mb);
    vec3 F = FresnelSchlick(LdotH, F0);

    float specularTerm = max(0.0, (V * D) * PI * NdotL);

    vec3 kD = (vec3(1.0) - F) * (1.0 - metallic);

    // Disney Diffuse with averaged perceptual roughness
    float diffuseTerm = DisneyDiffuse(NdotV, NdotL, LdotH, perceptualRoughness) * NdotL;

    // Shadow
    float shadow = 0.0;
    if (_LightShadowEnabled[i] != 0) {
        if (lightType == 0) {
            shadow = SampleDirectionalShadow(worldPos, worldNormal);
        } else {
            int slot = _LightShadowSlot[i];
            if (slot >= 0) {
                if (lightType == 1)
                    shadow = SamplePointShadow(i, slot, worldPos, worldNormal);
                else
                    shadow = SampleSpotShadow(i, slot, worldPos, worldNormal);
            }
        }
    }
    float shadowFactor = 1.0 - shadow;

    vec3 lightColor = _LightColors[i] * _LightIntensities[i] * attenuation * shadowFactor;

    // Final composition (matches Lux BRDF output structure)
    return (kD * albedo * diffuseTerm + specularTerm * F) * lightColor * ao;
}

// ============================================================
//  Anisotropic forward lighting entry point
// ============================================================

vec3 CalculateForwardLightingAniso(vec3 worldPos, vec3 worldNormal, vec3 viewDir,
                                    vec3 worldTangent, vec3 worldBitangent,
                                    vec3 albedo, float metallic,
                                    float roughness, float anisotropy, float ao)
{
    roughness = ApplySpecularAA(roughness, worldNormal);

    // Split roughness into tangent/bitangent based on anisotropy [-1, 1]
    float roughnessT = roughness * (1.0 + anisotropy);
    float roughnessB = roughness * (1.0 - anisotropy);

    // Square roughness for D and V functions (Lux convention)
    float mt = roughnessT * roughnessT;
    float mb = roughnessB * roughnessB;

    vec3 F0 = mix(vec3(0.04), albedo, metallic);
    vec3 totalLight = vec3(0.0);

    for (int i = 0; i < _LightCount && i < MAX_FORWARD_LIGHTS; i++) {
        totalLight += CalculateSingleLightAniso(i, worldPos, worldNormal, viewDir,
                                                 worldTangent, worldBitangent,
                                                 albedo, metallic, mt, mb, roughness, ao, F0);
    }

    return totalLight;
}

// ============================================================
//  Main forward lighting entry point
// ============================================================

vec3 CalculateForwardLighting(vec3 worldPos, vec3 worldNormal, vec3 viewDir,
                              vec3 albedo, float metallic, float roughness, float ao)
{
    // Specular anti-aliasing: increase roughness where normal variance is high
    roughness = ApplySpecularAA(roughness, worldNormal);

    vec3 F0 = mix(vec3(0.04), albedo, metallic);
    vec3 totalLight = vec3(0.0);

    for (int i = 0; i < _LightCount && i < MAX_FORWARD_LIGHTS; i++) {
        totalLight += CalculateSingleLight(i, worldPos, worldNormal, viewDir,
                                           albedo, metallic, roughness, ao, F0);
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
    // If no fog mode is active, return color unchanged
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
