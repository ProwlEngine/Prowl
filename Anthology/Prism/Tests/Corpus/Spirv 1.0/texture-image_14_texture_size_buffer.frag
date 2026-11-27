#version 460 core

// textureSize on samplerBuffer has no LOD argument and must emit OpImageQuerySize.
layout(set = 0, binding = 0) uniform samplerBuffer tex;

layout(location = 0) out vec4 outColor;

void main() {
    int size = textureSize(tex);
    outColor = vec4(float(size), 0.0, 0.0, 1.0);
}
