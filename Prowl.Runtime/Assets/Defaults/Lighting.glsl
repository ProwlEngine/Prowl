#ifndef LIGHTING_FUNCTIONS
#define LIGHTING_FUNCTIONS

#include "PBR"

// ------------------------------------------------------------------------------
// Light Structures
// ------------------------------------------------------------------------------

struct SunLightStruct {
    vec3 direction;       // Maps to dirX, dirY, dirZ
    vec3 color;           // Maps to colR, colG, colB
    float intensity;      // Maps to intensity
    mat4 shadowMatrix;    // Maps to shadowMatrix
    float shadowBias;     // Maps to shadowBias
    float shadowNormalBias;     // Maps to shadowNormalBias
    float shadowStrength; // Maps to shadowStrength
    float shadowDistance; // Maps to shadowDistance

    float atlasX; // AtlasWidth
    float atlasY; // AtlasY,
    float atlasWidth; //  AtlasWidth
};

struct SpotLightStruct {
    vec3 position;
    vec3 direction;
    vec3 color;
    float intensity;
    float range;
    float innerAngle; // Cosine of inner cone half-angle
    float outerAngle; // Cosine of outer cone half-angle
    mat4 shadowMatrix;
    float shadowBias;
    float shadowNormalBias;
    float shadowStrength;
    float atlasX;
    float atlasY;
    float atlasWidth;
};

struct PointLightStruct {
    vec3 position;
    vec3 color;
    float intensity;
    float range;
    mat4 shadowMatrix;
    float shadowBias;
    float shadowNormalBias;
    float shadowStrength;
    float atlasX;
    float atlasY;
    float atlasWidth;
};

// ------------------------------------------------------------------------------
// Shadow Sampling Functions
// ------------------------------------------------------------------------------

float SampleDirectionalShadow(SunLightStruct sun, vec3 worldPos, vec3 worldNormal, sampler2D shadowAtlas, vec2 shadowAtlasSize)
{
    float BIAS_SCALE = 0.001;
    float NORMAL_BIAS_SCALE = 0.05;

    // Perform perspective divide to get NDC coordinates
    vec3 worldPosBiased = worldPos + (normalize(worldNormal) * sun.shadowNormalBias * NORMAL_BIAS_SCALE);
    vec4 lightSpacePos = sun.shadowMatrix * vec4(worldPosBiased, 1.0);
    vec3 projCoords = lightSpacePos.xyz / lightSpacePos.w;

    // Transform to [0,1] range
    projCoords = projCoords * 0.5 + 0.5;

    // Early exit if beyond shadow distance or outside shadow map
    if (projCoords.z > 1.0 ||
        projCoords.x < 0.0 || projCoords.x > 1.0 ||
        projCoords.y < 0.0 || projCoords.y > 1.0) {
        return 0.0;
    }

    // Get shadow atlas coordinates
    vec2 atlasCoords;
    atlasCoords.x = sun.atlasX + (projCoords.x * sun.atlasWidth);
    atlasCoords.y = sun.atlasY + (projCoords.y * sun.atlasWidth);

    float atlasSize = shadowAtlasSize.x;
    atlasCoords /= atlasSize;

    // Get depth from shadow map
    float closestDepth = texture(shadowAtlas, atlasCoords.xy).r;

    // Get current depth with bias
    float currentDepth = projCoords.z - (sun.shadowBias * BIAS_SCALE);

    // Check if fragment is in shadow
    float shadow = currentDepth > closestDepth ? 1.0 : 0.0;

    // PCF (Percentage Closer Filtering) for soft shadows
    vec2 texelSize = vec2(1.0) / shadowAtlasSize;
    float pcfRadius = 1.0;
    float pcfSamples = 0.0;
    for(float x = -pcfRadius; x <= pcfRadius; x += 1.0) {
        for(float y = -pcfRadius; y <= pcfRadius; y += 1.0) {
            vec2 offset = vec2(x, y) * texelSize;
            float pcfDepth = texture(shadowAtlas, atlasCoords + offset).r;
            shadow += currentDepth > pcfDepth ? 1.0 : 0.0;
            pcfSamples += 1.0;
        }
    }
    shadow /= pcfSamples;

    // Apply shadow strength
    shadow *= sun.shadowStrength;

    return shadow;
}

