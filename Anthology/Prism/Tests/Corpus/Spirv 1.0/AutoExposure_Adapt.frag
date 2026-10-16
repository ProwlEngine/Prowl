#version 460 core

#ifndef SHADER_SHADERVARIABLES
#define SHADER_SHADERVARIABLES

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

#endif // SHADER_SHADERVARIABLES

layout(set = 2, binding = 1) uniform sampler2D _MainTex; // Current measured log-luminance (1x1 or small)
layout(set = 2, binding = 2) uniform sampler2D _AdaptedTex; // Previous adapted luminance
layout(std140, set = 2, binding = 0) uniform PassUniforms
{
    float _AdaptSpeedUp;     // Speed when going brighter (EV/s)
    float _AdaptSpeedDown;   // Speed when going darker (EV/s)
    float _HistoryValid;     // 0.0 = first frame, snap to current
};

layout(location = 0) out vec4 FragColor;

layout(location = 1) in vec2 TexCoords;

void main()
{
    // Current geometric mean luminance from downsample chain
    float currentLogLum = texture(_MainTex, vec2(0.5)).r;
    float currentLum = exp(currentLogLum);

    // Previous adapted luminance
    float prevLum = texture(_AdaptedTex, vec2(0.5)).r;

    if (_HistoryValid < 0.5)
    {
        // First frame - snap to current
        FragColor = vec4(currentLum, 0.0, 0.0, 1.0);
        return;
    }

    // Asymmetric adaptation speed: faster going dark->bright, slower bright->dark
    // (or vice versa depending on user config)
    float speed = (currentLum > prevLum) ? _AdaptSpeedUp : _AdaptSpeedDown;

    float dt = prowl_DeltaTime.x;
    float adaptFactor = 1.0 - exp(-dt * speed);
    float adaptedLum = prevLum + (currentLum - prevLum) * adaptFactor;

    // Clamp to reasonable range to prevent extreme values
    adaptedLum = clamp(adaptedLum, 0.0001, 100.0);

    FragColor = vec4(adaptedLum, 0.0, 0.0, 1.0);
}
