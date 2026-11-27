#version 460 core

layout(location=0) out vec4 FragColor;

float square_value(float x);

void main()
{
    float result = square_value(3.0);
    FragColor = vec4(result * 0.1, 0.0, 0.0, 1.0);
}

float square_value(float x)
{
    return x * x;
}
