#version 460 core

// textureProjLodOffset on sampler2D: explicit-LOD projective sampling with a constant offset.
layout(set = 0, binding = 0) uniform sampler2D tex;

layout(location = 0) in vec2 uv;
layout(location = 0) out vec4 outColor;

void main() {
    vec4 proj = vec4(uv, 0.0, 2.0);
    outColor = textureProjLodOffset(tex, proj, 1.5, ivec2(2, 2));
}
