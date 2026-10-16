#version 460 core

// Screen space reflection resolve pass.
// Reads the trace pass output, samples the hdr scene color at the hit uv,
// adds a handful of roughness scaled taps as a cheap blur approximation,
// then outputs a premultiplied reflection color ready for additive
// compositing onto the main scene color.

layout(location = 0) in vec2 uv;

layout(location = 0) out vec4 outReflection;
// outReflection.rgb = reflection color already weighted by confidence
// outReflection.a   = confidence, exposed in case a later pass wants it

layout(set = 0, binding = 0) uniform sampler2D traceTex;
layout(set = 0, binding = 1) uniform sampler2D sceneColorTex;
layout(set = 0, binding = 2) uniform sampler2D roughnessTex;

layout(set = 0, binding = 3) uniform SSRResolveParams
{
    vec2 screenSize;
    float maxBlurRadiusPixels;
    float roughnessBlurScale;
} params;

const int TAP_COUNT = 6;
const vec2 TAP_OFFSETS[TAP_COUNT] = vec2[TAP_COUNT](
    vec2(1.0, 0.0),
    vec2(-1.0, 0.0),
    vec2(0.0, 1.0),
    vec2(0.0, -1.0),
    vec2(0.7071, 0.7071),
    vec2(-0.7071, -0.7071)
);

void main()
{
    vec4 traceSample = texture(traceTex, uv);
    vec2 hitUV = traceSample.xy;
    float hitFlag = traceSample.z;
    float confidence = traceSample.a * step(0.5, hitFlag);

    float roughness = texture(roughnessTex, uv).r;

    vec2 texelSize = 1.0 / params.screenSize;
    float blurRadiusPixels = clamp(roughness * params.roughnessBlurScale, 0.0, 1.0) * params.maxBlurRadiusPixels;

    vec3 colorSum = texture(sceneColorTex, hitUV).rgb;
    float weightSum = 1.0;

    for (int i = 0; i < TAP_COUNT; i++)
    {
        vec2 offset = TAP_OFFSETS[i] * blurRadiusPixels * texelSize;
        vec2 tapUV = clamp(hitUV + offset, vec2(0.0), vec2(1.0));
        colorSum += texture(sceneColorTex, tapUV).rgb;
        weightSum += 1.0;
    }

    vec3 blurredColor = colorSum / weightSum;
    vec3 reflectionColor = blurredColor * confidence;

    outReflection = vec4(reflectionColor, confidence);
}
