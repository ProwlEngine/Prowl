#version 460 core

layout(binding=0) uniform sampler2D diffuse_map;

layout(std140, binding=1) uniform Settings
{
    vec4 tint_color;
};

layout(location=0) in vec2 uv;
layout(location=0) out vec4 FragColor;

void main()
{
    vec4 tex_color = texture(diffuse_map, uv);
    FragColor = tex_color * tint_color;
}
