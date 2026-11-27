#version 460 core

layout(location=0) out vec4 FragColor;

const float scale_factor = 2.0 * 3.0;

float apply_scale(float x)
{
    return x * scale_factor;
}

void main()
{
    float result = apply_scale(0.1);
    FragColor = vec4(result, result, result, 1.0);
}
