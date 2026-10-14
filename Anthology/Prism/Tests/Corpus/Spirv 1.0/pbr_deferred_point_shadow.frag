#version 460 core

layout(push_constant) uniform PointShadowPushConstant
{
    mat4 faceViewProj[6];
    vec4 lightPositionRange;
    uint instanceIndex;
} pc;

layout(location = 0) in vec3 inWorldPos;

layout(location = 0) out float outDistance;

void main()
{
    outDistance = length(inWorldPos - pc.lightPositionRange.xyz);
}
