#version 460 core

// Ocean surface shader: Fresnel-blended reflection/refraction, foam,
// GGX sun glint and a cheap backlit-crest subsurface-scattering trick.

#define PI 3.14159265359

layout(set = 0, binding = 0) uniform CameraUBO
{
    mat4 view;
    mat4 proj;
    vec3 camPosition;
    float nearPlane;
    float farPlane;
} uCamera;

layout(set = 0, binding = 1) uniform LightUBO
{
    vec3 lightDirection; // points from surface toward the light, normalized
    vec3 lightColor;
    float lightIntensity;
} uLight;

layout(set = 0, binding = 2) uniform WaterUBO
{
    vec3 absorptionColor;   // per-unit-depth extinction tint
    float foamThreshold;
    vec3 foamColor;
    float foamRange;
    vec3 sssColor;
    float roughness;
    float time;
} uWater;

layout(set = 1, binding = 0) uniform sampler2D reflectionTex;
layout(set = 1, binding = 1) uniform sampler2D refractionTex;
layout(set = 1, binding = 2) uniform sampler2D sceneDepthTex;
layout(set = 1, binding = 3) uniform sampler2D foamTex;

layout(location = 0) in vec3 inWorldPos;
layout(location = 1) in vec3 inNormal;
layout(location = 2) in float inWaveHeight;

layout(location = 0) out vec4 outColor;

float linearizeDepth(float depthNdc, float nearZ, float farZ)
{
    float z = depthNdc * 2.0 - 1.0;
    return (2.0 * nearZ * farZ) / (farZ + nearZ - z * (farZ - nearZ));
}

float distributionGGX(float NdotH, float roughness)
{
    float a = roughness * roughness;
    float a2 = a * a;
    float denom = (NdotH * NdotH) * (a2 - 1.0) + 1.0;
    return a2 / max(PI * denom * denom, 1e-6);
}

float geometrySmithGGX(float NdotV, float NdotL, float roughness)
{
    float r = roughness + 1.0;
    float k = (r * r) / 8.0;
    float gv = NdotV / (NdotV * (1.0 - k) + k);
    float gl = NdotL / (NdotL * (1.0 - k) + k);
    return gv * gl;
}

vec3 fresnelSchlick(float cosTheta, vec3 f0)
{
    return f0 + (vec3(1.0) - f0) * pow(clamp(1.0 - cosTheta, 0.0, 1.0), 5.0);
}

void main()
{
    vec3 normal = normalize(inNormal);
    vec3 viewDir = normalize(uCamera.camPosition - inWorldPos);
    vec3 lightDir = normalize(uLight.lightDirection);
    vec3 halfVec = normalize(viewDir + lightDir);

    float NdotV = max(dot(normal, viewDir), 1e-4);
    float NdotL = max(dot(normal, lightDir), 0.0);
    float NdotH = max(dot(normal, halfVec), 0.0);

    // Screen-space projected UV for the planar reflection / refraction targets.
    vec4 clipPos = uCamera.proj * uCamera.view * vec4(inWorldPos, 1.0);
    vec2 ndc = clipPos.xy / clipPos.w;
    vec2 baseUV = ndc * 0.5 + 0.5;

    vec2 distortion = normal.xz * 0.02;
    vec2 reflectionUV = clamp(vec2(baseUV.x, 1.0 - baseUV.y) + distortion, 0.0, 1.0);
    vec2 refractionUV = clamp(baseUV + distortion, 0.0, 1.0);

    vec3 reflectionColor = texture(reflectionTex, reflectionUV).rgb;
    vec3 sceneColor = texture(refractionTex, refractionUV).rgb;

    float sceneDepthNdc = texture(sceneDepthTex, refractionUV).r;
    float sceneLinearDepth = linearizeDepth(sceneDepthNdc, uCamera.nearPlane, uCamera.farPlane);
    float surfaceLinearDepth = linearizeDepth(clipPos.z / clipPos.w, uCamera.nearPlane, uCamera.farPlane);
    float waterColumnDepth = max(sceneLinearDepth - surfaceLinearDepth, 0.0);

    vec3 extinction = exp(-uWater.absorptionColor * waterColumnDepth);
    vec3 refractionColor = sceneColor * extinction;

    float f0Scalar = 0.02;
    vec3 f0 = vec3(f0Scalar);
    vec3 fresnel = fresnelSchlick(NdotV, f0);

    vec3 waterColor = mix(refractionColor, reflectionColor, fresnel);

    // GGX sun glint.
    float distribution = distributionGGX(NdotH, uWater.roughness);
    float geometry = geometrySmithGGX(NdotV, NdotL, uWater.roughness);
    vec3 specFresnel = fresnelSchlick(max(dot(halfVec, viewDir), 0.0), f0);
    vec3 specular = (distribution * geometry * specFresnel) / max(4.0 * NdotV * NdotL, 1e-4);
    specular *= uLight.lightColor * uLight.lightIntensity * NdotL;

    // Foam: crest-height threshold blended with a scrolling foam texture.
    float foamMask = smoothstep(uWater.foamThreshold, uWater.foamThreshold + uWater.foamRange, inWaveHeight);
    vec2 foamUV = inWorldPos.xz * 0.15 + vec2(uWater.time * 0.02, uWater.time * 0.015);
    float foamSample = texture(foamTex, foamUV).r;
    vec3 foamResult = uWater.foamColor * foamSample * foamMask;

    // Cheap subsurface-scattering approximation: warm boost when backlit through a wave crest.
    float backlight = pow(clamp(dot(viewDir, -lightDir), 0.0, 1.0), 4.0);
    float crestFactor = clamp(inWaveHeight * 2.0, 0.0, 1.0);
    vec3 sss = uWater.sssColor * backlight * crestFactor * uLight.lightIntensity;

    vec3 finalColor = waterColor + specular + sss;
    finalColor = mix(finalColor, foamResult + finalColor * (1.0 - foamMask * 0.5), foamMask);

    outColor = vec4(finalColor, 1.0);
}
