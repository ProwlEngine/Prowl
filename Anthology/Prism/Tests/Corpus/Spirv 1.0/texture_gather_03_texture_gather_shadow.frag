#version 460 core

layout(set = 0, binding = 0) uniform sampler2DShadow shadowTex;

layout(location = 0) in vec2 uv;
layout(location = 1) in float compareValue;
layout(location = 0) out vec4 outColor;

void main()
{
    vec4 shadowGather = textureGather(shadowTex, uv, compareValue);
    float shadowFactor = (shadowGather.x + shadowGather.y + shadowGather.z + shadowGather.w) * 0.25;

    outColor = vec4(shadowFactor, shadowFactor, shadowFactor, 1.0);
}
