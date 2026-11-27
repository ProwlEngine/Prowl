#version 460 core
#extension GL_ARB_sparse_texture2 : require

// sparseTexelsResidentARB: interpret a residency code from a sparse sample to gate output.
layout(set = 0, binding = 0) uniform sampler2D tex;

layout(location = 0) in vec2 uv;
layout(location = 0) out vec4 outColor;

void main() {
    vec4 texel;
    int code = sparseTextureARB(tex, uv, texel);
    bool resident = sparseTexelsResidentARB(code);
    outColor = resident ? texel : vec4(1.0, 0.0, 1.0, 1.0);
}
