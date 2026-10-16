#version 460 core

layout(location = 0) out vec4 OutputColor;

layout(location = 1) in vec2 TexCoords;

layout(std140, set = 2, binding = 0) uniform PassUniforms
{
    float _FocusStrength;
    float _ManualFocusPoint;
    float _MaxBlurRadius;
    vec2 _Resolution;
};

layout(set = 2, binding = 1) uniform sampler2D _MainTex;
layout(set = 2, binding = 2) uniform sampler2D _BlurredTex;
layout(set = 2, binding = 3) uniform sampler2D _CameraDepthTexture;

float calculateCoC(float depth, float focusPoint)
{
    float normalizedDepthDiff = abs(depth - focusPoint) / focusPoint;
    float cocPixels = normalizedDepthDiff * _FocusStrength * 0.01 * _Resolution.y;
    float maxBlurPixels = _MaxBlurRadius * 0.01 * _Resolution.y;
    return min(cocPixels, maxBlurPixels);
}

void main()
{
    float focusPoint = texture(_CameraDepthTexture, vec2(0.5, 0.5)).x;

    vec4 originalColor = texture(_MainTex, TexCoords);
    vec4 blurredColor = texture(_BlurredTex, TexCoords);

    float depth = texture(_CameraDepthTexture, TexCoords).x;
    float coc = calculateCoC(depth, focusPoint);

    // Smooth blend based on CoC
    float maxBlurPixels = _MaxBlurRadius * 0.005 * _Resolution.y;
    float blendFactor = smoothstep(0.5, maxBlurPixels * 0.5, coc);

    OutputColor = mix(originalColor, blurredColor, blendFactor);
}
