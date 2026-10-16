#version 460 core

layout(location=0) in vec3 in_position;
layout(location=1) in vec3 in_normal;
layout(location=2) in vec2 in_uv;

layout(location=0) out VertexData
{
    vec3 normal;
    vec2 uv;
} v_out;

void main()
{
    v_out.normal = in_normal;
    v_out.uv = in_uv;
    gl_Position = vec4(in_position, 1.0);
}
