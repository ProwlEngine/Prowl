#version 460 core

layout(set = 0, binding = 0) buffer InstanceBuffer
{
    mat4 models[];
};

layout(push_constant) uniform ShadowPushConstant
{
    mat4 cascadeViewProj;
    uint instanceIndex;
} pc;

layout(location = 0) in vec3 inPosition;
layout(location = 3) in vec2 inUV;

layout(location = 0) out vec2 outUV;

void main()
{
    mat4 model = models[pc.instanceIndex];
    vec4 worldPos = model * vec4(inPosition, 1.0);
    outUV = inUV;
    gl_Position = pc.cascadeViewProj * worldPos;
}
