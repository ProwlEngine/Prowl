#version 460 core

layout(location=0) out vec4 FragColor;

void main()
{
    float value = 1.0;

    float outer_result = value;

    {
        float value = 2.0;
        outer_result += value;
    }

    FragColor = vec4(value, outer_result, 0.0, 1.0);
}
