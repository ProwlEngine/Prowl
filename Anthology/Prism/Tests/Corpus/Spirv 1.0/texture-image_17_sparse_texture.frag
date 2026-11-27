#version 460 core
#extension GL_ARB_sparse_texture2 : require

// sparseTextureARB: implicit-LOD sparse sampling returning a residency code and an out texel.
layout(set = 0, binding = 0) uniform sampler2D tex;

layout(location = 0) in vec2 uv;
layout(location = 0) out vec4 outColor;

void main() {
    vec4 texel;
    int code = sparseTextureARB(tex, uv, texel);
    outColor = sparseTexelsResidentARB(code) ? texel : vec4(0.0);
}
