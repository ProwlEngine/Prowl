#version 460 core

// isampler1D: integer-texel 1D sampling via texture, texelFetch, and textureLod (result is ivec4).
layout(set = 0, binding = 0) uniform isampler1D tex;

layout(location = 0) in float u;
layout(location = 0) out vec4 outColor;

void main() {
    ivec4 a = texture(tex, u);
    ivec4 b = texelFetch(tex, int(u * 256.0), 0);
    ivec4 c = textureLod(tex, u, 1.0);
    outColor = vec4(a + b + c);
}
