#version 460 core

// Soft-particle fragment shader: modulates the sprite by color-over-life
// and fades alpha near opaque scene geometry using a linearized depth
// comparison, output as premultiplied alpha.

layout(location = 0) in vec2 inUV;
layout(location = 1) in vec4 inColor;
layout(location = 2) in float inViewDepth;

layout(location = 0) out vec4 outColor;

layout(set = 1, binding = 0) uniform sampler2D spriteTexture;
layout(set = 1, binding = 1) uniform sampler2D sceneDepthTexture;

layout(set = 1, binding = 2) uniform DepthParamsUBO
{
    float nearPlane;
    float farPlane;
    float softFadeDistance;
    float padding0;
} depthParams;

float linearizeDepth(float depth)
{
    float n = depthParams.nearPlane;
    float f = depthParams.farPlane;
    return (n * f) / (f - depth * (f - n));
}

void main()
{
    vec2 screenUV = gl_FragCoord.xy / vec2(textureSize(sceneDepthTexture, 0));
    float sceneDepthRaw = texture(sceneDepthTexture, screenUV).r;
    float sceneLinearDepth = linearizeDepth(sceneDepthRaw);

    float depthDelta = sceneLinearDepth - inViewDepth;
    float softFactor = clamp(depthDelta / max(depthParams.softFadeDistance, 0.0001), 0.0, 1.0);

    vec4 spriteColor = texture(spriteTexture, inUV);
    vec4 finalColor = spriteColor * inColor;
    finalColor.a *= softFactor;

    // premultiplied alpha for additive/alpha blending
    finalColor.rgb *= finalColor.a;

    outColor = finalColor;
}
