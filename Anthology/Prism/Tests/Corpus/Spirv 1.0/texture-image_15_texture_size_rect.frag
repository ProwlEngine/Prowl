#version 460 core

// textureSize on sampler2DRect has no LOD argument and must emit OpImageQuerySize.
layout(set = 0, binding = 0) uniform sampler2DRect tex;

layout(location = 0) out vec4 outColor;

void main() {
    ivec2 size = textureSize(tex);
    outColor = vec4(vec2(size), 0.0, 1.0);
}
