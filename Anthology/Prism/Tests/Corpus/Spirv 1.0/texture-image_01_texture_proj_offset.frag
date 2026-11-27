#version 460 core

// textureProjOffset on sampler2D: implicit-LOD projective sampling with a constant offset.
layout(set = 0, binding = 0) uniform sampler2D tex;

layout(location = 0) in vec2 uv;
layout(location = 0) out vec4 outColor;

void main() {
    vec4 proj = vec4(uv, 0.0, 2.0);
    outColor = textureProjOffset(tex, proj, ivec2(1, -1));
}
