#version 460 core

invariant gl_Position;

layout(location=0) invariant out vec3 world_normal;

layout(location=0) in vec3 in_position;
layout(location=1) in vec3 in_normal;

void main()
{
    world_normal = in_normal;
    gl_Position = vec4(in_position, 1.0);
}
