#version 460 core

layout(set = 0, binding = 0) uniform SkyboxUBO
{
    mat4 view;
    mat4 projection;
} camera;

layout(location = 0) in vec3 inPosition;

layout(location = 0) out vec3 outDirection;

void main()
{
    outDirection = inPosition;

    mat4 viewNoTranslation = mat4(mat3(camera.view));
    vec4 clipPos = camera.projection * viewNoTranslation * vec4(inPosition, 1.0);
    clipPos.z = clipPos.w;
    gl_Position = clipPos;
}
