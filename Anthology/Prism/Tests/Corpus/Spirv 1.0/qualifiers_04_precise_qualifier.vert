#version 460 core

layout(location=0) in vec3 in_position;
layout(push_constant) uniform PushConstants
{
    mat4 mvp;
};

void main()
{
    precise vec4 clip_position = mvp * vec4(in_position, 1.0);
    gl_Position = clip_position;
}