float SampleSpotLightShadow(SpotLightStruct light, vec3 worldPos, vec3 worldNormal, sampler2D shadowAtlas, vec2 shadowAtlasSize)
{
    float BIAS_SCALE = 0.001;
    float NORMAL_BIAS_SCALE = 0.05;

    // Check if shadows are enabled for this light
    if (light.atlasX < 0.0 || light.shadowStrength <= 0.0) {
        return 0.0;
    }

    // Perform perspective divide to get NDC coordinates
    vec3 worldPosBiased = worldPos + (normalize(worldNormal) * light.shadowNormalBias * NORMAL_BIAS_SCALE);
    vec4 lightSpacePos = light.shadowMatrix * vec4(worldPosBiased, 1.0);
    vec3 projCoords = lightSpacePos.xyz / lightSpacePos.w;

    // Transform to [0,1] range
    projCoords = projCoords * 0.5 + 0.5;

    // Early exit if outside shadow map
    if (projCoords.z > 1.0 ||
        projCoords.x < 0.0 || projCoords.x > 1.0 ||
        projCoords.y < 0.0 || projCoords.y > 1.0) {
        return 0.0;
    }

    // Get shadow atlas coordinates
    vec2 atlasCoords;
    atlasCoords.x = light.atlasX + (projCoords.x * light.atlasWidth);
    atlasCoords.y = light.atlasY + (projCoords.y * light.atlasWidth);

    float atlasSize = shadowAtlasSize.x;
    atlasCoords /= atlasSize;

    // Get depth from shadow map
    float closestDepth = texture(shadowAtlas, atlasCoords.xy).r;

    // Get current depth with bias
    float currentDepth = projCoords.z - (light.shadowBias * BIAS_SCALE);

    // Check if fragment is in shadow
    float shadow = currentDepth > closestDepth ? 1.0 : 0.0;

    // Apply shadow strength
    shadow *= light.shadowStrength;

    return shadow;
}

float SamplePointLightShadow(PointLightStruct light, vec3 worldPos, sampler2D shadowAtlas, vec2 shadowAtlasSize)
{
    float BIAS_SCALE = 0.001;
    float NORMAL_BIAS_SCALE = 0.05;

    // Check if shadows are enabled for this light
    if (light.atlasX < 0.0 || light.shadowStrength <= 0.0) {
        return 0.0;
    }

    // Get direction from light to fragment
    vec3 lightToFrag = worldPos - light.position;
    vec3 absLightToFrag = abs(lightToFrag);

    // Determine which cubemap face to use based on the dominant axis
    int faceIndex;
    vec3 uvw = lightToFrag;
    float maxAxis = max(absLightToFrag.x, max(absLightToFrag.y, absLightToFrag.z));

    vec2 uv;
    float depth;

    // Select face and calculate UV coordinates
    if (absLightToFrag.x >= absLightToFrag.y && absLightToFrag.x >= absLightToFrag.z) {
        // X-axis dominant
        if (lightToFrag.x > 0.0) {
            // +X face (index 0, column 0, row 0)
            uv = vec2(lightToFrag.z, -lightToFrag.y) / absLightToFrag.x;
            faceIndex = 0;
        } else {
            // -X face (index 1, column 1, row 0)
            uv = vec2(-lightToFrag.z, -lightToFrag.y) / absLightToFrag.x;
            faceIndex = 1;
        }
    } else if (absLightToFrag.y >= absLightToFrag.x && absLightToFrag.y >= absLightToFrag.z) {
        // Y-axis dominant
        if (lightToFrag.y > 0.0) {
            // +Y face (index 2, column 0, row 1)
            uv = vec2(-lightToFrag.x, lightToFrag.z) / absLightToFrag.y;
            faceIndex = 2;
        } else {
            // -Y face (index 3, column 1, row 1)
            uv = vec2(-lightToFrag.x, -lightToFrag.z) / absLightToFrag.y;
            faceIndex = 3;
        }
    } else {
        // Z-axis dominant
        if (lightToFrag.z > 0.0) {
            // +Z face (index 4, column 0, row 2)
            uv = vec2(-lightToFrag.x, -lightToFrag.y) / absLightToFrag.z;
            faceIndex = 4;
        } else {
            // -Z face (index 5, column 1, row 2)
            uv = vec2(lightToFrag.x, -lightToFrag.y) / absLightToFrag.z;
            faceIndex = 5;
        }
    }

    // Convert UV from [-1, 1] to [0, 1]
    uv = uv * 0.5 + 0.5;

    // Calculate face offset in the 2x3 grid
    int col = faceIndex % 2;
    int row = faceIndex / 2;

    // Calculate atlas coordinates for this face
    vec2 atlasCoords;
    atlasCoords.x = light.atlasX + (col * light.atlasWidth) + (uv.x * light.atlasWidth);
    atlasCoords.y = light.atlasY + (row * light.atlasWidth) + (uv.y * light.atlasWidth);

    float atlasSize = shadowAtlasSize.x;
    atlasCoords /= atlasSize;

    // Get depth from shadow map
    float closestDepth = texture(shadowAtlas, atlasCoords.xy).r;

    // Calculate current depth (distance from light normalized by range)
    float currentDistance = length(lightToFrag);
    float currentDepth = currentDistance / light.range;
    currentDepth -= (light.shadowBias * BIAS_SCALE);

    // Check if fragment is in shadow
    float shadow = currentDepth > closestDepth ? 1.0 : 0.0;

    // Apply shadow strength
    shadow *= light.shadowStrength;

    return shadow;
}

