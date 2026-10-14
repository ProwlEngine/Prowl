#version 460 core

// SSR - Reproject pass
// Reproject a buffer by motion (used to feed last frame's result back in -> 1 bounce).

layout(set = 2, binding = 1) uniform sampler2D _MainTex;                    // buffer to reproject (previous combined)
layout(set = 2, binding = 2) uniform sampler2D _CameraMotionVectorsTexture; // .rg motion

layout(location = 1) in vec2 TexCoords;
layout(location = 0) out vec4 fragColor;

void main()
{
    vec2 velocity = texture(_CameraMotionVectorsTexture, TexCoords).rg;
    fragColor = texture(_MainTex, TexCoords - velocity);
}
