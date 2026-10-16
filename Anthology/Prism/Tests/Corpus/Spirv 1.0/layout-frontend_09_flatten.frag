#version 460 core
#extension GL_EXT_control_flow_attributes : require

// [[flatten]] selection control attribute on an if.

layout(location = 0) in vec4 v_color;
layout(location = 0) out vec4 fragColor;

void main()
{
    vec4 result;
    [[flatten]] if (v_color.x > 0.5)
    {
        result = v_color;
    }
    else
    {
        result = vec4(0.0);
    }
    fragColor = result;
}
