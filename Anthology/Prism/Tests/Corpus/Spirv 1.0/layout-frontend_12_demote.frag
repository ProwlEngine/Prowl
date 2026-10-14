#version 460 core
#extension GL_EXT_demote_to_helper_invocation : require

// demote to helper invocation, then read the helper state back.

layout(location = 0) in vec4 v_color;
layout(location = 0) out vec4 fragColor;

void main()
{
    if (v_color.a < 0.1)
    {
        demote;
    }

    bool helper = helperInvocationEXT();
    fragColor = helper ? vec4(0.0) : v_color;
}
