#version 460 core

layout(std140, set = 2, binding = 0) uniform PassUniforms
{
    float _Threshold;
};

layout(set = 2, binding = 1) uniform sampler2D _MainTex;

layout(location = 0) out vec4 FragColor;

layout(location = 1) in vec2 TexCoords;

void main()
{
    vec4 base = texture(_MainTex, TexCoords);
    vec3 color = base.rgb;

    float luminance = dot(color, vec3(0.2126, 0.7152, 0.0722));
    float contribution = max(0.0, luminance - _Threshold);
    contribution /= max(luminance, 0.00001);

    FragColor = vec4(color * contribution, base.a);
}
