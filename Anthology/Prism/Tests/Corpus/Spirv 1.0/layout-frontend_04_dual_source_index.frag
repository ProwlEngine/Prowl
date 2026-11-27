#version 460 core

// Dual-source blending: two outputs at location 0 with distinct blend indices.

layout(location = 0) in vec4 v_color;

layout(location = 0, index = 0) out vec4 c0;
layout(location = 0, index = 1) out vec4 c1;

void main()
{
    c0 = v_color;
    c1 = vec4(1.0) - v_color;
}
