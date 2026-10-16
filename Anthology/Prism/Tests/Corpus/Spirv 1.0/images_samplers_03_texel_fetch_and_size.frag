#version 460 core

// texelFetch and textureSize at multiple lods
layout(location = 0) out vec4 FragColor;

layout(binding = 0) uniform sampler2D tex;

void main() {
    ivec2 size0 = textureSize(tex, 0);
    ivec2 size1 = textureSize(tex, 1);

    ivec2 coord = ivec2(size0.x / 2, size0.y / 2);
    vec4 texel = texelFetch(tex, coord, 0);

    FragColor = texel + vec4(vec2(size1), 0.0, 0.0);
}
