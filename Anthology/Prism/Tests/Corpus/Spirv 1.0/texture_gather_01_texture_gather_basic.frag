#version 460 core

layout(set = 0, binding = 0) uniform sampler2D tex;

layout(location = 0) in vec2 uv;
layout(location = 0) out vec4 outColor;

void main()
{
    vec4 redGather = textureGather(tex, uv);
    vec4 blueGather = textureGather(tex, uv, 2);

    outColor = redGather * 0.5 + blueGather * 0.5;
}
