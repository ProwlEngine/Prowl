#version 460 core
#extension GL_ARB_sparse_texture_clamp : require

// textureGradOffsetClampARB: explicit-gradient sampling with a constant offset and a minimum-LOD clamp.
layout(set = 0, binding = 0) uniform sampler2D tex;

layout(location = 0) in vec2 uv;
layout(location = 0) out vec4 outColor;

void main() {
    outColor = textureGradOffsetClampARB(tex, uv, vec2(0.01, 0.0), vec2(0.0, 0.01), ivec2(1, -1), 1.0);
}
