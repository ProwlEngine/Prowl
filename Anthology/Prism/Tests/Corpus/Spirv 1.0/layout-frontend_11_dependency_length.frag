#version 460 core
#extension GL_EXT_control_flow_attributes : require

// [[dependency_length(4)]] loop control attribute on a loop.

layout(location = 0) in vec4 v_color;
layout(location = 0) out vec4 fragColor;

void main()
{
    vec4 acc = v_color;
    [[dependency_length(4)]] for (int i = 0; i < 16; i++)
    {
        acc = acc * 0.5 + v_color;
    }
    fragColor = acc;
}
