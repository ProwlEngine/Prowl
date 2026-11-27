#version 460 core

const float PI = 3.14159265359;

layout(location = 0) in vec3 inWorldPos;
layout(location = 1) in vec3 inWorldNormal;
layout(location = 2) in vec2 inUV;
layout(location = 3) in float inViewSpaceZ;

layout(location = 0) out vec4 outColor;

layout(set = 0, binding = 0) uniform ForwardFrameData {
    vec4 cameraPositionWS; // xyz = world-space eye position, w unused
    vec2 screenDimensions;
    float nearZ;
    float farZ;
    ivec4 clusterGridDim; // xyz = cluster grid dimensions (16, 9, 24), w unused
} uFrame;

struct Light {
    vec4 position;   // world-space xyz, w = radius
    vec4 color;      // rgb = color, a = intensity
    vec4 direction;  // world-space spot direction xyz, w = cos(outerConeAngle)
    vec4 params;     // x = cos(innerConeAngle), y = lightType (0=point,1=spot), zw unused
};

struct LightGridEntry {
    uint offset;
    uint count;
};

layout(std430, set = 0, binding = 1) readonly buffer LightGrid {
    LightGridEntry entries[];
} bLightGrid;

layout(std430, set = 0, binding = 2) readonly buffer LightIndexList {
    uint indices[];
} bLightIndexList;

layout(std430, set = 0, binding = 3) readonly buffer LightsBuffer {
    Light lights[];
} bLights;

layout(set = 1, binding = 0) uniform sampler2D albedoMap;
layout(set = 1, binding = 1) uniform sampler2D normalMap;
layout(set = 1, binding = 2) uniform sampler2D metallicRoughnessMap;

float distributionGGX(vec3 N, vec3 H, float roughness) {
    float a = roughness * roughness;
    float a2 = a * a;
    float NdotH = max(dot(N, H), 0.0);
    float NdotH2 = NdotH * NdotH;
    float denom = (NdotH2 * (a2 - 1.0) + 1.0);
    denom = PI * denom * denom;
    return a2 / max(denom, 1e-7);
}

float geometrySchlickGGX(float NdotV, float roughness) {
    float r = roughness + 1.0;
    float k = (r * r) / 8.0;
    return NdotV / (NdotV * (1.0 - k) + k);
}

float geometrySmith(float NdotV, float NdotL, float roughness) {
    return geometrySchlickGGX(NdotV, roughness) * geometrySchlickGGX(NdotL, roughness);
}

vec3 fresnelSchlick(float cosTheta, vec3 F0) {
    return F0 + (1.0 - F0) * pow(clamp(1.0 - cosTheta, 0.0, 1.0), 5.0);
}

// derivative-based tangent frame, no precomputed tangent attribute needed
vec3 applyNormalMap(vec3 N, vec3 worldPos, vec2 uv, vec3 normalSampleRGB) {
    vec3 dp1 = dFdx(worldPos);
    vec3 dp2 = dFdy(worldPos);
    vec2 duv1 = dFdx(uv);
    vec2 duv2 = dFdy(uv);

    vec3 dp2perp = cross(dp2, N);
    vec3 dp1perp = cross(N, dp1);
    vec3 T = dp2perp * duv1.x + dp1perp * duv2.x;
    vec3 B = dp2perp * duv1.y + dp1perp * duv2.y;

    float invMax = inversesqrt(max(dot(T, T), dot(B, B)));
    T *= invMax;
    B *= invMax;

    mat3 TBN = mat3(T, B, N);
    vec3 mappedN = normalSampleRGB * 2.0 - 1.0;
    return normalize(TBN * mappedN);
}

void main() {
    vec2 tileSize = uFrame.screenDimensions / vec2(uFrame.clusterGridDim.xy);
    ivec2 clusterXY = ivec2(gl_FragCoord.xy / tileSize);
    clusterXY = clamp(clusterXY, ivec2(0), uFrame.clusterGridDim.xy - ivec2(1));

    float viewSpaceDepth = -inViewSpaceZ;
    float sliceRatio = log(max(viewSpaceDepth / uFrame.nearZ, 1e-6)) / log(uFrame.farZ / uFrame.nearZ);
    int clusterZ = int(sliceRatio * float(uFrame.clusterGridDim.z));
    clusterZ = clamp(clusterZ, 0, uFrame.clusterGridDim.z - 1);

    uint clusterIndex = uint(clusterZ) * uint(uFrame.clusterGridDim.x * uFrame.clusterGridDim.y)
        + uint(clusterXY.y) * uint(uFrame.clusterGridDim.x)
        + uint(clusterXY.x);

    LightGridEntry gridEntry = bLightGrid.entries[clusterIndex];

    vec4 albedoSample = texture(albedoMap, inUV);
    vec3 albedo = albedoSample.rgb;
    vec3 normalSample = texture(normalMap, inUV).rgb;
    vec2 metallicRoughnessSample = texture(metallicRoughnessMap, inUV).bg; // b = metallic, g = roughness
    float metallic = metallicRoughnessSample.x;
    float roughness = clamp(metallicRoughnessSample.y, 0.045, 1.0);

    vec3 N = normalize(inWorldNormal);
    N = applyNormalMap(N, inWorldPos, inUV, normalSample);
    vec3 V = normalize(uFrame.cameraPositionWS.xyz - inWorldPos);
    float NdotV = max(dot(N, V), 1e-4);

    vec3 F0 = mix(vec3(0.04), albedo, metallic);
    vec3 Lo = vec3(0.0);

    for (uint i = 0u; i < gridEntry.count; i++) {
        uint lightIndex = bLightIndexList.indices[gridEntry.offset + i];
        Light light = bLights.lights[lightIndex];

        vec3 toLight = light.position.xyz - inWorldPos;
        float distToLight = length(toLight);
        vec3 L = toLight / max(distToLight, 1e-5);
        vec3 H = normalize(V + L);

        float radius = light.position.w;
        float distRatio = clamp(distToLight / radius, 0.0, 1.0);
        float rangeAtten = 1.0 - (distRatio * distRatio * distRatio * distRatio);
        rangeAtten = clamp(rangeAtten, 0.0, 1.0);
        rangeAtten = rangeAtten * rangeAtten;
        float distAtten = rangeAtten / max(distToLight * distToLight, 1e-4);

        float spotAtten = 1.0;
        if (light.params.y > 0.5) {
            float cosAngle = dot(-L, normalize(light.direction.xyz));
            float cosInner = light.params.x;
            float cosOuter = light.direction.w;
            spotAtten = clamp((cosAngle - cosOuter) / max(cosInner - cosOuter, 1e-4), 0.0, 1.0);
            spotAtten *= spotAtten;
        }

        float attenuation = distAtten * spotAtten;
        if (attenuation <= 0.0) {
            continue;
        }

        float NdotL = max(dot(N, L), 0.0);

        float D = distributionGGX(N, H, roughness);
        float G = geometrySmith(NdotV, NdotL, roughness);
        vec3 F = fresnelSchlick(max(dot(H, V), 0.0), F0);

        vec3 numerator = D * G * F;
        float denom = 4.0 * NdotV * NdotL + 1e-4;
        vec3 specular = numerator / denom;

        vec3 kS = F;
        vec3 kD = (vec3(1.0) - kS) * (1.0 - metallic);

        vec3 radiance = light.color.rgb * light.color.a * attenuation;
        Lo += (kD * albedo / PI + specular) * radiance * NdotL;
    }

    vec3 ambient = vec3(0.03) * albedo;
    vec3 color = ambient + Lo;

    outColor = vec4(color, albedoSample.a);
}
