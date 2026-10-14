#version 460 core

layout(set = 0, binding = 1) uniform sampler2D albedoMap;

layout(push_constant) uniform ShadowPushConstant
{
    mat4 cascadeViewProj;
    uint instanceIndex;
} pc;

layout(location = 0) in vec2 inUV;

const float ALPHA_CUTOFF = 0.5;
const float DEPTH_BIAS = 0.0000015;

void main()
{
    float alpha = texture(albedoMap, inUV).a;
    if (alpha < ALPHA_CUTOFF)
    {
        discard;
    }

    gl_FragDepth = gl_FragCoord.z + DEPTH_BIAS;
}
