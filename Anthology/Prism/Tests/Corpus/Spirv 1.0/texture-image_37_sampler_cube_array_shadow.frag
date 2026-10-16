#version 460 core

// samplerCubeArrayShadow: the depth reference is a separate argument and the result is a scalar.
layout(set = 0, binding = 0) uniform samplerCubeArrayShadow tex;

layout(location = 0) in vec4 dir;
layout(location = 0) out vec4 outColor;

void main() {
    float a = texture(tex, dir, 0.5);
    outColor = vec4(a, a, a, 1.0);
}
