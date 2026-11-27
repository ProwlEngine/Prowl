#version 460 core
#extension GL_ARB_enhanced_layouts : require

// Transform feedback with an explicit xfb_stride spanning two captured outputs.

layout(location = 0) in vec3 in_position;
layout(location = 1) in vec2 in_uv;

layout(location = 0, xfb_buffer = 0, xfb_offset = 0, xfb_stride = 32) out vec3 v_position;
layout(location = 1, xfb_buffer = 0, xfb_offset = 16) out vec2 v_uv;

void main()
{
    v_position = in_position;
    v_uv = in_uv;
    gl_Position = vec4(in_position, 1.0);
}