// ------------------------------------------------------------------------------
// Light Calculation Functions
// ------------------------------------------------------------------------------

vec3 CalculateDirectionalLight(SunLightStruct sun, vec3 worldPos, vec3 worldNormal, vec3 cameraPos, vec3 albedo, float metallic, float roughness, float ao, sampler2D shadowAtlas, vec2 shadowAtlasSize)
{
    // Constants
    vec3 lightDir = normalize(sun.direction); // Direction from surface to light
    vec3 viewDir = normalize(-(worldPos - cameraPos));
    vec3 halfDir = normalize(lightDir + viewDir);

    // Calculate base reflectivity for metals vs non-metals
    vec3 F0 = vec3(0.04); // Default reflectivity for non-metals at normal incidence
    F0 = mix(F0, albedo, metallic); // For metals, base reflectivity is tinted by albedo

    // Calculate light radiance
    vec3 radiance = sun.color * sun.intensity;

    // Cook-Torrance BRDF
    float NDF = DistributionGGX(worldNormal, halfDir, roughness);
    float G = GeometrySmith(worldNormal, viewDir, lightDir, roughness);
    vec3 F = FresnelSchlick(max(dot(halfDir, viewDir), 0.0), F0);

    // Calculate specular and diffuse components
    vec3 kS = F; // Energy of light that gets reflected
    vec3 kD = vec3(1.0) - kS; // Energy of light that gets refracted
    kD *= 1.0 - metallic; // Metals don't have diffuse lighting

    // Put it all together
    float NdotL = max(dot(worldNormal, lightDir), 0.0);

    // Specular term
    vec3 numerator = NDF * G * F;
    float denominator = 4.0 * max(dot(worldNormal, viewDir), 0.0) * NdotL + 0.0001;
    vec3 specular = numerator / denominator;

    // Calculate shadow factor
    float shadow = SampleDirectionalShadow(sun, worldPos, worldNormal, shadowAtlas, shadowAtlasSize);
    float shadowFactor = 1.0 - shadow;

    // Final lighting contribution with shadow
    vec3 diffuse = kD * albedo / PI;
    return (diffuse + specular) * radiance * NdotL * shadowFactor * ao;
}

