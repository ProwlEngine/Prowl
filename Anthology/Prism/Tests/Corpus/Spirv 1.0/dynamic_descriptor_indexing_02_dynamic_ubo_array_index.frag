#version 460 core

layout(set = 0, binding = 0) uniform MaterialData {
    vec4 baseColor;
    float roughness;
    float metallic;
} materials[16];

layout(push_constant) uniform PushConstants {
    int materialIndex;
} pc;

layout(location = 0) in vec2 uv;
layout(location = 0) out vec4 FragColor;

void main() {
    int idx = pc.materialIndex;
    vec4 color = materials[idx].baseColor;
    float roughness = materials[idx].roughness;
    float metallic = materials[idx].metallic;
    vec3 shaded = color.rgb * (1.0 - metallic) + vec3(roughness) * uv.x;
    FragColor = vec4(shaded, color.a);
}
