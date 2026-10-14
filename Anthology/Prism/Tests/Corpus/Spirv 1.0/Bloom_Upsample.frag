#version 460 core

// Dual-Kawase style upsample, tent filter
// Note: original ShaderLab pass declares "Blend Additive" render state - not expressible in GLSL, handled by the pipeline's blend state at draw time
layout(set = 2, binding = 0) uniform sampler2D _MainTex;

layout(location = 0) out vec4 FragColor;

layout(location = 1) in vec2 TexCoords;

void main()
{
    vec2 halfpixel = 0.5 / vec2(textureSize(_MainTex, 0));

    vec4 sum = texture(_MainTex, TexCoords + vec2(-halfpixel.x * 2.0, 0.0));
    sum += texture(_MainTex, TexCoords + vec2(-halfpixel.x, halfpixel.y)) * 2.0;
    sum += texture(_MainTex, TexCoords + vec2(0.0, halfpixel.y * 2.0));
    sum += texture(_MainTex, TexCoords + vec2(halfpixel.x, halfpixel.y)) * 2.0;
    sum += texture(_MainTex, TexCoords + vec2(halfpixel.x * 2.0, 0.0));
    sum += texture(_MainTex, TexCoords + vec2(halfpixel.x, -halfpixel.y)) * 2.0;
    sum += texture(_MainTex, TexCoords + vec2(0.0, -halfpixel.y * 2.0));
    sum += texture(_MainTex, TexCoords + vec2(-halfpixel.x, -halfpixel.y)) * 2.0;

    FragColor = sum / 12.0;
}
