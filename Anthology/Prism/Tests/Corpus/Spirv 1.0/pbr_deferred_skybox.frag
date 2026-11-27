#version 460 core

layout(set = 0, binding = 1) uniform samplerCube skyboxMap;

layout(push_constant) uniform SkyboxPushConstant
{
    float exposure;
} pc;

layout(location = 0) in vec3 inDirection;
layout(location = 0) out vec4 outColor;

void main()
{
    vec3 color = texture(skyboxMap, normalize(inDirection)).rgb;
    vec3 mapped = vec3(1.0) - exp(-color * pc.exposure);
    outColor = vec4(mapped, 1.0);
}
