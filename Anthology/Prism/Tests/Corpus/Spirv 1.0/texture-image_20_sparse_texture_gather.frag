#version 460 core
#extension GL_ARB_sparse_texture2 : require

// sparseTextureGatherARB: sparse gather returning a residency code and an out texel.
layout(set = 0, binding = 0) uniform sampler2D tex;

layout(location = 0) in vec2 uv;
layout(location = 0) out vec4 outColor;

void main() {
    vec4 texel;
    int code = sparseTextureGatherARB(tex, uv, texel, 1);
    outColor = sparseTexelsResidentARB(code) ? texel : vec4(0.0);
}
