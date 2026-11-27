#version 460 core
#extension GL_ARB_sparse_texture2 : require

// sparseTexelFetchARB: sparse texel fetch with explicit LOD.
layout(set = 0, binding = 0) uniform sampler2D tex;

layout(location = 0) in vec2 uv;
layout(location = 0) out vec4 outColor;

void main() {
    vec4 texel;
    int code = sparseTexelFetchARB(tex, ivec2(uv * 256.0), 0, texel);
    outColor = sparseTexelsResidentARB(code) ? texel : vec4(0.0);
}
