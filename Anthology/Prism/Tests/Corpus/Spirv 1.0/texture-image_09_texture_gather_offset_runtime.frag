#version 460 core

// textureGatherOffset with a non-constant offset (read from a buffer) plus a constant-offset baseline.
layout(set = 0, binding = 0) uniform sampler2D tex;
layout(set = 0, binding = 1) readonly buffer OffsetBuf { ivec2 dynOffset; };

layout(location = 0) in vec2 uv;
layout(location = 0) out vec4 outColor;

void main() {
    vec4 runtime = textureGatherOffset(tex, uv, dynOffset, 0);
    vec4 constant = textureGatherOffset(tex, uv, ivec2(1, 1), 0);
    outColor = runtime * 0.5 + constant * 0.5;
}
