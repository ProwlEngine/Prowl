#version 460 core

layout(location=0) in vec3 in_color;
layout(location=2) in vec2 in_uv;
layout(location=5) in float in_intensity;

layout(location=0) out vec4 FragColor;

void main()
{
    vec3 combined = in_color * in_intensity;
    FragColor = vec4(combined + vec3(in_uv, 0.0), 1.0);
}
