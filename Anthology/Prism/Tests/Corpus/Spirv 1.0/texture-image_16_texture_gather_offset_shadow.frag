#version 460 core

// Shadow gather with offsets on sampler2DShadow: needs OpImageDrefGather with the depth reference.
layout(set = 0, binding = 0) uniform sampler2DShadow tex;

layout(location = 0) in vec2 uv;
layout(location = 0) out vec4 outColor;

const ivec2 offsets[4] = ivec2[4](
    ivec2(-1, -1),
    ivec2(1, -1),
    ivec2(-1, 1),
    ivec2(1, 1)
);

void main() {
    float refZ = 0.5;
    vec4 single = textureGatherOffset(tex, uv, refZ, ivec2(1, 1));
    vec4 multi = textureGatherOffsets(tex, uv, refZ, offsets);
    outColor = single * 0.5 + multi * 0.5;
}
