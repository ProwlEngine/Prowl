#version 460 core

layout(set = 0, binding = 0) uniform sampler2D tex;

layout(location = 0) in vec2 uv;
layout(location = 0) out vec4 outColor;

const ivec2 pcfOffsets[4] = ivec2[4](
    ivec2(-2, -2),
    ivec2(2, -2),
    ivec2(-2, 2),
    ivec2(2, 2)
);

void main()
{
    vec4 singleOffset = textureGatherOffset(tex, uv, ivec2(2, 2));
    vec4 multiOffset = textureGatherOffsets(tex, uv, pcfOffsets, 1);

    outColor = singleOffset * 0.5 + multiOffset * 0.5;
}
