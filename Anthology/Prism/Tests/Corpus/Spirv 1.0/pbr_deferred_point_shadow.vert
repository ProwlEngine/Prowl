#version 460 core

layout(set = 0, binding = 0) buffer InstanceBuffer
{
    mat4 models[];
};

layout(push_constant) uniform PointShadowPushConstant
{
    mat4 faceViewProj[6];
    vec4 lightPositionRange;
    uint instanceIndex;
} pc;

layout(location = 0) in vec3 inPosition;

layout(location = 0) out vec3 outWorldPos;

void main()
{
    mat4 model = models[pc.instanceIndex];
    vec4 worldPos = model * vec4(inPosition, 1.0);
    outWorldPos = worldPos.xyz;
    gl_Position = worldPos;
}
