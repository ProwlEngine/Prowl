#version 460 core

layout(set = 0, binding = 0) uniform sampler2D ssaoInput;
layout(set = 0, binding = 1) uniform sampler2D gDepth;

layout(push_constant) uniform BlurPushConstant
{
    vec2 texelSize;
    float depthSigma;
} pc;

layout(location = 0) in vec2 inUV;
layout(location = 0) out float outOcclusion;

void main()
{
    float centerDepth = texture(gDepth, inUV).r;

    float result = 0.0;
    float weightSum = 0.0;

    for (int x = -2; x < 2; x++)
    {
        for (int y = -2; y < 2; y++)
        {
            vec2 offset = vec2(float(x), float(y)) * pc.texelSize;
            vec2 sampleUV = inUV + offset;

            float sampleDepth = texture(gDepth, sampleUV).r;
            float depthDiff = abs(centerDepth - sampleDepth);
            float weight = exp(-(depthDiff * depthDiff) / max(pc.depthSigma, 1e-5));

            result += texture(ssaoInput, sampleUV).r * weight;
            weightSum += weight;
        }
    }

    outOcclusion = result / max(weightSum, 1e-5);
}
