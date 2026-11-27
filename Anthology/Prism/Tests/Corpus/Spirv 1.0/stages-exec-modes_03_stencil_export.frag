#version 460
#extension GL_ARB_shader_stencil_export : require
out int gl_FragStencilRefARB;
layout(location = 0) out vec4 oColor;
void main()
{
    gl_FragStencilRefARB = 100;
    oColor = vec4(1.0, 0.0, 0.0, 1.0);
}
