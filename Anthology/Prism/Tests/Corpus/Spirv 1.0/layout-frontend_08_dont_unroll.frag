#version 460 core
#extension GL_EXT_control_flow_attributes : require

// [[dont_unroll]] loop control attribute on a for-loop.

layout(location = 0) in vec4 v_color;
layout(location = 0) out vec4 fragColor;

void main()
{
    vec4 acc = vec4(0.0);
    [[dont_unroll]] for (int i = 0; i < 8; i++)
    {
        acc += v_color * float(i);
    }
    fragColor = acc;
}
