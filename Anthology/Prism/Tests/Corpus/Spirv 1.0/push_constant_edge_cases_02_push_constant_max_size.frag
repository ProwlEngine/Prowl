#version 460 core

// Tests a push_constant block sized close to the guaranteed Vulkan minimum
// of 128 bytes. This is not asserting hardware limits at compile time, only
// that the compiler handles a block near that boundary without choking.
// Layout: 7 * vec4 (112 bytes) + 1 float (4 bytes) = 116 bytes total.
layout(push_constant) uniform PushConstants
{
    vec4 colorA;
    vec4 colorB;
    vec4 colorC;
    vec4 colorD;
    vec4 colorE;
    vec4 colorF;
    vec4 colorG;
    float blendFactor;
} pc;

layout(location = 0) out vec4 outColor;

void main()
{
    vec4 accum = pc.colorA + pc.colorB + pc.colorC + pc.colorD;
    accum += pc.colorE + pc.colorF + pc.colorG;

    outColor = mix(accum * 0.142857, pc.colorG, pc.blendFactor);
}
