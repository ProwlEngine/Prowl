#version 460 core

layout(location=0) in vec3 in_position;

layout(location=0) flat out int flat_value;
layout(location=1) smooth out vec3 smooth_color;
layout(location=2) noperspective out vec2 noperspective_uv;

void main()
{
    flat_value = 1;
    smooth_color = vec3(0.5, 0.5, 0.5);
    noperspective_uv = vec2(0.0, 1.0);
    gl_Position = vec4(in_position, 1.0);
}
