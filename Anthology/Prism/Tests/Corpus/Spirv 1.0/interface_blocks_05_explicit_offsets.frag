#version 460 core

layout(std140, binding=0) uniform Params
{
    layout(offset=0) float scale_factor;
    layout(offset=16) vec3 base_color;
    layout(offset=32) float bias_value;
};

layout(location=0) out vec4 FragColor;

void main()
{
    FragColor = vec4(base_color * scale_factor + bias_value, 1.0);
}
