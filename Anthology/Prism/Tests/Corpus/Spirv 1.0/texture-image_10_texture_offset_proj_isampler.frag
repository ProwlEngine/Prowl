#version 460 core

// textureOffset and textureProj on isampler2D: the texel type is ivec4, not vec4.
layout(set = 0, binding = 0) uniform isampler2D tex;

layout(location = 0) in vec2 uv;
layout(location = 0) out vec4 outColor;

void main() {
    ivec4 a = textureOffset(tex, uv, ivec2(1, 1));
    ivec4 b = textureProj(tex, vec4(uv, 0.0, 2.0));
    outColor = vec4(a + b);
}
