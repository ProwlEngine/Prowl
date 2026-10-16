#version 460 core

// SSR - SceneBlur pass
// Separable Gaussian, used to build a convolved scene-colour pyramid.

layout(std140, set = 2, binding = 0) uniform PassUniforms
{
    vec2 _BlurDir; // (1,0) or (0,1)
};

layout(set = 2, binding = 1) uniform sampler2D _MainTex;

layout(location = 1) in vec2 TexCoords;
layout(location = 0) out vec4 fragColor;

void main()
{
    // Separable 7-tap Gaussian over the source texel grid (downsamples on the V write).
    vec2 step = _BlurDir / vec2(textureSize(_MainTex, 0));
    vec4 c  = texture(_MainTex, TexCoords) * 0.474;
    c      += texture(_MainTex, TexCoords + step * 1.0) * 0.233;
    c      += texture(_MainTex, TexCoords - step * 1.0) * 0.233;
    c      += texture(_MainTex, TexCoords + step * 2.0) * 0.028;
    c      += texture(_MainTex, TexCoords - step * 2.0) * 0.028;
    c      += texture(_MainTex, TexCoords + step * 3.0) * 0.001;
    c      += texture(_MainTex, TexCoords - step * 3.0) * 0.001;
    fragColor = c;
}
