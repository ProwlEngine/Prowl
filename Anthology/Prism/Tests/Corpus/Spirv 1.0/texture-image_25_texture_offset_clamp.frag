#version 460 core
#extension GL_ARB_sparse_texture_clamp : require

// textureOffsetClampARB: implicit-LOD sampling with a constant offset and a minimum-LOD clamp.
layout(set = 0, binding = 0) uniform sampler2D tex;

layout(location = 0) in vec2 uv;
layout(location = 0) out vec4 outColor;

void main() {
    outColor = textureOffsetClampARB(tex, uv, ivec2(1, 1), 1.0);
}
