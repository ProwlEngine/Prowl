#version 460 core

// textureOffset and textureProj on usampler2D: the texel type is uvec4, not vec4.
layout(set = 0, binding = 0) uniform usampler2D tex;

layout(location = 0) in vec2 uv;
layout(location = 0) out vec4 outColor;

void main() {
    uvec4 a = textureOffset(tex, uv, ivec2(1, 1));
    uvec4 b = textureProj(tex, vec4(uv, 0.0, 2.0));
    outColor = vec4(a + b);
}
