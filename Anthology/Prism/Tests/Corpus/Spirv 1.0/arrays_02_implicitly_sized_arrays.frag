#version 460 core

layout(location=0) out vec4 FragColor;

const float weights[] = float[](1.0, 2.0, 3.0, 4.0);

void main()
{
    float total = 0.0;
    for (int i = 0; i < weights.length(); i++)
    {
        total += weights[i];
    }

    FragColor = vec4(total * 0.1, 0.0, 0.0, 1.0);
}
