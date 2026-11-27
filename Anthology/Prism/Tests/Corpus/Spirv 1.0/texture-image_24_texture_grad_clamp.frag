#version 460 core
#extension GL_ARB_sparse_texture_clamp : require

// textureGradClampARB: explicit-gradient sampling with a minimum-LOD clamp.
layout(set = 0, binding = 0) uniform sampler2D tex;

layout(location = 0) in vec2 uv;
layout(location = 0) out vec4 outColor;

void main() {
    outColor = textureGradClampARB(tex, uv, vec2(0.01, 0.0), vec2(0.0, 0.01), 1.0);
}
