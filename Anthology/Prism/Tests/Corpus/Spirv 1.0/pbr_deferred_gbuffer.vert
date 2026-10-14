#version 460 core

layout(set = 0, binding = 0) buffer InstanceBuffer
{
    mat4 models[];
};

layout(set = 0, binding = 1) uniform CameraUBO
{
    mat4 view;
    mat4 projection;
    mat4 prevViewProjection;
} camera;

layout(location = 0) in vec3 inPosition;
layout(location = 1) in vec3 inNormal;
layout(location = 2) in vec4 inTangent;
layout(location = 3) in vec2 inUV;

layout(location = 0) out vec3 outWorldPos;
layout(location = 1) out vec3 outNormal;
layout(location = 2) out vec4 outTangent;
layout(location = 3) out vec2 outUV;
layout(location = 4) out vec4 outCurrClipPos;
layout(location = 5) out vec4 outPrevClipPos;

void main()
{
    mat4 model = models[gl_InstanceIndex];
    mat3 normalMatrix = mat3(transpose(inverse(model)));

    vec4 worldPos = model * vec4(inPosition, 1.0);
    outWorldPos = worldPos.xyz;
    outNormal = normalize(normalMatrix * inNormal);
    outTangent = vec4(normalize(normalMatrix * inTangent.xyz), inTangent.w);
    outUV = inUV;

    vec4 currClipPos = camera.projection * camera.view * worldPos;
    vec4 prevClipPos = camera.prevViewProjection * worldPos;

    outCurrClipPos = currClipPos;
    outPrevClipPos = prevClipPos;

    gl_Position = currClipPos;
}
