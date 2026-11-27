#version 460 core

layout(push_constant) uniform PushConstants
{
    mat4 mvp;
    vec4 clipPlanes[4];
    float time;
    float scale;
} pc;

layout(location = 0) in vec3 inPosition;

layout(location = 0) out float outClipDistanceSum;

void main()
{
    vec4 worldPos = vec4(inPosition * pc.scale, 1.0);

    float clipSum = 0.0;
    for (int i = 0; i < 4; ++i)
    {
        clipSum += dot(worldPos, pc.clipPlanes[i]);
    }

    worldPos.xyz += vec3(sin(pc.time), cos(pc.time), 0.0) * 0.01;

    outClipDistanceSum = clipSum;
    gl_Position = pc.mvp * worldPos;
}
