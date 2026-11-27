#version 460 core

// The 1.4 interface also lists samplers and Private globals, in materialization order.

layout(set = 0, binding = 0) uniform sampler2D albedo;

layout(location = 0) in vec2 uv;
layout(location = 0) out vec4 FragColor;

float gExposure = 1.5;

void main()
{
    vec4 c = texture(albedo, uv);
    FragColor = c * gExposure;
}
