#version 460 core

// sampler1DArray: texture, texelFetch, and textureLod (coordinate carries the layer).
layout(set = 0, binding = 0) uniform sampler1DArray tex;

layout(location = 0) in vec2 uv;
layout(location = 0) out vec4 outColor;

void main() {
    vec4 a = texture(tex, uv);
    vec4 b = texelFetch(tex, ivec2(int(uv.x * 256.0), int(uv.y)), 0);
    vec4 c = textureLod(tex, uv, 1.0);
    outColor = a + b + c;
}
