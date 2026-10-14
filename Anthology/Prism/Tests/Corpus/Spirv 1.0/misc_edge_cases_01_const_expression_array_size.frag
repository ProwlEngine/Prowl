#version 460 core

layout(location=0) out vec4 FragColor;

const int A = 3;
const int B = 5;

void main()
{
    float values[A + B - 1];

    for (int i = 0; i < A + B - 1; i++)
    {
        values[i] = float(i) * 0.1;
    }

    float sum = 0.0;
    for (int i = 0; i < A + B - 1; i++)
    {
        sum += values[i];
    }

    FragColor = vec4(sum, sum, sum, 1.0);
}
