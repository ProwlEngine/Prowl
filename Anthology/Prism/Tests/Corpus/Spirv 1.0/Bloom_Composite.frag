#version 460 core

layout(std140, set = 2, binding = 0) uniform PassUniforms
{
    float _Intensity;
};

layout(set = 2, binding = 1) uniform sampler2D _MainTex;
layout(set = 2, binding = 2) uniform sampler2D _BloomTex;

layout(location = 0) out vec4 FragColor;

layout(location = 1) in vec2 TexCoords;

void main()
{
    vec4 originalColor = texture(_MainTex, TexCoords);
    vec3 bloomColor = texture(_BloomTex, TexCoords).rgb;

    vec3 finalColor = originalColor.rgb + bloomColor * _Intensity;

    FragColor = vec4(finalColor, originalColor.a);
}
