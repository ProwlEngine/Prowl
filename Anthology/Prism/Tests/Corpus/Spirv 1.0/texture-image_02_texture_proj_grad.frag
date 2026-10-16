#version 460 core

// textureProjGrad on sampler2D: explicit-gradient projective sampling.
layout(set = 0, binding = 0) uniform sampler2D tex;

layout(location = 0) in vec2 uv;
layout(location = 0) out vec4 outColor;

void main() {
    vec4 proj = vec4(uv, 0.0, 2.0);
    outColor = textureProjGrad(tex, proj, vec2(0.01, 0.0), vec2(0.0, 0.01));
}
