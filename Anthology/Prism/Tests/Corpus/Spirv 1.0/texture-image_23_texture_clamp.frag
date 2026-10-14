#version 460 core
#extension GL_ARB_sparse_texture_clamp : require

// textureClampARB: implicit-LOD sampling with a minimum-LOD clamp (MinLod image operand).
layout(set = 0, binding = 0) uniform sampler2D tex;

layout(location = 0) in vec2 uv;
layout(location = 0) out vec4 outColor;

void main() {
    outColor = textureClampARB(tex, uv, 1.0);
}
