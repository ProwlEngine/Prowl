#version 460 core

layout(location = 0) in vec3 inPosition;
layout(location = 1) in vec2 inUV;

layout(location = 0) out vec3 vWorldPos;
layout(location = 1) out vec2 vUV;

layout(set = 0, binding = 3) uniform PatchTransform
{
    mat4 model;
} patchTransform;

void main()
{
    vWorldPos = (patchTransform.model * vec4(inPosition, 1.0)).xyz;
    vUV = inUV;
}
