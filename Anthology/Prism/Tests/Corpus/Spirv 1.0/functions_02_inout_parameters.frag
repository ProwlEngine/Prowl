#version 460 core

layout(location=0) out vec4 FragColor;

void adjust_values(in float scale, out float doubled, inout float accumulator)
{
    doubled = accumulator * scale * 2.0;
    accumulator += scale;
}

void main()
{
    float acc = 1.0;
    float result = 0.0;

    adjust_values(2.0, result, acc);

    FragColor = vec4(result * 0.1, acc * 0.1, 0.0, 1.0);
}
