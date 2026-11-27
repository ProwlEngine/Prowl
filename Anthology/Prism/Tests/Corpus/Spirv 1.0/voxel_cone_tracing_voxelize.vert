#version 460 core

// Vertex stage of the conservative voxelization pass.
// No projection matrix is applied here: the geometry shader picks the
// dominant projection axis per triangle, so we only transform to world
// space and forward everything untouched.

layout(location = 0) in vec3 inPosition;
layout(location = 1) in vec3 inNormal;
layout(location = 2) in vec2 inUV;

layout(set = 0, binding = 0) uniform ObjectData
{
    mat4 model;
} uObject;

layout(location = 0) out vec3 vWorldPos;
layout(location = 1) out vec3 vWorldNormal;
layout(location = 2) out vec2 vUV;

void main()
{
    vec4 worldPos4 = uObject.model * vec4(inPosition, 1.0);
    vWorldPos = worldPos4.xyz;
    vWorldNormal = normalize(mat3(uObject.model) * inNormal);
    vUV = inUV;

    // gl_Position is unused by the geometry shader's projection logic,
    // set to world position so the stage still has a well defined output.
    gl_Position = worldPos4;
}
