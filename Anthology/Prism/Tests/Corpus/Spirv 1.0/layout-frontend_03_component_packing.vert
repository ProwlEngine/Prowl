#version 460 core

// Two outputs packed into a single location via the component qualifier.

layout(location = 0) in vec3 in_position;
layout(location = 1) in vec4 in_data;

layout(location = 0, component = 0) out vec2 v_uv;
layout(location = 0, component = 2) out vec2 v_extra;

void main()
{
    v_uv = in_data.xy;
    v_extra = in_data.zw;
    gl_Position = vec4(in_position, 1.0);
}
