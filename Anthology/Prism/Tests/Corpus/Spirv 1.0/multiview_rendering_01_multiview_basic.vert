#version 460 core
#extension GL_EXT_multiview : require

// Standard stereo/VR multiview pattern: gl_ViewIndex selects which
// view-projection matrix to use for this instance of the multiview pass.

layout(location = 0) in vec3 inPosition;
layout(location = 1) in vec3 inNormal;

layout(set = 0, binding = 0) uniform ViewUBO
{
    mat4 viewProj[2];
    mat4 model;
} view;

layout(location = 0) out vec3 outNormal;

void main()
{
    vec4 worldPos = view.model * vec4(inPosition, 1.0);
    outNormal = mat3(view.model) * inNormal;

    gl_Position = view.viewProj[gl_ViewIndex] * worldPos;
}
