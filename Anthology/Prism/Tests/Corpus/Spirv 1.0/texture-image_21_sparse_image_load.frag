#version 460 core
#extension GL_ARB_sparse_texture2 : require

// sparseImageLoadARB: sparse storage-image load returning a residency code and an out texel.
layout(set = 0, binding = 0, rgba32f) uniform readonly image2D img;

layout(location = 0) in vec2 uv;
layout(location = 0) out vec4 outColor;

void main() {
    vec4 texel;
    int code = sparseImageLoadARB(img, ivec2(uv * 256.0), texel);
    outColor = sparseTexelsResidentARB(code) ? texel : vec4(0.0);
}
