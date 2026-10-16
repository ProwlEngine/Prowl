#version 460 core

// A push_constant block in the 1.4 interface, plus the built-in gl_Position path.

layout(push_constant) uniform Push
{
    mat4 mvp;
    vec4 tint;
} pc;

layout(location = 0) in vec3 position;
layout(location = 0) out vec4 vTint;

void main()
{
    gl_Position = pc.mvp * vec4(position, 1.0);
    vTint = pc.tint;
}
