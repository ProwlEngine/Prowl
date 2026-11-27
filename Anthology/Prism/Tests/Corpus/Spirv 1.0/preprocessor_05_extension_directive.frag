#version 460 core
#extension GL_ARB_shader_draw_parameters : enable

layout(location=0) out vec4 FragColor;

void main()
{
#ifdef GL_ARB_shader_draw_parameters
    float flag = 1.0;
#else
    float flag = 0.0;
#endif
    FragColor = vec4(flag, 0.0, 0.0, 1.0);
}
