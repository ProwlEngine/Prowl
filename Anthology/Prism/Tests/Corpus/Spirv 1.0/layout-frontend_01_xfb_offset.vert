#version 460 core
#extension GL_ARB_enhanced_layouts : require

// Transform feedback: capture a per-vertex value into xfb buffer 0 at offset 0.

layout(location = 0) in vec3 in_position;

layout(location = 0, xfb_buffer = 0, xfb_offset = 0) out vec3 v_feedback;

void main()
{
    v_feedback = in_position * 2.0;
    gl_Position = vec4(in_position, 1.0);
}
