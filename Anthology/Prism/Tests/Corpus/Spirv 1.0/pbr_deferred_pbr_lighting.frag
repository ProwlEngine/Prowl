#version 460 core

layout(set = 0, binding = 0) uniform sampler2D gAlbedo;
layout(set = 0, binding = 1) uniform sampler2D gNormal;
layout(set = 0, binding = 2) uniform sampler2D gMaterial;
layout(set = 0, binding = 3) uniform sampler2D gDepth;
layout(set = 0, binding = 4) uniform sampler2DArray shadowCascadeMap;
layout(set = 0, binding = 5) uniform samplerCube irradianceMap;
layout(set = 0, binding = 6) uniform samplerCube prefilteredEnvMap;
layout(set = 0, binding = 7) uniform sampler2D brdfLUT;
layout(set = 0, binding = 8) uniform sampler2D ssaoMap;

#define MAX_LIGHTS 32
#define MAX_CASCADES 4

struct PunctualLight
{
    vec4 positionType;
    vec4 directionRange;
    vec4 colorIntensity;
    vec4 spotParams;
};

layout(set = 1, binding = 0) uniform CameraUBO
{
    mat4 view;
    mat4 projection;
    mat4 invViewProj;
    vec4 cameraPosition;
    vec4 cascadeSplits;
    mat4 cascadeViewProj[MAX_CASCADES];
} camera;

layout(set = 1, binding = 1) uniform LightsUBO
{
    PunctualLight lights[MAX_LIGHTS];
    int lightCount;
    float maxPrefilteredMip;
    float iblIntensity;
    float aoStrength;
} sceneLights;

layout(location = 0) in vec2 inUV;
layout(location = 0) out vec4 outColor;

const float PI = 3.14159265359;

vec3 octDecode(vec2 e)
{
    e = e * 2.0 - 1.0;
    vec3 n = vec3(e.x, e.y, 1.0 - abs(e.x) - abs(e.y));
    float t = clamp(-n.z, 0.0, 1.0);
    n.x += n.x >= 0.0 ? -t : t;
    n.y += n.y >= 0.0 ? -t : t;
    return normalize(n);
}

float distributionGGX(vec3 N, vec3 H, float roughness)
{
    float a = roughness * roughness;
    float a2 = a * a;
    float NdotH = max(dot(N, H), 0.0);
    float NdotH2 = NdotH * NdotH;
    float denom = (NdotH2 * (a2 - 1.0) + 1.0);
    denom = PI * denom * denom;
    return a2 / max(denom, 1e-7);
}

float visibilitySmithHeightCorrelated(float NdotV, float NdotL, float roughness)
{
    float a2 = roughness * roughness * roughness * roughness;
    float ggxV = NdotL * sqrt(NdotV * NdotV * (1.0 - a2) + a2);
    float ggxL = NdotV * sqrt(NdotL * NdotL * (1.0 - a2) + a2);
    return 0.5 / max(ggxV + ggxL, 1e-7);
}

vec3 fresnelSchlickRoughness(float cosTheta, vec3 F0, float roughness)
{
    float f90 = clamp(1.0 - roughness, 0.0, 1.0);
    return F0 + (max(vec3(f90), F0) - F0) * pow(clamp(1.0 - cosTheta, 0.0, 1.0), 5.0);
}

vec3 reconstructWorldPos(vec2 uv, float depth)
{
    vec4 ndc = vec4(uv * 2.0 - 1.0, depth, 1.0);
    vec4 worldPos = camera.invViewProj * ndc;
    return worldPos.xyz / worldPos.w;
}

int selectCascade(float viewSpaceDepth)
{
    if (viewSpaceDepth < camera.cascadeSplits.x) return 0;
    if (viewSpaceDepth < camera.cascadeSplits.y) return 1;
    if (viewSpaceDepth < camera.cascadeSplits.z) return 2;
    return 3;
}

float cascadeSplitDistance(int cascadeIndex)
{
    if (cascadeIndex == 0) return camera.cascadeSplits.x;
    if (cascadeIndex == 1) return camera.cascadeSplits.y;
    if (cascadeIndex == 2) return camera.cascadeSplits.z;
    return camera.cascadeSplits.w;
}

float sampleShadowCascade(int cascadeIndex, vec3 worldPos, vec3 N, vec3 L)
{
    vec4 lightClip = camera.cascadeViewProj[cascadeIndex] * vec4(worldPos, 1.0);
    vec3 projCoords = lightClip.xyz / lightClip.w;
    projCoords.xy = projCoords.xy * 0.5 + 0.5;

    if (projCoords.z > 1.0 || projCoords.x < 0.0 || projCoords.x > 1.0 || projCoords.y < 0.0 || projCoords.y > 1.0)
    {
        return 1.0;
    }

    float bias = max(0.0025 * (1.0 - dot(N, L)), 0.00025);
    vec2 texelSize = 1.0 / vec2(textureSize(shadowCascadeMap, 0).xy);

    float shadow = 0.0;
    for (int x = -2; x <= 2; x++)
    {
        for (int y = -2; y <= 2; y++)
        {
            vec2 offset = vec2(float(x), float(y)) * texelSize;
            float closestDepth = texture(shadowCascadeMap, vec3(projCoords.xy + offset, float(cascadeIndex))).r;
            shadow += (projCoords.z - bias) > closestDepth ? 0.0 : 1.0;
        }
    }
    return shadow / 25.0;
}

