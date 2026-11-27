#version 460 core
#extension GL_EXT_terminate_invocation : require

// terminateInvocation ends the fragment invocation without helper semantics.

layout(location = 0) in vec4 v_color;
layout(location = 0) out vec4 fragColor;

void main()
{
    if (v_color.a < 0.1)
    {
        terminateInvocation;
    }
    fragColor = v_color;
}
