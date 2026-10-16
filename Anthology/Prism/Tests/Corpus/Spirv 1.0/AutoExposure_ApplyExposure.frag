#version 460 core

layout(set = 2, binding = 1) uniform sampler2D _MainTex; // HDR scene color
layout(set = 2, binding = 2) uniform sampler2D _AdaptedTex; // 1x1 adapted luminance
layout(std140, set = 2, binding = 0) uniform PassUniforms
{
    float _ExposureComp;     // Exposure compensation in EV stops
    float _MinExposure;      // Minimum exposure multiplier
    float _MaxExposure;      // Maximum exposure multiplier
};

layout(location = 0) out vec4 FragColor;

layout(location = 1) in vec2 TexCoords;

void main()
{
    vec4 sceneColor = texture(_MainTex, TexCoords);
    float adaptedLum = texture(_AdaptedTex, vec2(0.5)).r;

    // Standard exposure formula: key / luminance
    // 0.18 is the standard "middle gray" key value
    float exposure = 0.18 / max(adaptedLum, 0.0001);

    // Apply EV compensation (each stop doubles/halves exposure)
    exposure *= exp2(_ExposureComp);

    // Clamp exposure to user-defined range
    exposure = clamp(exposure, _MinExposure, _MaxExposure);

    FragColor = vec4(sceneColor.rgb * exposure, sceneColor.a);
}
