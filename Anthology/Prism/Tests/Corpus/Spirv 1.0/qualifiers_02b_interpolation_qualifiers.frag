#version 460 core

layout(location=0) flat in int flat_value;
layout(location=1) smooth in vec3 smooth_color;
layout(location=2) noperspective in vec2 noperspective_uv;

layout(location=0) out vec4 FragColor;

void main()
{
    FragColor = vec4(smooth_color + vec3(noperspective_uv, 0.0), float(flat_value));
}
