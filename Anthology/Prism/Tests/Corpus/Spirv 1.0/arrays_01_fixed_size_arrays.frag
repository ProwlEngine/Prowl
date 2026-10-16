#version 460 core

layout(location=0) out vec4 FragColor;

void main()
{
    float values[8];
    for (int i = 0; i < 8; i++)
    {
        values[i] = float(i) * 0.5;
    }

    float total = 0.0;
    for (int i = 0; i < 8; i++)
    {
        total += values[i];
    }

    FragColor = vec4(total * 0.05, 0.0, 0.0, 1.0);
}
