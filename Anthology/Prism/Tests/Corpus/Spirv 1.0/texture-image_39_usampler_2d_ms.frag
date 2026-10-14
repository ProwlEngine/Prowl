#version 460 core

// usampler2DMS: per-sample integer texel fetch (result is uvec4).
layout(set = 0, binding = 0) uniform usampler2DMS tex;

layout(location = 0) in vec2 uv;
layout(location = 0) out vec4 outColor;

void main() {
    uvec4 texel = texelFetch(tex, ivec2(uv * 256.0), 3);
    outColor = vec4(texel);
}
