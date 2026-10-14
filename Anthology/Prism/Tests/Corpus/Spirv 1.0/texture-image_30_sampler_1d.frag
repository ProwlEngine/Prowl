#version 460 core

// sampler1D: texture, texelFetch, and textureLod.
layout(set = 0, binding = 0) uniform sampler1D tex;

layout(location = 0) in float u;
layout(location = 0) out vec4 outColor;

void main() {
    vec4 a = texture(tex, u);
    vec4 b = texelFetch(tex, int(u * 256.0), 0);
    vec4 c = textureLod(tex, u, 1.0);
    outColor = a + b + c;
}