float sampleShadow(vec3 worldPos, float viewSpaceDepth, vec3 N, vec3 L)
{
    int cascadeIndex = selectCascade(viewSpaceDepth);
    float shadow = sampleShadowCascade(cascadeIndex, worldPos, N, L);

    float splitDist = cascadeSplitDistance(cascadeIndex);
    float blendRange = 0.06;
    float fadeStart = splitDist * (1.0 - blendRange);

    if (viewSpaceDepth > fadeStart && cascadeIndex < MAX_CASCADES - 1)
    {
        float nextShadow = sampleShadowCascade(cascadeIndex + 1, worldPos, N, L);
        float blendFactor = clamp((viewSpaceDepth - fadeStart) / max(splitDist - fadeStart, 1e-4), 0.0, 1.0);
        shadow = mix(shadow, nextShadow, blendFactor);
    }
    return shadow;
}

void main()
{
    float depth = texture(gDepth, inUV).r;
    if (depth >= 1.0)
    {
        outColor = vec4(0.0, 0.0, 0.0, 1.0);
        return;
    }

    vec3 worldPos = reconstructWorldPos(inUV, depth);
    vec4 albedoSample = texture(gAlbedo, inUV);
    vec3 albedo = albedoSample.rgb;
    vec3 N = octDecode(texture(gNormal, inUV).rg);
    vec4 materialSample = texture(gMaterial, inUV);
    float metallic = materialSample.r;
    float roughness = clamp(materialSample.g, 0.045, 1.0);
    float materialAO = materialSample.b;
    float ssao = texture(ssaoMap, inUV).r;
    float ao = clamp(mix(1.0, materialAO * ssao, sceneLights.aoStrength), 0.0, 1.0);

    vec3 V = normalize(camera.cameraPosition.xyz - worldPos);
    float NdotV = max(dot(N, V), 1e-4);

    vec3 F0 = mix(vec3(0.04), albedo, metallic);
    vec3 diffuseColor = albedo * (1.0 - metallic);

    vec4 viewSpacePos = camera.view * vec4(worldPos, 1.0);
    float viewSpaceDepth = -viewSpacePos.z;

    vec3 directLighting = vec3(0.0);
    for (int i = 0; i < sceneLights.lightCount; i++)
    {
        PunctualLight light = sceneLights.lights[i];
        int lightType = int(light.positionType.w);

        vec3 L;
        float attenuation = 1.0;

        if (lightType == 0)
        {
            L = normalize(-light.directionRange.xyz);
        }
        else
        {
            vec3 toLight = light.positionType.xyz - worldPos;
            float dist = length(toLight);
            L = toLight / max(dist, 1e-5);

            float range = light.directionRange.w;
            float distRatio = clamp(dist / max(range, 1e-4), 0.0, 1.0);
            float rangeFalloff = distRatio * distRatio;
            rangeFalloff = clamp(1.0 - rangeFalloff * rangeFalloff, 0.0, 1.0);
            rangeFalloff = rangeFalloff * rangeFalloff;
            attenuation = rangeFalloff / max(dist * dist, 1e-4);

            if (lightType == 2)
            {
                vec3 spotDir = normalize(light.directionRange.xyz);
                float cosAngle = dot(-L, spotDir);
                float innerCos = light.spotParams.x;
                float outerCos = light.spotParams.y;
                float spotFalloff = clamp((cosAngle - outerCos) / max(innerCos - outerCos, 1e-4), 0.0, 1.0);
                attenuation *= spotFalloff * spotFalloff;
            }
        }

        if (attenuation <= 0.0)
        {
            continue;
        }

        vec3 H = normalize(V + L);
        float NdotL = max(dot(N, L), 0.0);
        if (NdotL <= 0.0)
        {
            continue;
        }

        float shadowFactor = 1.0;
        if (lightType == 0)
        {
            shadowFactor = sampleShadow(worldPos, viewSpaceDepth, N, L);
        }

        float D = distributionGGX(N, H, roughness);
        float Vis = visibilitySmithHeightCorrelated(NdotV, NdotL, roughness);
        vec3 F = fresnelSchlickRoughness(max(dot(H, V), 0.0), F0, roughness);

        vec3 specular = D * Vis * F;
        vec3 diffuse = diffuseColor * (1.0 - F) / PI;

        vec3 radiance = light.colorIntensity.rgb * light.colorIntensity.w * attenuation;
        directLighting += (diffuse + specular) * radiance * NdotL * shadowFactor;
    }

    vec3 kS = fresnelSchlickRoughness(NdotV, F0, roughness);
    vec3 kD = (1.0 - kS) * (1.0 - metallic);

    vec3 irradiance = texture(irradianceMap, N).rgb;
    vec3 diffuseIBL = irradiance * diffuseColor * kD;

    vec3 R = reflect(-V, N);
    float lod = roughness * sceneLights.maxPrefilteredMip;
    vec3 prefilteredColor = textureLod(prefilteredEnvMap, R, lod).rgb;
    vec2 envBRDF = texture(brdfLUT, vec2(NdotV, roughness)).rg;
    vec3 specularIBL = prefilteredColor * (kS * envBRDF.x + envBRDF.y);

    vec3 ambient = (diffuseIBL + specularIBL) * ao * sceneLights.iblIntensity;

    vec3 finalColor = directLighting + ambient;
    outColor = vec4(finalColor, albedoSample.a);
}
