#version 460 core

layout(location = 0) in vec3 inPosition;
layout(location = 1) in vec3 inNormal;
layout(location = 2) in vec2 inUV;

layout(set = 0, binding = 0) uniform CameraData {
    mat4 view;
    mat4 projection;
} uCamera;

layout(push_constant) uniform PushConstants {
    mat4 model;
} uPush;

layout(location = 0) out vec3 outWorldPos;
layout(location = 1) out vec3 outWorldNormal;
layout(location = 2) out vec2 outUV;
layout(location = 3) out float outViewSpaceZ;

void main() {
    vec4 worldPos = uPush.model * vec4(inPosition, 1.0);
    mat3 normalMatrix = mat3(transpose(inverse(uPush.model)));
    vec3 worldNormal = normalize(normalMatrix * inNormal);

    vec4 viewPos = uCamera.view * worldPos;

    outWorldPos = worldPos.xyz;
    outWorldNormal = worldNormal;
    outUV = inUV;
    outViewSpaceZ = viewPos.z;

    gl_Position = uCamera.projection * viewPos;
}
