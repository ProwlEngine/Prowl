#version 450
const float PI = 3.14159265359;
layout(set = 0, binding = 0) uniform Material {
    vec4 baseColor;
    float metallic;
    float roughness;
} mat;
layout(location = 0) in vec3 vWorldPos;
layout(location = 1) in vec3 vNormal;
layout(location = 2) in vec3 vViewDir;
layout(location = 3) in vec3 vLightDir;
layout(location = 0) out vec4 oColor;

float distributionGGX(vec3 N, vec3 H, float roughness) {
    float a = roughness * roughness;
    float a2 = a * a;
    float NdotH = max(dot(N, H), 0.0);
    float denom = NdotH * NdotH * (a2 - 1.0) + 1.0;
    return a2 / (PI * denom * denom);
}

vec3 fresnelSchlick(float cosTheta, vec3 F0) {
    return F0 + (1.0 - F0) * pow(clamp(1.0 - cosTheta, 0.0, 1.0), 5.0);
}

void main() {
    vec3 N = normalize(vNormal);
    vec3 V = normalize(vViewDir);
    vec3 L = normalize(vLightDir);
    vec3 H = normalize(V + L);
    vec3 F0 = mix(vec3(0.04), mat.baseColor.rgb, mat.metallic);
    float D = distributionGGX(N, H, mat.roughness);
    vec3 F = fresnelSchlick(max(dot(H, V), 0.0), F0);
    float NdotL = max(dot(N, L), 0.0);
    vec3 spec = D * F;
    vec3 diffuse = (1.0 - mat.metallic) * mat.baseColor.rgb / PI;
    vec3 color = (diffuse + spec) * NdotL;
    oColor = vec4(color, 1.0);
}