vec3 CalculateSpotLight(SpotLightStruct light, vec3 worldPos, vec3 worldNormal, vec3 cameraPos, vec3 albedo, float metallic, float roughness, float ao, sampler2D shadowAtlas, vec2 shadowAtlasSize)
{
    // Calculate direction from surface to light
    vec3 lightDir = normalize(light.position - worldPos);
    vec3 viewDir = normalize(-(worldPos - cameraPos));
    vec3 halfDir = normalize(lightDir + viewDir);

    // Calculate distance attenuation
    float distance = length(light.position - worldPos);
    if (distance > light.range) {
        return vec3(0.0);
    }

    // Physical distance attenuation (inverse square law with smoothing)
    float attenuation = clamp(1.0 - pow(distance / light.range, 4.0), 0.0, 1.0);
    attenuation = (attenuation * attenuation) / (distance * distance + 1.0);

    // Calculate spot light cone attenuation
    float theta = dot(lightDir, normalize(-light.direction));
    float epsilon = light.innerAngle - light.outerAngle;
    float spotAttenuation = clamp((theta - light.outerAngle) / epsilon, 0.0, 1.0);

    // Early exit if outside cone
    if (spotAttenuation <= 0.0) {
        return vec3(0.0);
    }

    // Calculate base reflectivity
    vec3 F0 = vec3(0.04);
    F0 = mix(F0, albedo, metallic);

    // Calculate light radiance with attenuation
    vec3 radiance = light.color * light.intensity * attenuation * spotAttenuation;

    // Cook-Torrance BRDF
    float NDF = DistributionGGX(worldNormal, halfDir, roughness);
    float G = GeometrySmith(worldNormal, viewDir, lightDir, roughness);
    vec3 F = FresnelSchlick(max(dot(halfDir, viewDir), 0.0), F0);

    // Calculate specular and diffuse components
    vec3 kS = F;
    vec3 kD = vec3(1.0) - kS;
    kD *= 1.0 - metallic;

    // Put it all together
    float NdotL = max(dot(worldNormal, lightDir), 0.0);

    // Specular term
    vec3 numerator = NDF * G * F;
    float denominator = 4.0 * max(dot(worldNormal, viewDir), 0.0) * NdotL + 0.0001;
    vec3 specular = numerator / denominator;

    // Calculate shadow factor
    float shadow = SampleSpotLightShadow(light, worldPos, worldNormal, shadowAtlas, shadowAtlasSize);
    float shadowFactor = 1.0 - shadow;

    // Final lighting contribution with shadow
    vec3 diffuse = kD * albedo / PI;
    return (diffuse + specular) * radiance * NdotL * shadowFactor * ao;
}

vec3 CalculatePointLight(PointLightStruct light, vec3 worldPos, vec3 worldNormal, vec3 cameraPos, vec3 albedo, float metallic, float roughness, float ao, sampler2D shadowAtlas, vec2 shadowAtlasSize)
{
    // Calculate direction from surface to light
    vec3 lightDir = normalize(light.position - worldPos);
    vec3 viewDir = normalize(-(worldPos - cameraPos));
    vec3 halfDir = normalize(lightDir + viewDir);

    // Calculate distance attenuation
    float distance = length(light.position - worldPos);
    if (distance > light.range) {
        return vec3(0.0);
    }

    // Physical distance attenuation (inverse square law with smoothing)
    float attenuation = clamp(1.0 - pow(distance / light.range, 4.0), 0.0, 1.0);
    attenuation = (attenuation * attenuation) / (distance * distance + 1.0);

    // Calculate base reflectivity
    vec3 F0 = vec3(0.04);
    F0 = mix(F0, albedo, metallic);

    // Calculate light radiance with attenuation
    vec3 radiance = light.color * light.intensity * attenuation;

    // Cook-Torrance BRDF
    float NDF = DistributionGGX(worldNormal, halfDir, roughness);
    float G = GeometrySmith(worldNormal, viewDir, lightDir, roughness);
    vec3 F = FresnelSchlick(max(dot(halfDir, viewDir), 0.0), F0);

    // Calculate specular and diffuse components
    vec3 kS = F;
    vec3 kD = vec3(1.0) - kS;
    kD *= 1.0 - metallic;

    // Put it all together
    float NdotL = max(dot(worldNormal, lightDir), 0.0);

    // Specular term
    vec3 numerator = NDF * G * F;
    float denominator = 4.0 * max(dot(worldNormal, viewDir), 0.0) * NdotL + 0.0001;
    vec3 specular = numerator / denominator;

    // Calculate shadow factor
    float shadow = SamplePointLightShadow(light, worldPos, shadowAtlas, shadowAtlasSize);
    float shadowFactor = 1.0 - shadow;

    // Final lighting contribution with shadow
    vec3 diffuse = kD * albedo / PI;
    return (diffuse + specular) * radiance * NdotL * shadowFactor * ao;
}

#endif
