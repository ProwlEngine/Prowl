#version 460 core

layout(std140, set = 0, binding = 0) uniform GlobalUniforms
{
    mat4 prowl_MatV;
    mat4 prowl_MatIV;
    mat4 prowl_MatP;
    mat4 prowl_MatVP;
    mat4 prowl_PrevViewProj;
    mat4 prowl_MatIP;
    mat4 prowl_MatIVP;
    mat4 prowl_MatVP_NonJittered;
    vec3 _WorldSpaceCameraPos;
    float _padding0;
    vec4 _ProjectionParams;
    vec4 _ScreenParams;
    vec2 _CameraJitter;
    vec2 _CameraPreviousJitter;
    vec4 _Time;
    vec4 _SinTime;
    vec4 _CosTime;
    vec4 prowl_DeltaTime;
};

layout(std140, set = 2, binding = 0) uniform PassUniforms
{
    vec2 _BlurDirection;
    float _BlurRadius;
};

layout(set = 2, binding = 1) uniform sampler2D _MainTex;
layout(set = 2, binding = 2) uniform sampler2D _CameraDepthTexture;

layout(location = 1) in vec2 TexCoords;

layout(location = 0) out vec4 fragColor;

void main()
{
    vec2 texelSize = 1.0 / _ScreenParams.xy;
    float centerDepth = texture(_CameraDepthTexture, TexCoords).r;

    vec4 result = texture(_MainTex, TexCoords);
    float totalWeight = 1.0;

    // Depth-aware bilateral blur
    for (int i = -2; i <= 2; i++) {
        if (i == 0) continue;

        float offset = float(i) * _BlurRadius;
        vec2 sampleUV = TexCoords + _BlurDirection * texelSize * offset;

        float sampleDepth = texture(_CameraDepthTexture, sampleUV).r;
        float depthDiff = abs(centerDepth - sampleDepth);

        // Weight based on depth similarity
        float weight = exp(-depthDiff * 100.0) * exp(-0.5 * float(i * i) / 2.0);

        result += texture(_MainTex, sampleUV) * weight;
        totalWeight += weight;
    }

    fragColor = result / totalWeight;
}
