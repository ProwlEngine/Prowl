#version 460 core

layout(set = 2, binding = 1) uniform sampler2D _MainTex;

layout(location = 0) out vec4 FragColor;

layout(location = 1) in vec2 TexCoords;

void main()
{
    vec2 texelSize = 1.0 / vec2(textureSize(_MainTex, 0));

    // 4-tap box filter
    float s0 = texture(_MainTex, TexCoords + vec2(-0.5, -0.5) * texelSize).r;
    float s1 = texture(_MainTex, TexCoords + vec2( 0.5, -0.5) * texelSize).r;
    float s2 = texture(_MainTex, TexCoords + vec2(-0.5,  0.5) * texelSize).r;
    float s3 = texture(_MainTex, TexCoords + vec2( 0.5,  0.5) * texelSize).r;

    FragColor = vec4((s0 + s1 + s2 + s3) * 0.25, 0.0, 0.0, 1.0);
}
